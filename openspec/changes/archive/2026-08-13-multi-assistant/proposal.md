# Proposal: multi-assistant — selectable Ollama chat models per question

## Intent

Hardware without GPU: wired-in `phi3:mini` takes ~2.3 min per RAG pipeline. Users need per-question choice among local Ollama assistants: `phi3:mini` (default, quality), `qwen2.5:1.5b` (fast), `llama3.2:1b` (fastest). Choice applies per question, nothing persisted.

## Scope

### In Scope
- Config-driven assistant catalog (`AI:Ollama:Assistants`) in `rag/appsettings.json`; existing `ChatModel` stays the default
- Per-request routing: `RagService.AskAsync(..., string? modelId = null, ...)` → `ChatOptions.ModelId`
- Allow-list validation; unknown/blank → default
- Selector UI on `/Ask` composer and Documents floating chat (`rag/Views/Documents/Index.cshtml:205` posts to `AskController.Ask`)
- Assistant attribution on the answer surface
- `RagEndpoints.AskRequest` gains optional `ModelId` (default null)
- Strict TDD: routing/fallback unit + WebApplicationFactory Ask tests

### Out of Scope
- Embeddings stay `nomic-embed-text` — vector store MUST NOT be invalidated
- Reranker stays on default model (`OllamaReranker` unchanged)
- No per-model persistence, streaming, or analytics
- No install UI (`ollama pull` is ops)

## Capabilities

### New Capabilities
- `assistant-selection`: config-driven assistant catalog, per-request model routing, allow-list validation with default fallback, answer attribution.

### Modified Capabilities
- `mvc-rag-ask`: Ask form + result surface gain assistant selector and attribution; POST passes selected model.
- `mvc-document-upload`: embedded chat panel on the Documents landing page gains the same selector (same Ask flow).

## Approach

Config-driven list (id/label/model/description) from `appsettings.json`, registered in `rag/Program.cs`. `RagService.AskAsync` accepts optional `modelId`; non-null and on the allow-list → `GetResponseAsync(prompt, new ChatOptions { ModelId = modelId }, ct)`. Controller validates against the catalog; invalid/blank → default. `AskViewModel` carries `AvailableAssistants` / `SelectedModelId` / `UsedAssistant`. Embeddings and reranker untouched.

## Test Impact

- Update `AskControllerTests` + `AskViewRenderTests`; add: model id via `ChatOptions`, invalid → default, blank → default; catalog validation tests

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| New model not pulled → Ollama 404 per request | Med | Fallback to default + user-friendly error; document `ollama pull` |
| CPU contention from new models | Low-Med | Per-question only, no persistence; reranker stays default |
| Tampered/unknown model id | Med | Allow-list validation, fallback default |
| API host regression | Low | Optional param with default null; tests cover endpoint |

## Rollback

Remove catalog config + `modelId` param, restore `GetResponseAsync(prompt, ct)`, revert view blocks. No migration/schema; vector store untouched.

## Dependencies

- `ollama pull` of both new models (ops, pre-flight)
- Per-request `ChatOptions.ModelId` (verified)

## Success Criteria

- [ ] Selector renders on `/Ask` and Documents chat; choice applies per question
- [ ] Mocked `IChatClient` captures `ChatOptions.ModelId` per selection
- [ ] Answer surface shows the generating assistant
- [ ] Embeddings + reranker models unchanged; vector store intact
- [ ] API `/ask` works; `dotnet test tests/RAG.Mvc.Tests` green