# mvc-rag-ask Specification

## Purpose

Web form for querying the RAG system. Users type a question and receive an AI-generated answer with context citations scoped to their documents.

## Requirements

| ID | Requirement | Strength |
|----|-------------|----------|
| ASK-1 | Render a GET form with a text input for the question | MUST |
| ASK-2 | POST handler calls `RagService.AskAsync(query, topKRetrieve, topKRank)` with user-scoped retrieval | MUST |
| ASK-3 | Display answer text alongside source document name and relevant excerpt per citation | MUST |
| ASK-4 | Show user-friendly error when RAG pipeline is unavailable (connection timeout, service down) | MUST |
| ASK-5 | Reject empty or whitespace-only queries with both client-side and server-side validation | MUST |
| ASK-6 | Scope search results to the current user's document space for multi-tenant isolation | SHOULD |
| ASK-7 | LLM provider (`IChatClient`, `IEmbeddingGenerator`) configurable via `appsettings.json` — not hardcoded | MUST |
| ASK-8 | Ask flow requires authentication and `rag.ask` permission; anonymous → login redirect, missing permission → access-denied, gate before pipeline | MUST |
| ASK-9 | Ask form follows the design system (UDS-1..UDS-4): token-based styling, shared layout with theme toggle, real copy; ASK-5 validation re-render unchanged | MUST |
| ASK-10 | Answer screen renders question echo, answer, citation sources, and "Ask another" per design system; ASK-1..ASK-8 behavior unchanged | MUST |
| ASK-11 | Service-unavailable view renders per design system with friendly copy and retry guidance; no stack traces (ASK-4 unchanged) | MUST |

### Scenario: Happy path — valid question answered

- GIVEN the RAG pipeline is healthy AND the user's document space has indexed content
- WHEN the user submits a non-empty question via the POST form
- THEN the system renders a view with the generated answer text
- AND each citation displays the source document name and a relevant text excerpt
- AND the response completes within a reasonable timeout (default 60 s)

### Scenario: Empty query rejected

- GIVEN the user submits a blank or whitespace-only query
- WHEN the POST handler validates the input
- THEN the form re-renders with a validation error message
- AND no RAG pipeline call is made

### Scenario: Service unavailable

- GIVEN pgvector, Ollama, or the configured LLM provider is unreachable
- WHEN the user submits a question
- THEN the system returns a user-friendly error view explaining the service is temporarily unavailable
- AND suggests retrying later

### Scenario: Sequential questions — stateless

- GIVEN the user received an answer for one question
- WHEN they submit a new, unrelated question
- THEN the system processes it independently with a fresh RAG pipeline call
- AND no conversation state persists between questions

### Scenario: Configurable LLM provider

- GIVEN `appsettings.json` configures a different `IChatClient` provider (e.g., OpenAI, Azure OpenAI)
- WHEN the user submits a question
- THEN `RagService.AskAsync` uses the configured provider for answer generation
- AND the system is NOT hardcoded to use Ollama

### Scenario: Multi-tenant isolation

- GIVEN User A and User B each have separate document spaces
- WHEN User A asks a question
- THEN only documents belonging to User A are searched
- AND User B's documents do not influence User A's results

### Scenario: Anonymous user redirected to login

- GIVEN no authentication cookie
- WHEN the user requests the Ask page or submits a question
- THEN the response redirects to `/Account/Login?returnUrl=...`
- AND no RAG pipeline call is made

### Scenario: User with rag.ask can ask

- GIVEN an authenticated principal with `permission: rag.ask`
- WHEN the user submits a valid question
- THEN the existing ASK-1..ASK-7 behavior applies (answer rendered with citations)

### Scenario: User without rag.ask sees access denied

- GIVEN an authenticated principal without `permission: rag.ask`
- WHEN the user requests the Ask page
- THEN the access-denied page is shown (403)

### Scenario: Ask screen renders per design system

- GIVEN the light or dark theme
- WHEN an authorized user opens the Ask page
- THEN the query form renders with token-based styling and the shared layout
- AND no placeholder copy is displayed

### Scenario: Validation error on the form

- GIVEN the user submits a blank query
- WHEN the POST handler validates
- THEN the form re-renders in the design system with the validation error
- AND no RAG pipeline call is made

### Scenario: Answer with citations rendered

- GIVEN a successful RAG response
- WHEN the result view renders
- THEN the question echo and answer text are displayed with token-based styling
- AND each citation shows the source document name and a relevant excerpt

### Scenario: Ask another action

- GIVEN the answer view
- WHEN the user selects "Ask another"
- THEN the system returns to the Ask form
- AND no conversation state persists (ASK-3 unchanged)

### Scenario: Service-unavailable styled error

- GIVEN the RAG pipeline is unreachable
- WHEN the Ask POST fails
- THEN a token-styled error view explains the service is temporarily unavailable
- AND suggests retrying later
