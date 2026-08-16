# Proposal: chat-history-persistente — persistent per-user chat history for Documents

## Intent

The Documents chat is 100% in-memory DOM: a reload loses everything. Persist per-user history (approach B) so messages survive reload with faithful credit/source re-render. Untouched: RAG pipeline, RAG.Api, AI providers, identity schema.

## Scope

### In Scope
- `ChatMessage` + `IChatHistoryStore` (Domain); `ChatHistoryService` (Application); `PgChatHistoryStore`, idempotent bootstrap (Infrastructure/Chat/); DI in both DI files
- `GET`/`POST /Ask/History` in `AskController.cs` (repo pattern; class policy + antiforgery; UserId from `ClaimTypes.NameIdentifier`)
- `data-history-url` attr; load-on-open + save-on-send/done in `site.js`
- Tests: service unit; controller integration (isolation, antiforgery); gated real-DB store test

### Out of Scope
Multi-turn context (future change); shared history; edit/delete; pagination beyond LIMIT 50; Ask* contracts unchanged.

## Capabilities

### New Capabilities
- `chat-history`: `public.chat_messages` table, Dapper store, service, GET/POST `/Ask/History`.

### Modified Capabilities
- `mvc-document-upload`: panel loads on open, saves each message, renders markdown, shows timestamps.
- `mvc-rag-ask`: ~~None~~ — approved visual scope (2026-08-16) adds sanitized markdown rendering + timestamps on Ask/Index & Ask/Result; AskStream itself unchanged (CH-8).
- `ui-design-system`: contrast conformance fixes (Privacy tokens, sidebar username token, avatar grayscale typo).

## Approach

`public.chat_messages`: `id UUID PK, user_id UUID NOT NULL, role TEXT NOT NULL, content TEXT NOT NULL, model_id TEXT NULL, sources JSONB NULL, created_at TIMESTAMPTZ DEFAULT NOW()`, index `(user_id, created_at)`; idempotent bootstrap, no EF migration (D3).

## Data Contract (fixed)

| Endpoint | Request | Response |
|---|---|---|
| `GET /Ask/History` | — | `200 [{id, role, content, createdAt, modelId, sources}]`, 50 latest asc |
| `POST /Ask/History` | `{role, content, modelId?, sources?}` | `201 {id, createdAt}` |

- **No FK**: cross-schema FK couples EF/Dapper schemas (D3), makes bootstrap order-dependent; user_id is claim-derived.
- **Client-side save**: AskStream stays store-free; truncated stream leaves no answer, error bubble already rendered.
- **`modelId` = assistant label** (credit snapshot; client can't resolve ids→labels).
- `sources` = done-event shape `[{fileName, snippet, page}]`; role/content validated -> 400.

## Test Impact

New: `ChatHistoryServiceTests`; `ChatHistoryControllerTests` (status codes, antiforgery, two-user isolation); gated `PgChatHistoryStore` round-trip. AskStream untouched.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Cross-schema bootstrap failure | Low | No FK |
| Isolation regression | Med | Two-user integration test |
| JS regression | Med | View render tests + manual check |
| No real DB in CI | Low | Gated test; store mocked |

## Rollback

`DROP TABLE IF EXISTS chat_messages`; revert actions, DI, data-attr, JS. Additive; no EF migration.

## Dependencies

Existing PostgreSQL/Dapper/Npgsql; no new packages. Runner: `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj -p:UseAppHost=false "-p:OutputPath=bin\Debug-tdd\"`.

## Success Criteria

- [ ] Reload keeps the conversation; users' history isolated
- [ ] GET returns 50 latest ascending; POST returns 201 `{id, createdAt}`
- [ ] Stored `modelId`/`sources` re-render credit and chips
- [ ] Suite green; AskStream tests unchanged

## Estimation

~1000–1200 authored lines (prod ~500–600 incl. Markdig+Ganss.Xss pipeline, tests ~450–550 incl. sanitizer XSS coverage, views/CSS ~80; vendored `marked`+`DOMPurify` excluded from authored count). Well over the 400-line guard — **High risk, chained PRs recommended** (slice 1: chat-history backend; slice 2: chat client persistence; slice 3: visual scope). Orchestrator resolves post-tasks: chained slices vs `size:exception`.