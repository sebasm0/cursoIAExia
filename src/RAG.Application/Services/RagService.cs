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
        // 1. Generar embedding de la consulta
        var queryEmbeddings = await embeddingGenerator.GenerateAsync(new[] { query }, cancellationToken: ct);

        // 2. Hybrid search: vector + keyword con RRF
        var results = await vectorStore.HybridSearchAsync(
            queryEmbeddings[0].Vector, query, topK: topKRetrieve, ct);

        // 3. Reranking con LLM
        var reranked = await reranker.RerankAsync(query, results, ct);
        var topResults = reranked.Take(topKRank).ToList();

        // 4. Generar respuesta con contexto aumentado
        var context = string.Join("\n\n---\n\n", topResults.Select(r => r.Chunk.Content));

        var prompt = $"""
            You are an expert document analyst assistant.
            Answer the question STRICTLY based on the provided context.
            If the context does not contain enough information, say it explicitly.
            Include citations to relevant fragments when possible.

            ## Context:
            {context}

            ## Question:
            {query}

            ## Answer:
            """;

        // Per-request model routing (ASEL-2): resolve the model id against the
        // catalog allow-list so only a catalog model ever reaches the chat
        // client. Retrieval above is identical regardless of selection (ASEL-4/5/6).
        var model = assistantCatalog.Resolve(modelId);

        var response = await chatClient.GetResponseAsync(
            prompt, new ChatOptions { ModelId = model.Model }, ct);

        return response.Text ?? "No se pudo generar una respuesta.";
    }
}
