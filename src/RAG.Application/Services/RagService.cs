using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;

namespace RAG.Application.Services;

public class RagService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    IReranker reranker,
    IChatClient chatClient,
    AssistantCatalog assistantCatalog)
{
    public async Task<string> AskAsync(
        string query,
        int topKRetrieve = 20,
        int topKRank = 5,
        string? modelId = null,
        CancellationToken ct = default)
    {
        // Per-request model routing (ASEL-2): resolve the model id against the
        // catalog allow-list ONCE, so the same resolved model drives both the
        // reranker chat call and the final generation (latency fix: rerank no
        // longer runs on the default slow model). Retrieval is identical
        // regardless of selection (ASEL-4/5/6); null/blank/unknown resolve to
        // the default assistant.
        var model = assistantCatalog.Resolve(modelId);

        var topResults = await RetrieveTopResultsAsync(query, topKRetrieve, topKRank, model.Model, ct);
        var prompt = BuildPrompt(query, BuildContext(topResults));

        var response = await chatClient.GetResponseAsync(
            prompt, new ChatOptions { ModelId = model.Model }, ct);

        return response.Text ?? "No se pudo generar una respuesta.";
    }

    /// <summary>
    /// Streaming variant of <see cref="AskAsync"/> (DocsChat-3): runs the
    /// identical retrieval pipeline (ASEL-4/5/6) and yields the chat response as
    /// it is generated, one string per text delta, so callers can render tokens
    /// as they arrive instead of waiting for the full answer.
    /// </summary>
    public async IAsyncEnumerable<string> AskStreamingAsync(
        string query,
        int topKRetrieve = 20,
        int topKRank = 5,
        string? modelId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = assistantCatalog.Resolve(modelId);

        var topResults = await RetrieveTopResultsAsync(query, topKRetrieve, topKRank, model.Model, ct);
        var prompt = BuildPrompt(query, BuildContext(topResults));

        await foreach (var update in chatClient.GetStreamingResponseAsync(
                           prompt, new ChatOptions { ModelId = model.Model }, ct))
        {
            // Some providers emit non-text updates (role/finish signals); only
            // text deltas become visible output.
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>
    /// Non-streaming variant of <see cref="AskAsync"/> that also exposes the
    /// citations (DocsChat-4): returns the answer AND the top-ranked fragments
    /// that backed it (filename + snippet + optional page), so callers can
    /// render clickable sources under the answer. Retrieval runs exactly once
    /// and is shared with <see cref="AskAsync"/> — sources are a view over the
    /// same <c>topResults</c>, never an extra pipeline pass.
    /// </summary>
    public async Task<(string Answer, IReadOnlyList<SourceRef> Sources)> AskWithSourcesAsync(
        string query,
        int topKRetrieve = 20,
        int topKRank = 5,
        string? modelId = null,
        CancellationToken ct = default)
    {
        var model = assistantCatalog.Resolve(modelId);

        var topResults = await RetrieveTopResultsAsync(query, topKRetrieve, topKRank, model.Model, ct);
        var prompt = BuildPrompt(query, BuildContext(topResults));

        var response = await chatClient.GetResponseAsync(
            prompt, new ChatOptions { ModelId = model.Model }, ct);

        var answer = response.Text ?? "No se pudo generar una respuesta.";
        return (answer, ToSources(topResults));
    }

    /// <summary>
    /// Streaming variant that also exposes the citations (DocsChat-4): the
    /// retrieval pass runs eagerly and its <c>topResults</c> become the sources
    /// delivered alongside the terminal SSE event, while the answer still
    /// streams as deltas. Same single-pipeline guarantee as
    /// <see cref="AskWithSourcesAsync"/>.
    /// </summary>
    public async Task<(IAsyncEnumerable<string> Deltas, IReadOnlyList<SourceRef> Sources)> AskStreamWithSourcesAsync(
        string query,
        int topKRetrieve = 20,
        int topKRank = 5,
        string? modelId = null,
        CancellationToken ct = default)
    {
        var model = assistantCatalog.Resolve(modelId);

        var topResults = await RetrieveTopResultsAsync(query, topKRetrieve, topKRank, model.Model, ct);
        var prompt = BuildPrompt(query, BuildContext(topResults));
        var deltas = StreamDeltasAsync(prompt, model.Model, ct);

        return (deltas, ToSources(topResults));
    }

    /// <summary>
    /// Shared retrieval pipeline (steps 1-3 of the RAG flow): embedding the
    /// query, hybrid search with RRF, then LLM rerank capped at
    /// <paramref name="topKRank"/>. Every public entry point delegates here so
    /// retrieval is identical and runs once per request (ASEL-5/6).
    /// </summary>
    private async Task<List<SearchResult>> RetrieveTopResultsAsync(
        string query,
        int topKRetrieve,
        int topKRank,
        string modelId,
        CancellationToken ct)
    {
        var queryEmbeddings = await embeddingGenerator.GenerateAsync(new[] { query }, cancellationToken: ct);
        var results = await vectorStore.HybridSearchAsync(
            queryEmbeddings[0].Vector, query, topK: topKRetrieve, ct);
        var reranked = await reranker.RerankAsync(query, results, modelId, ct);
        return reranked.Take(topKRank).ToList();
    }

    /// <summary>
    /// Joins the top-ranked chunk contents into the prompt's context block.
    /// </summary>
    private static string BuildContext(IEnumerable<SearchResult> topResults)
        => string.Join("\n\n---\n\n", topResults.Select(r => r.Chunk.Content));

    /// <summary>
    /// Maps the top-ranked results to citation sources (DocsChat-4): the file
    /// name comes from the chunk's <c>Metadata["source"]</c> when the chunk
    /// carries it (set at chunking time); the page stays null until the
    /// extraction pipeline tracks pages.
    /// </summary>
    private static IReadOnlyList<SourceRef> ToSources(IEnumerable<SearchResult> topResults)
        => topResults
            .Select(r => new SourceRef(
                r.Chunk.Metadata.TryGetValue("source", out var source) ? source?.ToString() : null,
                r.Chunk.Content,
                Page: null))
            .ToList();

    /// <summary>
    /// Yields the chat client's text deltas for a streaming answer, skipping
    /// non-text updates (role/finish signals).
    /// </summary>
    private async IAsyncEnumerable<string> StreamDeltasAsync(
        string prompt,
        string modelId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in chatClient.GetStreamingResponseAsync(
                           prompt, new ChatOptions { ModelId = modelId }, ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>
    /// Builds the system prompt shared by <see cref="AskAsync"/> and
    /// <see cref="AskStreamingAsync"/>: strict context grounding, citation
    /// instruction and the deterministic language instruction (ASEL-9).
    /// </summary>
    private static string BuildPrompt(string query, string context)
    {
        var language = ResolveResponseLanguage(query);
        var languageInstruction = language == "en"
            ? "Answer in English."
            : "Answer in Spanish.";

        return $"""
            You are an expert document analyst assistant.
            Answer the question STRICTLY based on the provided context.
            If the context does not contain enough information, say it explicitly.
            Include citations to relevant fragments when possible.
            {languageInstruction}

            ## Context:
            {context}

            ## Question:
            {query}

            ## Answer:
            """;
    }

    /// <summary>
    /// Deterministic response-language routing (ASEL-9): English queries are
    /// answered in English, everything else defaults to Spanish because the app
    /// UI is Spanish and small local models follow an explicit language
    /// instruction more reliably than "answer in the same language".
    /// </summary>
    private static string ResolveResponseLanguage(string query)
    {
        if (query.Contains("what", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("how", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("why", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("the ", StringComparison.OrdinalIgnoreCase) ||
            query.Contains(" is ", StringComparison.OrdinalIgnoreCase) ||
            query.Contains(" are ", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return "es";
    }
}
