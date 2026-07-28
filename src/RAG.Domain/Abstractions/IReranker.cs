using RAG.Domain.Entities;

namespace RAG.Domain.Abstractions;

public interface IReranker
{
    Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        CancellationToken ct = default);
}
