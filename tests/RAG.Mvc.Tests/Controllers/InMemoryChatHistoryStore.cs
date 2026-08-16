using RAG.Domain.Abstractions;
using RAG.Domain.Chat;

namespace RAG.Mvc.Tests.Controllers;

/// <summary>
/// In-memory <see cref="IChatHistoryStore"/> fake used by the chat history
/// controller integration tests (spec CH-7): one shared instance across two
/// factories proves per-user isolation against real cross-factory state.
/// Mirrors <c>PgChatHistoryStore</c> semantics — CreatedAt from a monotonic
/// clock (DB-clock stand-in), last N ascending by <c>created_at</c> — and is
/// thread-safe because the two WAF clients can hit the same instance.
/// </summary>
public sealed class InMemoryChatHistoryStore : IChatHistoryStore
{
    private readonly object _gate = new();
    private readonly List<ChatMessage> _messages = [];
    private long _sequence;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    public Task<ChatMessage> AddAsync(ChatMessage message, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var stored = new ChatMessage
            {
                Id = message.Id,
                UserId = message.UserId,
                Role = message.Role,
                Content = message.Content,
                ModelId = message.ModelId,
                Sources = message.Sources,
                CreatedAt = DateTime.UtcNow.AddTicks(Interlocked.Increment(ref _sequence)),
            };
            _messages.Add(stored);
            return Task.FromResult(stored);
        }
    }

    public Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var recent = _messages
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .OrderBy(m => m.CreatedAt)
                .ToList();
            return Task.FromResult<IReadOnlyList<ChatMessage>>(recent);
        }
    }
}