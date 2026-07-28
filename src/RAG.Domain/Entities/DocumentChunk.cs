namespace RAG.Domain.Entities;

public class DocumentChunk
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DocumentId { get; init; }
    public required string Content { get; set; }
    public int Index { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = [];
}
