# Delta for mvc-document-upload

## ADDED Requirements

### Requirement: UPLOAD-9 — Upload requires authentication and documents.upload permission

The Upload flow MUST require an authenticated principal with the `permission: documents.upload` claim, enforced via `[Authorize(Policy = "documents.upload")]`. Anonymous principals MUST be redirected to `/Account/Login` (with `returnUrl`). Authenticated principals without the permission MUST be routed to the access-denied page. This gating MUST apply before any ingestion call.

#### Scenario: Anonymous user redirected to login

- GIVEN no authentication cookie
- WHEN the user requests the upload page or submits a file
- THEN the response redirects to `/Account/Login?returnUrl=...`
- AND no ingestion pipeline call is made

#### Scenario: User with documents.upload can upload

- GIVEN an authenticated principal with `permission: documents.upload`
- WHEN the user uploads a supported file
- THEN the existing UPLOAD-1..UPLOAD-8 behavior applies (ingest + success view)

#### Scenario: User without documents.upload sees access denied

- GIVEN an authenticated principal without `permission: documents.upload`
- WHEN the user submits the upload form
- THEN the access-denied page is shown (403)
- AND no file is persisted

## Assumptions

- Requirement IDs UPLOAD-1..UPLOAD-8 are unchanged; this delta only gates access to the existing flow.
- The background directory scanner (UPLOAD-8) is a server-side flow and is not subject to the web auth gate.
