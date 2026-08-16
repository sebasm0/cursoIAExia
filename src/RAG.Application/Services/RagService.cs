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

        var language = ResolveResponseLanguage(query);
        var languageInstruction = language == "en"
            ? "Answer in English."
            : "Answer in Spanish.";

        var prompt = $"""
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

        var response = await chatClient.GetResponseAsync(
            prompt, new ChatOptions { ModelId = model.Model }, ct);

        return response.Text ?? "No se pudo generar una respuesta.";
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
