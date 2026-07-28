using RAG.Domain.Entities;

namespace RAG.Domain.Abstractions;

public interface IVectorStore
{
    Task StoreChunkAsync(DocumentChunk chunk, ReadOnlyMemory<float> embedding, CancellationToken ct = default);
    Task StoreChunksBatchAsync(
        IEnumerable<(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding)> chunks,
        CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult>> HybridSearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        string queryText,
        int topK = 10,
        CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);
}
