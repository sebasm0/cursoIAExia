# Design: Stitch App Pages (16 screens)

## Technical Approach

Design every user-facing screen over the real backend using Google Stitch as the **visual reference only**, then implement as tokenized Razor + Bootstrap 5 views with a light/dark theme toggle. Controllers, services, and models stay unchanged except one render-only fix: `DocumentsController` gains a GET `Upload` action so `/Documents/Upload` renders the form (UPLOAD-1).

**Stitch setup (created this phase):** project `6023734159486081209` "RAG App Pages"; design system `assets/14652031893643470775` "RAG Modern Minimal" — colorMode LIGHT, colorVariant NEUTRAL, customColor `#0d6efd`, fonts INTER, roundness ROUND_EIGHT, designMd encodes placeholder-neutral copy, no-stats, WCAG AA. Screen generation (16 × `generate_screen_from_text`) runs in APPLY, in the order below.

## Architecture Decisions

| Decision | Options | Tradeoff | Choice |
|---|---|---|---|
| Theme mechanism | (a) `data-bs-theme` attr (b) custom CSS classes (c) server cookie | (a) native Bootstrap 5.3.3 dark palette, zero extra JS (b) full control, more CSS (c) server round trip — violates UDS-2 | (a) `html[data-bs-theme]`, default light |
| Theme persistence | localStorage vs cookie vs none | UDS-3 needs persistence; localStorage keeps toggle client-side; no-JS falls back to light | localStorage key `rag-theme`; inline head script sets attr pre-render (no flash) |
| Token mapping | Stitch tokens → Bootstrap `--bs-*` vars | Stitch is reference; Bootstrap 5.3.3 exposes theme vars natively | `site.css`: `#0d6efd` → `--bs-primary`/link, focus `#258cfb`; surfaces → `--bs-body-bg/color/border`; dark palette as `[data-bs-theme="dark"]` overrides |
| Font | INTER (Stitch) vs system-ui | No font asset in repo; Google Fonts adds dependency/CSP surface | Stitch refs INTER; apply maps to system-ui stack |
| Roundness | ROUND_EIGHT vs Bootstrap default | 8px ≈ Bootstrap `--bs-border-radius` (0.375rem) | Set `--bs-border-radius: .5rem` to match reference |
| Stitch order | dependency-first vs flow-first | Shared shell must exist before pages reuse it; screens are reference, order only affects iteration cost | Shell (navbar/toggle/footer/dashboard) → Ask flow → Documents flow → Account → Admin |
| Placeholder copy | Stitch placeholder vs real copy | UDS-4 forbids placeholder/lorem/fabricated stats | Stitch screens = reference; APPLY lands real copy; dashboard has no stats (UDS-5) |
| UPLOAD-1 fix | GET action + re-render target vs JS-only nav | Controller fix is render-only; mirrors AskController re-render pattern (`View("Index", model)`) | Add `[HttpGet] Upload() => View()`; change POST's three `return View("Index")` → `return View()` |

## Data Flow

```
Stitch screens (16, reference) ──► Razor views (apply) ◄── models (unchanged:
        │                                  │             AskViewModel, UploadViewModel, ...)
  designMd tokens ──► site.css vars ──► [data-bs-theme] toggle ──► localStorage
```

Controllers keep their exact request/response contract; only `DocumentsController` gains one GET action.

## File Changes

| File | Action | Description |
|---|---|---|
| `rag/Views/Shared/_Layout.cshtml` | Modify | Redesign navbar/footer, theme toggle, login/logout (AUTH-11) |
| `rag/Views/Shared/_ConfirmModal.cshtml` | Create | Shared confirm/cancel modal partial (UDS-7) |
| `rag/wwwroot/css/site.css` | Modify | Token vars, dark overrides, component polish |
| `rag/wwwroot/js/site.js` | Modify | Theme toggle + localStorage persistence |
| `rag/Views/Home/Index.cshtml` | Modify | Hero + 3 action cards, no stats (UDS-5) |
| `rag/Views/Ask/{Index,Result}.cshtml` | Modify | Redesign (ASK-9/10/11) |
| `rag/Views/Documents/{Index,Upload,Result}.cshtml` | Modify | Redesign + form reachability (UPLOAD-1/10) |
| `rag/Views/Shared/Error.cshtml` | Modify | Token-styled error + request ID (UDS-6) |
| `rag/Views/Account/{Login,ForgotPassword,ResetPassword,AccessDenied}.cshtml` | Modify | Redesign (AUTH-10/12/13) |
| `rag/Views/Admin/{Users,Roles}/**` | Modify | Responsive tables + permission matrix (ADMIN-8/9) |
| `rag/Controllers/DocumentsController.cs` | Modify | GET Upload; re-render target (UPLOAD-1) |
| `tests/RAG.Mvc.Tests` | Modify | View render + UPLOAD-1 tests |

## Interfaces / Contracts

UPLOAD-1 — only behavior change in the solution (DocumentsController):

```csharp
[HttpGet]
public IActionResult Upload() => View();   // renders Upload.cshtml, 200
// POST Upload: replace the three `return View("Index")` with `return View();`
// -> Upload.cshtml re-renders with ModelState errors in place
```

Theme bootstrap — in `_Layout` head, before CSS, to avoid flash of wrong theme:

```html
<script>document.documentElement.setAttribute('data-bs-theme', localStorage.getItem('rag-theme') || 'light');</script>
```

## Testing Strategy

| Layer | What | Approach |
|---|---|---|
| Unit | UPLOAD-1 GET → Upload view; POST validation → form re-render with errors | `DocumentsControllerTests` via existing factory |
| Integration | All 16 views render 200 with design-system markers; no stack traces/lorem/fabricated stats | `WebApplicationFactory` + `TestAuthHandler` (existing pattern), body assertions |
| Theming | Default light; `data-bs-theme` switches vars | Render assert `data-bs-theme="light"`; dark palette smoke test on CSS vars |

## Threat Matrix

N/A — no shell, subprocess, VCS/PR automation, or executable-classification boundary. UPLOAD-1 adds one MVC action inside framework conventions; auth/antiforgery/policy gating unchanged. No adversarial tests manufactured.

## Migration / Rollout

No schema/migration. Chained PRs (each ≤400 lines): **A — Foundation** (tokens, layout + theme, Dashboard, Error, Account screens, navbar login/logout); **B — Core flows** (Ask, Documents incl. UPLOAD-1 fix, confirm modal); **C — Admin** (Users/Roles + permission matrix). Each onto the feature branch, independently revertible; theme defaults light.

## Open Questions

- [ ] Font fidelity: load Inter (Google Fonts/local) or accept system-ui stack — decide in APPLY.
- [ ] WCAG AA contrast pairs for the dark palette — verify each named token pair in APPLY with a contrast check.
