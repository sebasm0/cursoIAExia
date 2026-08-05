# Delta for ui-design-system

## ADDED Requirements

### Requirement: UDS-8 — Scoped layout colors derive from design tokens

The scoped styles in `rag/Views/Shared/_Layout.cshtml.css` (link color `a`, `.btn-primary`, `.nav-pills .nav-link.active`) MUST derive their colors from the design-system tokens via `var(--bs-*)` instead of hardcoded hex values. The scoped rules MUST NOT introduce ad-hoc colors that override the shared token definitions (UDS-1 unchanged). Visual output in the light theme SHOULD remain equivalent to the current palette.

#### Scenario: Scoped colors resolve from tokens

- GIVEN the light theme
- WHEN any page renders with the shared layout
- THEN link, primary-button, and active-nav-pill colors resolve from `--bs-*` CSS variables
- AND no hardcoded hex brand color (`#0077cc`, `#1b6ec2`, `#1861ac`) remains in `_Layout.cshtml.css`

#### Scenario: Dark theme follows the token palette

- GIVEN the dark theme is active
- WHEN a page renders with the shared layout
- THEN the scoped link, button, and nav-pill colors resolve from the dark token variants
- AND interactive/background contrast is preserved (UDS-1 / UDS-2 dark behavior unchanged)

## Assumptions

- Requirement IDs UDS-1..UDS-7 are unchanged; this delta replaces hardcoded hex values with token references in one scoped stylesheet only.
- The `--bs-*` variables are provided by the existing shared token setup; no new token definitions are introduced by this change.
- No runtime behavior change: the change is static CSS token substitution with equivalent light-theme output.