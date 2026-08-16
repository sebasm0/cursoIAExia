# Apply Progress — multi-assistant

Change: `multi-assistant`
Slice: **1 — Foundation (catalog + routing)** — tasks 1.1-1.6
Mode: **Strict TDD** (runner: `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj`)
Artifact store: openspec
Delivery: chained PRs (stacked-to-main) — this slice = PR #1 (Foundation). NO commits/pushes made (orchestrator decides).

---

## Slice 1 — Foundation: catalog + routing (COMPLETE)

- [x] 1.1 [RED] `tests/RAG.Mvc.Tests/Application/AssistantCatalogTests.cs` — absent→default (ASEL-1); Resolve known/blank/unknown→default (ASEL-2)
- [x] 1.2 `src/RAG.Application/Services/AssistantCatalog.cs` — `AssistantDefinition` + `AssistantCatalog` (Default/TryResolve/Resolve) (ASEL-1)
- [x] 1.3 [RED] `tests/RAG.Mvc.Tests/Application/RagServiceRoutingTests.cs` — mock IChatClient captures `ChatOptions.ModelId`: known→model, blank/unknown→default (ASEL-2/8)
- [x] 1.4 `src/RAG.Application/Services/RagService.cs` — `AssistantCatalog` ctor + `modelId` param; final call `GetResponseAsync(prompt, new ChatOptions { ModelId }, ct)`; retrieval unchanged (ASEL-2/4/5/6)
- [x] 1.5 `rag/Program.cs` + `src/RAG.Api/Program.cs` — register `AssistantCatalog` from `AI:Ollama:Assistants` / `Ollama:ChatModel` (ASEL-1)
- [x] 1.6 [GREEN] Slice filters + full `dotnet test` green

### Evidence — commands executed (red → green)

| Step | Command | Result |
|------|---------|--------|
| Baseline (safety net) | `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj --no-restore` | ✅ 122 passed / 1 skipped / 123 total |
| 1.1 RED | `dotnet test ... --filter "FullyQualifiedName~AssistantCatalogTests"` | ❌ RED — CS0246 `AssistantCatalog` not found |
| 1.2 GREEN | same filter after creating `AssistantCatalog.cs` | ✅ 11/11 passed |
| 1.3 RED | `dotnet test ... --filter "FullyQualifiedName~RagServiceRoutingTests"` | ❌ RED — CS1739 `modelId` param missing, CS1729 5-arg ctor missing |
| 1.4 GREEN (1st attempt) | same filter after `RagService` change | ❌ 6/6 failed — Moq `ArgumentException`: `IList<ChatMessage>` callback vs `IEnumerable<ChatMessage>` interface signature (test-side fix) |
| 1.4 GREEN (fixed) | same filter | ✅ 6/6 passed |
| 1.5 build | `dotnet build RAG.slnx --no-restore` | ✅ 0 errors (1 pre-existing NU1510 warning) |
| 1.6 slice filters | `--filter "FullyQualifiedName~AssistantCatalog\|FullyQualifiedName~RagServiceRouting"` | ✅ 17/17 passed |
| 1.6 full suite | `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj --no-restore` | ✅ 139 passed / 1 skipped / 0 failed / 140 total |

Note: tasks.md 1.6 quoted "74/74 baseline intact" — stale; the real baseline is **122 passed / 1 skipped / 123 total**. New total after slice: **139/1/140**.

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | `tests/RAG.Mvc.Tests/Application/AssistantCatalogTests.cs` | Unit | N/A (new) | ✅ Written (CS0246) | ✅ 11/11 | ✅ 11 cases (absent, empty, configured metadata, default preference, known/blank×3/unknown, TryResolve ×2) | ✅ Clean |
| 1.2 | same file | Unit | N/A (new) | ✅ (test-first) | ✅ 11/11 | ✅ (via 1.1 cases) | ✅ Clean |
| 1.3 | `tests/RAG.Mvc.Tests/Application/RagServiceRoutingTests.cs` | Unit | N/A (new) | ✅ Written (CS1739/CS1729) | ✅ 6/6 | ✅ 4 behaviors × (known, null/blank theory ×3, unknown, retrieval-unchanged) | ✅ Fixed mock signature to interface contract |
| 1.4 | same file | Unit | ✅ 122/1 baseline run before edit | ✅ (test-first) | ✅ 6/6 | ✅ (via 1.3 cases) | ➖ None needed |
| 1.5 | (host registrations — verified via full suite) | Integration | ✅ 122/1 | N/A — registration + DI wiring, no new behavior | ✅ full suite 139/1 | ➖ Single (fallback covered by 1.1) | ➖ None needed |
| 1.6 | — | — | ✅ | N/A | ✅ 139/1/140 | N/A | N/A |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantCatalog\|FullyQualifiedName~RagServiceRouting"` → **17/17 passed, exit 0** |
| Runtime harness command/scenario and exact result | `dotnet build RAG.slnx --no-restore` → 0 errors (both hosts compile with catalog registered). Full WAF integration suite re-run (139 passed) proves the real host DI resolves `RagService` with the catalog — runtime path exercised by `CustomRagWebApplicationFactory` Ask flow. Manual `dotnet run --project rag/` NOT executed (would need live Ollama + a free port; not required for this slice's routing-only scope). |
| Rollback boundary | Revert: `src/RAG.Application/Services/AssistantCatalog.cs` (delete), `src/RAG.Application/Services/RagService.cs` (remove ctor param + `modelId` + ChatOptions line), `rag/Program.cs` + `src/RAG.Api/Program.cs` (remove registration + usings), `src/RAG.Api/Endpoints/RagEndpoints.cs` (revert `ct: ct` → `ct`), delete `tests/RAG.Mvc.Tests/Application/` (2 test files). No other work touched. |

### Deviations / Notes

1. **`RagEndpoints.cs` minimal compile fix (required)**: the new `AskAsync(..., modelId, ct)` signature moved `ct` after `modelId`; the existing positional call `AskAsync(q, 20, 5, ct)` no longer compiles. Changed to named `ct: ct` — behavior identical (default model). Full ModelId plumbing for the API host is Slice 3 (task 3.2). `AskController` already used `ct: ct` — unaffected.
2. **`IChatClient` interface signature**: in `Microsoft.Extensions.AI` 9.7.0 the interface method is `GetResponseAsync(IEnumerable<ChatMessage>, ChatOptions?, CancellationToken)` — NOT `IList<ChatMessage>`. The existing `CustomRagWebApplicationFactory` mock compiles only via implicit reference conversion; a typed Moq `Callback<IList<...>,...>` throws `ArgumentException`. Routing test uses `IEnumerable<ChatMessage>` to match the real contract.
3. **`AssistantCatalog.Default` selection for a configured catalog** (design D1 leaves it open): Default = entry whose `Model` == host chat model → else entry with `Id == "default"` → else first entry. This keeps "existing ChatModel stays the default" (proposal). Slice 2's `appsettings.json` Assistants must include an entry with `model: phi3:mini` (or id `default`) to preserve current behavior.
4. **Blocker hit mid-slice**: a running `rag.exe` dev server (PID 6192) locked `rag/bin/Debug/net10.0` DLLs and blocked the 1.2 GREEN build. Resolved by the orchestrator (process stopped). No code impact.
5. Retrieval section of `RagService` (embedding → hybrid → rerank) is byte-identical to pre-change; only the final chat call changed. Existing Spanish comments in that section left untouched (existing code, out of slice scope).

### Slice 2 / 3 — NOT touched

Tasks 2.1-2.8 and 3.1-3.4 remain pending; no appsettings/views/controller/endpoint-DTO changes were made.

---

## Slice 2 — MVC UI (COMPLETE)

Change: `multi-assistant`
Slice: **2 — MVC UI** — tasks 2.1-2.8
Mode: **Strict TDD** (runner: `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj`)
Artifact store: openspec
Delivery: chained PRs (stacked-to-main) — this slice = PR #2 (targets PR #1 branch). NO commits/pushes made (orchestrator decides).

- [x] 2.1 `rag/appsettings.json` — `AI:Ollama:Assistants`: default/Phi3 Mini/phi3:mini/"Equilibrio entre calidad y velocidad", fast/Qwen 2.5 1.5B/qwen2.5:1.5b/"Más rápido manteniendo buena calidad", tiny/Llama 3.2 1B/llama3.2:1b/"La opción más rápida" (ASEL-1)
- [x] 2.2 `rag/Models/AskViewModel.cs` — added `AvailableAssistants` (IReadOnlyList<AssistantDefinition>), `SelectedModelId` (string, default ""), `UsedAssistant` (string?) (ASK-14/15)
- [x] 2.3 [RED] `AskControllerTests.cs` — unit test ctor now `new AskController(null!, Mock.Of<ILogger<AskController>>(), new AssistantCatalog("phi3:mini", null))`; +3 integration tests via `CustomRagWebApplicationFactory`: selected→attributed, invalid→default no error, blank→default no error. `AskViewRenderTests.cs` — GET /Ask selector test (labels + descriptions + default preselected). RED confirmed: **CS1729** `AskController` has no 3-arg ctor.
- [x] 2.4 `rag/Controllers/AskController.cs` — 3-arg ctor (`AssistantCatalog _catalog`); `Index` populates `AvailableAssistants = _catalog.All` + `SelectedModelId = _catalog.Default.Id`; `Ask` first `TryResolve(model.SelectedModelId, out var assistant)` → sets `SelectedModelId = assistant.Id`, `UsedAssistant = assistant.Label`, `AvailableAssistants = _catalog.All` → then Query validation → `_ragService.AskAsync(model.Query, modelId: assistant.Id, ct: ct)` (ASK-2, ASEL-4/7)
- [x] 2.5 `rag/Views/Ask/Index.cshtml` — `<select asp-for="SelectedModelId">` with `<option value="@assistant.Id" title="@assistant.Description">@assistant.Label — @assistant.Description</option>` loop, visually-hidden label "Asistente"; `Result.cshtml` — card-footer `Generado por @Model.UsedAssistant` when non-empty (ASK-14/15)
- [x] 2.6 [RED] `DocumentsViewRenderTests.cs` — `Documents_Index_RendersAssistantSelectorInFloatingChat` (GET /Documents via PolicyTestWebApplicationFactory [DocumentsUpload]: `name="SelectedModelId"` + 3 labels). RED confirmed (1 failed).
- [x] 2.7 `rag/Views/Documents/Index.cshtml` — `@inject RAG.Application.Services.AssistantCatalog AssistantCatalog` (fully-qualified; no `RAG.Application.Services` using in `_ViewImports`); `<select name="SelectedModelId" id="SelectedModelId">` inside the floating chat form (raw form, no asp-for), default option rendered with `selected` via `@if (assistant.Id == AssistantCatalog.Default.Id)` (UPLOAD-13)
- [x] 2.8 [GREEN] Ask/Documents filters green + full suite green

### Evidence — commands executed (red → green)

| Step | Command | Result |
|------|---------|--------|
| Baseline (safety net) | Slice 1 end state | ✅ 139 passed / 1 skipped / 140 total |
| 2.3 RED | `dotnet test ... --filter "FullyQualifiedName~AskControllerTests\|FullyQualifiedName~AskViewRenderTests"` | ❌ RED — CS1729 `AskController` no 3-arg ctor |
| 2.4 controller only | same filter after `AskController` change | ⚠️ 8/12 — controller green; 4 view-render assertions still red (views not yet changed) |
| 2.5 GREEN (1st attempt) | same filter after view changes | ❌ 11/12 — `Assert.Contains("Más rápido manteniendo buena calidad")` failed: Razor HTML-encodes dynamic catalog text (`á` → `&#xE1;`) |
| 2.5 GREEN (fixed) | same filter after `WebUtility.HtmlDecode(body)` in selector test | ✅ 12/12 passed |
| 2.6 RED | `dotnet test ... --filter "FullyQualifiedName~Documents_Index_RendersAssistantSelectorInFloatingChat"` | ❌ RED — 1 failed (selector absent) |
| 2.7 GREEN | `dotnet test ... --filter "FullyQualifiedName~DocumentsViewRenderTests"` | ✅ 10/10 passed |
| 2.8 slice filters | `--filter "FullyQualifiedName~Ask\|FullyQualifiedName~Documents"` | ✅ 52/52 passed |
| 2.8 build | `dotnet build RAG.slnx --no-restore` | ✅ 0 errors (1 pre-existing NU1510 warning) |
| 2.8 full suite | `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj --no-restore` | ✅ 144 passed / 1 skipped / 0 failed / 145 total |

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 2.1 | (config — verified via 2.3/2.6 tests) | Config | ✅ 139/1 | N/A — data change, no behavior | ✅ (via selector tests) | ✅ 3 entries incl. default model == ChatModel | ✅ Clean |
| 2.2 | (VM — verified via controller tests) | Unit | ✅ 139/1 | N/A — DTO shape, no behavior | ✅ (via 2.4) | ✅ (via 2.3 cases) | ✅ Clean |
| 2.3 | `AskControllerTests.cs` + `AskViewRenderTests.cs` | Unit+Integration | ✅ 139/1 | ✅ CS1729 + view assertions red | ✅ 12/12 | ✅ 3 attribution flows (known/invalid/blank) + selector render + preselection | ✅ HtmlDecode on rendered body (dynamic content is HTML-encoded) |
| 2.4 | same | Unit | ✅ (2.3 red) | ✅ (test-first) | ✅ 8/12 (controller portion) | ✅ (via 2.3 cases) | ➖ None needed |
| 2.5 | `AskViewRenderTests.cs` | View | ✅ (2.4 state) | ✅ (test-first) | ✅ 12/12 | ✅ (via 2.3 cases) | ✅ HtmlDecode (real fix: encode-aware assertions) |
| 2.6 | `DocumentsViewRenderTests.cs` | View | ✅ 139/1 | ✅ RED 1 failed | ✅ 10/10 | ✅ single (selector present + 3 labels) | ➖ None needed |
| 2.7 | same file | View | ✅ (2.6 red) | ✅ (test-first) | ✅ 10/10 | ✅ (via 2.6) | ✅ `@inject` fully-qualified (no using in _ViewImports) |
| 2.8 | — | — | ✅ | N/A | ✅ 144/1/145 | N/A | N/A |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj --no-restore --filter "FullyQualifiedName~Ask\|FullyQualifiedName~Documents"` → **52/52 passed, exit 0** |
| Runtime harness command/scenario and exact result | `dotnet build RAG.slnx --no-restore` → 0 errors (MVC host compiles with catalog DI). WAF integration flows exercised the REAL host pipeline end-to-end: GET /Ask (selector + preselection), POST /Ask selected/invalid/blank (attribution + routing through `RagService` mock IChatClient), GET /Documents (floating-chat selector) — all via `CustomRagWebApplicationFactory`/`PolicyTestWebApplicationFactory`. Manual `dotnet run --project rag/` NOT executed (would need live Ollama `ollama pull qwen2.5:1.5b llama3.2:1b` ops pre-flight + free port; ops-side pre-flight deferred to orchestrator). |
| Rollback boundary | Revert: `rag/appsettings.json` (drop `AI:Ollama:Assistants`), `rag/Models/AskViewModel.cs` (remove 3 props + using), `rag/Controllers/AskController.cs` (back to 2-arg ctor + old Ask body), `rag/Views/Ask/Index.cshtml` + `Result.cshtml` + `rag/Views/Documents/Index.cshtml` (remove selector/attribution), `tests/RAG.Mvc.Tests/Controllers/AskControllerTests.cs` + `Views/AskViewRenderTests.cs` + `Views/DocumentsViewRenderTests.cs` (revert to slice-1 state). No other work touched. |

### Deviations / Notes

1. **Razor HTML-encoding of dynamic catalog text (discovered)**: Razor renders STATIC view text verbatim but HTML-encodes DYNAMIC `@` content. The selector options (label/description come from `appsettings.json` via the catalog) are encoded: `á` → `&#xE1;`. The render test now decodes the body with `WebUtility.HtmlDecode` before asserting — asserts the copy the user actually sees. (Static Spanish placeholder text in the same views passes verbatim, which is why older tests never hit this.)
2. **`Documents/Index.cshtml` uses a raw form** (no `asp-*` tag helpers): the floating-chat `<select>` uses `name="SelectedModelId"` (raw `name`, no `asp-for`), matching the form's existing `name="Query"` pattern. Default preselected via explicit `selected` attribute (not tag-helper state).
3. **`@inject` fully-qualified**: `rag/Views/_ViewImports.cshtml` does not import `RAG.Application.Services`; the Documents view injects `RAG.Application.Services.AssistantCatalog AssistantCatalog` fully-qualified to avoid touching shared view imports.
4. **UI copy stays Spanish, neutral/professional** (persona scope: generated artifacts in the repo do not adopt Rioplatense); attribution text `Generado por {label}` per design D3.
5. **Floating chat on /Documents posts to AskController.Ask** (existing action contract): the raw form posts `Query` + `SelectedModelId`; AskController already handles it (2.4). No controller change needed for UPLOAD-13.
6. Controller-side: `Ask` resolves the assistant BEFORE query validation, so an invalid/blank selection still yields a valid `SelectedModelId` on the re-rendered form (ASEL-9 no-error fallback) — decision matches D3.

### Slice 3 — NOT touched

Tasks 3.1-3.4 remain pending; no `RagEndpoints` ModelId plumbing, no `RAG.Api.Tests`, no config-pin changes.

---

## Slice 3 — API host + non-regression (COMPLETE)

Change: `multi-assistant`
Slice: **3 — API host + non-regression** — tasks 3.1-3.4
Mode: **Strict TDD** (runners: `dotnet test tests/RAG.Api.Tests/RAG.Api.Tests.csproj` + `dotnet test RAG.slnx`)
Artifact store: openspec
Delivery: chained PRs (stacked-to-main) — this slice = PR #3 (final, targets PR #2 branch). NO commits/pushes made (orchestrator decides).

- [x] 3.1 [RED] `tests/RAG.Api.Tests/` created (csproj + `ApiWebApplicationFactory` + `AskEndpointTests`): POST w/o ModelId → 200 default (regression); known `fast` → routed to qwen2.5:1.5b; unknown → default 200; blank → default. RED confirmed: `Post_WithKnownModelId_RoutesToThatModel` failed (endpoint ignored `modelId`).
- [x] 3.2 `src/RAG.Api/Endpoints/RagEndpoints.cs` — `AskRequest` gains `string? ModelId = null` (D4); `/ask` handler injects `AssistantCatalog catalog`, resolves via `catalog.Resolve(request.ModelId)`, passes the resolved allow-listed `assistant.Id` to `AskAsync` (ASEL-3/4: tampered value never reaches the chat client).
- [x] 3.3 [RED→GREEN] `ConfigPinTests` guard (ASEL-5/6/10): parses `src/RAG.Api/appsettings.json` + `rag/appsettings.json` from repo root; pins `Ollama:EmbeddingModel`/`AI:Ollama:EmbeddingModel` == `nomic-embed-text` and reranker default chat models (`Ollama:ChatModel` == `llama3.2` API, `AI:Ollama:ChatModel` == `phi3:mini` MVC). Guard test: RED = absent before (nothing to break); GREEN = passes now, fails if values change.
- [x] 3.4 [GREEN] Solution build 0 errors; full suite green: **RAG.Mvc.Tests 144 passed / 1 skipped / 145 total (baseline intact)** + **RAG.Api.Tests 6 passed / 6 total** → solution total **150 passed / 1 skipped / 151 total**.

### Evidence — commands executed (red → green)

| Step | Command | Result |
|------|---------|--------|
| Baseline (safety net) | Slice 2 end state | ✅ RAG.Mvc.Tests 144 passed / 1 skipped / 145 total |
| 3.1 RED | `dotnet test tests/RAG.Api.Tests/RAG.Api.Tests.csproj` (new project) | ❌ RED — 3/4 passed; `Post_WithKnownModelId_RoutesToThatModel` failed: endpoint ignored `modelId` (ChatOptions.ModelId == default) |
| 3.2 GREEN | same command after `RagEndpoints.cs` change | ✅ 4/4 passed |
| 3.3 GREEN | same command after `ConfigPinTests` added | ✅ 6/6 passed (4 Ask + 2 config-pin) |
| 3.4 build | `dotnet build RAG.slnx` | ✅ 0 errors (2 warnings: pre-existing NU1510 + transitive) |
| 3.4 full suite | `dotnet test RAG.slnx` | ✅ RAG.Mvc.Tests 144/1/145 + RAG.Api.Tests 6/6 = **150 passed / 1 skipped / 151 total** |

### TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 3.1 | `tests/RAG.Api.Tests/AskEndpointTests.cs` | Integration (API WAF) | ✅ 144/1 (MVC) | ✅ 1 failed (known-model) | ✅ 4/4 | ✅ 4 behaviors: omitted/known/unknown/blank × ModelId capture + answer + 200 | ✅ None needed |
| 3.2 | same file | Integration | ✅ (3.1 red) | ✅ (test-first) | ✅ 4/4 | ✅ (via 3.1 cases) | ➖ None needed |
| 3.3 | `tests/RAG.Api.Tests/ConfigPinTests.cs` | Guard (config contract) | ✅ 144/1 | ✅ Guard: absent before (fails only on contract change) | ✅ 2/2 | ✅ 4 pins: embedding ×2 hosts, reranker default ×2 hosts | ✅ None needed |
| 3.4 | — | — | ✅ | N/A | ✅ 150/1/151 solution-wide | N/A | N/A |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| Focused test command and exact result | `dotnet test tests/RAG.Api.Tests/RAG.Api.Tests.csproj` → **6/6 passed, exit 0** (AskEndpointTests 4/4 + ConfigPinTests 2/2) |
| Runtime harness command/scenario and exact result | `dotnet build RAG.slnx` → 0 errors; `dotnet test RAG.slnx` → 150 passed / 1 skipped / 151 total across both hosts' test projects. The real API host pipeline was exercised end-to-end via `WebApplicationFactory<Program>` (public generated entry point — same SDK behavior verified against rag.dll): POST /api/rag/ask with/without/known/unknown `modelId` through minimal API binding → catalog resolution → `RagService.AskAsync` → mocked `IChatClient` capturing `ChatOptions.ModelId`. Manual `dotnet run --project src/RAG.Api` NOT executed (would need live Ollama + Postgres; WAF tests cover the request path without ops dependencies). |
| Rollback boundary | Revert: `src/RAG.Api/Endpoints/RagEndpoints.cs` (drop `ModelId` + catalog param + resolve line), `RAG.slnx` (remove `/tests/RAG.Api.Tests` entry), delete `tests/RAG.Api.Tests/` (3 files). No other work touched. |

### Deviations / Notes

1. **Test-only catalog override**: the API host ships default-only (`new AssistantCatalog(chatModel, [])`, design D1) — with only one entry, "known → routed" would be indistinguishable from fallback. The API WAF replaces the catalog with a 2-entry allow-list (`default`/llama3.2, `fast`/qwen2.5:1.5b) so ASEL-3's known-vs-unknown scenarios are provable at the boundary. Production config untouched (default-only per D1).
2. **Boundary enforcement (ASEL-4)**: the endpoint passes `assistant.Id` (the resolved allow-listed id) to `AskAsync`, not `request.ModelId` — a tampered value never flows beyond catalog resolution. `AskAsync` re-resolves (idempotent, ASEL-2) — belt and suspenders.
3. **`Program` accessibility**: no `public partial class Program` needed — the .NET SDK generates a public `Program` type for top-level statements (verified via reflection: `rag.dll` Program.IsPublic == True; same SDK produces the same for RAG.Api). `WebApplicationFactory<Program>` binds directly.
4. **Moq typed callback**: the `IChatClient.GetResponseAsync` interface signature is `IEnumerable<ChatMessage>` (M.E.AI 9.7), NOT `IList<ChatMessage>` — a typed `Callback<IList<...>,...>` throws ArgumentException (Slice 1 gotcha; respected here).
5. **`ApiWebApplicationFactory` stubs**: mirrors `RagWebApplicationFactoryBase` minus Identity/DB-migration bits — the API host has no Identity and never migrates; `ConnectionStrings:PostgreSQL` still overridden so the lazy `PgVectorStore` can never touch a real DB.
6. **Config-pin reads repo files directly** (walk up from `AppContext.BaseDirectory` to `RAG.slnx`), parsing the actual `appsettings.json` artifacts — deterministic guard independent of env/config-merging, fails the moment the file changes.
7. **tasks.md 3.4 stale number fixed**: "baseline 74/74" (pre-existing stale figure, same as 1.6's) → real numbers recorded.
8. Runtime verification of the live API host (`ollama pull` + Postgres + `dotnet run`) remains ops pre-flight, out of scope — same as Slices 1-2.

### Slices 1-2 — preserved

Slices 1 and 2 sections above are complete and unchanged (MVC UI + catalog + routing). All 14 tasks (1.1-3.4) are now [x].
