# Design: MVC RAG Integration

## Technical Approach

Upgrade the existing `rag/` MVC project from net9.0 to net10.0, add project references to `RAG.Application` and `RAG.Domain`, and mirror the AI client + DI registration pattern from `RAG.Api/Program.cs`. Two new controllers (`AskController`, `DocumentsController`) with paired Razor views provide the web UI. The existing `RagService.AskAsync` and `IngestionService.IngestAsync` are called directly — no changes to `src/` projects.

## Architecture Decisions

### Decision: net9.0 → net10.0 upgrade is mandatory

| Option | Tradeoff | Decision |
|--------|----------|----------|
| Keep net9.0 | Can't reference net10.0 src/ projects | **Upgrade** — all src/ projects are already net10.0 |
| Dual-target | Unnecessary complexity | **Single target net10.0** |

### Decision: AI client registration in rag/Program.cs

| Option | Tradeoff | Decision |
|--------|----------|----------|
| Copy OOTB from RAG.Api | Simple but duplicates registration logic | **Adopt, with config-driven settings** |
| Shared registration library | Requires new src/ project (out of scope) | **Rejected** |

`OllamaChatClient` and `OllamaEmbeddingGenerator` are registered as singletons in `rag/Program.cs`. URLs and model names come from `appsettings.json` `"AI"` section — not hardcoded strings.

### Decision: Citation display from RagService.AskAsync

`AskAsync` returns a plain `string` with inline citations. No structured citation DTO exists in `src/`. Creating one would modify `src/` (out of scope). **Display the raw response** in a styled `<pre>` / markdown block.

### Decision: Multi-tenant isolation (ASK-6, UPLOAD-7)

Current `IngestionService.IngestAsync` and `RagService.AskAsync` have no `userId` parameter. Adding one requires `src/` changes. **Deferred** — document as known limitation. A future change should thread `userId` through the pipeline and scope vector store queries by user.

### Decision: Background directory scanner (UPLOAD-8)

SHOULD requirement. A `FileSystemWatcher`-based `BackgroundService` can watch a configurable directory and auto-ingest new files. **Deferred to follow-up** — not in this design.

## Data Flow

```
User → GET /Ask → AskController.Index() → Ask/Index.cshtml (form)
User → POST /Ask → AskController.Ask() → RagService.AskAsync(query, ...) → answer string → Ask/Result.cshtml

User → GET /Documents/Upload → DocumentsController.Index() → Upload.cshtml (form)
User → POST /Documents/Upload → DocumentsController.Upload() → IngestionService.IngestAsync(fileName, contentType, stream) → Document → Upload/Result.cshtml
```

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `rag/rag.csproj` | Modify | Target `net10.0`, add project refs to `RAG.Application` + `RAG.Domain`, add `Microsoft.Extensions.AI.Ollama` package |
| `RAG.slnx` | Modify | Add `<Project Path="rag/rag.csproj" />` entry |
| `rag/Program.cs` | Modify | Register AI clients, `AddApplication()`, `AddRagInfrastructure(config)` |
| `rag/appsettings.json` | Modify | Add `"AI"` section (provider, baseUrl, models) + `"ConnectionStrings"` for PostgreSQL |
| `rag/Controllers/AskController.cs` | Create | GET form + POST submit via `RagService.AskAsync` |
| `rag/Controllers/DocumentsController.cs` | Create | GET form + POST upload via `IngestionService.IngestAsync` |
| `rag/Models/AskViewModel.cs` | Create | Query + Answer + ErrorMessage properties |
| `rag/Models/UploadViewModel.cs` | Create | Result display + ErrorMessage properties |
| `rag/Views/Ask/Index.cshtml` | Create | Question input form |
| `rag/Views/Ask/Result.cshtml` | Create | Answer display with error handling |
| `rag/Views/Documents/Upload.cshtml` | Create | File upload form |
| `rag/Views/Documents/Result.cshtml` | Create | Upload success/error display |
| `rag/Views/Shared/_Layout.cshtml` | Modify | Add nav links for Ask and Upload |

## Interfaces / Contracts

No new interfaces. All consumed interfaces come from `src/`:

- `RagService.AskAsync(string query, int topKRetrieve, int topKRank, CancellationToken ct)` → `string`
- `IngestionService.IngestAsync(string fileName, string contentType, Stream content, CancellationToken ct)` → `Document`

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit | Controller action validation | Mock `RagService` / `IngestionService`, verify empty input rejection |
| Integration | Full POST flow | Spin up `WebApplicationFactory` with stubbed AI clients, verify model binding + view rendering |
| Manual | Ollama/pgvector unavailable | Point at wrong port, verify graceful error view renders |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary.

## Migration / Rollout

No migration required. New controllers sit alongside existing `HomeController`. Rollback: revert `rag.csproj`, `RAG.slnx`, `Program.cs`, `_Layout.cshtml`, remove new controller/view files.

## Open Questions

- [ ] What file extension → MIME type mapping for upload validation? (list of known supported types from parsers)
- [ ] Should AskController expose topKRetrieve / topKRank as hidden fields or use sensible defaults?
