# Tasks: Multi-Assistant — selectable Ollama chat models per question

## Review Workload Forecast

Est. changed lines ~550-650 (S1≈300, S2≈250, S3≈150).
Delivery strategy: ask-on-risk.

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending (choose stacked-to-main or feature-branch-chain before apply)
400-line budget risk: High

### Work Units

| Unit | PR | Est | Focused test | Runtime harness | Rollback boundary |
|------|----|-----|--------------|-----------------|-------------------|
| 1 Foundation | 1 | ~300 | `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj --filter "FullyQualifiedName~AssistantCatalog\|FullyQualifiedName~RagServiceRouting"` | `dotnet run --project rag/` → /Ask answers via default phi3:mini | Revert AssistantCatalog.cs, RagService.cs, both Program.cs registrations |
| 2 MVC UI | 2 | ~250 | `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj --filter "FullyQualifiedName~Ask\|FullyQualifiedName~Documents"` | `dotnet run --project rag/` → select qwen2.5:1.5b → answer shows "Generado por" (needs `ollama pull`, ops pre-flight) | Revert appsettings Assistants, AskViewModel, AskController, Ask/Documents views |
| 3 API + regression | 3 | ~150 | `dotnet test` (full) | `dotnet run --project src/RAG.Api` → POST /api/rag/ask with and without ModelId | Revert RagEndpoints ModelId + delete tests/RAG.Api.Tests |

Deps: slice N ← N-1; RED before production (strict_tdd). Threat matrix: N/A (no routing/shell boundary) — no cases to port.

## Slice 1 — Foundation: catalog + routing

- [x] 1.1 [RED] Create `tests/RAG.Mvc.Tests/Application/AssistantCatalogTests.cs`: absent→default (ASEL-1); Resolve known/blank/unknown→default (ASEL-2)
- [x] 1.2 Create `src/RAG.Application/Services/AssistantCatalog.cs` — `AssistantDefinition` + `AssistantCatalog` (Default/TryResolve/Resolve) (ASEL-1)
- [x] 1.3 [RED] Create `tests/RAG.Mvc.Tests/Application/RagServiceRoutingTests.cs` — mock IChatClient captures `ChatOptions.ModelId`: known→model, blank/unknown→default (ASEL-2/8)
- [x] 1.4 Modify `src/RAG.Application/Services/RagService.cs` — add `AssistantCatalog` ctor + `modelId` param; final call `GetResponseAsync(prompt, new ChatOptions { ModelId }, ct)`; retrieval unchanged (ASEL-2/4/5/6)
- [x] 1.5 Modify `rag/Program.cs` + `src/RAG.Api/Program.cs` — register `AssistantCatalog` from `AI:Ollama:Assistants` / `Ollama:ChatModel` (ASEL-1)
- [x] 1.6 [GREEN] Slice filters + full `dotnet test` green (baseline intact: 122 passed / 1 skipped / 123 total → 139 passed / 1 skipped / 140 total)

## Slice 2 — MVC UI

- [x] 2.1 Modify `rag/appsettings.json` — `AI:Ollama:Assistants` (phi3:mini default, qwen2.5:1.5b, llama3.2:1b) (ASEL-1)
- [x] 2.2 Modify `rag/Models/AskViewModel.cs` — `AvailableAssistants`, `SelectedModelId=""`, `UsedAssistant` (ASK-14/15)
- [x] 2.3 [RED] Update `tests/RAG.Mvc.Tests/Controllers/AskControllerTests.cs` (ctor takes catalog now) + `Views/AskViewRenderTests.cs`: GET /Ask renders selector w/ default preselected; POST selected → result attributed; invalid/blank → default no error (ASK-14/15, ASEL-9)
- [x] 2.4 Modify `rag/Controllers/AskController.cs` — inject `AssistantCatalog`; Index populates VM; Ask TryResolve→pass modelId→set `UsedAssistant`=label (ASK-2, ASEL-4/7)
- [x] 2.5 Modify `rag/Views/Ask/Index.cshtml` — `<select asp-for="SelectedModelId">` label+description, default preselected; `Result.cshtml` — "Generado por {UsedAssistant}" when answer present (ASK-14/15)
- [x] 2.6 [RED] Update `tests/RAG.Mvc.Tests/Views/DocumentsViewRenderTests.cs` — floating chat shows selector (UPLOAD-13)
- [x] 2.7 Modify `rag/Views/Documents/Index.cshtml` — `<select name="SelectedModelId">` in floating chat form (UPLOAD-13)
- [x] 2.8 [GREEN] Ask/Documents filters green

## Slice 3 — API host + non-regression

- [x] 3.1 [RED] Create `tests/RAG.Api.Tests/` (csproj + AskEndpointTests): POST w/o ModelId → 200 default (regression); known → routed; unknown → default (ASEL-3)
- [x] 3.2 Modify `src/RAG.Api/Endpoints/RagEndpoints.cs` — `AskRequest.ModelId = null`; inject catalog; resolve + pass (ASEL-3/4)
- [x] 3.3 [RED] Config-pin test: embedding `nomic-embed-text` + reranker default unchanged (ASEL-5/6/10)
- [x] 3.4 [GREEN] Full suite green: baseline intact (MVC 144/1/145) + new API tests 6/6 (ASEL-10)