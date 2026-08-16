using RAG.Domain.Chat;

namespace RAG.Domain.Abstractions;

/// <summary>
/// Persistence abstraction for the per-user chat history (spec CH-2):
/// persistence-agnostic — no Npgsql/Dapper types — and mockable for unit tests.
/// <see cref="GetRecentAsync"/> returns messages ordered ascending by
/// <c>created_at</c>, capped at <paramref name="limit"/>.
/// </summary>
public interface IChatHistoryStore
{
    /// <summary>Persists a message and returns it with the authoritative id and DB clock timestamp.</summary>
    Task<ChatMessage> AddAsync(ChatMessage message, CancellationToken ct = default);

    /// <summary>Returns the caller's last <paramref name="limit"/> messages ascending by <c>created_at</c>.</summary>
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid userId, int limit, CancellationToken ct = default);
}