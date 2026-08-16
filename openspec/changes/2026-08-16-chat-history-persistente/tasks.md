# Tasks: chat-history-persistente — per-user chat history + markdown/timestamps/contrast

## Review Workload Forecast

Est. changed lines: ~1200 (S1≈520, S2≈560, S3≈45; vendored marked/DOMPurify excluded). Delivery strategy: ask-on-risk.

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending (choose stacked-to-main or feature-branch-chain before apply)
400-line budget risk: High

| Unit | PR | Est | Focused test (runner + filter) | Runtime harness | Rollback |
|------|----|-----|--------------------------------|-----------------|-------------------|
| 1 Backend history | 1 | ~520 | `--filter "FullyQualifiedName~ChatHistory"` | Gated PG round-trip via `RAG_TEST_PG_CONNECTION_STRING`; HTTP proven by WAF tests, endpoints dormant until S2 | Revert Domain/App/Infra Chat files + DI + AskController History + DTOs; `DROP TABLE chat_messages` |
| 2 Frontend | 2 | ~560 | `--filter "FullyQualifiedName~MarkdownRenderer\|AskViewRender\|DocumentsViewRender"` | `dotnet run --project rag/` → reload keeps chat; markdown + timestamps render | Revert site.js, Ask/Documents views, _Layout, _ViewImports, rag.csproj; delete libs + MarkdownRenderer |
| 3 Visual | 3 | ~45 | `--filter "FullyQualifiedName~HomePrivacy"` | `dotnet run --project rag/` → Privacy readable light/dark; avatar desaturated | Revert Privacy.cshtml + site.css 2 lines |

Runner: `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj -p:UseAppHost=false "-p:OutputPath=bin\Debug-tdd\"`
Deps: S1→S2 (JS needs the endpoint); S3 independent. RED before production (strict_tdd). Threat matrix: N/A — no cases to port.

## Slice 1 — Backend history (T1–T12)

- [x] T1 [RED] `tests/RAG.Mvc.Tests/Application/ChatHistoryServiceTests.cs` — valid user/assistant persisted w/ claim userId + trimmed content; `system`/empty/oversize rejected, store untouched; sources null→`[]`; modelId as-sent; mock records exact args (CH-2/3, ~40)
- [x] T2 Create `src/RAG.Domain/Entities/ChatMessage.cs` — `ChatMessage` entity + `ChatSource` record (CH-2, ~26)  (NOTE: landed as `src/RAG.Domain/Chat/ChatMessage.cs`, namespace RAG.Domain.Chat — see apply-progress)
- [x] T3 Create `src/RAG.Domain/Abstractions/IChatHistoryStore.cs` — `AddAsync`/`GetRecentAsync` (CH-2, ~8)
- [x] T4 Create `src/RAG.Application/Services/ChatHistoryService.cs` — role guard, trim, `MaxContentLength=8000`, sources normalize, limit 50, `ChatHistoryAddResult` (CH-3, ~75)
- [x] T5 Modify `src/RAG.Application/DependencyInjection.cs` — register `ChatHistoryService` (CH-3, +1)
- [x] T6 Create `src/RAG.Infrastructure/Chat/PgChatHistoryStore.cs` — lazy `EnsureSchemaAsync` (CREATE TABLE/INDEX IF NOT EXISTS, no FK), Dapper INSERT `RETURNING id, created_at`, GET subquery DESC LIMIT reversed ASC, sources JSONB camelCase (CH-1/4/5, ~120)
- [x] T7 Modify `src/RAG.Infrastructure/DependencyInjection.cs` — `AddSingleton<IChatHistoryStore>` from connection string (CH-4, +3)
- [x] T8 [RED] `ChatHistoryControllerTests.cs` + `InMemoryChatHistoryStore` + test-auth overload (optional userId/userName) — GET 200 `[]`; POST valid→201 `{id,createdAt}` then GET returns it; invalid role/empty→400 store untouched; no token→400; two-user isolation via 2 factories, 1 shared fake store (CH-5/6/7, ~260)
- [x] T9 Create `rag/Models/ChatHistoryModels.cs` — `ChatHistoryRequest` + `ChatHistoryItem` (CH-5/6, ~26)
- [x] T10 Modify `rag/Controllers/AskController.cs` — `History` GET/POST `[ValidateAntiForgeryToken]`, `UserIdFromPrincipal` (Guid.TryParse, 401 when null), 201/400 JSON (CH-5/6/7, +60)
- [x] T11 [RED+gated] `PgChatHistoryStoreRoundTripTests.cs` — SkippableFact on `RAG_TEST_PG_CONNECTION_STRING` (PgVectorStoreByteRoundTrip pattern): bootstrap idempotency, sources round-trip, limit/ascending + per-user filter (CH-1/4/5/7, ~90)
- [x] T12 [GREEN] Slice filters + full suite: MVC 235/4/239 intact (baseline 209/1/210 + 26 passed + 3 gated skipped), API 10/10, build 0 errors; AskStream tests untouched (CH-8)

## Slice 2 — Frontend persistence + markdown + timestamps (T13–T21)

- T13 Modify `rag/rag.csproj` — Markdig 1.3.2 + HtmlSanitizer (Ganss.Xss) 9.2.995; re-verify on NuGet before pinning (ASK-16, +2)
- T14 Create `rag/Services/MarkdownRenderer.cs` — `ToSanitizedHtml` = sanitizer(Markdig HTML), D8 allow-list tags/attrs/schemes (ASK-16, ~50)
- T15 [RED] `MarkdownRendererTests.cs` — bold→`<strong>`, list, fenced code→`<pre><code>`; `<script>`/`onclick=`/`javascript:` neutralized; plain passthrough (ASK-16, ~60)
- T16 Vendor `marked`+`DOMPurify` into `rag/wwwroot/lib/{marked,dompurify}/` + LICENSE; add script tags to `_Layout.cshtml` before site.js (UPLOAD-17, ~2)
- T17 Modify `rag/wwwroot/js/site.js` — `loadChatHistory` (open), `saveMessage` (send + done; no done→no assistant row), `renderMarkdown` = DOMPurify(marked) render-on-done only, `formatClock`, `formatRelativeTime` ("hace N min/h", else `d MMM, HH:mm`); posture comment → sanitized-only innerHTML (UPLOAD-14..18, ~110)
- T18 Modify `rag/Views/Documents/Index.cshtml` — `data-history-url` attr on chat form (UPLOAD-14, +1)
- T19 Modify `rag/Views/Ask/Index.cshtml` + `Result.cshtml` — `@Html.Raw(MarkdownRenderer.ToSanitizedHtml(...))`, `HH:mm` per bubble; `_ViewImports.cshtml` `@using rag.Services` (ASK-16/17, ~27)
- T20 [RED] `AskViewRenderTests.cs` +45 — Result renders `<strong>`/`<pre><code>` + `\d{2}:\d{2}`; Index empty state none; `DocumentsViewRenderTests.cs` +12 — `data-history-url` present (ASK-16/17, UPLOAD-14, ~57)
- T21 [GREEN] Slice filters + full suite green; AskStream tests unchanged (CH-8)

## Slice 3 — Visual UDS-9..11 (T22–T25)

- T22 [RED] `HomePrivacyTests.cs` +30 — Privacy headings/labels resolve `var(--rag-text-primary)`; avatar `grayscale(100%)` (UDS-9/10/11, ~30)
- T23 Modify `rag/Views/Home/Privacy.cshtml` — 4×h6 + 4×strong `text-white` → `var(--rag-text-primary)` (UDS-9, ~8)
- T24 Modify `rag/wwwroot/css/site.css` — L1244 username → `var(--rag-text-primary)`; L1385 `grayscale(1%)`→`grayscale(100%)` (UDS-10/11, ~2)
- T25 [GREEN] Full suite green — MVC 209/1/210 + new totals; AskStream untouched (CH-8)
