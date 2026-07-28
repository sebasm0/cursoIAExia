using Microsoft.Extensions.AI;
using RAG.Domain.Abstractions;

namespace RAG.Application.Services;

public class RagService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    IReranker reranker,
    IChatClient chatClient)
{
    public async Task<string> AskAsync(string query, int topKRetrieve = 20, int topKRank = 5, CancellationToken ct = default)
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

        // Use the extension method that accepts a plain string
        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);

        return response.Text ?? "No se pudo generar una respuesta.";
    }
}
