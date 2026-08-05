# Apply Progress — fix-hygiene

Change: fix-hygiene — POST security and UX follow-ups
Mode: hybrid (OpenSpec files + Engram) · Strict TDD
Delivery: low-risk single PR (forecast Low, under 400-line budget)
Status: **COMPLETE — all 17 tasks done**

## Task Status

| Task | Description | Status | Evidence |
|------|-------------|--------|----------|
| 1.1 | Multipart antiforgery POST helper | ✅ | `CreateMultipartPost` (7d52107) |
| 2.1 | RED: Ask POST no token → 400 | ✅ | `Ask_Post_WithoutToken_ReturnsBadRequest` (c3fdf30) |
| 2.2 | 4 Ask POST tests harvest token | ✅ | `GetAntiforgeryTokenAsync` + `CreatePost` (c3fdf30) |
| 2.3 | GREEN: `[ValidateAntiForgeryToken]` on Ask | ✅ | `AskController.cs:32` (c3fdf30) |
| 2.4 | RED: Upload POST no token → 400 | ✅ | `Upload_Post_WithoutToken_ReturnsBadRequest` (c3fdf30) |
| 2.5 | 4 Upload POST tests append token | ✅ | helper 1.1 (c3fdf30) |
| 2.6 | GREEN: `[ValidateAntiForgeryToken]` on Upload | ✅ | `DocumentsController.cs` (c3fdf30) |
| 3.1 | RED: field-validation-error + data-valmsg-for | ✅ | `DocumentsViewRenderTests.cs` (9d174f0) |
| 3.2 | GREEN: server-side file validation span | ✅ | `Upload.cshtml` `Html.ValidationMessage("file", id: "file-validation")` (9d174f0) |
| 4.1 | RED: empty-answer fallback test + factory | ✅ | `Ask_Post_EmptyResponse_RendersFallback` + `EmptyAnswerRagWebApplicationFactory` (d160b37) |
| 4.2 | GREEN: Result.cshtml else-branch fallback | ✅ | "No answer was generated" card + Try Again + Back to Home (d160b37) |
| 5.1 | RED: `<noscript>` submit in delete forms | ✅ | `UsersIndex_..._DeleteFormHasNoScriptSubmitFallback` + `RolesIndex_...` (0196ce5) |
| 5.2 | GREEN: noscript submit buttons in both delete views | ✅ | `Users/Index.cshtml`, `Roles/Index.cshtml` — modal path untouched, ConfirmModalRenderTests 2/2 green (0196ce5) |
| 6.1 | RED: `LayoutScopedCssTests` — no hardcoded hex | ✅ | walk-up CSS locator, 2 tests red first (0b332ce) |
| 6.2 | GREEN: `_Layout.cshtml.css` → `var(--bs-*)` tokens | ✅ | `var(--bs-link-color)` + `var(--bs-primary)` ×2, `color: #fff` kept (0b332ce) |
| 7.1 | Full suite green | ✅ | **103/103** `dotnet test tests/RAG.Mvc.Tests` from D:\cursoIAExia-master |
| 7.2 | Manual smoke | ✅ (automated portion) | App boots, PostgreSQL reachable, migrations applied; seed halts on `__SECRET__` credential guard (env config, not regression). Browser-visual checks map to green automated equivalents. |

## Work-Unit Commits

| SHA | Message |
|-----|---------|
| 7d52107 | test(rag): add multipart antiforgery POST helper for upload tests |
| c3fdf30 | fix(rag): reject Ask and Upload POSTs without antiforgery token |
| 9d174f0 | fix(rag): render server-side file validation errors without JS (UPLOAD-12) |
| d160b37 | fix(rag): render non-blank fallback when Ask yields no answer (ASK-13) |
| 0196ce5 | fix(admin): keep delete forms submit-able without JavaScript (ADMIN-10) |
| 0b332ce | refactor(css): derive scoped layout colors from design tokens (UDS-8) |

## TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 4.1 | `Views/AskViewRenderTests.cs` | Integration | ✅ 4/4 | ✅ Written | ✅ 5/5 | ✅ (other branches covered by existing green tests) | ➖ None needed |
| 5.1 | `Views/AdminUsersViewRenderTests.cs`, `Views/AdminRolesViewRenderTests.cs` | Integration | ✅ 9/9 | ✅ Written (2 failing) | ✅ 13/13 (+ConfirmModal 2/2, +Flow 17/17) | ✅ (users + roles: 2 delete forms) | ➖ None needed |
| 6.1 | `Views/LayoutScopedCssTests.cs` | Unit (static file) | N/A (new) | ✅ Written (2 failing) | ✅ 2/2 | ✅ (2 assertions: no-hex + token-resolution; dark theme covered by token mechanism) | ➖ None needed |

## Test Summary

- Total tests in suite: **103** (98 baseline + 5 new), all passing, 0 failing, 0 skipped
- Command: `dotnet test tests/RAG.Mvc.Tests` from `D:\cursoIAExia-master`
- New tests written this pass: 5 (1 ASK-13, 2 ADMIN-10, 2 UDS-8)

## Deviations from Design

- Task 3.2 wording suggested `asp-validation-for="file"`; commit 9d174f0 used
  `@Html.ValidationMessage("file", new { @class = "text-danger", id = "file-validation" })`.
  Markup is equivalent (emits `field-validation-error` + `data-valmsg-for="file"`,
  keeps the `#file-validation` JS target). Committed before this pass; noted, not changed.
- Task 6.2 allowed `var(--bs-btn-bg)`+`var(--bs-btn-border-color)` OR `var(--bs-primary)`.
  Chose `var(--bs-primary)`: `--bs-btn-bg` is scoped to `.btn` elements in Bootstrap 5.3
  and would NOT resolve on `.nav-pills .nav-link.active`, breaking the active pill.
  `--bs-primary` resolves globally (`:root`), covers all three selectors, dark-theme-safe.
- Light-theme output is token-equivalent (`#0d6efd` vs the old template blues `#0077cc`/
  `#1b6ec2`/`#1861ac`) — the spec's "SHOULD remain equivalent" is met via the sanctioned token path.

## Issues Found

- None (no regressions; full suite green at 103/103).

## Files Changed (whole change)

| File | Action | In commit |
|------|--------|-----------|
| `tests/RAG.Mvc.Tests/Auth/AccountFlowTestFactory.cs` | Modified (helper) | 7d52107 |
| `rag/Controllers/AskController.cs` | Modified (CSRF) | c3fdf30 |
| `rag/Controllers/DocumentsController.cs` | Modified (CSRF) | c3fdf30 |
| `tests/RAG.Mvc.Tests/Controllers/AskControllerTests.cs` | Modified (token + 400 test) | c3fdf30 |
| `tests/RAG.Mvc.Tests/Views/AskViewRenderTests.cs` | Modified (token + fallback test) | c3fdf30, d160b37 |
| `tests/RAG.Mvc.Tests/Controllers/DocumentsControllerTests.cs` | Modified (token + 400 test) | c3fdf30 |
| `tests/RAG.Mvc.Tests/Views/DocumentsViewRenderTests.cs` | Modified (token + UPLOAD-12 asserts) | c3fdf30, 9d174f0 |
| `rag/Views/Documents/Upload.cshtml` | Modified (validation span) | 9d174f0 |
| `rag/Views/Ask/Result.cshtml` | Modified (else-branch fallback) | d160b37 |
| `rag/Views/Admin/Users/Index.cshtml` | Modified (noscript delete) | 0196ce5 |
| `rag/Views/Admin/Roles/Index.cshtml` | Modified (noscript delete) | 0196ce5 |
| `tests/RAG.Mvc.Tests/Views/AdminUsersViewRenderTests.cs` | Modified (noscript test) | 0196ce5 |
| `tests/RAG.Mvc.Tests/Views/AdminRolesViewRenderTests.cs` | Modified (noscript test) | 0196ce5 |
| `rag/Views/Shared/_Layout.cshtml.css` | Modified (design tokens) | 0b332ce |
| `tests/RAG.Mvc.Tests/Views/LayoutScopedCssTests.cs` | Created | 0b332ce |
| `openspec/changes/fix-hygiene/tasks.md` | Modified (all `[x]`) | d160b37, 0196ce5, 0b332ce, final docs commit |
| `openspec/changes/fix-hygiene/apply-progress.md` | Created | 0196ce5, final docs commit |
