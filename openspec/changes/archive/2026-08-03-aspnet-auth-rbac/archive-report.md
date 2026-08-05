# Archive Report: aspnet-auth-rbac

- **Phase**: SDD ARCHIVE
- **Date**: 2026-08-03
- **Repo**: `D:\cursoIAexia-master`
- **Artifact store mode**: hybrid (OpenSpec filesystem + Engram)

## Change Summary

### Intent
Add ASP.NET Core authentication and role-based access control to the RAG MVC app (`rag/`). The app previously had `UseAuthorization()` with no `UseAuthentication()` — every controller was open. This change delivers cookie auth (ASP.NET Core Identity over an isolated `identity` PostgreSQL schema), a static code-defined permission catalog enforced via authorization policies, and Razor Account + Admin pages.

### Delivered
- **15 commits** on `main` (`64782c6..0c83897`), delivered as **4 chained PR slices** (stacked-to-main):
  - Slice 1 — Foundation: packages, Identity entities/DbContext, permission catalog, claims factory, seeder, migration, pipeline wiring
  - Slice 2 — Account pages: login/logout/forgot/reset/access-denied + auth test fixture (suite green)
  - Slice 3 — Admin pages: users/roles CRUD, permission matrix, guards
  - Slice 4 — Enforcement: `[Authorize(Policy=...)]` on Ask/Upload + legacy test fixes (suite green)
- **23/23 requirements** implemented (AUTH-1..9, RBAC-1..5, ADMIN-1..7, ASK-8, UPLOAD-9)
- **59 tests** passing (0 failed, 0 skipped), `dotnet test` exit 0, output hash `4AF1C75B53F18541B77A8807CB51C6E6B73942BE8A6953D13391C6C8A0078924`

### Verification Result
**PASS** — 0 CRITICAL, 0 WARNING, 4 SUGGESTION (S-1 CSRF on pre-existing POSTs deferred per design D5; S-2 pre-existing NU1510; S-3 no-CRUD guard test; S-4 deny-body assertions cosmetic). Design invariants verified: EF touches only `identity.*`, Dapper only `public.documents/chunks`, no `search_path` override, RAG.Api untouched, secrets only in User Secrets, antiforgery on all 10 new POSTs.

## Specs Promoted (delta → stable)

| Domain | Action | Details |
|--------|--------|---------|
| `mvc-rag-ask` | Updated | ASK-8 added (auth + `rag.ask` policy, gate before pipeline) + 3 scenarios |
| `mvc-document-upload` | Updated | UPLOAD-9 added (auth + `documents.upload` policy, gate before ingestion) + 3 scenarios |
| `user-auth` | Created | New stable spec — AUTH-1..9 (full spec copied from delta) |
| `user-rbac` | Created | New stable spec — RBAC-1..5 (full spec copied from delta) |
| `user-admin` | Created | New stable spec — ADMIN-1..7 (full spec copied from delta) |

OpenSpec ADDED-not-MODIFIED rule applied: ASK-8/UPLOAD-9 appended to existing specs; ASK-1..7 and UPLOAD-1..8 preserved unchanged; delta specs for new domains were full specs and copied directly.

## Artifact Locations

- **Archived change**: `openspec/changes/archive/2026-08-03-aspnet-auth-rbac/` (moved from `openspec/changes/aspnet-auth-rbac/`, dated-prefix convention matching `2026-07-27-mvc-rag-integration`)
  - `exploration.md`, `proposal.md`, `design.md`, `tasks.md`, `verify-report.md`, `specs/{user-auth,user-rbac,user-admin,mvc-rag-ask,mvc-document-upload}.md`, `archive-report.md`
- **Stable specs**: `openspec/specs/{mvc-rag-ask,mvc-document-upload,user-auth,user-rbac,user-admin}/spec.md`

## Engram Observation IDs (traceability)

| Artifact | Observation ID |
|----------|---------------|
| explore report | #40 |
| proposal | #41 |
| spec user-auth | #42 |
| spec user-rbac | #43 |
| spec user-admin | #44 |
| spec delta mvc-rag-ask | #45 |
| spec delta mvc-document-upload | #46 |
| design | #47 |
| tasks | #48 |
| apply progress (final, Slice 4 complete) | #50 |
| verify report | #51 |
| delivery decision (chained PRs stacked-to-main) | #49 |

## Notes

- **Intentional deviations / observations**:
  - Tasks artifact: all 23 tasks (1.1–4.4) checked `[x]`; no stale checkboxes. No archive-time reconciliation needed.
  - No review receipt artifacts exist for this change (transaction/ledger/receipt) — consistent with the previous `mvc-rag-integration` archive; archive gate = verified PASS + orchestrator instruction.
  - Delta spec files inside the change folder are flat (`specs/{name}.md`) rather than nested (`specs/{domain}/spec.md`); preserved as-is in the archive for audit fidelity.
- **Rollback**: revert Identity registration + `[Authorize]` attributes, drop `identity` schema; RAG.Api and `documents`/`chunks` untouched.
- **Next**: change `stitch-app-pages` is parked and now unblocked (auth screens are excluded from its design).

## SDD Cycle Complete

The change has been fully planned, implemented, verified, and archived.
