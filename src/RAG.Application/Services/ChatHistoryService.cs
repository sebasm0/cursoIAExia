using RAG.Domain.Abstractions;
using RAG.Domain.Chat;

namespace RAG.Application.Services;

/// <summary>
/// Application service for the per-user chat history (spec CH-3, design D7/D10):
/// validates role and content before persisting, always derives <c>user_id</c>
/// from the caller (never from a request body), normalizes sources to an empty
/// list, snapshots the model credit, and maps wire <c>SourceRef</c> ↔ domain
/// <c>ChatSource</c>. Reads default to the 50 most recent messages (CH-5).
/// </summary>
public class ChatHistoryService(IChatHistoryStore store)
{
    /// <summary>Maximum accepted content length (design D10); longer content is rejected.</summary>
    public const int MaxContentLength = 8000;

    /// <summary>Default history window: the 50 most recent messages (spec CH-5).</summary>
    public const int DefaultLimit = 50;

    /// <summary>Returns the caller's last 50 messages ascending by <c>created_at</c>.</summary>
    public Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid userId, CancellationToken ct = default)
        => store.GetRecentAsync(userId, DefaultLimit, ct);

    /// <summary>
    /// Validates and persists a chat message (CH-3): role must be exactly
    /// <c>user</c>|<c>assistant</c>, content non-empty after trimming and bounded
    /// by <see cref="MaxContentLength"/>. On success the result carries the
    /// persisted message; on validation failure nothing is persisted and the
    /// result carries a user-facing error message (Spanish UI copy).
    /// </summary>
    public async Task<ChatHistoryAddResult> AddAsync(
        Guid userId,
        string? role,
        string? content,
        string? modelId,
        IReadOnlyList<SourceRef>? sources,
        CancellationToken ct = default)
    {
        // D10: role must be exactly user|assistant — no trimming, no aliases.
        if (role is not ("user" or "assistant"))
        {
            return new ChatHistoryAddResult(false, null, "El rol debe ser 'user' o 'assistant'.");
        }

        var trimmed = content?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new ChatHistoryAddResult(false, null, "El contenido no puede estar vacío.");
        }

        if (trimmed.Length > MaxContentLength)
        {
            return new ChatHistoryAddResult(
                false, null, $"El contenido supera el máximo de {MaxContentLength} caracteres.");
        }

        var message = new ChatMessage
        {
            UserId = userId,
            Role = role,
            Content = trimmed,
            ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim(),
            Sources = (sources ?? [])
                .Select(s => new ChatSource(s.FileName, s.Snippet, s.Page))
                .ToList(),
        };

        var stored = await store.AddAsync(message, ct);
        return new ChatHistoryAddResult(true, stored, null);
    }
}

/// <summary>
/// Result of a <see cref="ChatHistoryService.AddAsync"/> attempt (spec CH-6):
/// valid adds carry the persisted message; validation failures carry an error
/// message and persist nothing.
/// </summary>
public sealed record ChatHistoryAddResult(bool IsValid, ChatMessage? Message, string? ErrorMessage);
