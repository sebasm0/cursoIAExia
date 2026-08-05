# Delta for mvc-rag-ask

## ADDED Requirements

### Requirement: ASK-12 — POST /Ask requires a valid antiforgery token

The `Ask` POST handler MUST validate an antiforgery token (`[ValidateAntiForgeryToken]`) on every submission, mirroring the repo's documented per-action CSRF posture (D5). A POST `Ask` without a valid `__RequestVerificationToken` MUST be rejected with HTTP 400 before any RAG pipeline call. The GET form already emits the token via the form tag helper; no view change is required. ASK-1..ASK-11 behavior MUST remain unchanged.

#### Scenario: POST with token succeeds

- GIVEN an authenticated user with `permission: rag.ask` and a form-rendered `__RequestVerificationToken`
- WHEN they POST a valid question to `/Ask` with the token present
- THEN the request is processed and the result view renders (ASK-2/ASK-3 unchanged)

#### Scenario: POST without token rejected

- GIVEN an authenticated user with `permission: rag.ask` issuing a POST without a token
- WHEN the request reaches the `Ask` action
- THEN the server returns HTTP 400
- AND no RAG pipeline call is made

### Requirement: ASK-13 — Empty-answer result renders a non-blank fallback

When `Ask` produces neither an `ErrorMessage` nor an `Answer`, the result view MUST render a non-blank fallback state so the page is never blank. The fallback MUST be the `else` branch of the existing error/answer checks so it cannot collide with either populated branch. ASK-3/ASK-4 rendering MUST remain unchanged for populated states.

#### Scenario: Empty response renders fallback

- GIVEN `ErrorMessage` and `Answer` are both empty after the Ask POST
- WHEN the result view renders
- THEN a non-blank fallback message is displayed in place of an empty page
- AND the retry / back-navigation actions remain available

#### Scenario: Populated states unchanged

- GIVEN `ErrorMessage` or `Answer` is populated
- WHEN the result view renders
- THEN the existing error or answer branch renders exactly as before (ASK-3/ASK-4 unchanged)

## Assumptions

- Requirement IDs ASK-1..ASK-11 are unchanged; this delta adds a CSRF posture and the empty-answer fallback only.
- The form tag helper already emits the token (`rag/Views/Ask/Index.cshtml` `asp-action="Ask"`), so no form markup change is required.
- `[ValidateAntiForgeryToken]` is applied per action only; no global filter is introduced (`Program.cs` unchanged).