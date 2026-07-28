using RAG.Domain.Entities;

namespace RAG.Domain.Abstractions;

public interface IChunker
{
    Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        Document document, string content, CancellationToken ct = default);
}
