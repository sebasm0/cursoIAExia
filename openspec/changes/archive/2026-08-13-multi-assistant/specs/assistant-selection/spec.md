# assistant-selection Specification

## Purpose

Per-question choice among local Ollama chat models (`phi3:mini` default, `qwen2.5:1.5b`, `llama3.2:1b`). Choice is per request, never persisted. Embeddings and reranker are untouched; only final answer generation is routed.

## Requirements

| ID | Requirement | Strength |
|----|-------------|----------|
| ASEL-1 | Config-driven assistant catalog with default fallback | MUST |
| ASEL-2 | Per-request model routing with default fallback | MUST |
| ASEL-3 | API host accepts optional ModelId without regression | MUST |
| ASEL-4 | Boundary allow-list validation | MUST |
| ASEL-5 | Embeddings non-regression | MUST |
| ASEL-6 | Reranker non-regression | MUST |
| ASEL-7 | Answer attribution | MUST |
| ASEL-8 | Unit tests for routing and fallback | MUST |
| ASEL-9 | WebApplicationFactory tests for the selector | MUST |
| ASEL-10 | Non-regression tests | MUST |

### Requirement: ASEL-1 — Config-driven assistant catalog with default fallback

The system MUST load an assistant catalog from `AI:Ollama:Assistants` in `appsettings.json`; each entry MUST provide `id`, `label`, `model`, and `description`. When the catalog is absent or empty, the system MUST expose a single default assistant derived from `AI:Ollama:ChatModel` (backward compatible with current behavior).

#### Scenario: Catalog configured

- GIVEN `AI:Ollama:Assistants` defines at least one entry
- WHEN the app reads the catalog
- THEN each assistant exposes id, label, model, and description

#### Scenario: Catalog absent

- GIVEN no `AI:Ollama:Assistants` section in `appsettings.json`
- WHEN the app reads the catalog
- THEN a single default assistant is derived from `AI:Ollama:ChatModel`

### Requirement: ASEL-2 — Per-request model routing with default fallback

`RagService.AskAsync` MUST accept an optional `modelId`. Null/whitespace `modelId` MUST use the default assistant. A `modelId` NOT in the catalog MUST fall back to the default without error. A known `modelId` MUST route final answer generation via `ChatOptions.ModelId` on the `IChatClient` call. Retrieval (embedding, hybrid search, rerank) MUST be identical regardless of selection.

#### Scenario: Known model routed

- GIVEN a catalog `modelId` mapped to an Ollama model
- WHEN `AskAsync` runs with that `modelId`
- THEN `GetResponseAsync` receives `ChatOptions` with `ModelId` set to the mapped model

#### Scenario: Null or blank modelId

- GIVEN `modelId` is null or whitespace
- WHEN `AskAsync` runs
- THEN the default assistant is used and no error is surfaced

#### Scenario: Unknown modelId

- GIVEN a `modelId` not present in the catalog
- WHEN `AskAsync` runs
- THEN the default assistant is used and the request completes without error

### Requirement: ASEL-3 — API host accepts optional ModelId without regression

`AskRequest` (POST `/api/rag/ask`) MUST gain an optional `ModelId` property defaulting to `null`. Omitted `ModelId` MUST behave exactly as today (default assistant). Present `ModelId` MUST be validated against the catalog and routed per ASEL-2. Existing clients MUST NOT break.

#### Scenario: POST without ModelId

- GIVEN a POST to `/api/rag/ask` with only `Query`
- WHEN the endpoint runs
- THEN the response is 200 with the default-assistant answer (current behavior)

#### Scenario: POST with known ModelId

- GIVEN a POST with a catalog `ModelId`
- WHEN the endpoint runs
- THEN the answer is generated with that model

#### Scenario: POST with unknown ModelId

- GIVEN a POST with a `ModelId` outside the catalog
- WHEN the endpoint runs
- THEN the response is 200 and the default assistant answers

### Requirement: ASEL-4 — Boundary allow-list validation

Any `modelId` arriving from an HTTP boundary (MVC POST or API) MUST be validated against the catalog allow-list before reaching the chat client. A value outside the allow-list MUST NOT be passed to Ollama; it MUST resolve to the default.

#### Scenario: Tampered modelId

- GIVEN a POST carries a `modelId` not in the catalog
- WHEN the handler validates it
- THEN the chat client never receives the tampered value and the default model answers

### Requirement: ASEL-5 — Embeddings non-regression

The embedding generator MUST remain `nomic-embed-text` (from `AI:Ollama:EmbeddingModel`); the vector store MUST NOT be invalidated. Assistant selection MUST NOT change any embedding call.

#### Scenario: Any assistant selected

- GIVEN a request routed to any catalog model
- WHEN the RAG pipeline runs
- THEN embeddings use `nomic-embed-text` and the vector store contract is unchanged

### Requirement: ASEL-6 — Reranker non-regression

The reranker MUST remain unchanged and keep using the default chat model (`OllamaReranker` untouched). Assistant selection MUST NOT alter reranking.

#### Scenario: Any assistant selected

- GIVEN a request routed to any catalog model
- WHEN the RAG pipeline runs
- THEN reranking behavior and model configuration are unchanged

### Requirement: ASEL-7 — Answer attribution

The Ask flow MUST identify which assistant generated the answer so the UI can display it (used assistant label). When the default is used via fallback, attribution MUST still show the default assistant.

#### Scenario: Known model answers

- GIVEN a request routed to a catalog model
- WHEN the answer is produced
- THEN the answer surface shows that assistant's label

#### Scenario: Fallback answers

- GIVEN a blank or unknown selection
- WHEN the default assistant produces the answer
- THEN the answer surface shows the default assistant's label

### Requirement: ASEL-8 — Unit tests for routing and fallback

Unit tests with a mocked `IChatClient` MUST verify: a known catalog `modelId` reaches `ChatOptions.ModelId`; null/blank and unknown `modelId` fall back to the default; no value outside the allow-list reaches the chat client.

#### Scenario: Mock captures per-selection model

- GIVEN a mock `IChatClient` recording `ChatOptions`
- WHEN `AskAsync` runs with known, blank, and unknown `modelId`
- THEN `ChatOptions.ModelId` matches the known selection and is default/null for blank and unknown

### Requirement: ASEL-9 — WebApplicationFactory tests for the selector

`RAG.Mvc.Tests` MUST cover the Ask flow with the selector through WebApplicationFactory: POSTing a selected assistant renders the answer with attribution; an invalid selection falls back without error.

#### Scenario: Selected assistant end to end

- GIVEN an authenticated client on the Ask flow
- WHEN it POSTs `Query` with a catalog `SelectedModelId`
- THEN the result view renders the answer attributed to that assistant

### Requirement: ASEL-10 — Non-regression tests

Tests MUST assert the embeddings model (`nomic-embed-text`) and the reranker default model remain unchanged after this change, guarding the vector-store contract.

#### Scenario: Config contract pinned

- GIVEN the change is implemented
- WHEN the test suite runs
- THEN embedding and reranker configuration assertions pass unchanged

## Assumptions

- The API host DTO (`AskRequest`) has no dedicated spec file; its change is modeled under `assistant-selection` (ASEL-3/ASEL-4).
- `ollama pull qwen2.5:1.5b` and `llama3.2:1b` are ops pre-flight (out of scope, per proposal).
- Requirement IDs ASK-1..ASK-13 and UPLOAD-1..UPLOAD-12 are unchanged except ASK-2 (see `mvc-rag-ask` delta).