using Dapper;
using Npgsql;
using RAG.Domain.Chat;
using RAG.Infrastructure.Chat;
using Xunit;

namespace RAG.Mvc.Tests.Integration;

/// <summary>
/// Gated integration tests for <see cref="PgChatHistoryStore"/> against a REAL
/// PostgreSQL database (spec CH-1/CH-4/CH-5/CH-7), following the
/// <c>PgVectorStoreByteRoundTripTests</c> pattern: every test is gated so the
/// default <c>dotnet test</c> suite stays green in CI without the database —
/// they report SKIPPED (never PASSED) unless <c>RAG_TEST_PG_CONNECTION_STRING</c>
/// points at a dedicated throwaway test database. Inserts are cleaned up in
/// <c>finally</c> so a rerun never accumulates rows.
/// </summary>
public class PgChatHistoryStoreRoundTripTests
{
    public const string ConnectionStringEnvVar = "RAG_TEST_PG_CONNECTION_STRING";

    private static string ConnectionStringOrSkip()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"{ConnectionStringEnvVar} is not set. Set it to a dedicated (throwaway) PostgreSQL "
            + "connection string to run this integration test.");
        return connectionString!;
    }

    private static ChatMessage NewMessage(Guid userId, string role, string content,
        IReadOnlyList<ChatSource>? sources = null, string? modelId = null)
        => new()
        {
            UserId = userId,
            Role = role,
            Content = content,
            ModelId = modelId,
            Sources = sources ?? [],
        };

    private static async Task CleanupAsync(string connectionString, IEnumerable<Guid> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.ExecuteAsync("DELETE FROM chat_messages WHERE id = ANY(@ids)", new { ids = idList });
    }

    // ── CH-1: bootstrap is idempotent across store instances ──

    [SkippableFact(DisplayName = "Chat history bootstrap is idempotent across store instances (CH-1)")]
    public async Task Bootstrap_SecondStoreInstance_DoesNotFail()
    {
        var connectionString = ConnectionStringOrSkip();
        var userId = Guid.NewGuid();
        var inserted = new List<Guid>();

        await using (var store = new PgChatHistoryStore(connectionString))
        {
            var first = await store.AddAsync(NewMessage(userId, "user", "Hola"));
            inserted.Add(first.Id);
        }

        try
        {
            // A second store re-runs EnsureSchemaAsync on the same table — the
            // CREATE TABLE/INDEX IF NOT EXISTS must be a no-op (CH-1).
            await using var second = new PgChatHistoryStore(connectionString);
            var secondAdd = await second.AddAsync(NewMessage(userId, "user", "Segundo"));
            inserted.Add(secondAdd.Id);
        }
        finally
        {
            await CleanupAsync(connectionString, inserted);
        }
    }

    // ── CH-4: sources round-trip as camelCase JSONB ──

    [SkippableFact(DisplayName = "Sources round-trip as camelCase JSONB (CH-4)")]
    public async Task Sources_RoundTripAsCamelCaseJsonb()
    {
        var connectionString = ConnectionStringOrSkip();
        var userId = Guid.NewGuid();
        var sources = new List<ChatSource>
        {
            new("francia.pdf", "Paris es la capital de Francia.", 3),
            new(null, "Fragmento sin archivo.", null),
        };
        var inserted = new List<Guid>();

        await using var store = new PgChatHistoryStore(connectionString);
        try
        {
            var stored = await store.AddAsync(
                NewMessage(userId, "assistant", "Respuesta.", sources, modelId: "phi3:mini"));
            inserted.Add(stored.Id);

            var history = await store.GetRecentAsync(userId, 50);
            var message = Assert.Single(history);

            Assert.Equal("assistant", message.Role);
            Assert.Equal("Respuesta.", message.Content);
            Assert.Equal("phi3:mini", message.ModelId);
            Assert.Equal(2, message.Sources.Count);
            Assert.Equal(new ChatSource("francia.pdf", "Paris es la capital de Francia.", 3), message.Sources[0]);
            Assert.Equal(new ChatSource(null, "Fragmento sin archivo.", null), message.Sources[1]);
        }
        finally
        {
            await CleanupAsync(connectionString, inserted);
        }
    }

    // ── CH-5/CH-7: last 50 ascending, filtered per user ──

    [SkippableFact(DisplayName = "GetRecentAsync returns the last 50 ascending, per user (CH-5/CH-7)")]
    public async Task GetRecent_ReturnsLast50Ascending_PerUser()
    {
        var connectionString = ConnectionStringOrSkip();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var inserted = new List<Guid>();

        await using var store = new PgChatHistoryStore(connectionString);
        try
        {
            // User A: 60 messages; User B: 5 — interleaved so the ordering and
            // the per-user filter are proven against real DB clock timestamps.
            for (var i = 1; i <= 60; i++)
            {
                inserted.Add((await store.AddAsync(NewMessage(userA, "user", $"A-{i:D3}"))).Id);
                if (i <= 5)
                {
                    inserted.Add((await store.AddAsync(NewMessage(userB, "user", $"B-{i:D3}"))).Id);
                }
            }

            var historyA = await store.GetRecentAsync(userA, 50);
            Assert.Equal(50, historyA.Count);
            // Ascending by created_at: the 50 latest are A-011..A-060.
            Assert.Equal("A-011", historyA[0].Content);
            Assert.Equal("A-060", historyA[^1].Content);

            // User B never sees A's rows (CH-7).
            var historyB = await store.GetRecentAsync(userB, 50);
            Assert.Equal(5, historyB.Count);
            Assert.All(historyB, m => Assert.Equal(userB, m.UserId));
        }
        finally
        {
            await CleanupAsync(connectionString, inserted);
        }
    }
}