using Microsoft.Extensions.AI;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;

namespace RAG.Application.Services;

public class IngestionService(
    IEnumerable<IDocumentParser> parsers,
    IChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore)
{
    public async Task<Document> IngestAsync(
        string fileName,
        string contentType,
        Stream content,
        CancellationToken ct = default)
    {
        var parser = parsers.FirstOrDefault(p => p.CanHandle(contentType))
            ?? throw new NotSupportedException($"No hay un parser disponible para el tipo: {contentType}");

        var document = new Document
        {
            FileName = fileName,
            ContentType = contentType,
            Size = content.Length,
        };

        // Capture the raw bytes BEFORE parsing so the original file can be
        // served back later. Streams are forward-only, so copy the whole
        // stream into memory first and parse from a fresh buffer.
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var fileBytes = buffer.ToArray();
        document.Content = fileBytes;

        await using var parseStream = new MemoryStream(fileBytes, writable: false);
        var text = await parser.ParseAsync(parseStream, ct);
        var chunks = await chunker.ChunkAsync(document, text, ct);

        var batch = new List<(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding)>();

        foreach (var chunk in chunks)
        {
            var embeddings = await embeddingGenerator.GenerateAsync(new[] { chunk.Content }, cancellationToken: ct);
            batch.Add((chunk, embeddings[0].Vector));
        }

        await vectorStore.StoreChunksBatchAsync(document, batch, ct);

        return document;
    }
}
