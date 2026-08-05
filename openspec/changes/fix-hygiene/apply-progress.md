# Apply Progress — fix-hygiene

Change: fix-hygiene — POST security and UX follow-ups
Mode: hybrid (OpenSpec files + Engram) · Strict TDD
Delivery: low-risk single PR (under 400-line budget forecast: Low)

## Task Status

| Task | Description | Status | Evidence |
|------|-------------|--------|----------|
| 1.1 | Multipart antiforgery POST helper | ✅ done (7d52107) | `CreateMultipartPost` in `AccountFlowTestFactory.cs` |
| 2.1 | RED: Ask POST no token → 400 | ✅ done (c3fdf30) | `Ask_Post_WithoutToken_ReturnsBadRequest` |
| 2.2 | 4 Ask POST tests harvest token | ✅ done (c3fdf30) | `GetAntiforgeryTokenAsync` + `CreatePost` |
| 2.3 | GREEN: `[ValidateAntiForgeryToken]` on Ask | ✅ done (c3fdf30) | `rag/Controllers/AskController.cs:32` |
| 2.4 | RED: Upload POST no token → 400 | ✅ done (c3fdf30) | `Upload_Post_WithoutToken_ReturnsBadRequest` |
| 2.5 | 4 Upload POST tests append token | ✅ done (c3fdf30) | helper 1.1 |
| 2.6 | GREEN: `[ValidateAntiForgeryToken]` on Upload | ✅ done (c3fdf30) | `rag/Controllers/DocumentsController.cs` |
| 3.1 | RED: field-validation-error + data-valmsg-for | ✅ done (9d174f0) | `DocumentsViewRenderTests.cs` assertions |
| 3.2 | GREEN: server-side file validation span | ✅ done (9d174f0) | `Upload.cshtml` → `Html.ValidationMessage("file", ..., id: "file-validation")` |
| 4.1 | RED: empty-answer fallback test + factory | ✅ done (d160b37) | `Ask_Post_EmptyResponse_RendersFallback` + `EmptyAnswerRagWebApplicationFactory` |
| 4.2 | GREEN: Result.cshtml else-branch fallback | ✅ done (d160b37) | "No answer was generated" card + Try Again + Back to Home |
| 5.1 | RED: `<noscript>` submit in delete forms | ⏳ pending | — |
| 5.2 | GREEN: noscript submit buttons in both delete views | ⏳ pending | — |
| 6.1 | RED: `LayoutScopedCssTests` — no hardcoded hex | ⏳ pending | — |
| 6.2 | GREEN: `_Layout.cshtml.css` → `var(--bs-*)` tokens | ⏳ pending | — |
| 7.1 | Full suite green | ⏳ pending | — |
| 7.2 | Manual smoke (JS-off delete, dark theme) | ⏳ pending | — |

## Work-Unit Commits

| SHA | Message |
|-----|---------|
| 7d52107 | test(rag): add multipart antiforgery POST helper for upload tests |
| c3fdf30 | fix(rag): reject Ask and Upload POSTs without antiforgery token |
| 9d174f0 | fix(rag): render server-side file validation errors without JS (UPLOAD-12) |
| d160b37 | fix(rag): render non-blank fallback when Ask yields no answer (ASK-13) |

## Deviations

- Task 3.2 wording suggested `asp-validation-for="file"`; commit 9d174f0 used
  `@Html.ValidationMessage("file", new { @class = "text-danger", id = "file-validation" })`.
  Markup is equivalent (emits `field-validation-error` + `data-valmsg-for="file"`,
  keeps the `#file-validation` JS target) and was committed before this pass.
