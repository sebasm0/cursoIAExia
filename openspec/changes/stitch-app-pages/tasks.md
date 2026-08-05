# Tasks: Stitch App Pages (16 screens)

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | ~1,400 total: A ~500 · B ~450 · C ~450 |
| 400-line budget risk | High overall (A Medium-High) |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (A) → PR 2 (B) → PR 3 (C) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

### Work Units

| Unit | Goal | PR / base | Focused test | Runtime harness | Rollback |
|---|---|---|---|---|---|
| A | Tokens, layout+theme, Dashboard, Error, Account | PR 1 / feature branch | `dotnet test --filter "FullyQualifiedName~View\|FullyQualifiedName~Theme"` | `dotnet run` (rag): toggle dark, reload persists | Revert site.css, site.js, _Layout, Home/Account views |
| B | Ask+Documents redesign, UPLOAD-1 fix, modal | PR 2 / PR 1 branch | `dotnet test --filter "FullyQualifiedName~Documents\|FullyQualifiedName~Ask"` | `dotnet run`: invalid upload → errors on Upload view | Revert Ask/Documents views, _ConfirmModal, controller |
| C | Admin Users/Roles + matrix | PR 3 / PR 2 branch | `dotnet test --filter "FullyQualifiedName~Admin"` | `dotnet run`: /Admin/Users at narrow width | Revert Admin views only |

## Slice A — Foundation (PR 1)

- [x] A1 Apply DS `assets/14652031893643470775` to project `6023734159486081209`; generate shell+Home+Error, then Account screens. [UDS-1]
- [x] A2 `rag/wwwroot/css/site.css`: `--bs-*` tokens (`#0d6efd`→`--bs-primary`, focus `#258cfb`, `--bs-border-radius:.5rem`), system-ui stack, `[data-bs-theme="dark"]` overrides; WCAG AA check. [UDS-1] ~180ln
- [x] A3 `rag/wwwroot/js/site.js`: theme toggle writes `localStorage['rag-theme']`; no-JS falls back light. [UDS-2/3]
- [x] A4 `rag/Views/Shared/_Layout.cshtml`: navbar/footer per shell, pre-render theme script, logout POST when signed in. [UDS-2, AUTH-11]
- [x] A5 `rag/Views/Home/Index.cshtml`: hero + 3 cards → real Ask/Upload/Documents routes; no stats. [UDS-5]
- [x] A6 `rag/Views/Shared/Error.cshtml`: friendly token-styled error + request ID, no stack trace. [UDS-6]
- [x] A7 Redesign `rag/Views/Account/{Login,ForgotPassword,ResetPassword,AccessDenied}.cshtml`: keep AUTH-1/2 generic error, AUTH-4/5 confirmation, 403 routing. [AUTH-10/12/13]
- [x] A8 Tests: A views render 200 + `data-bs-theme="light"` default + no lorem/stats (WAF+TestAuthHandler); dark-palette smoke. [UDS-2/4/5]

## Slice B — Core Flows (PR 2)

- [x] B1 Generate Ask, Documents, confirm-modal screens. [UDS-7]
- [x] B2 RED `tests/RAG.Mvc.Tests/Controllers/DocumentsControllerTests.cs`: add GET Upload→200 renders form; update 2 POST tests to assert Upload-view re-render. [UPLOAD-1]
- [x] B3 GREEN `rag/Controllers/DocumentsController.cs`: add `[HttpGet] Upload() => View()`; three POST `return View("Index")` → `return View()`. [UPLOAD-1]
- [x] B4 Redesign `rag/Views/Ask/{Index,Result}.cshtml`: validation re-render, echo+answer+citations, "Ask another", service-unavailable state. [ASK-9/10/11] ~200ln
- [x] B5 Redesign `rag/Views/Documents/{Index,Upload,Result}.cshtml`: format hints, landing link→reachable route, success name/size/timestamp, errors list types. [UPLOAD-10]
- [x] B6 Create `rag/Views/Shared/_ConfirmModal.cshtml`; wire into destructive actions. [UDS-7]
- [x] B7 Tests: Ask/Documents views 200 + markers; POST validation re-renders form; modal blocks until choice. [ASK-9, UPLOAD-1/10]

## Slice C — Admin (PR 3)

- [ ] C1 Generate Admin screens (Users, Roles, matrix). [ADMIN-8/9]
- [ ] C2 Redesign `rag/Views/Admin/Users/{Index,Create,Edit}.cshtml`: responsive table, token forms, validation re-render. [ADMIN-8] ~200ln
- [ ] C3 Redesign `rag/Views/Admin/Roles/{Index,Create,Edit}.cshtml` + checkbox permission matrix; grants persist as claims. [ADMIN-9] ~200ln
- [ ] C4 Tests: admin views 200 under `admin.users`/`admin.permissions`; matrix persists; narrow-viewport smoke.
