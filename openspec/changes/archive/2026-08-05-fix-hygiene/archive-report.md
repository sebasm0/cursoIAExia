# Archive Report: fix-hygiene

- **Phase**: SDD ARCHIVE
- **Date**: 2026-08-05
- **Repo**: `D:\cursoIAExia-master`
- **Artifact store mode**: hybrid (OpenSpec filesystem + Engram)

## Change Summary

### Intent
Close five non-blocking hygiene follow-ups from the stitch-app-pages 4R review. Highest value: `POST /Ask` and `POST /Documents/Upload` accepted requests with no server-side antiforgery validation, breaking the app's documented per-action CSRF posture (D5). The rest make server validation errors visible without JS, give no-JS delete a fallback, fix a blank Ask result, and align scoped brand colors with design tokens.

### Delivered
- **Implementation on `feat/fix-hygiene`** (7 commits ahead of `origin/main`, NOT pushed; low-risk single PR per delivery forecast — 400-line budget risk Low, chained PRs not recommended).
- **7 work-unit commits**: 7d52107 (multipart antiforgery test helper) · c3fdf30 (CSRF on Ask + Upload) · 9d174f0 (UPLOAD-12 server-rendered validation) · d160b37 (ASK-13 empty-answer fallback) · 0196ce5 (ADMIN-10 no-JS delete) · 0b332ce (UDS-8 token-based scoped colors) · 07de009 (tasks/apply-progress docs) + **final docs commit (this archive)**.
- **6 requirements** specified (ASK-12/-13, UPLOAD-11/-12, ADMIN-10, UDS-8); **6/6 implemented**, verified via apply-progress.
- **103/103 tests passing** (98 baseline + 5 net-new: 1 ASK-13, 2 ADMIN-10, 2 UDS-8), 0 failed, 0 skipped — `dotnet test tests/RAG.Mvc.Tests` from `D:\cursoIAExia-master` (recorded in apply-progress task 7.1; re-confirmed green on the final docs commit, see Notes).

### Verification Result
**PASS (no warnings required)** — 0 CRITICAL, 0 WARNING issues recorded in apply-progress. All 17 tasks `[x]`, no stale checkboxes. TDD cycle evidence per phase (RED→GREEN tables in apply-progress). Manual smoke (task 7.2): app boots, PostgreSQL reachable, migrations applied; seed halts on the `__SECRET__` credential guard (environment config, not a regression); browser-visual checks mapped to the green automated equivalents. No `verify-report.md` artifact exists for this change — verification evidence was carried in `apply-progress.md` (small change; consistent with the repo's prior archives, which also rely on apply evidence + orchestrator instruction as the archive gate).

## Specs Promoted (delta → stable)

| Domain | Action | Details |
|--------|--------|---------|
| `mvc-rag-ask` | Updated | ASK-12/-13 added (CSRF on POST `Ask`; non-blank empty-answer fallback) — 2 table rows + 4 scenarios appended |
| `mvc-document-upload` | Updated | UPLOAD-11/-12 added (CSRF on multipart POST `Upload`; server-side file validation errors render without JS) — 2 table rows + 5 scenarios appended |
| `user-admin` | Updated | ADMIN-10 added (delete forms degrade gracefully with JS disabled via `<noscript>` submit) — 1 requirement block + 2 scenarios appended |
| `ui-design-system` | Updated | UDS-8 added (scoped layout colors derive from `var(--bs-*)` tokens, no hardcoded hex) — 1 requirement block + 2 scenarios appended |

OpenSpec delta-apply convention followed: all deltas were ADDED-only (no MODIFIED/REMOVED/RENAMED). ADDED requirements appended with requirement IDs preserved; existing stable IDs and content untouched (ASK-1..11, UPLOAD-1..10, ADMIN-1..9, UDS-1..7 preserved verbatim, no renumbering). Requirement IDs converted from delta wording into each stable file's existing format (table rows for mvc-rag-ask/mvc-document-upload, requirement blocks for user-admin/ui-design-system); scenario headings normalized to the stable heading level and language.

## Artifact Locations

- **Archived change**: `openspec/changes/archive/2026-08-05-fix-hygiene/` (moved from `openspec/changes/fix-hygiene/`, dated-prefix convention matching `2026-07-27-mvc-rag-integration`, `2026-08-03-aspnet-auth-rbac`, `2026-08-04-stitch-app-pages`)
  - `proposal.md`, `specs/{mvc-rag-ask,mvc-document-upload,user-admin,ui-design-system}.md` (flat delta files, preserved as-is for audit fidelity), `tasks.md`, `apply-progress.md`, `archive-report.md`
- **Stable specs**: `openspec/specs/{mvc-rag-ask,mvc-document-upload,user-admin,ui-design-system}/spec.md`
- **Live change folder**: REMOVED — `openspec/changes/fix-hygiene/` no longer exists (same convention as the previous archives).
- **config.yaml**: NOT modified — inspected; the project convention (matching all prior archives) does not record archived changes in `openspec/config.yaml`, and `rules.archive` is not defined. No update required.

## Engram Observation IDs (traceability)

| Artifact | Observation ID |
|----------|---------------|
| proposal | #71 |
| spec (4 delta specs, single observation) | #72 |
| tasks | #73 |
| apply-progress | #74 |
| archive-report | saved at archive time (see Engram topic `sdd/fix-hygiene/archive-report`) |

No verify-report observation exists (consistent with the missing `verify-report.md` — verification evidence lives in apply-progress #74 / tasks 7.1-7.2).

## Notes

- **Intentional deviations / observations**:
  - Tasks artifact: all 17 tasks across 7 phases checked `[x]`; no stale unchecked implementation tasks. No archive-time reconciliation needed.
  - No native review receipt artifacts (transaction/ledger/receipt/gate-context) exist for this change — consistent with all three prior archives; archive gate = verified PASS (0 CRITICAL per apply-progress) + explicit orchestrator instruction.
  - The change has no `design.md` and no `exploration.md` (small change; proposal + specs carried the design — recorded in tasks #73). Preserved as-is.
  - Merge used ADDED requirements with new sequential IDs rather than MODIFIED — no stable requirement covered CSRF/degrade/fallback, so new IDs merge cleanly (decision recorded in spec #72).
  - Pre-existing Engram hygiene note: proposal observation #71 carries two pending conflict markers (`#obs-4eae45d8cbc57265`, `#obs-34ec1b10598d4aee`) left by earlier phases. Surfaced here for the orchestrator; not an archive gate issue and not resolved by this phase.
  - `dotnet test tests/RAG.Mvc.Tests` re-run on this final docs commit: **103/103 green** — docs-only changes to `openspec/` confirm build unaffected.
- **Known follow-ups (out of scope, recorded in proposal)**: Matrix `IsBuiltInRole` guard, N+1 admin index queries, silent matrix save, test culture issue, test-helper duplication, admin partial extraction, upload constants consolidation.
- **Rollback**: docs-only merge + archive folder move; revert the final docs commit (specs sync + archive move) to restore the live change folder. Archived folder is an AUDIT TRAIL — never deleted or modified. No schema/migration impact.
- **Next**: delivery — push `feat/fix-hygiene` (7+1 commits) and open the single PR against `origin/main`.

## SDD Cycle Complete

The change has been fully planned, implemented, verified, and archived.
