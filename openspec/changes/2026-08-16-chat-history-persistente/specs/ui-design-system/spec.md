# Delta for ui-design-system

> Scope expansion (approved 2026-08-16): the approved visual scope adds contrast conformance fixes. UDS-1 already forbids hardcoded ad-hoc colors; UDS-9..UDS-11 pin the three fixed surfaces. UDS-1..UDS-8 remain unchanged.

## ADDED Requirements

### Requirement: UDS-9 — Privacy page text resolves from theme tokens

`Home/Privacy` MUST render its section headings and emphasized list labels with theme tokens (`var(--bs-body-color)` / `var(--rag-text-primary)`) instead of `.text-white`, so the text is readable on the light card surface and in the dark variant (UDS-1 conformance).

#### Scenario: Privacy readable in light theme

- GIVEN the light theme
- WHEN `/Home/Privacy` renders
- THEN headings and list labels resolve from the light token values (dark text on light surface) and are readable

#### Scenario: Privacy readable in dark theme

- GIVEN the dark theme
- WHEN `/Home/Privacy` renders
- THEN the same text resolves from the dark token variant and remains readable

### Requirement: UDS-10 — Sidebar username resolves from theme tokens

The sidebar profile-trigger username MUST derive its color from theme tokens instead of the hardcoded `#ffffff`, so it is readable in both themes.

#### Scenario: Username readable in light theme

- GIVEN the light theme
- WHEN the sidebar profile trigger renders
- THEN the username color resolves from the light token variant and is readable

#### Scenario: Username readable in dark theme

- GIVEN the dark theme
- WHEN the sidebar profile trigger renders
- THEN the username color resolves from the dark token variant and is readable

### Requirement: UDS-11 — Message author avatar fully desaturated

The `.msg-author-avatar` grayscale filter MUST apply full desaturation (`grayscale(100%)`, matching `.avatar-gray`) instead of the current `grayscale(1%)` typo.

#### Scenario: Avatar renders desaturated

- GIVEN a message row with a user avatar
- WHEN the row renders
- THEN the avatar applies `grayscale(100%)`