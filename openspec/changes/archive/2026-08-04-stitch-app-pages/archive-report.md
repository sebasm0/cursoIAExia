# Archive Report: stitch-app-pages

- **Phase**: SDD ARCHIVE
- **Date**: 2026-08-04
- **Repo**: `D:\cursoIAexia-master`
- **Artifact store mode**: hybrid (OpenSpec filesystem + Engram)

## Change Summary

### Intent
Redesign every user-facing page of the RAG MVC app (`rag/`) via Google Stitch (text-to-UI) with a modern, minimalist, professional design. The UI was raw Bootstrap defaults plus a placeholder landing page; the auth/admin backend (archived as `aspnet-auth-rbac`) is real and delivered. This change restyles all 16 screens over that backend without touching controller/service behavior — with one render-only exception: `DocumentsController` gains a GET `Upload` action so `/Documents/Upload` renders the form (UPLOAD-1 reachability fix).

### Delivered
- **Implementation on a feature-branch chain** (NOT yet pushed; PR #1 of the chain — `feat/auth-rbac` — is already open on the repo; these branches build on top):
  - Slice A — Foundation: `feat/stitch-app-pages-01-foundation` (design tokens, `_Layout` + dark toggle, Dashboard, Error, Account screens; 843 ln)
  - Slice B — Core flows: `feat/stitch-app-pages-02-core-flows` (Ask + Documents redesign, UPLOAD-1 GET/re-render fix, `_ConfirmModal`; 879 ln)
  - Slice C — Admin: `feat/stitch-app-pages-03-admin` (Users/Roles + permission matrix; 751 ln) — CURRENT branch, tip of the chain, contains all 3 slices
- **Delivery strategy**: feature-branch-chain, 3 chained PRs. `size:exception` accepted by the user for all three slices (A 843 / B 879 / C 751 vs ~500 guard) — pre-decision honored, recorded in verify-report W-3.
- **18 requirements** specified (UDS-1..7, ASK-9..11, UPLOAD-1 mod + UPLOAD-10, AUTH-10..13, ADMIN-8..9); **17/18 implemented** (1 PARTIAL: ASK-10 citations — see follow-ups).
- **96 tests passing** (0 failed, 0 skipped; 59 baseline → 96; 37 net-new across 7 test files), `dotnet test` exit 0, output hash `sha256:b212e92d088db0046303ff4215ad34aa06153ebdea3665f382779bdf1e604235`; build 0 errors, hash `sha256:f94ee328853c74113bd955d32ac6a6b5e79dd964215da74153f14b5602454471`.

### Verification Result
**PASS WITH WARNINGS** — 0 CRITICAL, 3 WARNING, 4 SUGGESTION. 31/32 scenarios compliant (1 PARTIAL: ASK-10 citation clause — backend `RagService.AskAsync` returns a plain string, `AskViewModel` has no citation structure, and the spec assumption forbids controller/service/model changes; view renders answer text only). Warnings: (W-1) ASK-10 partial; (W-2) formal per-task RED/GREEN table lost to Engram topic-key upsert — RED/GREEN confirmed empirically (all 7 change test files exist, 96/96 pass); (W-3) slice line counts exceeded ~500 guard, `size:exception` accepted. Design invariants verified: `data-bs-theme` default light + localStorage `rag-theme`, no-JS light fallback, Stitch tokens → `--bs-*` vars, system-ui font stack (Inter not shipped), `--bs-border-radius: .5rem` (ROUND_EIGHT), no placeholder/lorem/fabricated stats, UPLOAD-1 contract exact, no sign-up/detail screens.

## Specs Promoted (delta → stable)

| Domain | Action | Details |
|--------|--------|---------|
| `mvc-rag-ask` | Updated | ASK-9..11 added (design-system Ask form, answer screen with echo/answer/citations/"Ask another", styled service-unavailable) — 3 table rows + 5 scenarios appended |
| `mvc-document-upload` | Updated | UPLOAD-1 MODIFIED (reachable GET route + in-place validation re-render, 4 scenarios aligned to verified behavior, new "GET renders the upload form" scenario); UPLOAD-10 added (design-system upload screens) — 3 scenarios appended |
| `user-auth` | Updated | AUTH-10..13 added (design-system login, layout logout control, recover screens, access-denied) — 4 requirement blocks appended |
| `user-admin` | Updated | ADMIN-8..9 added (design-system admin screens, permission matrix) — 2 requirement blocks appended; Purpose/Assumptions "visual polish deferred" note updated to delivered state (editorial coherence fix, recorded here) |
| `ui-design-system` | **Created** | New stable spec — UDS-1..7 (full spec copied from delta; no prior spec existed) |

OpenSpec delta-apply convention followed: ADDED requirements appended with requirement IDs preserved; MODIFIED requirement (UPLOAD-1) replaced in place with unchanged requirements (UPLOAD-2..9, ASK-1..8, AUTH-1..9, ADMIN-1..7, RBAC-1..5) preserved verbatim; no REMOVED/RENAMED deltas in this change.

## Artifact Locations

- **Archived change**: `openspec/changes/archive/2026-08-04-stitch-app-pages/` (moved from `openspec/changes/stitch-app-pages/`, dated-prefix convention matching `2026-07-27-mvc-rag-integration` and `2026-08-03-aspnet-auth-rbac`)
  - `exploration.md`, `proposal.md`, `design.md`, `tasks.md`, `verify-report.md`, `specs/{ui-design-system,mvc-rag-ask,mvc-document-upload,user-auth,user-admin}.md`, `archive-report.md`
- **Stable specs**: `openspec/specs/{ui-design-system,mvc-rag-ask,mvc-document-upload,user-auth,user-admin}/spec.md`
- **config.yaml**: NOT modified — inspected; the project convention (matching both prior archives) does not record archived changes in `openspec/config.yaml`, and `rules.archive` is not defined. No update required.

## Engram Observation IDs (traceability)

| Artifact | Observation ID |
|----------|---------------|
| preflight (config) | #37 |
| explore report | #38 |
| scope decision (auth first, then Stitch) | #39 |
| proposal | #53 |
| spec ui-design-system | #54 |
| spec delta mvc-rag-ask | #55 |
| spec delta mvc-document-upload | #56 |
| spec delta user-auth | #57 |
| spec delta user-admin | #58 |
| design | #59 |
| tasks | #60 |
| delivery decision (3-PR feature-branch-chain) | #61 |
| apply progress (slices A+B+C final) | #62 |
| verify report | #63 |

## Notes

- **Intentional deviations / observations**:
  - Tasks artifact: all 12 tasks (A1–A8, B1–B7, C1–C4) checked `[x]`; no stale checkboxes. No archive-time reconciliation needed.
  - No review receipt artifacts exist for this change (transaction/ledger/receipt) — consistent with both prior archives; archive gate = verified PASS (0 CRITICAL) + orchestrator instruction.
  - Delta spec files inside the change folder are flat (`specs/{name}.md`) rather than nested (`specs/{domain}/spec.md`); preserved as-is in the archive for audit fidelity (same as `2026-08-03-aspnet-auth-rbac`).
  - `exploration.md` predates the auth delivery (recommends excluding auth screens because no auth existed); the proposal superseded it after `aspnet-auth-rbac` landed. Preserved unmodified as historical context.
  - Editorial coherence fix in stable `user-admin`: Purpose/Assumptions claimed "visual polish is deferred to the Stitch redesign" — now false after ADMIN-8/9; updated to reflect delivered design-system styling. No requirement text altered.
- **Known follow-ups (out of scope, per verify-report S-2 / W-1)**:
  - ASK-10 structured citation rendering — requires a follow-up change extending the Ask response contract (model/service) so each citation shows source document name + excerpt. The spec assumption explicitly forbids model changes in this change.
  - Move the Ask answer `<pre>` inline style into `site.css` (UDS-1 purity, S-1).
  - E2E theming (real browser click toggle) unavailable — capabilities `e2e: false` (S-4).
- **Rollback**: UI-only; no schema/data migrations. Revert the slice PRs; dark toggle defaults to light; views recoverable via git history. Only behavior change: `DocumentsController.Upload` GET + re-render target (render-only, no ingestion impact).
- **Next**: archive complete → orchestrator proceeds to PR assembly guidance (push the 3 feature-branch-chain branches, open PRs in order A→B→C, PR #1 targeting `feat/auth-rbac`/tracker relationship already established).

## SDD Cycle Complete

The change has been fully planned, implemented, verified, and archived.
