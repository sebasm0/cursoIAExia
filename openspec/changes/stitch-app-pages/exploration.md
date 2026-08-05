# Exploration — Stitch App Pages

## Change
`stitch-app-pages` — build ALL app pages via Google Stitch (text-to-UI) with a modern minimalist professional design.

## App Context (current state)

The user-facing web UI is the ASP.NET Core MVC app (`rag/`) of a .NET 10 RAG solution (PostgreSQL + pgvector, Ollama, Clean Architecture). It is a **3-flow application**:

| Route | Controller / Action | View | Purpose |
|---|---|---|---|
| `/` | Home/Index | `Home/Index.cshtml` | Landing page (raw ASP.NET "Welcome" template, placeholder) |
| `/Home/Privacy` | Home/Privacy | `Home/Privacy.cshtml` | Placeholder privacy policy |
| `/Home/Error` | Home/Error | `Shared/Error.cshtml` | Global error page (request ID) |
| `/Ask` (GET) | Ask/Index | `Ask/Index.cshtml` | Question form |
| `/Ask` (POST) | Ask/Ask | `Ask/Result.cshtml` | Answer view + service-unavailable error state |
| `/Documents` (GET) | Documents/Index | `Documents/Index.cshtml` | Upload landing (card linking to Upload) |
| `/Documents/Upload` (POST) | Documents/Upload | `Documents/Result.cshtml` | Upload success or error result |

Backend surface (Application layer) exposes exactly two operations:
- `IngestionService.IngestAsync(fileName, contentType, stream)` → `Document`
- `RagService.AskAsync(query, topKRetrieve, topKRank)` → `string answer`

**There is NO authentication, user store, or account system.** No login/logout/signup/password-recovery/identity code exists anywhere in the solution (`Program.cs` registers no auth services; `UseAuthorization()` is a no-op). Multi-tenancy per user is only a SHOULD in the OpenSpec specs, not implemented. **There is NO document listing/detail/delete capability** — no service methods, controllers, or routes for it.

## Screen Inventory — Requested vs. Real

### REAL screens (map to existing routes/views — recommend designing)

| # | Screen | Maps to | Purpose | Key UI elements | Data shown |
|---|---|---|---|---|---|
| 1 | **Dashboard** | `Home/Index` | Redesign the placeholder landing page into the app entry point | Product brand/hero, tagline, primary action cards ("Ask a Question", "Upload Document", "View Documents"), status chips | None today (no stats endpoint); stats would need new backend or static copy |
| 2 | **Ask** | `Ask/Index` (GET) | Question form | Query textarea, submit button (send icon), client+server validation, empty-query error | Query only |
| 3 | **Answer / Results** | `Ask/Result` (success) | Display AI answer | Question echo card, answer card (pre-wrap text), "Ask another" / "Back to Home" actions | Query, Answer (citations are spec'd SHOULD/future) |
| 4 | **Upload form** | `Documents/Upload` | Core feature: ingest documents | File input, accepted formats hint (.cs, .md, .pdf), 10 MB limit, client-side validation, submit with upload icon | Selected file |
| 5 | **Upload success** | `Documents/Result` (success) | Confirmation | Success alert, document details definition list | File name, content type, size (bytes/KB/MB), ingestion timestamp |
| 6 | **Upload error** | `Documents/Result` (error) | Parse/storage failures | Error alert, failure reason, retry guidance | File name, error message |
| 7 | **Service unavailable** | `Ask/Result` (error) | RAG pipeline down (Ollama/PostgreSQL) | Error alert, retry guidance | Error message |
| 8 | **Global error** | `Shared/Error` | Unhandled errors | Friendly error page, request ID | Request ID |
| 9 | **Documents landing** | `Documents/Index` | Entry to upload flow | Section heading, upload call-to-action card | — |
| 10 | **Confirm / modal** | pattern (no page) | Overlay confirmations (e.g., leave-with-unsaved-query, re-upload) | Modal overlay, confirm/cancel actions | Context-dependent |

### NOT real (exclude from scope unless user expands it)

| Requested screen | Verdict | Why |
|---|---|---|
| **Login** | EXCLUDE | No auth exists in the app. A login screen is a design artifact that cannot be implemented without a separate auth feature change. |
| **Logout** | EXCLUDE | Depends on login. |
| **Recover (password)** | EXCLUDE | No user store / password mechanism exists. |
| **Sign up** | EXCLUDE | Fixed users; no registration flow. Multi-tenant isolation is only a SHOULD spec. |
| **Detail** | EXCLUDE (or design-only) | No document detail route or service method exists; would require backend additions. |
| **Custom** | CLARIFY | Undefined — ask the user what "custom" maps to before proposal. |

## Existing UI Technology & Branding

- **Tech**: Razor `.cshtml` views + **Bootstrap 5** (libman-managed `wwwroot/lib/bootstrap`), jQuery + jQuery Validation, minimal custom `site.css` (Bootstrap default overrides), default `_Layout.cshtml` (navbar + footer).
- **Responsiveness**: Bootstrap grid (`container`, `col-md-*`) — responsive but **desktop-first feel**; not mobile-first. Stitch generates **mobile-first** screens — the apply phase must reconcile this (Bootstrap is mobile-first internally, but screens must be adapted to breakpoints).
- **Branding to carry**:
  - Brand name: `rag`
  - Primary color: Bootstrap default blue (`#0d6efd`), focus ring `#258cfb`
  - Light theme: white navbar (`bg-white`, bottom border), light gray footer border, muted secondary text
  - Components: navbar, cards, alerts (danger/success), forms, buttons (btn-primary, btn-outline-*)
  - **UI copy is English** across all views
  - No custom fonts, no design tokens, no dark mode
- **Spec constraints** (OpenSpec `mvc-rag-ask`, `mvc-document-upload`): errors must be user-friendly and not expose stack traces; validation messages must list supported types / max size; success view must show name, size, timestamp.

## Approach Options

1. **Single design system + 10 screens (recommended)** — Create one Stitch design system (modern minimalist, light theme, primary blue), generate the 10 real screens from it, then translate to Razor/Bootstrap in apply.
   - Pros: Consistent branding, covers every real route, reuses existing Bootstrap structure
   - Cons: Requires excluding auth screens; list/detail remain design-only
   - Effort: Medium
2. **Inclusive screen set (all requested incl. auth)** — Generate every requested screen including login/logout/recover/signup/detail.
   - Pros: User asked for "ALL" pages; design catalog is complete for future auth work
   - Cons: Produces artifacts with no implementation target; scope creep; strict_tdd means untestable views; risk of user confusion
   - Effort: High
3. **Minimal set (core flows only)** — Dashboard, Ask, Answer, Upload form, Upload success, Upload error, Global error.
   - Pros: Fastest, zero unused artifacts
   - Cons: Skips documents landing, confirm-modal pattern, service-unavailable variant
   - Effort: Low

## Recommendation

**Approach 1**: design the **10 real screens** (dashboard, ask, answer, upload form, upload success, upload error, service unavailable, global error, documents landing, confirm modal) under one Stitch design system, and **explicitly exclude login/logout/recover/signup/detail** (no backend exists). Ask the user to clarify the "custom" screen before the proposal. Mark documents-list/detail as design-only if the user wants them (they need new backend capability).

Stitch pipeline note for the design/proposal phase: screens are generated in a Stitch project (mobile-first), a design system is applied, then in apply the screens are translated to Razor views + Bootstrap/CSS with the existing routing.

## Risks

- **CRITICAL**: Auth screens (login, logout, recover, sign up) do NOT map to any feature — the app has zero authentication code. Designing them creates artifacts that cannot be implemented without a separate auth change (out of scope).
- **WARNING**: List/detail screens have no backend support (no service methods, controllers, or routes for listing/reading documents).
- **WARNING**: "Custom" screen is undefined — needs user clarification before proposal.
- **WARNING**: Stitch generates mobile-first screens; the current UI is desktop-first Bootstrap — responsive reconciliation needed in apply.
- **WARNING**: No dashboard stats data exists (no counts endpoint); dashboard numbers would be static unless backend is added.
- **NOTE**: strict_tdd is on — any views produced from these screens will be covered by tests in apply (following existing `RAG.Mvc.Tests` controller-view test patterns).

## Ready for Proposal

**Yes** — with the recommendation to exclude auth screens, mark list/detail as design-only, and clarify "custom" with the user. Orchestrator should tell the user: "Login/logout/recover/sign-up don't exist in this app (no auth). Do you want them as design-only placeholders, or exclude them? What is the 'custom' screen?"
