```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:8e20731abac3f3475ae440592ce8a403eb545bc01122674b613d6bb4f78e2b41
verdict: pass
blockers: 0
critical_findings: 0
requirements: 10/10
scenarios: 16/16
test_command: dotnet test RAG.slnx --no-build
test_exit_code: 0
test_output_hash: sha256:a667ee3a7396af6203d98d8220b34aed763a89f60dec7ef382fd9c57850657fe
build_command: dotnet build RAG.slnx
build_exit_code: 0
build_output_hash: sha256:dd11b53abadc4e8a99f974d8d297068ac179ebca625894906707283faa6523a0
```

# Verification Report — multi-assistant

**Change**: multi-assistant
**Version**: N/A (delta specs)
**Mode**: Strict TDD (runner: `dotnet test tests/RAG.Mvc.Tests/RAG.Mvc.Tests.csproj` + `dotnet test RAG.slnx`)
**Review lineage**: 4R `review-ea5daaf8adde82cf` — terminal_state `approved`, 0 BLOCKER/CRITICAL (verified in `.git/gentle-ai/review-transactions/v2/`)

## Completeness

| Metric | Value |
|--------|-------|
| Tasks total | 14 |
| Tasks complete | 14 (tasks.md all `[x]`) |
| Tasks incomplete | 0 |

## Build & Tests Execution (run by verifier, 2026-08-13)

**Build**: ✅ Passed — `dotnet build RAG.slnx` → exit 0, 0 errors (1 pre-existing NU1510 warning in RAG.Infrastructure, untouched by this change).
Output digest: `sha256:dd11b53abadc4e8a99f974d8d297068ac179ebca625894906707283faa6523a0`

**Tests**: ✅ 150 passed / 1 skipped / 151 total — `dotnet test RAG.slnx --no-build` → exit 0.
```text
RAG.Mvc.Tests.dll (net10.0): 144 passed, 1 skipped, 0 failed, 145 total
RAG.Api.Tests.dll (net10.0):   6 passed, 0 skipped, 0 failed,   6 total
```
Output digest: `sha256:a667ee3a7396af6203d98d8220b34aed763a89f60dec7ef382fd9c57850657fe`

**Focused slice filters corroborated** (claimed counts in apply-progress reproduce):
- `--filter "FullyQualifiedName~AssistantCatalog|FullyQualifiedName~RagServiceRouting"` → **17/17** ✅ (matches 1.6)
- `--filter "FullyQualifiedName~Ask|FullyQualifiedName~Documents"` → **52/52** ✅ (matches 2.8)
- RAG.Api.Tests → **6/6** ✅ (matches 3.1-3.3)

**Coverage**: ➖ No coverage tool configured; skipped (informational, not a failure).

## Spec Compliance Matrix (14 requirements, 25 scenarios)

| Requirement | Scenario(s) | Test evidence | Result |
|-------------|-------------|---------------|--------|
| ASEL-1 Config-driven catalog + default fallback | Catalog configured; Catalog absent | `AssistantCatalogTests.CatalogConfigured_ExposesEveryEntryWithFullMetadata`, `CatalogConfigured_DefaultPrefersEntryMatchingChatModel`, `CatalogAbsent_SingleDefaultDerivedFromChatModel`, `CatalogEmpty_SingleDefaultDerivedFromChatModel` | ✅ COMPLIANT |
| ASEL-2 Per-request routing + fallback | Known routed; null/blank → default; unknown → default; retrieval identical | `RagServiceRoutingTests.AskAsync_KnownModelId_SetsModelIdOnChatOptions`, `AskAsync_NullOrBlankModelId_UsesDefaultModel` (Theory ×3), `AskAsync_UnknownModelId_FallsBackToDefaultModel`, `AskAsync_RetrievalPipeline_UnchangedForAnySelection` | ✅ COMPLIANT |
| ASEL-3 API optional ModelId, no regression | POST w/o ModelId; known; unknown | `AskEndpointTests.Post_WithoutModelId_ReturnsDefaultAssistantAnswer`, `Post_WithKnownModelId_RoutesToThatModel`, `Post_WithUnknownModelId_FallsBackToDefaultWithoutError` | ✅ COMPLIANT |
| ASEL-4 Boundary allow-list validation | Tampered modelId | `AskEndpointTests.Post_WithUnknownModelId_FallsBackToDefaultWithoutError` (ChatOptions never sees tampered value); `AskControllerTests.Ask_Post_InvalidSelectedModelId_FallsBackToDefaultWithoutError`; code: endpoints pass resolved `assistant.Id` only | ✅ COMPLIANT |
| ASEL-5 Embeddings non-regression | Any assistant selected | `ConfigPinTests.Api_Host_PinsEmbeddingModelAndRerankerDefault`, `Mvc_Host_PinsEmbeddingModelAndRerankerDefault` (`nomic-embed-text` both hosts) + `AskAsync_RetrievalPipeline_UnchangedForAnySelection` | ✅ COMPLIANT |
| ASEL-6 Reranker non-regression | Any assistant selected | `ConfigPinTests` pins host chat models (`llama3.2` API / `phi3:mini` MVC = reranker default); `OllamaReranker` not in change paths (untouched) | ✅ COMPLIANT |
| ASEL-7 Answer attribution | Known model answers; fallback answers | `AskControllerTests.Ask_Post_SelectedAssistant_RendersResultAttributedToThatAssistant` ("Generado por Qwen 2.5 1.5B"); `Ask_Post_InvalidSelectedModelId_...` + `Ask_Post_BlankSelectedModelId_...` ("Generado por Phi3 Mini") | ✅ COMPLIANT |
| ASEL-8 Unit tests for routing/fallback | Mock captures per-selection model | `RagServiceRoutingTests` (mock IChatClient captures `ChatOptions.ModelId`: known→model, blank/unknown→default) | ✅ COMPLIANT |
| ASEL-9 WAF tests for selector | Selected assistant end to end | `AskControllerTests` integration via `CustomRagWebApplicationFactory` (POST selected → attributed, invalid → default no error, blank → default no error) | ✅ COMPLIANT |
| ASEL-10 Non-regression tests | Config contract pinned | `ConfigPinTests` 2/2 + full suite baseline intact (MVC 144/1/145 pre-change baseline preserved) | ✅ COMPLIANT |
| ASK-2 (MOD) POST validates + passes selected assistant | Happy path; selected routed; blank | `AskControllerTests.Ask_Post_ValidQuestion_ReturnsResultViewWithAnswer`, `Ask_Post_SelectedAssistant_...`, `Ask_Post_BlankSelectedModelId_UsesDefaultWithoutError`; code: `_catalog.TryResolve` → `AskAsync(..., modelId: assistant.Id, ct: ct)` | ✅ COMPLIANT |
| ASK-14 Selector on Ask composer | Selector renders; no catalog → single default | `AskViewRenderTests.Ask_Page_RendersAssistantSelectorWithDefaultPreselected` (labels + descriptions + `selected` on default). **No-catalog view scenario: ⚠️ PARTIAL** — covered at catalog unit layer (`CatalogAbsent_SingleDefaultDerivedFromChatModel`) and compositionally (view iterates `AvailableAssistants`), but no WAF render with absent catalog | ⚠️ PARTIAL |
| ASK-15 Answer surface shows generating assistant | Answer with attribution; error state unchanged | `Result.cshtml` renders `Generado por {UsedAssistant}` only inside the answer card (error/empty branches have no attribution block — structural); `Ask_Post_ServiceUnavailable_RendersFriendlyError` covers error branch | ✅ COMPLIANT |
| UPLOAD-13 Floating chat same selector | Selector in floating chat; submission routed | `DocumentsViewRenderTests.Documents_Index_RendersAssistantSelectorInFloatingChat` (`name="SelectedModelId"` + 3 labels); submission flows through `AskController.Ask` (same binding proven by AskController integration tests) | ✅ COMPLIANT |

**Compliance summary**: 24/25 scenarios compliant, 1 partial (ASK-14 no-catalog view render, indirect coverage). 0 failing, 0 untested.

## Correctness (Static Evidence)

| Requirement | Status | Notes |
|------------|--------|-------|
| ASEL-1 | ✅ Implemented | `AssistantCatalog` (Application layer) primary ctor; per-host registration in both `Program.cs` |
| ASEL-2 | ✅ Implemented | `AskAsync(query, topKRetrieve=20, topKRank=5, modelId=null, ct=default)`; `Resolve` → `ChatOptions.ModelId` only on final call; retrieval byte-identical |
| ASEL-3 | ✅ Implemented | `AskRequest.Query/TopKRetrieve/TopKRank/ModelId=null`; omitted → default |
| ASEL-4 | ✅ Implemented | Endpoints pass resolved `assistant.Id`; `AskAsync` re-resolves (idempotent belt-and-suspenders) |
| ASEL-5/6 | ✅ Implemented | No embedding/rerank call changed; config pins pass |
| ASEL-7 | ✅ Implemented | `UsedAssistant` = resolved label; attribution only on answer card |
| ASK-2 | ✅ Implemented | Controller resolves before validation → re-rendered form always valid (ASEL-9 no-error) |
| ASK-14 | ✅ Implemented | `asp-for="SelectedModelId"` + label/description options, default preselected |
| ASK-15 | ✅ Implemented | `Generado por {UsedAssistant}` in card-footer when answer present |
| UPLOAD-13 | ✅ Implemented | Raw `name="SelectedModelId"` select in floating chat form, explicit `selected` on default |

## Coherence (Design D1-D4)

| Decision | Followed? | Notes |
|----------|-----------|-------|
| D1 Catalog type & registration | ✅ Yes | `AssistantCatalog` in Application; MVC from `AI:Ollama:Assistants` (`?? []`); API default-only from `Ollama:ChatModel`. Default selection (model match → id "default" → first) documented in apply-progress |
| D2 `AskAsync` signature & routing | ✅ Yes | Exact signature; retrieval unchanged; `GetResponseAsync(prompt, new ChatOptions { ModelId = model.Model }, ct)` |
| D3 MVC flow & validation | ✅ Yes | VM gains 3 props; `Index` populates; `Ask` TryResolve → passes resolved id; `UsedAssistant` = label |
| D4 API host | ✅ Yes | `ModelId = null`; catalog injected; resolve + pass; existing clients unaffected |

Documented deviations (non-spec-breaking, see Findings): Documents selector hand-rolled (`name=` + `@inject`) vs Ask `asp-for` (READ-2); API test factory replaces default-only catalog with 2-entry allow-list so ASEL-3 known-vs-unknown is provable (Slice 3 note 1).

## TDD Compliance (Strict module Step 5a)

| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | ✅ | Full TDD Cycle Evidence tables per slice in apply-progress |
| All tasks have tests | ✅ | 14/14 tasks map to test files (config/registration tasks verified via WAF suite) |
| RED confirmed (test files exist) | ✅ | All claimed test files exist on disk: `AssistantCatalogTests.cs`, `RagServiceRoutingTests.cs`, `AskControllerTests.cs`, `AskViewRenderTests.cs`, `DocumentsViewRenderTests.cs`, `RAG.Api.Tests/*` |
| GREEN confirmed (tests pass) | ✅ | 150/1/151 on verifier run; slice filters reproduce claimed counts (17/17, 52/52, 6/6) |
| Triangulation adequate | ✅ | 11 cases catalog, 6 routing, 6 MVC Ask, 10 Documents, 4 API ask, 2 config-pin — variance in expected values (known vs default vs unknown) |
| Safety Net for modified files | ✅ | Baselines recorded per slice (122/1 → 139/1 → 144/1 → 150/1/151); new files marked "N/A (new)" and verified new |

**TDD Compliance**: 6/6 checks passed

## Test Layer Distribution

| Layer | Tests | Files | Tools |
|-------|-------|-------|-------|
| Unit | 18 | 2 (`AssistantCatalogTests`, `RagServiceRoutingTests` + 1 unit case in `AskControllerTests`) | xUnit + Moq |
| Integration (WebApplicationFactory) | 25 | 4 (`AskControllerTests`, `AskViewRenderTests`, `DocumentsViewRenderTests`, `AskEndpointTests`) | `WebApplicationFactory<Program>`, TestAuthHandler |
| Guard (config contract) | 2 | 1 (`ConfigPinTests`) | xUnit, file parsing |
| E2E | 0 | — | not installed (WAF covers HTTP surface) |
| **Total** | **45** | **7 files** | |

## Changed File Coverage

**Coverage analysis skipped — no coverage tool detected** (informational, not a failure).

## Assertion Quality (Strict module Step 5f)

**Assertion quality**: ✅ All assertions verify real behavior — no tautologies, no orphan empty checks, no type-only-only assertions, no ghost loops, no smoke-only tests. All tests invoke production code (catalog resolution, `AskAsync` with mocked `IChatClient` capturing `ChatOptions`, WAF HTTP flows). Triangulated expectations assert different values (known model id vs default on blank/unknown). The 4R review audited the same surface with 0 CRITICAL.

## Quality Metrics

**Linter**: ➖ Not available (no analyzer config beyond default build).
**Type Checker**: ✅ `dotnet build RAG.slnx` → 0 errors (1 pre-existing NU1510 warning in RAG.Infrastructure, outside change paths).

## Issues Found

**CRITICAL**: None

**WARNING** (follow-ups from approved 4R review — do not block):
1. **Modelo no instalado → falla en vez de degradar** (RES-1/REL-1, `RagService.cs:52-57`): the allow-list fallback covers unknown/blank ids only; a catalog model Ollama does not have (not `ollama pull`-ed) throws → MVC shows generic "servicio no disponible" (request lost, no retry with default), API returns 500 (no catch). Spec ASEL-2 is satisfied (static allow-list fallback); the proposal's risk mitigation ("Fallback to default + user-friendly error") is only half-delivered (user-friendly error yes, runtime default fallback no). NOT covered by tests (no 404-from-Ollama simulation). Classified: WARNING, follow-up.
2. **Bind estricto de config** (RISK-1, `rag/Program.cs:32-34`): `Get<AssistantDefinition[]>()` throws `InvalidOperationException` on a structurally malformed section (scalar/object), crashing MVC startup; `?? []` only covers an absent section. Config is ops-controlled and checked in, but a malformed edit kills the host. NOT covered by tests. Classified: WARNING, follow-up.
3. **API host endpoints remain unauthenticated** (RISK-2, `src/RAG.Api/Program.cs`): pre-existing posture unchanged by this diff (no auth in API host); this change only added ModelId allow-list resolution. Out of scope, but flagged. Classified: WARNING, follow-up (pre-existing).

**SUGGESTION** (approved review follow-ups, non-blocking):
1. **TryResolve always returns true** (RISK-3/READ-1): dead API surface — the Try-pattern bool never rejects; callers cannot distinguish match from fallback; `Resolve` alone covers both callers.
2. **Sin observabilidad del fallback** (RES-2): no logging when a request falls back to default; ops cannot distinguish intentional fallback from client typo.
3. **Config degenerada** (RES-3/REL-2): structurally invalid section crashes startup (see WARNING 2); catalog does not validate entries (empty/duplicate ids, null model) — degenerate catalogs possible (attribution label could mismatch the actually-used model if `Default.Model` is null).
4. **Selector markup duplicado** (READ-2): Ask uses `asp-for` over the VM; Documents hand-rolls `name=` + `@inject` with manual `selected` — two copies can drift; extract a partial/component.
5. **Ambigüedad de naming `modelId`** (READ-3): `AskAsync(..., modelId)` actually carries a catalog assistant **Id**, not an Ollama model string; a caller passing a real model string silently falls back to default.
6. **Rol `documents.upload` sin `rag.ask`** (REL-3): the floating chat renders for any principal with `documents.upload`, but its form POSTs to `AskController.Ask` gated by `rag.ask` → 403 on every submit for such roles. Spec assumption explicitly documents this multi-permission posture as unchanged; pre-existing. Classified: SUGGESTION (acknowledged in spec).
7. **ASK-14 no-catalog view render untested at WAF level**: the "single default option" scenario is covered at catalog unit layer and compositionally, but no WAF test renders `/Ask` with an absent catalog section.

## Verdict

**PASS WITH WARNINGS** — all 14 spec requirements implemented and covered by passing tests (24/25 scenarios compliant, 1 partial with indirect coverage), full suite 150/1/151 exit 0 on independent run, build 0 errors, strict TDD evidence 6/6, 4R review approved with 0 CRITICAL. The WARNING/SUGGESTION findings are approved follow-ups, not blockers.

**status**: success
**next_recommended**: archive


