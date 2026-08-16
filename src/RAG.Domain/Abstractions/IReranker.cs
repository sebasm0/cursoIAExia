using RAG.Domain.Entities;

namespace RAG.Domain.Abstractions;

public interface IReranker
{
    /// <summary>
    /// Reranks candidate results. <paramref name="modelId"/> is the resolved
    /// Ollama model id to use for the rerank chat call; when null or blank the
    /// implementation falls back to the chat client's default model.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        string? modelId = null,
        CancellationToken ct = default);
}
