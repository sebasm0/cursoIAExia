using System.Text.Json;
using Dapper;
using Npgsql;
using RAG.Domain.Abstractions;
using RAG.Domain.Chat;

namespace RAG.Infrastructure.Chat;

/// <summary>
/// PostgreSQL implementation of <see cref="IChatHistoryStore"/> (spec CH-1/CH-4):
/// mirrors the PgVectorStore bootstrap pattern (design D6) — lazy
/// <c>NpgsqlDataSource</c> with an idempotent <c>EnsureSchemaAsync</c> on first
/// use, no EF migration, no FK to <c>identity."AspNetUsers"</c>. Queries run
/// through Dapper; <c>created_at</c> is authoritative from the database clock
/// via <c>RETURNING</c>; sources persist as camelCase JSONB. No new packages.
/// </summary>
public sealed class PgChatHistoryStore : IChatHistoryStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private NpgsqlDataSource? _dataSource;

    public PgChatHistoryStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    // ─────────────── Setup (design D6: lazy, idempotent bootstrap) ───────────────

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

        // CREATE TABLE/INDEX IF NOT EXISTS keeps the bootstrap idempotent on
        // every first use; no FK to identity."AspNetUsers" — user_id is always
        // claim-derived (CH-1).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS chat_messages (
                    id UUID PRIMARY KEY,
                    user_id UUID NOT NULL,
                    role TEXT NOT NULL CHECK (role IN ('user', 'assistant')),
                    content TEXT NOT NULL,
                    model_id TEXT NULL,
                    sources JSONB NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_chat_messages_user_created
                    ON chat_messages (user_id, created_at);
                """;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────── IChatHistoryStore ───────────────

    public async Task<ChatMessage> AddAsync(ChatMessage message, CancellationToken ct = default)
    {
        var ds = await GetDataSourceAsync();
        await using var conn = await ds.OpenConnectionAsync(ct);

        // The client never supplies created_at: the DB clock (NOW()) is
        // authoritative and comes back via RETURNING (design D9).
        var (id, createdAt) = await conn.QuerySingleAsync<(Guid, DateTime)>(
            new CommandDefinition("""
                INSERT INTO chat_messages (id, user_id, role, content, model_id, sources)
                VALUES (@Id, @UserId, @Role, @Content, @ModelId, @Sources::jsonb)
                RETURNING id, created_at
                """,
                new
                {
                    message.Id,
                    message.UserId,
                    message.Role,
                    message.Content,
                    message.ModelId,
                    Sources = SerializeSources(message.Sources),
                },
                cancellationToken: ct));

        return new ChatMessage
        {
            Id = id,
            UserId = message.UserId,
            Role = message.Role,
            Content = message.Content,
            ModelId = message.ModelId,
            Sources = message.Sources,
            CreatedAt = createdAt,
        };
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        var ds = await GetDataSourceAsync();
        await using var conn = await ds.OpenConnectionAsync(ct);

        // CH-5: the caller's last `limit` messages ascending — DESC LIMIT in a
        // subquery, then the outer query reverses to ASC. The per-user filter in
        // the subquery is the isolation boundary (CH-7).
        var rows = await conn.QueryAsync<(Guid Id, Guid UserId, string Role, string Content, string? ModelId, string? Sources, DateTime CreatedAt)>(
            new CommandDefinition("""
                SELECT id, user_id, role, content, model_id, sources, created_at
                FROM (SELECT id, user_id, role, content, model_id, sources, created_at
                      FROM chat_messages
                      WHERE user_id = @userId
                      ORDER BY created_at DESC
                      LIMIT @limit) recent
                ORDER BY created_at ASC
                """,
                new { userId, limit },
                cancellationToken: ct));

        return rows
            .Select(r => new ChatMessage
            {
                Id = r.Id,
                UserId = r.UserId,
                Role = r.Role,
                Content = r.Content,
                ModelId = r.ModelId,
                Sources = DeserializeSources(r.Sources),
                CreatedAt = r.CreatedAt,
            })
            .ToList();
    }

    // ─────────────── Sources JSONB ───────────────

    // camelCase keys (fileName/snippet/page) match the wire SourceRef shape (CH-4).
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static string SerializeSources(IReadOnlyList<ChatSource> sources)
        => sources.Count == 0 ? "[]" : JsonSerializer.Serialize(sources, WebJsonOptions);

    private static IReadOnlyList<ChatSource> DeserializeSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];

        return JsonSerializer.Deserialize<List<ChatSource>>(json, WebJsonOptions) ?? [];
    }

    // ─────────────── Cleanup ───────────────

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }
}