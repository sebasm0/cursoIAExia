# Archive Report: multi-assistant

- **Phase**: SDD ARCHIVE
- **Date**: 2026-08-13
- **Repo**: `D:\cursoIAExia-master`
- **Artifact store mode**: openspec (filesystem only — phase artifacts NOT persisted to Engram, per orchestrator instruction)

## Change Summary

### Intent
Hardware without GPU: wired-in `phi3:mini` takes ~2.3 min per RAG pipeline. Add per-question choice among local Ollama assistants — `phi3:mini` (default, quality), `qwen2.5:1.5b` (fast), `llama3.2:1b` (fastest). Choice applies per request, nothing persisted. New capability: config-driven assistant catalog with allow-list routing and default fallback, plus assistant selector + answer attribution on the Ask composer and Documents floating chat, and an optional `ModelId` on the API host.

### Delivered
- **14/14 tasks complete** (`tasks.md` all `[x]`, no stale checkboxes) across 3 chained-PR slices: S1 Foundation (catalog + routing), S2 MVC UI (selector + attribution), S3 API host + non-regression config pins.
- **3 delta specs / 14 requirements / 25 scenarios**: new `assistant-selection` capability (ASEL-1..10), `mvc-rag-ask` delta (ASK-2 modified + ASK-14/15 added), `mvc-document-upload` delta (UPLOAD-13 added).
- **Strict TDD** evidenced per slice in `apply-progress.md` (RED→GREEN tables, safety nets 122/1 → 139/1 → 144/1 → 150/1/151).
- **Delivery**: chained PRs planned (stacked-to-main; PR #1 Foundation, #2 MVC UI, #3 API+regression) — NO commits/pushes made; orchestrator decides delivery timing.

### Verification Result
**PASS WITH WARNINGS** — 4R review `review-ea5daaf8adde82cf` terminal_state `approved`, 0 BLOCKER / 0 CRITICAL (verified in `.git/gentle-ai/review-transactions/v2/`). Independent verifier run (2026-08-13): `dotnet build RAG.slnx` → 0 errors; `dotnet test RAG.slnx --no-build` → **150 passed / 1 skipped / 151 total** (MVC 144/1/145 + API 6/6), exit 0. Build hash `sha256:dd11b53a...`, test hash `sha256:a667ee3a...`. Compliance matrix: 24/25 scenarios compliant, 1 partial (ASK-14 no-catalog view render — indirect coverage only), 0 failing, 0 untested. WARNING/SUGGESTION findings are **approved follow-ups, not blockers** (see below).

## Specs Promoted (delta → stable)

| Domain | Action | Details |
|--------|--------|---------|
| `assistant-selection` | **Created** | New stable spec — ASEL-1..10 (full spec copied from delta, `specs/assistant-selection/spec.md`) |
| `mvc-rag-ask` | Updated | ASK-2 modified (POST handler passes selected assistant — table row replaced) + ASK-14/15 added (selector on composer; answer surface attribution) — 2 new table rows + 4 new scenarios appended, 2 ASK-2 scenarios appended; ASK-1..ASK-13 preserved verbatim except ASK-2 |
| `mvc-document-upload` | Updated | UPLOAD-13 added (floating chat renders same selector) — 1 table row + 2 scenarios appended; UPLOAD-1..UPLOAD-12 preserved verbatim |

Merge notes: deltas used `### Requirement:` blocks; stable `mvc-*` specs use a requirements table + flat `### Scenario:` blocks, so the merge adapted the delta content to the stable format (table row + scenarios appended at the end), matching the local convention from the `2026-08-05-fix-hygiene` and `2026-08-03-aspnet-auth-rbac` archives. `assistant-selection` is a new capability — the delta was a full spec and was copied directly (same as `user-auth`/`user-rbac`/`user-admin` in the auth archive). Scenario text preserved from the deltas; ASK-2's "Happy path — valid question answered" scenario already existed in the stable spec and was left unchanged (delta marks ASK-2/ASK-3 behavior unchanged).

## Artifact Locations

- **Archived change**: `openspec/changes/archive/2026-08-13-multi-assistant/` (moved from `openspec/changes/multi-assistant/`, dated-prefix convention matching prior archives)
  - `proposal.md`, `design.md`, `tasks.md`, `apply-progress.md`, `verify-report.md`, `specs/` (nested `assistant-selection/spec.md` full spec + flat `mvc-rag-ask.md` / `mvc-document-upload.md` deltas), `archive-report.md` — **nothing deleted; full audit trail preserved**
- **Stable specs**: `openspec/specs/{assistant-selection,mvc-rag-ask,mvc-document-upload}/spec.md`
- **Live change folder**: REMOVED — `openspec/changes/multi-assistant/` no longer exists (same convention as previous archives)
- **config.yaml**: NOT modified — no `rules.archive` defined; prior archives do not record archived changes in config. No update required.

## Engram Observation IDs (traceability)

No Engram observation IDs recorded — artifact store mode is **openspec** (filesystem) for this change; the orchestrator instructed not to persist phase artifacts to Engram. The archived files above are the complete audit trail.

## Approved Follow-ups (do not block archive — from verify-report / approved 4R review)

**WARNING** (3):
1. **Modelo no instalado → falla en vez de degradar** (RES-1/REL-1, `RagService.cs:52-57`): allow-list fallback covers unknown/blank ids only; a catalog model Ollama does not have (not `ollama pull`-ed) throws → MVC shows generic "servicio no disponible" (request lost, no retry with default), API returns 500 (no catch). Proposal risk mitigation ("fallback to default + user-friendly error") half-delivered. NOT covered by tests (no 404-from-Ollama simulation).
2. **Bind estricto de config** (RISK-1, `rag/Program.cs:32-34`): `Get<AssistantDefinition[]>()` throws `InvalidOperationException` on structurally malformed section, crashing MVC startup; `?? []` only covers an absent section. NOT covered by tests.
3. **API host endpoints remain unauthenticated** (RISK-2, `src/RAG.Api/Program.cs`): pre-existing posture unchanged by this diff; flagged, out of scope.

**SUGGESTION** (7):
1. **TryResolve always returns true** (RISK-3/READ-1): dead API surface — the Try-pattern bool never rejects; `Resolve` alone covers both callers.
2. **Sin observabilidad del fallback** (RES-2): no logging when a request falls back to default; ops cannot distinguish intentional fallback from client typo.
3. **Config degenerada** (RES-3/REL-2): catalog does not validate entries (empty/duplicate ids, null model) — degenerate catalogs possible; attribution label could mismatch the used model if `Default.Model` is null.
4. **Selector markup duplicado** (READ-2): Ask uses `asp-for` over the VM; Documents hand-rolls `name=` + `@inject` with manual `selected` — extract a partial/component.
5. **Ambigüedad de naming `modelId`** (READ-3): `AskAsync(..., modelId)` carries a catalog assistant **Id**, not an Ollama model string; a caller passing a real model string silently falls back to default.
6. **Rol `documents.upload` sin `rag.ask`** (REL-3): floating chat renders for any principal with `documents.upload`, but its form POSTs to `AskController.Ask` gated by `rag.ask` → 403 per submit for such roles; acknowledged in spec as unchanged multi-permission posture; pre-existing.
7. **ASK-14 no-catalog view render untested at WAF level**: the "single default option" scenario is covered at catalog unit layer and compositionally, but no WAF test renders `/Ask` with an absent catalog section.

## Notes

- **Intentional deviations / observations**:
  - Tasks artifact: all 14 tasks (1.1-3.4) checked `[x]`; no stale unchecked implementation tasks. No archive-time reconciliation needed.
  - Review receipt: native review artifacts (transaction/ledger/receipt/gate-context) live in `.git/gentle-ai/review-transactions/v2/` (4R `review-ea5daaf8adde82cf` approved); the change folder carries no receipt copies — consistent with prior archives. Archive gate = verified PASS + approved review + explicit orchestrator instruction.
  - `verify-report.md` exists for this change (unlike `fix-hygiene`); verification evidence recorded there and in `apply-progress.md`.
  - No `exploration.md` and no `state.yaml` existed in the change folder (pre-existing repo convention); preserved as-is.
  - Spec `assistant-selection` uses requirement-block + nested-scenario structure (as authored); the other two stable specs use table + flat scenarios (as authored). Both follow the local conventions established by prior archive merges.
- **Rollback of this archive**: docs-only merge + folder move; restoring the live change folder (moving `2026-08-13-multi-assistant/` back to `changes/multi-assistant/`) and reverting the three stable spec merges restores the pre-archive state. Archived folder is an AUDIT TRAIL — never deleted or modified.
- **Next**:
  1. Delivery — push the planned stacked PR chain (Foundation → MVC UI → API+regression) or a single PR per orchestrator decision; NO commits/pushes were made during apply.
  2. Ops pre-flight before runtime smoke: `ollama pull qwen2.5:1.5b llama3.2:1b` (documented in proposal; missing models surface WARNING 1).
  3. Follow-ups above are candidates for a future change (fallback-on-404, lenient config bind, fallback logging, catalog validation, selector partial extraction, WAF no-catalog render test).

## SDD Cycle Complete

The change has been fully planned, implemented, verified, and archived.
