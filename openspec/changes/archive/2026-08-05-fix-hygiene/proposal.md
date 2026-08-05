# Proposal: fix-hygiene — Mvc.App POST security and UX follow-ups

## Intent

Close five non-blocking hygiene follow-ups from the stitch-app-pages 4R review. Highest value: `POST /Ask` and `POST /Documents/Upload` accept requests with no server-side antiforgery validation, breaking the app's documented per-action CSRF posture (D5). The rest make server validation errors visible, give no-JS delete a fallback, fix a blank Ask result, and align scoped brand colors with design tokens.

## Scope

### In Scope
1. **CSRF guard** — `[ValidateAntiForgeryToken]` on `AskController.Ask` (`rag/Controllers/AskController.cs:31`) and `DocumentsController.Upload` (`rag/Controllers/DocumentsController.cs:55`). Forms already emit the token via tag helpers.
2. **Upload validation visibility** — `rag/Views/Documents/Upload.cshtml:26` render `ModelState["file"]` errors (empty/unsupported/oversize) via `@Html.ValidationMessage("file")` so they show without JS. No controller-logic change.
3. **No-JS delete fallback** — add `<noscript><button type="submit"/></noscript>` inside each delete form (`Admin/Users/Index.cshtml:42`, `Admin/Roles/Index.cshtml:41`) so the destructive POST works without the modal.
4. **Ask empty-answer state** — `rag/Views/Ask/Result.cshtml:29` add a fallback branch when `ErrorMessage` and `Answer` are both empty.
5. **Scoped brand colors** — `rag/Views/Shared/_Layout.cshtml.css:10-24` replace `#0077cc`/`#1b6ec2`/`#1861ac` on `a`/`.btn-primary`/`.nav-pills` with `var(--bs-*)`.

### Out of Scope (stay open)
Matrix `IsBuiltInRole` guard, N+1 admin index queries, silent matrix save, test culture issue, test-helper duplication, admin partial extraction, upload constants consolidation.

## Capabilities

### New Capabilities
None.

### Modified Capabilities (delta specs)
- `mvc-rag-ask`: CSRF + Ask empty-answer fallback.
- `mvc-document-upload`: CSRF + server-side file validation must render.
- `user-admin`: delete gated actions degrade gracefully with JS disabled.
- `ui-design-system`: scoped colors derive from `--bs-*` tokens.

## Approach

Per-action `[ValidateAntiForgeryToken]`, mirroring existing Admin POSTs. No global filter (Program.cs:10 unchanged). View-only fixes 2-5; controller signatures unchanged. Strict TDD.

## Test Impact

- **Expected breaks**: `AskControllerTests.Ask_Post_ValidQuestion` and `DocumentsControllerTests.Upload_Post_ValidCsFile` POST without a token → 400. Fix by harvesting `__RequestVerificationToken` via existing `AccountTestHelpers` (pattern in `AdminUserFlowTests`); multipart needs a small helper addition.
- Direct-construction unit tests unaffected (filters don't run on direct calls).
- **Expand**: render tests assert new states; add a CSRF-rejection test.

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Token harvest breaks on multipart | Med | Add antiforgery field to multipart content |
| No-JS fallback changes row markup | Low | `<noscript>` invisible for JS users |
| Blank-state collides with error branch | Low | Guard as `else` |

## Rollback

Per-file revert — remove the attribute, restore the static span or hardcoded CSS. No migration/schema impact.

## Dependencies

None external.

## Success Criteria

- [ ] POST Ask/Upload without token → 400; with token succeeds
- [ ] Upload shows server validation errors with JS off
- [ ] Delete works via `<noscript>` submit; modal path unchanged
- [ ] Ask with empty response renders a non-blank fallback
- [ ] `dotnet test` passes; no spec-level regressions