using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using RAG.Domain.Abstractions;

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

        // 1. Generar embedding de la consulta
        var queryEmbeddings = await embeddingGenerator.GenerateAsync(new[] { query }, cancellationToken: ct);

        // 2. Hybrid search: vector + keyword con RRF
        var results = await vectorStore.HybridSearchAsync(
            queryEmbeddings[0].Vector, query, topK: topKRetrieve, ct);

        // 3. Reranking con LLM, usando el modelo seleccionado
        var reranked = await reranker.RerankAsync(query, results, model.Model, ct);
        var topResults = reranked.Take(topKRank).ToList();

        // 4. Generar respuesta con contexto aumentado
        var context = string.Join("\n\n---\n\n", topResults.Select(r => r.Chunk.Content));

        var prompt = BuildPrompt(query, context);

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

        var queryEmbeddings = await embeddingGenerator.GenerateAsync(new[] { query }, cancellationToken: ct);
        var results = await vectorStore.HybridSearchAsync(
            queryEmbeddings[0].Vector, query, topK: topKRetrieve, ct);
        var reranked = await reranker.RerankAsync(query, results, model.Model, ct);
        var topResults = reranked.Take(topKRank).ToList();

        var context = string.Join("\n\n---\n\n", topResults.Select(r => r.Chunk.Content));
        var prompt = BuildPrompt(query, context);

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
