using Microsoft.Extensions.AI;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;

namespace RAG.Infrastructure.Chunking;

/// <summary>
/// Chunker semántico: divide el texto usando similitud de embeddings entre oraciones.
/// Donde la similitud baja de un umbral, detecta un cambio de tema y crea un nuevo chunk.
/// </summary>
public sealed class SemanticChunker(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator) : IChunker
{
    private const double SimilarityThreshold = 0.65;
    private const int MinChunkSize = 100;
    private const int MaxChunkSize = 2000;
    private const int SentencesPerGroup = 3;

    public async Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        Document document, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<DocumentChunk>();

        // 1. Split into sentence groups (batches of ~3 sentences)
        var sentences = SplitSentences(content);
        var groups = GroupSentences(sentences, SentencesPerGroup);

        if (groups.Count == 0)
            return Array.Empty<DocumentChunk>();

        // 2. Generate embeddings for each group
        var embeddings = await GenerateGroupEmbeddingsAsync(groups, ct);

        // 3. Detect boundaries using cosine similarity
        var boundaries = DetectBoundaries(embeddings);

        // 4. Merge groups into final chunks
        var chunks = MergeIntoChunks(document, groups, embeddings, boundaries);

        return chunks;
    }

    // ─────────── Private ───────────

    private static List<string> SplitSentences(string text)
    {
        // Split on sentence-ending punctuation, newlines, or paragraph breaks
        var separators = new[] { ". ", "! ", "? ", "\n\n", "\r\n\r\n" };
        var sentences = new List<string>();
        var remaining = text;

        while (!string.IsNullOrEmpty(remaining))
        {
            var earliest = -1;
            var earliestSep = string.Empty;

            foreach (var sep in separators)
            {
                var idx = remaining.IndexOf(sep, StringComparison.Ordinal);
                if (idx >= 0 && (earliest < 0 || idx < earliest))
                {
                    earliest = idx;
                    earliestSep = sep;
                }
            }

            if (earliest < 0)
            {
                sentences.Add(remaining.Trim());
                break;
            }

            // Include the separator character in the sentence
            var sentence = remaining[..(earliest + earliestSep.Length)].Trim();
            if (!string.IsNullOrEmpty(sentence))
                sentences.Add(sentence);

            remaining = remaining[(earliest + earliestSep.Length)..];
        }

        // Filter very short fragments
        return sentences.Where(s => s.Length >= 3).ToList();
    }

    private static List<List<string>> GroupSentences(List<string> sentences, int groupSize)
    {
        var groups = new List<List<string>>();
        for (int i = 0; i < sentences.Count; i += groupSize)
        {
            groups.Add(sentences.Skip(i).Take(groupSize).ToList());
        }
        return groups;
    }

    private async Task<List<ReadOnlyMemory<float>>> GenerateGroupEmbeddingsAsync(
        List<List<string>> groups, CancellationToken ct)
    {
        var texts = groups.Select(g => string.Join(" ", g)).ToList();

        // Batch embedding generation
        var allEmbeddings = new List<ReadOnlyMemory<float>>();

        // Process in batches of 20 to avoid overwhelming the embedding model
        const int batchSize = 20;
        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var batch = texts.Skip(i).Take(batchSize).ToList();
            var results = await embeddingGenerator.GenerateAsync(batch, cancellationToken: ct);
            allEmbeddings.AddRange(results.Select(r => r.Vector));
        }

        return allEmbeddings;
    }

    private static List<int> DetectBoundaries(List<ReadOnlyMemory<float>> embeddings)
    {
        var boundaries = new List<int> { 0 }; // first group is always a boundary

        for (int i = 1; i < embeddings.Count; i++)
        {
            var similarity = CosineSimilarity(embeddings[i - 1], embeddings[i]);

            if (similarity < SimilarityThreshold)
            {
                boundaries.Add(i);
            }
        }

        return boundaries;
    }

    private static List<DocumentChunk> MergeIntoChunks(
        Document document,
        List<List<string>> groups,
        List<ReadOnlyMemory<float>> embeddings,
        List<int> boundaries)
    {
        var chunks = new List<DocumentChunk>();
        int chunkIndex = 0;

        for (int b = 0; b < boundaries.Count; b++)
        {
            var start = boundaries[b];
            var end = (b + 1 < boundaries.Count) ? boundaries[b + 1] : groups.Count;

            var content = string.Join("\n", groups.Skip(start).Take(end - start).Select(g => string.Join(" ", g)));

            // Skip chunks that are too small — merge with next if possible
            if (content.Length < MinChunkSize && b + 1 < boundaries.Count)
            {
                continue;
            }

            chunks.Add(new DocumentChunk
            {
                DocumentId = document.Id,
                Content = content.Length > MaxChunkSize ? content[..MaxChunkSize] : content,
                Index = chunkIndex++,
                Metadata = new Dictionary<string, object>
                {
                    ["source"] = document.FileName,
                    ["chunk_type"] = "semantic",
                },
            });
        }

        // Fallback: if no semantic chunk was created, split by character
        if (chunks.Count == 0)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var content = string.Join(" ", groups[i]);
                if (content.Length < 10) continue;

                chunks.Add(new DocumentChunk
                {
                    DocumentId = document.Id,
                    Content = content.Length > MaxChunkSize ? content[..MaxChunkSize] : content,
                    Index = chunkIndex++,
                    Metadata = new Dictionary<string, object>
                    {
                        ["source"] = document.FileName,
                        ["chunk_type"] = "fallback",
                    },
                });
            }
        }

        return chunks;
    }

    private static double CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var aSpan = a.Span;
        var bSpan = b.Span;

        double dotProduct = 0, magA = 0, magB = 0;

        for (int i = 0; i < aSpan.Length; i++)
        {
            dotProduct += aSpan[i] * bSpan[i];
            magA += aSpan[i] * aSpan[i];
            magB += bSpan[i] * bSpan[i];
        }

        var magnitude = Math.Sqrt(magA) * Math.Sqrt(magB);
        return magnitude is 0 ? 0 : dotProduct / magnitude;
    }
}
