namespace RAG.Domain.Abstractions;

public interface IDocumentParser
{
    bool CanHandle(string contentType);
    Task<string> ParseAsync(Stream content, CancellationToken ct = default);
}
