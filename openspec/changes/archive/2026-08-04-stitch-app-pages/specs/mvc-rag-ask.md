# Delta for mvc-rag-ask

## ADDED Requirements

### Requirement: ASK-9 — Ask screen follows the design system

The Ask form view MUST follow the design system (UDS-1..UDS-4): token-based styling, shared layout with theme toggle, and real copy. The existing query-validation behavior (ASK-5) MUST remain unchanged: validation errors render on the form.

#### Scenario: Ask screen renders per design system

- GIVEN the light or dark theme
- WHEN an authorized user opens the Ask page
- THEN the query form renders with token-based styling and the shared layout
- AND no placeholder copy is displayed

#### Scenario: Validation error on the form

- GIVEN the user submits a blank query
- WHEN the POST handler validates
- THEN the form re-renders in the design system with the validation error
- AND no RAG pipeline call is made

### Requirement: ASK-10 — Answer screen renders question and answer

The Ask result view MUST display the question echo, the generated answer, and citation sources per the design system, with an "Ask another" action. Behavior of ASK-1..ASK-8 MUST be unchanged.

#### Scenario: Answer with citations rendered

- GIVEN a successful RAG response
- WHEN the result view renders
- THEN the question echo and answer text are displayed with token-based styling
- AND each citation shows the source document name and a relevant excerpt

#### Scenario: Ask another action

- GIVEN the answer view
- WHEN the user selects "Ask another"
- THEN the system returns to the Ask form
- AND no conversation state persists (ASK-3 unchanged)

### Requirement: ASK-11 — Service-unavailable state per design system

The service-unavailable error view (RAG pipeline down) MUST render per the design system with a user-friendly message and retry guidance. It MUST NOT expose stack traces or internal details (ASK-4 unchanged).

#### Scenario: Service-unavailable styled error

- GIVEN the RAG pipeline is unreachable
- WHEN the Ask POST fails
- THEN a token-styled error view explains the service is temporarily unavailable
- AND suggests retrying later

## Assumptions

- Requirement IDs ASK-1..ASK-8 are unchanged; this delta adds UI presentation requirements only.
- No controller, service, or model changes.
