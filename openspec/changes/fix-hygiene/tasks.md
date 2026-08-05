# Tasks: fix-hygiene — POST security and UX follow-ups

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~180 (≈25 production, ≈155 tests) |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Chain strategy | pending |
| Delivery strategy | ask-always |
| Decision needed before apply | No |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

### Suggested Work Units

Single PR (under budget); commit-level slices only. Runtime harness: `dotnet run --project rag` — POST Ask/Upload with & without token, delete with JS off. Rollback: per-file revert (attribute/views/css), no schema impact.

Every filter below runs as `dotnet test tests/RAG.Mvc.Tests --filter "<filter>"`.

## Phase 1: Test infra (prerequisite)

- [x] 1.1 Add `AccountTestHelpers.CreateMultipartPost(url, token, fileName, content)` in `tests/RAG.Mvc.Tests/Auth/AccountFlowTestFactory.cs`: `MultipartFormDataContent` incl. `__RequestVerificationToken` field (mirrors `CreatePost`). Infra-only. Filter: `FullyQualifiedName~AdminUserFlowTests`

## Phase 2: CSRF guards (ASK-12, UPLOAD-11)

Ordering: option (a) — helper first, then per-controller RED→GREEN with existing-POST updates bundled with each attribute, so the build is green at every task end.

- [x] 2.1 RED: `Ask_Post_WithoutToken_ReturnsBadRequest` in `AskControllerTests.cs` — POST `/Ask/Ask`, no token, expect 400. Fails (200). Filter: `~AskControllerTests`
- [x] 2.2 Update 4 Ask POST tests (`AskControllerTests.Ask_Post_ValidQuestion`; `AskViewRenderTests` EmptyQuery/ValidQuestion/ServiceUnavailable) to harvest via `GetAntiforgeryTokenAsync("/Ask")` + `CreatePost("/Ask/Ask", ...)`.
- [x] 2.3 GREEN: `[ValidateAntiForgeryToken]` on `AskController.Ask` (`rag/Controllers/AskController.cs:31`). Filter: `~AskControllerTests|AskViewRenderTests`
- [x] 2.4 RED: `Upload_Post_WithoutToken_ReturnsBadRequest` in `DocumentsControllerTests.cs` — multipart POST `/Documents/Upload`, no token, expect 400. Fails (200). Filter: `~DocumentsControllerTests`
- [x] 2.5 Update 4 Upload POST tests (`DocumentsControllerTests.Upload_Post_ValidCsFile`; `DocumentsViewRenderTests` UnsupportedFile/Success/Error) to append token via helper 1.1.
- [x] 2.6 GREEN: `[ValidateAntiForgeryToken]` on `DocumentsController.Upload` (`rag/Controllers/DocumentsController.cs:55`). Filter: `~DocumentsControllerTests|DocumentsViewRenderTests`

## Phase 3: Upload validation visibility (UPLOAD-12)

- [x] 3.1 RED: Extend `Upload_Post_UnsupportedFile_ReRendersFormWithSupportedTypesError` (`DocumentsViewRenderTests.cs`): assert `field-validation-error` + `data-valmsg-for="file"` in re-render (error invisible server-side today). Fails. Filter: `~DocumentsViewRenderTests`
- [x] 3.2 GREEN: `rag/Views/Documents/Upload.cshtml:26` → `<span id="file-validation" asp-validation-for="file" class="text-danger"></span>` (keeps `#file-validation` JS target). Filter: `~DocumentsViewRenderTests`

## Phase 4: Ask empty-answer fallback (ASK-13)

- [x] 4.1 RED: `Ask_Post_EmptyResponse_RendersFallback` + `EmptyAnswerRagWebApplicationFactory` (chat returns "") in `AskViewRenderTests.cs`; POST with token, assert fallback + "Try Again". Fails (blank). Filter: `~AskViewRenderTests`
- [x] 4.2 GREEN: `rag/Views/Ask/Result.cshtml` add `else` branch after Answer branch: fallback card ("No answer was generated...") with Try Again + Back to Home. Filter: `~AskViewRenderTests`

## Phase 5: No-JS delete fallback (ADMIN-10)

- [ ] 5.1 RED: `AdminUsersViewRenderTests.cs` + `AdminRolesViewRenderTests.cs`: assert `<noscript>` with `type="submit"` button inside each delete form. Fails. Filter: `~AdminUsersViewRenderTests|AdminRolesViewRenderTests`
- [ ] 5.2 GREEN: `<noscript><button type="submit" class="btn btn-outline-danger btn-sm">Delete</button></noscript>` inside both delete forms (`rag/Views/Admin/Users/Index.cshtml:42`, `rag/Views/Admin/Roles/Index.cshtml:41`). Modal path untouched. Filter: `~AdminUsersViewRenderTests|AdminRolesViewRenderTests|ConfirmModalRenderTests` — existing `AdminUserFlowTests` direct POSTs already prove the no-JS destructive path.

## Phase 6: Scoped brand colors (UDS-8)

- [ ] 6.1 RED: `LayoutScopedCssTests.cs` (`tests/RAG.Mvc.Tests/Views/`): locate `rag/Views/Shared/_Layout.cshtml.css` walking up from `AppContext.BaseDirectory`; assert no `#0077cc`/`#1b6ec2`/`#1861ac`. Fails. Filter: `~LayoutScopedCss`
- [ ] 6.2 GREEN: `_Layout.cshtml.css:10-24` → `a { color: var(--bs-link-color) }`; `.btn-primary`/`.nav-pills` → `var(--bs-btn-bg)`+`var(--bs-btn-border-color)` (or `var(--bs-primary)`); keep `color: #fff`. Filter: `~LayoutScopedCss`

## Phase 7: Full verification

- [ ] 7.1 Full suite: `dotnet test` — all green; proposal success criteria met.
- [ ] 7.2 Manual smoke: `dotnet run --project rag` — with/without token, JS-off delete, dark-theme colors.
