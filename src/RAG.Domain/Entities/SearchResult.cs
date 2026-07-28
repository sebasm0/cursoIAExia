namespace RAG.Domain.Entities;

public class SearchResult
{
    public required DocumentChunk Chunk { get; set; }
    public double VectorScore { get; set; }
    public double KeywordScore { get; set; }
    public double RrfScore { get; set; }
    public double RerankScore { get; set; }
}
