# Delta for mvc-document-upload

## ADDED Requirements

### Requirement: UPLOAD-11 — POST /Upload requires a valid antiforgery token

The `Upload` POST handler MUST validate an antiforgery token (`[ValidateAntiForgeryToken]`) on every submission, mirroring the repo's documented per-action CSRF posture (D5). A POST `Upload` without a valid `__RequestVerificationToken` in the multipart body MUST be rejected with HTTP 400 before any file validation or ingestion call. The multipart form already emits the token via the form tag helper. UPLOAD-1..UPLOAD-10 behavior MUST remain unchanged.

#### Scenario: Multipart POST with token succeeds

- GIVEN an authenticated user with `permission: documents.upload` and a `__RequestVerificationToken` present in the multipart body
- WHEN they POST a supported file to `/Documents/Upload`
- THEN the file is validated and passed to `IngestionService.IngestAsync` (UPLOAD-2 unchanged)

#### Scenario: Multipart POST without token rejected

- GIVEN an authenticated user with `permission: documents.upload` submitting multipart content with no token field
- WHEN the request reaches the `Upload` action
- THEN the server returns HTTP 400
- AND no file validation, parsing, or ingestion call is made

### Requirement: UPLOAD-12 — Server-side file validation errors render without JS

When the `Upload` POST handler registers a `ModelState` error for the `file` key (empty file, unsupported type, or oversize), the re-rendered form MUST display that error via a server-rendered message bound to the `file` field (`@Html.ValidationMessage("file")`), so it is visible with JavaScript disabled. The existing client-side script (`rag/Views/Documents/Upload.cshtml` `#file-validation`) MAY remain for interactive feedback, but MUST NOT be the only error surface.

#### Scenario: Empty-file error visible without JS

- GIVEN an authenticated user submits a 0-byte file with JavaScript disabled
- WHEN the POST handler rejects it and re-renders the form
- THEN the rendered view shows the empty-file error message, server-rendered and bound to the `file` field
- AND no ingestion call is made (UPLOAD-1 re-render unchanged)

#### Scenario: Unsupported-type error visible without JS

- GIVEN an authenticated user submits a `.exe` file with JavaScript disabled
- WHEN the POST handler rejects it and re-renders the form
- THEN the rendered view shows the unsupported-type error listing supported types (.cs, .md, .pdf) (UPLOAD-3 content unchanged), server-rendered

#### Scenario: Oversize error visible without JS

- GIVEN an authenticated user submits a file above the configured limit with JavaScript disabled
- WHEN the POST handler rejects it and re-renders the form
- THEN the rendered view shows the maximum-size error message (UPLOAD-4 content unchanged), server-rendered
- AND no ingestion call is made

## Assumptions

- Requirement IDs UPLOAD-1..UPLOAD-10 are unchanged; this delta adds a CSRF posture and server-rendered validation visibility.
- The multipart form tag helper already emits the token (`rag/Views/Documents/Upload.cshtml` `asp-action="Upload"`), so no form markup change is required for the token.
- Integration tests harvesting the token for multipart uploads must include the antiforgery field in the multipart content.