# ui-design-system Specification

## Purpose

One design system for every app page: design tokens with light and dark variants, a shared layout shell with a theme toggle, shared components (dashboard, global error, confirm modal), and the placeholder-copy rule. Stitch screens are the visual reference; this spec pins the behavior views must satisfy.

## Requirements

### Requirement: UDS-1 — Design tokens define light and dark variants

The system MUST define a single design system with design tokens (colors, typography, spacing, shape) exposed as CSS variables, a light and a dark variant, and primary blue `#0d6efd`. Every page MUST derive its look from the tokens; no page MAY hardcode ad-hoc colors.

#### Scenario: Light theme renders from tokens

- GIVEN the default light theme
- WHEN any app page renders
- THEN its colors and typography resolve from the shared tokens
- AND no per-page inline styling is present

#### Scenario: Dark theme applies the dark variant

- GIVEN the dark theme is active
- WHEN a page renders
- THEN background, surface, text, and border tokens switch to the dark palette
- AND text and interactive elements keep sufficient contrast (WCAG AA)

### Requirement: UDS-2 — Shared layout shell with theme toggle

The shared layout MUST render the navbar, footer, and a theme toggle on every page. The theme MUST default to light on first visit. Toggling MUST switch the whole page between light and dark without a server round trip.

#### Scenario: Default light

- GIVEN a visitor with no saved theme preference
- WHEN they open any page
- THEN the page renders in light theme

#### Scenario: Toggle switches theme

- GIVEN a rendered page
- WHEN the user activates the theme toggle
- THEN every token-driven element switches to the opposite theme immediately

### Requirement: UDS-3 — Theme choice persists across visits

The selected theme SHOULD persist in the browser (localStorage) so subsequent visits restore it. Pages MUST remain usable with JavaScript disabled (light default).

#### Scenario: Restored after refresh

- GIVEN the user selected dark theme
- WHEN they reload the page
- THEN the page renders dark without further action

### Requirement: UDS-4 — Placeholder-neutral copy rule

Stitch screens serve as visual reference only. Production views MUST render real English copy decided in apply; placeholder, lorem, or sample figures MUST NOT ship. The system MUST NOT display data with no backend source (no invented statistics).

#### Scenario: Views render real copy

- GIVEN a Stitch screen contains placeholder copy
- WHEN the corresponding Razor view is implemented
- THEN the view renders final copy, not the placeholder

### Requirement: UDS-5 — Dashboard is hero plus action cards, no stats

`Home/Index` MUST render a hero and action cards linking to the Ask, Upload, and Documents routes. It MUST NOT render any statistics, counts, or metrics. All card links MUST resolve to real routes.

#### Scenario: Dashboard shows hero and cards

- GIVEN a visitor opens the home page
- WHEN the page renders
- THEN a hero and action cards for Ask, Upload, and Documents are displayed
- AND each card links to a real route

#### Scenario: No fabricated stats

- GIVEN the dashboard renders
- WHEN its content is inspected
- THEN no document, query, or user counts are displayed

### Requirement: UDS-6 — Global error page fits the design system

`Shared/Error` MUST render a friendly, token-styled error page with a request ID and guidance. It MUST NOT expose stack traces.

#### Scenario: Unhandled error shown

- GIVEN an unhandled exception
- WHEN the error page renders
- THEN a friendly message and the request ID are displayed
- AND no stack trace is shown

### Requirement: UDS-7 — Confirm modal overlay pattern

Destructive or leave-with-unsaved-changes actions MUST use a shared modal overlay with confirm and cancel actions. The modal MUST NOT navigate until the user chooses.

#### Scenario: Confirm blocks until choice

- GIVEN a user triggers a confirmable action
- WHEN the modal overlay appears
- THEN the action is blocked until confirm or cancel is selected

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

- UI-only: no controller, service, or model changes are introduced by these requirements.
- Stitch screens are the visual reference; final copy is decided in apply (UDS-4).
