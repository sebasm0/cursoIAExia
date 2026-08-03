# Tasks: MVC RAG Integration

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~310-400 |
| 400-line budget risk | Medium |
| Chained PRs recommended | Yes |
| Suggested split | PR #1 (Foundation + Ask) → PR #2 (Upload + Tests) |
| Delivery strategy | ask-on-risk → chained |
| Chain strategy | feature-branch-chain |

Decision needed before apply: Yes (resolved to single PR)
Chained PRs recommended: No (user chose single PR)
Chain strategy: size-exception
400-line budget risk: Medium

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Foundation + Ask (csproj upgrade, DI, AskController + Views) | PR #1 → feature branch | `dotnet build rag/` | `dotnet run --project rag/` + browse /Ask | Revert rag.csproj, RAG.slnx, Program.cs changes; remove AskController + views |
| 2 | Upload + Tests (DocumentsController, Views, tests) | PR #2 → PR #1 branch | `dotnet build rag/` | `dotnet run --project rag/` + browse /Documents/Upload | Remove DocumentsController + views; revert tests |

## CHAINED PR CONTEXT

**Strategy**: feature-branch-chain
**Tracker branch**: `feature/mvc-rag-integration` (draft PR, no merge until all children integrated)
**PR #1**: Foundation + Ask → targets `feature/mvc-rag-integration`
**PR #2**: Upload + Testing → targets PR #1 branch
**Final**: feature branch merges to `main` after PR #2 is reviewed

## PR #1: Foundation + Ask (current)

- [x] 1.1 Modify `rag/rag.csproj` — target net10.0, add project refs to RAG.Application + RAG.Domain, add Microsoft.Extensions.AI.Ollama package
- [x] 1.2 Add `<Project Path="rag/rag.csproj" />` entry to `RAG.slnx`
- [x] 1.3 Add `"AI"` section (provider, baseUrl, models) + `"ConnectionStrings"` to `rag/appsettings.json`
- [x] 1.4 Register AI clients, `AddApplication()`, `AddRagInfrastructure(config)` in `rag/Program.cs`
- [x] 2.1 Create `rag/Models/AskViewModel.cs` — Query, Answer, ErrorMessage properties
- [x] 2.2 Create `rag/Controllers/AskController.cs` — GET Index + POST Ask calling RagService.AskAsync
- [x] 2.3 Create `rag/Views/Ask/Index.cshtml` — question form with client-side validation (empty query)
- [x] 2.4 Create `rag/Views/Ask/Result.cshtml` — answer display with error handling for unavailable service
- [x] 4.1 Add Ask nav link to `rag/Views/Shared/_Layout.cshtml`
- [x] 4.2 Verify full build compiles

## Single PR: All tasks

<!-- PR #1 tasks already completed, PR #2 merge marker removed -->
<!-- Foundation + Ask tasks completed above -->

## PR #2: Upload + Tests

- [x] 3.1 Create `rag/Models/UploadViewModel.cs` — FileName, FileSize, ContentType, Timestamp, ErrorMessage
- [x] 3.2 Create `rag/Controllers/DocumentsController.cs` — GET Index + POST Upload with file validation (extension, empty, max size) + error handling
- [x] 3.3 Create `rag/Views/Documents/Upload.cshtml` — file input form with client-side validation
- [x] 3.4 Create `rag/Views/Documents/Result.cshtml` — success/error display with document details
- [x] 4.1 Add Upload nav link to layout
- [x] 5.1 Unit: POST Ask empty query returns validation error
- [x] 5.2 Unit: POST Upload unsupported file type error
- [x] 5.3 Unit: POST Upload 0-byte file error
- [x] 5.4 Integration: POST Ask valid question renders answer
- [x] 5.5 Integration: POST Upload valid file renders success
- [x] 5.6 Manual: graceful error when Ollama unavailable

## Success Criteria

- [x] Upload .cs, .md, or .pdf through MVC UI → success confirmation
- [x] Ask question through MVC UI → answer with context citations
- [x] No files modified in `src/` (Application, Domain, Infrastructure)
