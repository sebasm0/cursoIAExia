# Design: chat-history-persistente — persistent per-user chat history + markdown/timestamps/contrast

## Technical Approach

Additive Clean Architecture slice: Domain (`ChatMessage`, `ChatSource`, `IChatHistoryStore`) → Application (`ChatHistoryService` validation+mapping) → Infrastructure (`PgChatHistoryStore`, Dapper/Npgsql, lazy idempotent bootstrap per D6) → MVC (`AskController` GET/POST `/Ask/History`, presentation DTOs). The floating chat persists client-side (save on send + on SSE `done`, never from AskStream), re-renders history on open, and renders sanitized markdown per surface (server Markdig+Ganss.Xss on Ask views; client marked+DOMPurify in the floating chat, D5). Timestamps on all three surfaces. Covers CH-1..8, ASK-16/17, UPLOAD-14..18, UDS-9..11. `RAG.Api`, the RAG pipeline, AI providers and the `identity` schema stay untouched (CH-8).

## Architecture Decisions

### D6: Store bootstrap timing — lazy on first use (PgVectorStore pattern)

| Option | Tradeoff | Decision |
|---|---|---|
| **Lazy `GetDataSourceAsync()` → `EnsureSchemaAsync` once, cached** (exact PgVectorStore shape: `NpgsqlDataSource.Create` + `CREATE TABLE IF NOT EXISTS`/`CREATE INDEX IF NOT EXISTS`) | Bootstrap deferred to first chat use; idempotent on every startup; no startup coupling; matches existing pattern exactly | **Chosen** |
| Eager schema at app startup (scope in Program.cs) | Diverges from PgVectorStore; adds startup DB dependency to every host | Rejected |

`PgChatHistoryStore` mirrors `PgVectorStore` line-for-line: ctor takes connection string, lazy `ValueTask<NpgsqlDataSource>`, `IAsyncDisposable`, `using Dapper` + raw Npgsql commands for schema, Dapper for queries. Registered `AddSingleton<IChatHistoryStore>(_ => new PgChatHistoryStore(connectionString))` in `src/RAG.Infrastructure/DependencyInjection.cs`; both hosts resolve it through the same DI files (RAG.Api never activates it — lazy).

### D7: Sources typing — Domain `ChatSource`, wire stays `SourceRef`

| Option | Tradeoff | Decision |
|---|---|---|
| **Domain `ChatSource(FileName, Snippet, Page)` + explicit mapping in `ChatHistoryService`** | One duplicated record; clean layering; CH-2 "domain abstraction" satisfied; wire shape unchanged | **Chosen** |
| Move `SourceRef` from Application → Domain | Refactor touches RagService/controllers/tests — scope creep, CH-8 risk | Rejected |
| Store interface in Application | Violates CH-2 (Domain abstraction) | Rejected |

The controller request/response DTOs reuse the existing `SourceRef` (already the wire shape of the AskStream `done` event: `fileName/snippet/page` camelCase). Mapping `ChatSource ↔ SourceRef` lives only in `ChatHistoryService`/controller.

### D8: Server markdown pipeline lives in presentation layer

| Option | Tradeoff | Decision |
|---|---|---|
| **`rag/Services/MarkdownRenderer.cs` (static) + packages in `rag/rag.csproj`** | HTML output is a presentation concern; Application stays lean (only M.E.AI.Abstractions + DI); API host renders no HTML; unit-testable (InternalsVisibleTo) | **Chosen** |
| `MarkdownRenderer` service in Application | Drags Markdig/Ganss.Xss into Application for one MVC surface | Rejected |

Packages (verified 2026-08-16): **Markdig 1.3.2** (net10.0-compatible) and **HtmlSanitizer (Ganss.Xss) 9.2.995** — both stable; apply re-verifies latest on NuGet before pinning. Pipeline: `Sanitizer.Sanitize(Markdown.ToHtml(markdown))`. Explicit allow-list coherent with client: tags `p, br, strong, em, del, ul, ol, li, h1..h6, pre, code, blockquote, a, hr, table, thead, tbody, tr, th, td`; attrs `href, title`; schemes `http, https, mailto`. `img`, `on*`, `javascript:` dropped. Views inject via `@Html.Raw(MarkdownRenderer.ToSanitizedHtml(Model.Answer))` (add `@using rag.Services` to `_ViewImports`); query echo/error stay Razor-encoded plain text (ASK-16).

### D9: History API shape — JSON body + antiforgery header

| Option | Tradeoff | Decision |
|---|---|---|
| **POST `[FromBody]` JSON + `RequestVerificationToken` header** (ValidateAntiForgeryToken accepts the header; 400 before validation) | Sources array clean in JSON; header token trivially read from the form's hidden input | **Chosen** |
| Form-encoded POST (sources as fields) | Arrays awkward in form encoding | Rejected |
| Client-supplied `createdAt` | DB clock is authoritative | Rejected |

`createdAt` comes from SQL `RETURNING id, created_at` (DB `NOW()`), never the client. GET is a plain authenticated GET (no antiforgery). 201 `{id, createdAt}` / 400 `{error}` / 401 when the NameIdentifier claim is not a Guid.

### D10: Content bound — `ChatHistoryService.MaxContentLength = 8000`

Spec CH-3 leaves the default to design. Constant in Application; role must be exactly `user|assistant`; content trimmed, non-empty, ≤ 8000 chars; `sources` null/empty → `[]`; `modelId` stored as sent (credit snapshot, trimmed, null→null).

### D11: Timestamps

Ask views: server render-time clock (stateless flow), `@DateTime.Now.ToString("HH:mm")` in each bubble (ASK-17; none in empty/error states). Floating chat: client local clock `HH:mm` at bubble creation; history bubbles re-render from `createdAt` — relative `"hace N min"` / `"hace N h"` when < 24 h, else `"d MMM, HH:mm"` (`toLocaleDateString('es')`) (UPLOAD-18).

### D12: Client markdown vendored locally

`marked` + `DOMPurify` vendored under `rag/wwwroot/lib/marked/` + `rag/wwwroot/lib/dompurify/` with `LICENSE` (existing lib convention; no CDN — local/offline app). Exact JS versions pinned at apply (latest stable). Loaded globally in `_Layout.cshtml` before `site.js`. `renderMarkdown(text) = DOMPurify.sanitize(marked.parse(text), { ALLOWED_TAGS: <same as server>, ALLOWED_ATTR: ['href','title'] })`. Live bubbles keep plain `textContent` accumulation during streaming and re-render **once** on `done` via `bubble.innerHTML = renderMarkdown(acc)` — the only sanctioned innerHTML; site.js posture comment updates to "innerHTML only with DOMPurify-sanitized output".

## Data Flow

```
Documents open ──► loadChatHistory() ──GET──► AskController.History ──► ChatHistoryService.GetRecentAsync ──► IChatHistoryStore ──► PG (WHERE user_id ORDER BY created_at DESC LIMIT 50, reversed ASC)
submit ──► AskStream (SSE untouched, CH-8) + saveMessage('user', q) ──POST JSON+token──► History ──► AddAsync(validate) ──► Store.AddAsync ──► INSERT ... RETURNING id, created_at
done ──► answerBubble.innerHTML = renderMarkdown(acc); credit + chips; saveMessage('assistant', acc, usedModel, sources)   (no done ⇒ no assistant row)
```

## File Changes

| File | Action | Est. |
|---|---|---|
| `src/RAG.Domain/Entities/ChatMessage.cs` | Create — entity + `ChatSource` record | 26 |
| `src/RAG.Domain/Abstractions/IChatHistoryStore.cs` | Create | 8 |
| `src/RAG.Application/Services/ChatHistoryService.cs` | Create — validation, mapping, limit, `ChatHistoryAddResult` | 75 |
| `src/RAG.Application/DependencyInjection.cs` | Modify — `AddSingleton<ChatHistoryService>` | +1 |
| `src/RAG.Infrastructure/Chat/PgChatHistoryStore.cs` | Create — bootstrap + Add/GetRecent (D6) | 120 |
| `src/RAG.Infrastructure/DependencyInjection.cs` | Modify — register `IChatHistoryStore` | +3 |
| `rag/Controllers/AskController.cs` | Modify — `History` GET/POST, `UserIdFromPrincipal`, ctor | +60 |
| `rag/Models/ChatHistoryModels.cs` | Create — `ChatHistoryRequest` + `ChatHistoryItem` | 26 |
| `rag/Services/MarkdownRenderer.cs` | Create — pipeline + allow-list (D8) | 50 |
| `rag/rag.csproj` | Modify — Markdig 1.3.2 + HtmlSanitizer 9.2.995 | +2 |
| `rag/Views/Ask/Index.cshtml` | Modify — sanitized markdown bubble + `HH:mm` timestamps | +14 |
| `rag/Views/Ask/Result.cshtml` | Modify — same (replaces `<pre>`) | +12 |
| `rag/Views/Documents/Index.cshtml` | Modify — `data-history-url` on chat form | +1 |
| `rag/Views/Home/Privacy.cshtml` | Modify — `text-white` → `color: var(--rag-text-primary)` (4×h6 + 4×strong) | 8 |
| `rag/Views/Shared/_Layout.cshtml` | Modify — marked + dompurify script tags before site.js | +2 |
| `rag/Views/_ViewImports.cshtml` | Modify — `@using rag.Services` | +1 |
| `rag/wwwroot/js/site.js` | Modify — `loadChatHistory`, `saveMessage`, `renderMarkdown`, `formatClock`, `formatRelativeTime`, done-render | +110 |
| `rag/wwwroot/css/site.css` | Modify — L1244 username → `var(--rag-text-primary)`; L1385 `grayscale(100%)` | 2 |
| `rag/wwwroot/lib/{marked,dompurify}/…` | Add — vendored, LICENSE (excluded from authored count) | — |
| **Tests** | See Testing Strategy | ~700 |

Prod ≈ 520 authored (excl. vendored); tests ≈ 700; **total ≈ 1200**.

## Interfaces / Contracts

```csharp
// Domain
public interface IChatHistoryStore {
    Task<ChatMessage> AddAsync(ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid userId, int limit, CancellationToken ct = default);
}
public sealed class ChatMessage {
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public required string Role { get; init; }          // "user" | "assistant"
    public required string Content { get; init; }
    public string? ModelId { get; init; }
    public IReadOnlyList<ChatSource> Sources { get; init; } = [];
    public DateTime CreatedAt { get; init; }            // DB NOW() via RETURNING
}
public sealed record ChatSource(string? FileName, string Snippet, int? Page);

// Application
public sealed record ChatHistoryAddResult(bool IsValid, ChatMessage? Message, string? ErrorMessage);
public class ChatHistoryService(IChatHistoryStore store) {
    public const int MaxContentLength = 8000;           // D10
    public Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid userId, CancellationToken ct = default); // limit 50
    public Task<ChatHistoryAddResult> AddAsync(Guid userId, string? role, string? content,
        string? modelId, IReadOnlyList<SourceRef>? sources, CancellationToken ct = default);
}

// rag (presentation)
public sealed class ChatHistoryRequest {
    public string? Role { get; set; }
    public string? Content { get; set; }
    public string? ModelId { get; set; }
    public List<SourceRef>? Sources { get; set; }
}
public sealed record ChatHistoryItem(Guid Id, string Role, string Content, DateTime CreatedAt,
    string? ModelId, IReadOnlyList<SourceRef> Sources);   // serializes camelCase via MVC web defaults

// AskController
[HttpGet]  public async Task<IActionResult> History(CancellationToken ct);                                   // 200 [{id,role,content,createdAt,modelId,sources}]
[HttpPost] [ValidateAntiForgeryToken] public async Task<IActionResult> History([FromBody] ChatHistoryRequest r, CancellationToken ct); // 201 {id,createdAt} | 400 {error} | 401
private Guid? UserIdFromPrincipal() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
```

```sql
-- PgChatHistoryStore.EnsureSchemaAsync (lazy, first use — D6)
CREATE TABLE IF NOT EXISTS chat_messages (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    role TEXT NOT NULL CHECK (role IN ('user', 'assistant')),
    content TEXT NOT NULL,
    model_id TEXT NULL,
    sources JSONB NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_chat_messages_user_created ON chat_messages (user_id, created_at);

-- GetRecentAsync (50 latest ascending; sources read as TEXT then deserialized to ChatSource[] with JsonSerializerDefaults.Web)
SELECT id, user_id, role, content, model_id, sources, created_at
FROM (SELECT id, user_id, role, content, model_id, sources, created_at
      FROM chat_messages WHERE user_id = @userId ORDER BY created_at DESC LIMIT @limit) recent
ORDER BY created_at ASC;
```

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit | `ChatHistoryServiceTests.cs` — valid user/assistant accepted w/ claim-derived userId+trimmed content; `system`/empty/oversize rejected, nothing persisted; sources null→`[]`; modelId stored as sent; mock records exact args (CH-2/3) | Moq `IChatHistoryStore` |
| Unit | `MarkdownRendererTests.cs` — `**bold**`→`<strong>`, list, fenced code→`<pre><code>`; `<script>`, `onclick=`, `javascript:` href neutralized; plain passthrough (ASK-16) | Direct static call |
| Integration | `ChatHistoryControllerTests.cs` — GET 200 empty `[]`; POST valid→201 `{id,createdAt}` then GET returns it; POST invalid role/empty→400 store untouched; POST no token→400 (CH-5/6); **two-user isolation** via two factories sharing one `InMemoryChatHistoryStore` with distinct GUID `userId` claims (CH-7) | WAF + `InMemoryChatHistoryStore` fake; `AddPolicyTestAuthentication` gains optional `userId`/`userName` |
| Integration | `PgChatHistoryStoreRoundTripTests.cs` — gated `RAG_TEST_PG_CONNECTION_STRING` (SkippableFact, PgVectorStoreByteRoundTrip pattern): bootstrap idempotency (CH-1), sources camelCase JSONB round-trip (CH-4), limit/ascending order + per-user SQL filter (CH-5/7) | Real PG, throwaway DB |
| View | `AskViewRenderTests.cs` (+45) — Result renders `<strong>`/`<pre><code>` and `\d{2}:\d{2}` timestamps; Index empty state has none (ASK-16/17); `DocumentsViewRenderTests.cs` (+12) — `data-history-url` present (UPLOAD-14); `HomePrivacyTests.cs` (+30) — Privacy headings/labels carry `var(--rag-text-primary)` (UDS-9) | WAF view render |

Baselines: MVC 209/1/210 and API 10/10 stay green; AskStream tests untouched (CH-8).

## Threat Matrix

N/A — no routing (in the git/shell sense), shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary. New endpoints are MVC actions already gated by policy (`[Authorize(Policy = Permissions.RagAsk)]`) + per-action antiforgery, the repo's standard web posture (same rationale as multi-assistant design).

## Migration / Rollout

No data migration; additive table + lazy idempotent bootstrap. Rollback: `DROP TABLE IF EXISTS chat_messages`, revert actions/DI/data-attr/JS/CSS. Slices (chained PRs, per proposal "High risk"): **Slice 1 — backend historial** (Domain→Application→Infrastructure→DI→controller+DTOs, service/controller/gated tests) ≈ 520 — endpoints dormant until JS lands; **Slice 2 — frontend persistencia + markdown + timestamps** (vendored libs, `_Layout`, site.js load/save/render/timestamps, `data-history-url`, MarkdownRenderer + packages, Ask views, renderer/view tests) ≈ 560 — app behavior unchanged until both slices land; **Slice 3 — visual UDS-9..11** (Privacy, site.css, HomePrivacyTests) ≈ 45. Each slice: autonomous, verifiable, reversible.

## Open Questions

- [ ] Exact versions of vendored `marked`/`DOMPurify` — pin latest stable at apply (verify with context7/NuGet feed; no repo precedent for these two).
- [ ] Re-verify `HtmlSanitizer` 9.2.995 and `Markdig` 1.3.2 at apply against the live NuGet feed before pinning.
- [ ] Final Spanish copy for relative time fallback (>24 h: `"16 ago, 14:32"` proposed).