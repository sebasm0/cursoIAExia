# Delta for mvc-rag-ask

## MODIFIED Requirements

### Requirement: ASK-2 — POST handler calls RagService.AskAsync with user-scoped retrieval and the selected assistant

The POST handler MUST validate the submitted `SelectedModelId` against the assistant catalog and call `RagService.AskAsync(query, topKRetrieve, topKRank, modelId, ct)` with user-scoped retrieval. Existing ASK-1, ASK-3..ASK-13 behavior MUST remain unchanged.
(Previously: `AskAsync(query, topKRetrieve, topKRank)` — no assistant parameter.)

#### Scenario: Happy path — valid question answered

- GIVEN the RAG pipeline is healthy AND the user's document space has indexed content
- WHEN the user submits a non-empty question via the POST form
- THEN the system renders a view with the generated answer (ASK-2/ASK-3 unchanged)

#### Scenario: Selected assistant routed

- GIVEN the user selected a catalog assistant on the form
- WHEN the POST handler runs
- THEN `modelId` is validated against the catalog and passed to `AskAsync`

#### Scenario: Blank selection

- GIVEN the user submits without selecting an assistant
- WHEN the POST handler runs
- THEN the default assistant is used (no error)

## ADDED Requirements

### Requirement: ASK-14 — Assistant selector on the Ask composer

The Ask form MUST render a selector listing the catalog assistants (label + description) with the default preselected. GET `/Ask` MUST supply the catalog via the view model; POST MUST bind the selection. Without a catalog, the selector MUST render a single default option.

#### Scenario: Selector renders

- GIVEN the user opens `/Ask`
- THEN the composer shows the assistant selector with catalog options and the default preselected

#### Scenario: No catalog

- GIVEN no `AI:Ollama:Assistants` section
- WHEN `/Ask` renders
- THEN the selector shows a single default option derived from `ChatModel`

### Requirement: ASK-15 — Answer surface shows generating assistant

The result view MUST display the label of the assistant that generated the answer. Error and empty states MUST render unchanged (ASK-4/ASK-13).

#### Scenario: Answer with attribution

- GIVEN a successful RAG response
- WHEN the result view renders
- THEN the generating assistant's label is displayed with the answer

#### Scenario: Error state unchanged

- GIVEN the RAG pipeline is unreachable
- WHEN the error view renders
- THEN the error renders as before with no attribution block

## Assumptions

- Requirement IDs ASK-1..ASK-13 are unchanged except ASK-2; this delta modifies ASK-2 and adds ASK-14/ASK-15 only.
- The selector reuses the same catalog as `assistant-selection` (ASEL-1) — single source of truth.
- Attribution copy is "Generado por {label}" (Spanish UI, neutral/professional register).