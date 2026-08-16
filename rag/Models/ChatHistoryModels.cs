using RAG.Application.Services;

namespace rag.Models;

/// <summary>
/// POST /Ask/History request body (spec CH-6, design D9): JSON
/// <c>{role, content, modelId?, sources?}</c> where sources reuse the existing
/// <c>SourceRef</c> wire shape (fileName/snippet/page). <c>user_id</c> is never
/// accepted here — it always comes from the principal's NameIdentifier claim
/// (CH-3).
/// </summary>
public sealed class ChatHistoryRequest
{
    /// <summary>Exactly <c>"user"</c> or <c>"assistant"</c> (validated in ChatHistoryService).</summary>
    public string? Role { get; set; }

    /// <summary>Raw markdown content; validated non-empty and bounded (design D10).</summary>
    public string? Content { get; set; }

    /// <summary>Assistant label credit snapshot; optional, null for user messages.</summary>
    public string? ModelId { get; set; }

    /// <summary>Source fragments that backed an assistant answer; optional.</summary>
    public List<SourceRef>? Sources { get; set; }
}

/// <summary>
/// GET /Ask/History response item (spec CH-5): serialized camelCase by the MVC
/// web defaults — <c>{id, role, content, createdAt, modelId, sources}</c> with
/// sources as <c>[]</c> when absent and modelId as <c>null</c> when absent.
/// </summary>
public sealed record ChatHistoryItem(
    Guid Id,
    string Role,
    string Content,
    DateTime CreatedAt,
    string? ModelId,
    IReadOnlyList<SourceRef> Sources);
