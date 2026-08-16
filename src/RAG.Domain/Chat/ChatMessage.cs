namespace RAG.Domain.Chat;

/// <summary>
/// A persisted chat message in the per-user history (spec CH-2): role is exactly
/// <c>user</c> or <c>assistant</c>, content is the raw markdown as sent by the
/// client, and <see cref="ModelId"/> is a credit snapshot of the assistant that
/// produced it. <see cref="CreatedAt"/> is authoritative from the database clock
/// (<c>RETURNING created_at</c>), never the client.
///
/// NOTE: the namespace is <c>RAG.Domain.Chat</c> (not <c>RAG.Domain.Entities</c>)
/// because the bare name <c>ChatMessage</c> collides with
/// <c>Microsoft.Extensions.AI.ChatMessage</c> in every file that imports both
/// namespaces (design deviation — see apply-progress).
/// </summary>
public sealed class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }

    /// <summary>Exactly <c>"user"</c> or <c>"assistant"</c> (CH-3, design D10).</summary>
    public required string Role { get; init; }

    /// <summary>Raw markdown content, trimmed and non-empty (CH-3, design D10).</summary>
    public required string Content { get; init; }

    /// <summary>Assistant label snapshot; null for user messages (CH-3).</summary>
    public string? ModelId { get; init; }

    /// <summary>Source fragments that backed an assistant answer; empty when none (CH-3).</summary>
    public IReadOnlyList<ChatSource> Sources { get; init; } = [];

    /// <summary>Database-clock timestamp (<c>NOW()</c> via <c>RETURNING</c>).</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// A source fragment referenced by a persisted assistant message (design D7):
/// the domain counterpart of the application-layer <c>SourceRef</c>, kept here
/// so the store abstraction stays persistence-agnostic. Mapping between the two
/// lives only in <c>ChatHistoryService</c> and the controller.
/// </summary>
public sealed record ChatSource(string? FileName, string Snippet, int? Page);