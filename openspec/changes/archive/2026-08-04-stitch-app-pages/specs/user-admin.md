# Delta for user-admin

## ADDED Requirements

### Requirement: ADMIN-8 — Admin screens follow the design system

The users and roles index/create/edit views MUST follow the design system (UDS-1..UDS-4): token styling, shared layout, responsive tables. All `admin.*` policy gating (ADMIN-1..ADMIN-6) MUST remain unchanged.

#### Scenario: Users index styled and responsive

- GIVEN an admin with `admin.users`
- WHEN they request `/Admin/Users`
- THEN the users table renders per the design system
- AND the table is usable on narrow viewports without horizontal page overflow

#### Scenario: Create and edit forms per design system

- GIVEN an admin with `admin.users` on the create or edit form
- WHEN the form renders
- THEN fields and validation messages use the design-system styling
- AND duplicate/validation errors still re-render the form (ADMIN-2/ADMIN-3 unchanged)

### Requirement: ADMIN-9 — Permission-matrix screen per design system

The role-edit permission matrix view MUST follow the design system and MUST preserve ADMIN-6 behavior: full permission catalog as a checkbox matrix, grants persisted as role claims.

#### Scenario: Matrix renders per design system

- GIVEN an admin with `admin.permissions` editing a role
- WHEN the matrix renders
- THEN the full permission catalog displays as checkboxes with token-based styling
- AND toggling and posting persists grants as role claims (ADMIN-6 unchanged)

## Assumptions

- Requirement IDs ADMIN-1..ADMIN-7 are unchanged; this delta adds UI presentation requirements only.
- The permission catalog remains static and code-defined (RBAC-1 unchanged).