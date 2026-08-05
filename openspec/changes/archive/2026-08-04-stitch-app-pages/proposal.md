# Proposal: Stitch App Pages

## Intent
Redesign every user-facing page of the MVC app via Google Stitch (text-to-UI) with a modern, minimalist, professional design. Today the UI is raw Bootstrap defaults plus a placeholder landing page; no design system or dark mode. The auth/admin backend is now real and delivered — this change designs all screens over that backend without touching controller/service behavior.

## Scope

### In Scope — 16 screens (routes verified post-auth)
| # | Screen | Real route / view |
|---|---|---|
| 1 | Dashboard (hero + action cards, **no stats**) | Home/Index (anon) |
| 2 | Ask form | Ask/Index (`rag.ask`) |
| 3 | Answer / Results | Ask/Result success |
| 4 | Upload form (+reachability fix) | Documents/Upload → Index (`documents.upload`) |
| 5 | Upload success | Documents/Result success |
| 6 | Upload error | Documents/Result error |
| 7 | Service unavailable | Ask/Result error |
| 8 | Global error | Shared/Error |
| 9 | Documents landing | Documents/Index |
| 10 | Confirm / modal overlay | shared pattern |
| 11 | Login | Account/Login |
| 12 | Logout | `_Layout` navbar POST form |
| 13 | Recover | Account/ForgotPassword + ResetPassword |
| 14 | Access denied | Account/AccessDenied |
| 15 | Admin users | Admin/Users (`admin.users`) |
| 16 | Admin roles + matrix | Admin/Roles (`admin.roles`/`admin.permissions`) |

### Out of Scope
- Sign-up (AUTH-6: admin-only creation), document detail/list/delete (no backend for it), dashboard stats endpoint/counts.

## Effort Estimate
Design: 1 session (Stitch). Apply: 2–3 chained PRs. Overall Medium.

## Approach
1. Create a Stitch project; generate 16 mobile-first text screens grouped by flow (public/auth/core/admin).
2. Generate ONE design system: modern minimalist, primary blue `#0d6efd`, **light + dark variants** (colorMode + override tokens).
3. Apply to all screens → consistent screens act as visual reference (not code).
4. In apply: translate each to Razor + Bootstrap 5. CSS-variable tokens + `data-bs-theme` dark toggle (default light) in `_Layout`; controllers/models/validators untouched; Stitch copy stays placeholder-neutral, real English copy decided in apply; add view tests (RAG.Mvc.Tests, strict_tdd).

## Affected Areas
| Area | Impact | Description |
|---|---|---|
| rag/Views/** , Shared/_Layout | Modified | redesign ~16 views + navbar/footer, dark toggle |
| rag/Views/Documents/{Upload,Index}.cshtml | Modified | UPLOAD-1 reachability fix (see below) |
| rag/wwwroot/css/site.css, lib/bootstrap | Modified | tokens, theme |
| rag/Controllers/Documents | Minor | upload validation render fix only |
| tests/RAG.Mvc.Tests | Modified | view tests |

## Risks
| Risk | L | Mitigation |
|---|---|---|
| Stitch mobile-first vs desktop-first Bootstrap | Med | Bootstrap is mobile-first; tune breakpoints in apply |
| Light/dark translation + contrast cost | Med | design-system tokens; per-theme contrast check |
| Placeholder vs real copy drift | Med | copy map; real copy landed in apply |
| No stats data | Low | hero + cards; no invented figures |
| Upload reachability bug | Low | fix in slice B: form renders on validation error; landing link must not 404 |

## Rollback
UI-only; no schema/data migrations. Revert slice PRs; dark toggle defaults to light; views recoverable via git history.

## Dependencies
- Stitch MCP (verified) · Bootstrap 5 (already libman-managed) · auth backend (archived + delivered).

## Success Criteria
- [ ] 16 screens generated under one light+dark design system
- [ ] Each maps to a verified real route (table above)
- [ ] Dark toggle works, default light, all tests green
- [ ] Upload form reachable; validation errors render on it
- [ ] Dashboard shows no crafted/static stats

## Delivery / First-Slice Boundary (400-line review budget)
16 views + tokens + tests will exceed 400 lines → **Chained PRs recommended: Yes**.
- **A — Foundation**: design tokens, `_Layout` + dark toggle, `site.css`, Dashboard, Error, all Account screens (Login/Forgot/Reset/AccessDenied), navbar Login/Logout. Self-contained, proof that auth + theming integrate.
- **B — Core flows**: Ask (Index/Result incl. service-unavailable), Upload + Documents remap (reachability fix), confirm-modal pattern.
- **C — Admin**: Users (Index/Create/Edit), Roles (Index/Create/Edit + permission matrix), responsive tables.

Each slice: clean diff onto feature branch, end-to-end tests, independent rollback.
Decision needed before apply: **Yes** (approve slice split).