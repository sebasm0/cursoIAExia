# user-admin Specification

## Purpose

Admin pages for managing users, roles, and the role-permission matrix, each gated by the corresponding `admin.*` policy. Pages are plain Razor views styled per the design system (ADMIN-8/ADMIN-9).

## Requirements

### Requirement: ADMIN-1 — Users index (admin.users)

`GET /Admin/Users` MUST list users (username, email, roles) and MUST be protected by the `admin.users` policy. An admin MUST NOT be able to delete their own account.

#### Scenario: Admin lists users

- GIVEN an admin with `admin.users`
- WHEN they request `/Admin/Users`
- THEN a table of users with username, email, and roles is rendered

#### Scenario: Non-admin denied

- GIVEN an authenticated user without `admin.users`
- WHEN they request `/Admin/Users`
- THEN the access-denied page is shown

### Requirement: ADMIN-2 — User create (admin.users)

`GET/POST /Admin/Users/Create` MUST create a user with the chosen roles and MUST be protected by the `admin.users` policy. Duplicate usernames or emails MUST fail with a validation error and create no account.

#### Scenario: Admin creates user with roles

- GIVEN an admin with `admin.users` submitting username, email, password, and roles
- WHEN the create form is posted
- THEN the account is created and the new user can sign in

#### Scenario: Duplicate email rejected

- GIVEN an email already registered
- WHEN the create form is posted
- THEN the form re-renders with a validation error
- AND no account is created

### Requirement: ADMIN-3 — User edit and role assignment (admin.users)

`GET/POST /Admin/Users/Edit/{id}` MUST update email and role membership (via `AddToRolesAsync`/`RemoveFromRolesAsync`) and MUST be protected by the `admin.users` policy. An admin MUST NOT be able to remove their own last `admin.users` grant (prevents admin lockout).

#### Scenario: Admin edits user roles

- GIVEN an admin with `admin.users` editing an existing user
- WHEN roles are added/removed and the form is posted
- THEN the user's role membership reflects the changes

### Requirement: ADMIN-4 — Roles index with delete guards (admin.roles)

`GET /Admin/Roles` MUST list roles (name, permission count, user count) and MUST be protected by the `admin.roles` policy. The built-in `Admin` role and any role that still has members MUST NOT be deletable.

#### Scenario: Role with members cannot be deleted

- GIVEN a role assigned to at least one user
- WHEN an admin attempts to delete it
- THEN deletion is refused with an explanatory message

### Requirement: ADMIN-5 — Role create (admin.roles)

`GET/POST /Admin/Roles/Create` MUST create a role with a unique name and MUST be protected by the `admin.roles` policy. Duplicate names MUST be rejected.

#### Scenario: Admin creates a role

- GIVEN an admin with `admin.roles` submitting a new unique role name
- WHEN the create form is posted
- THEN the role is created and appears in the roles index

### Requirement: ADMIN-6 — Role edit permission matrix (admin.permissions)

`GET/POST /Admin/Roles/Edit/{id}` MUST render a checkbox matrix of the full permission catalog against the role and MUST persist grants as role claims. Protected by the `admin.permissions` policy.

#### Scenario: Admin toggles permissions

- GIVEN an admin with `admin.permissions` editing a role
- WHEN checkboxes are toggled and the form is posted
- THEN the role's permission claims match the checked set
- AND a subsequent sign-in by a member of that role yields the updated permissions

### Requirement: ADMIN-7 — Access-denied page for unauthorized admin access

Requests to admin endpoints without the required `admin.*` permission MUST render the access-denied view (403).

#### Scenario: User without admin.permissions denied

- GIVEN an authenticated user holding only `admin.users`
- WHEN they request `/Admin/Roles/Edit/{id}`
- THEN the access-denied view is returned

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

### Requirement: ADMIN-10 — Delete-gated actions degrade gracefully with JavaScript disabled

The users and roles index delete forms MUST remain submit-able with JavaScript disabled: each delete form MUST render a `<noscript>` submit button so the destructive POST works without the confirm modal. For JavaScript-enabled users the modal path (UDS-7) MUST remain the unchanged interactive flow. Existing delete-guard behavior MUST remain unchanged: an admin MUST NOT delete their own account, and built-in or member-bearing roles MUST NOT be deleted.

#### Scenario: No-JS admin submits delete directly

- GIVEN an authenticated admin with `admin.users` (or `admin.roles`) and JavaScript disabled
- WHEN they submit the delete form for a deletable user (or a deletable role with no members)
- THEN the destructive POST is sent to the delete action
- AND the existing guard logic (own-account / built-in / member-bearing protections) still applies

#### Scenario: JS-enabled user keeps the modal path

- GIVEN an authenticated admin with JavaScript enabled viewing a delete row
- WHEN they click Delete
- THEN the confirm modal opens and blocks until confirm or cancel (UDS-7 unchanged)
- AND the `<noscript>` fallback content renders inert / non-interactive for scripting-enabled browsers
- AND existing row markup and behavior for JS users remain unchanged

## Assumptions

- Admin pages are plain Razor views styled per the design system (ADMIN-8/ADMIN-9).
- Built-in `Admin` role is seeded and not deletable.
