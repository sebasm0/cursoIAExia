# Delta for mvc-rag-ask

## ADDED Requirements

### Requirement: ASK-8 — Ask requires authentication and rag.ask permission

The Ask flow MUST require an authenticated principal with the `permission: rag.ask` claim, enforced via `[Authorize(Policy = "rag.ask")]`. Anonymous principals MUST be redirected to `/Account/Login` (with `returnUrl`). Authenticated principals without the permission MUST be routed to the access-denied page. This gating MUST apply before any RAG pipeline call.

#### Scenario: Anonymous user redirected to login

- GIVEN no authentication cookie
- WHEN the user requests the Ask page or submits a question
- THEN the response redirects to `/Account/Login?returnUrl=...`
- AND no RAG pipeline call is made

#### Scenario: User with rag.ask can ask

- GIVEN an authenticated principal with `permission: rag.ask`
- WHEN the user submits a valid question
- THEN the existing ASK-1..ASK-7 behavior applies (answer rendered with citations)

#### Scenario: User without rag.ask sees access denied

- GIVEN an authenticated principal without `permission: rag.ask`
- WHEN the user requests the Ask page
- THEN the access-denied page is shown (403)

## Assumptions

- Requirement IDs ASK-1..ASK-7 are unchanged; this delta only gates access to the existing flow.
