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

        var text = await parser.ParseAsync(content, ct);
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
