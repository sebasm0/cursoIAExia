# Design: multi-assistant — selectable Ollama chat models per question

## Technical Approach

Config-driven assistant catalog (`AI:Ollama:Assistants`) loaded in each host's `Program.cs` into an `AssistantCatalog` singleton injected into `RagService`. `AskAsync` gains `string? modelId = null`; only the final `GetResponseAsync` call passes `ChatOptions.ModelId`. MVC + API validate the id against the catalog, falling back to default on null/blank/unknown. `AskViewModel` carries catalog + selection for selector/attribution UI. Embeddings and `OllamaReranker` untouched. Covers ASEL-1..10, ASK-2/14/15, UPLOAD-13.

## Architecture Decisions

### D1: Catalog type & registration

| Option | Tradeoff | Decision |
|---|---|---|
| **`AssistantCatalog` record in Application** (`Id/Label/Model/Description` list + `Default` derived from config), registered per-host in Program.cs from `IConfiguration` | Plain, shared by MVC + API hosts; no config access needed inside `AddApplication()` | **Chosen** — ASEL-1 |
| Hardcode allow-list in controller | Rejected by spec (risk of tamper bypass, no config) | **Rejected** |

Primary-constructor singleton: `new AssistantCatalog(config["AI:Ollama:ChatModel"], config.GetSection("AI:Ollama:Assistants").Get<AssistantDefinition[]>() ?? [])`. Null/empty list → single default `{ id="default", model=ChatModel }` (ASEL-1). API host binds from its own `Ollama:ChatModel` (no `Assistants` → default only). Registered in `rag/Program.cs` (Ollama branch) and `src/RAG.Api/Program.cs`.

### D2: `AskAsync` signature & routing

`AskAsync(string query, int topKRetrieve = 20, int topKRank = 5, string? modelId = null, CancellationToken ct = default)`. Inside: `var model = _catalog.Resolve(modelId);` (unknown/blank → default, ASEL-2). Retrieval (embedding → hybrid → rerank) unchanged; only the final call becomes `GetResponseAsync(prompt, new ChatOptions { ModelId = model.Model }, ct)`. ASEL-4: only a catalog-resolved model reaches the client.

### D3: MVC flow & validation

`AskViewModel` gains `AvailableAssistants`, `SelectedModelId = ""`, `UsedAssistant`. `Index()` injects the catalog into the model (ASK-14). POST `Ask` resolves via `_catalog.TryResolve(model.SelectedModelId, out _)` → passes resolved id (or default) to `AskAsync`; sets `UsedAssistant` = label (ASK-15, ASEL-7). Inject `AssistantCatalog` into `AskController`.

### D4: API host

`AskRequest` record gains `string? ModelId = null` (ASEL-3). Endpoint resolves via `_catalog` (injected `AssistantCatalog`) → passes to `AskAsync`. Omitted/unknown → default; existing clients unaffected.

## Data Flow

```
GET /Ask ──► AskController.Index ──► AskViewModel{ AvailableAssistants, SelectedModelId=default }
POST /Ask ──► AskController.Ask ──► catalog.TryResolve(SelectedModelId) ──► AskAsync(...,modelId)
                │  (blank/unknown→default)                                     │
                └──► RagService.AskAsync: embed(nomic) → hybrid → rerank(unchanged)
                          └─► GetResponseAsync(prompt, ChatOptions{ModelId})  → UsedAssistant=label
POST /api/rag/ask ──► RagEndpoints ──► catalog.TryResolve(request.ModelId) ──► AskAsync(...,modelId)
```

## File Changes

| File | Action | Description |
|---|---|---|
| `src/RAG.Application/Services/RagService.cs` | Modify | Add `AssistantCatalog` ctor param; `modelId` param; `ChatOptions.ModelId` on final call |
| `src/RAG.Application/Services/AssistantCatalog.cs` | Create | `AssistantDefinition` + `AssistantCatalog` with `Resolve`/`TryResolve`/`Default` |
| `rag/Program.cs` | Modify | Register `AssistantCatalog` from `AI:Ollama:Assistants` (Ollama branch) |
| `rag/Controllers/AskController.cs` | Modify | Inject catalog; populate view model; validate + pass modelId; set `UsedAssistant` |
| `rag/Models/AskViewModel.cs` | Modify | Add `AvailableAssistants`, `SelectedModelId`, `UsedAssistant` |
| `rag/Views/Ask/Index.cshtml` | Modify | Render `<select asp-for="SelectedModelId">` (label+description), default preselected (ASK-14) |
| `rag/Views/Ask/Result.cshtml` | Modify | Discreet attribution line: "Generado por {UsedAssistant}" when answer present (ASK-15) |
| `rag/Views/Documents/Index.cshtml` | Modify | Add same `<select name="SelectedModelId">` to floating chat form (UPLOAD-13) |
| `rag/appsettings.json` | Modify | Add `AI:Ollama:Assistants` (phi3:mini default, qwen2.5:1.5b, llama3.2:1b) |
| `src/RAG.Api/Endpoints/RagEndpoints.cs` | Modify | `AskRequest.ModelId = null`; inject catalog; resolve + pass |
| `src/RAG.Api/Program.cs` | Modify | Register `AssistantCatalog` from `Ollama:ChatModel` (default only) |

## Interfaces / Contracts

```csharp
public sealed record AssistantDefinition(string Id, string Label, string Model, string Description);
public sealed class AssistantCatalog {
    public IReadOnlyList<AssistantDefinition> All { get; }
    public AssistantDefinition Default { get; }
    public bool TryResolve(string? modelId, out AssistantDefinition a); // blank/unknown → Default, true
    public AssistantDefinition Resolve(string? modelId) => /* TryResolve, return Default */;
}
public record AskRequest(string Query, int TopKRetrieve = 20, int TopKRank = 5, string? ModelId = null);
```

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit | Catalog list, absent→default, `Resolve` known/blank/unknown (ASEL-1/2/8) | Direct construction |
| Unit | Mock `IChatClient` captures `ChatOptions.ModelId` known→model, blank/unknown→default; embeddings+reranker unchanged (ASEL-5/6/8/10) | Moq chat + fixed stubs |
| Integration (WAF) | `/Ask` GET renders selector w/ default preselected; POST selected → result attributed; invalid/blank → default no error (ASK-14/15, ASEL-9) | `CustomRagWebApplicationFactory` (+ `PolicyTestWebApplicationFactory`), reuse `AccountTestHelpers.GetAntiforgeryTokenAsync`/`CreatePost` |
| Integration (API) | POST `/api/rag/ask` w/ and w/o `ModelId`; known/unknown (ASEL-3) | WAF API client |
| Regression | Existing Ask view/controller tests stay green; embeddings `nomic-embed-text` + reranker config pinned (ASEL-10) | Existing suite + config assertions |

Existing helpers reused: `RagWebApplicationFactoryBase`, `RemoveService<T>`, `PolicyTestWebApplicationFactory`, `AccountTestHelpers`, `CustomRagWebApplicationFactory`.

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary (model id is an app-level allow-list over HTTP fields, validated in application code).

## Migration / Rollout

No data migration. Config-gated: absent catalog → single default (behavior identical today). Rollback: revert `modelId` param + views + catalog config. One work-unit commit (catalog + RagService + tests → MVC + views → API host).

## Open Questions

- [ ] Attribution copy on Result (Spanish UI) — "Generado por {label}".
