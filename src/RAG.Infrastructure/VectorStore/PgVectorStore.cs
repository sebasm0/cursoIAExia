using System.Data;
using Dapper;
using Npgsql;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;

namespace RAG.Infrastructure.VectorStore;

public sealed class PgVectorStore : IVectorStore, IAsyncDisposable
{
    private const int RrfK = 60;  // RRF smoothing constant
    private const int EmbeddingDimensions = 768; // nomic-embed-text default

    private readonly string _connectionString;
    private NpgsqlDataSource? _dataSource;

    public PgVectorStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    // ─────────────── Setup ───────────────

    private async ValueTask<NpgsqlDataSource> GetDataSourceAsync()
    {
        if (_dataSource is not null)
            return _dataSource;

        var dataSource = NpgsqlDataSource.Create(_connectionString);
        await EnsureSchemaAsync(dataSource);
        _dataSource = dataSource;
        return dataSource;
    }

    private static async Task EnsureSchemaAsync(NpgsqlDataSource ds)
    {
        await using var conn = await ds.OpenConnectionAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS documents (
                    id UUID PRIMARY KEY,
                    file_name TEXT NOT NULL,
                    content_type TEXT NOT NULL,
                    size BIGINT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS chunks (
                    id UUID PRIMARY KEY,
                    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                    content TEXT NOT NULL,
                    chunk_index INT NOT NULL,
                    metadata JSONB DEFAULT '{}'::jsonb,
                    embedding vector(:dims)
                );
                """.Replace(":dims", EmbeddingDimensions.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks(document_id);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_chunks_embedding
                    ON chunks USING ivfflat (embedding vector_cosine_ops)
                    WITH (lists = 100);
                """;
            await cmd.ExecuteNonQueryAsync();

            // Note: IVFFlat requires at least ~1000 rows to build a useful index.
            // For smaller datasets, consider HNSW or skip the index.
        }
    }

    // ─────────────── IVectorStore ───────────────

    public async Task StoreChunkAsync(DocumentChunk chunk, ReadOnlyMemory<float> embedding, CancellationToken ct = default)
    {
        var ds = await GetDataSourceAsync();
        await using var conn = await ds.OpenConnectionAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunks (id, document_id, content, chunk_index, embedding)
            VALUES (@id, @documentId, @content, @index, @embedding::vector)
            """;
        cmd.Parameters.AddWithValue("id", chunk.Id);
        cmd.Parameters.AddWithValue("documentId", chunk.DocumentId);
        cmd.Parameters.AddWithValue("content", chunk.Content);
        cmd.Parameters.AddWithValue("index", chunk.Index);
        cmd.Parameters.AddWithValue("embedding", embedding.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task StoreChunksBatchAsync(
        Document document,
        IEnumerable<(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding)> chunks,
        CancellationToken ct = default)
    {
        var ds = await GetDataSourceAsync();
        await using var conn = await ds.OpenConnectionAsync(ct);

        // Atomic: the document row and all its chunks commit (or roll back) together,
        // so a mid-ingest failure never leaves an orphaned document with zero chunks.
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var docCmd = conn.CreateCommand())
        {
            docCmd.CommandText = """
                INSERT INTO documents (id, file_name, content_type, size, created_at)
                VALUES (@id, @fileName, @contentType, @size, @createdAt)
                """;
            docCmd.Parameters.AddWithValue("id", document.Id);
            docCmd.Parameters.AddWithValue("fileName", document.FileName);
            docCmd.Parameters.AddWithValue("contentType", document.ContentType);
            docCmd.Parameters.AddWithValue("size", document.Size);
            docCmd.Parameters.AddWithValue("createdAt", document.CreatedAt);
            await docCmd.ExecuteNonQueryAsync(ct);
        }

        // NOTE: we use INSERT ... @embedding::vector per row instead of binary COPY.
        // Npgsql serializes float[] as a native PostgreSQL array in binary COPY, but pgvector
        // expects its own binary layout (int16 dims + float values). Passing the textual
        // representation "[v1,v2,...]" and casting to vector is the only reliable path here.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunks (id, document_id, content, chunk_index, embedding)
            VALUES (@id, @documentId, @content, @index, @embedding::vector)
            """;

        var pId = cmd.Parameters.Add("id", NpgsqlTypes.NpgsqlDbType.Uuid);
        var pDocumentId = cmd.Parameters.Add("documentId", NpgsqlTypes.NpgsqlDbType.Uuid);
        var pContent = cmd.Parameters.Add("content", NpgsqlTypes.NpgsqlDbType.Text);
        var pIndex = cmd.Parameters.Add("index", NpgsqlTypes.NpgsqlDbType.Integer);
        var pEmbedding = cmd.Parameters.Add("embedding", NpgsqlTypes.NpgsqlDbType.Text);

        foreach (var (chunk, embedding) in chunks)
        {
            pId.Value = chunk.Id;
            pDocumentId.Value = chunk.DocumentId;
            pContent.Value = chunk.Content;
            pIndex.Value = chunk.Index;
            pEmbedding.Value = FormatVector(embedding);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static string FormatVector(ReadOnlyMemory<float> embedding)
    {
        var sb = new System.Text.StringBuilder("[").AppendJoin(",", embedding.ToArray().Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        sb.Append(']');
        return sb.ToString();
    }

    public async Task<IReadOnlyList<SearchResult>> HybridSearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        string queryText,
        int topK = 10,
        CancellationToken ct = default)
    {
        var ds = await GetDataSourceAsync();
        await using var conn = await ds.OpenConnectionAsync(ct);

        const int fetchK = 40; // fetch more for RRF fusion

        // ── Vector search (cosine distance → similarity) ──
        var vectorResults = (await conn.QueryAsync<(Guid Id, double Score)>(new CommandDefinition("""
            SELECT id, 1 - (embedding <=> @queryEmbedding::vector) AS score
            FROM chunks
            ORDER BY embedding <=> @queryEmbedding::vector
            LIMIT @k
            """, new { queryEmbedding = queryEmbedding.ToArray(), k = fetchK }, cancellationToken: ct)))
            .Select((r, i) => (r.Id, r.Score, Rank: i + 1))
            .ToList();

        // ── Keyword search (PostgreSQL full-text) ──
        var keywordResults = (await conn.QueryAsync<(Guid Id, double Score)>(new CommandDefinition("""
            SELECT id, ts_rank(to_tsvector('english', content), websearch_to_tsquery('english', @queryText)) AS score
            FROM chunks
            WHERE to_tsvector('english', content) @@ websearch_to_tsquery('english', @queryText)
            ORDER BY score DESC
            LIMIT @k
            """, new { queryText, k = fetchK }, cancellationToken: ct)))
            .Select((r, i) => (r.Id, r.Score, Rank: i + 1))
            .ToList();

        // ── RRF fusion ──
        var vectorRanks = vectorResults.ToDictionary(r => r.Id, r => r.Rank);
        var keywordRanks = keywordResults.ToDictionary(r => r.Id, r => r.Rank);

        var allIds = vectorRanks.Keys.Union(keywordRanks.Keys);

        var combined = allIds.Select(id => new
        {
            Id = id,
            VectorRrf = vectorRanks.TryGetValue(id, out var vr) ? 1.0 / (RrfK + vr) : 0.0,
            KeywordRrf = keywordRanks.TryGetValue(id, out var kr) ? 1.0 / (RrfK + kr) : 0.0,
        })
        .Select(x => new
        {
            x.Id,
            Score = x.VectorRrf + x.KeywordRrf,
            x.VectorRrf,
            x.KeywordRrf,
        })
        .OrderByDescending(x => x.Score)
        .Take(topK)
        .ToList();

        // ── Load full chunk data ──
        var ids = combined.Select(x => x.Id).ToList();
        var chunks = (await conn.QueryAsync<(Guid Id, Guid DocId, string Content, int Index)>(new CommandDefinition("""
            SELECT id, document_id, content, chunk_index
            FROM chunks
            WHERE id = ANY(@ids)
            """, new { ids }, cancellationToken: ct)))
            .ToDictionary(c => c.Id);

        return combined.Select(x =>
        {
            var hasChunk = chunks.TryGetValue(x.Id, out var chunk);
            return new SearchResult
            {
                Chunk = new DocumentChunk
                {
                    Id = x.Id,
                    DocumentId = hasChunk ? chunk.DocId : Guid.Empty,
                    Content = hasChunk ? chunk.Content : string.Empty,
                    Index = hasChunk ? chunk.Index : 0,
                },
                VectorScore = vectorRanks.TryGetValue(x.Id, out var vr) ? x.VectorRrf : 0.0,
                KeywordScore = keywordRanks.TryGetValue(x.Id, out var kr) ? x.KeywordRrf : 0.0,
                RrfScore = x.Score,
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct = default)
    {
        var ds = await GetDataSourceAsync();
        await using var conn = await ds.OpenConnectionAsync(ct);

        var documents = new List<Document>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, file_name, content_type, size, created_at
            FROM documents
            ORDER BY created_at DESC
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            documents.Add(new Document
            {
                Id = reader.GetGuid(0),
                FileName = reader.GetString(1),
                ContentType = reader.GetString(2),
                Size = reader.GetInt64(3),
                CreatedAt = reader.GetDateTime(4),
            });
        }

        return documents;
    }

    public async Task<bool> DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var ds = await GetDataSourceAsync();
        await using var conn = await ds.OpenConnectionAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE id = @id";
        cmd.Parameters.AddWithValue("id", documentId);
        var affected = await cmd.ExecuteNonQueryAsync(ct);

        // chunks cascade-delete via FK
        return affected > 0;
    }

    // ─────────────── Cleanup ───────────────

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }
}
