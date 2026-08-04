# user-rbac Specification

## Purpose

Role-based granular permissions for the RAG MVC app: a static, code-defined permission catalog stored as role claims, materialized on the cookie principal by a custom claims factory, and enforced through authorization policies applied with `[Authorize(Policy=...)]`.

## Requirements

### Requirement: RBAC-1 — Static code-defined permission catalog

The system MUST define a static permission catalog in code with exactly these entries: `rag.ask`, `documents.upload`, `documents.view`, `documents.delete`, `admin.users`, `admin.roles`, `admin.permissions`. The catalog MUST NOT be editable through the UI; adding a permission requires a code change.

#### Scenario: Catalog matches the contract

- GIVEN the compiled application
- WHEN the catalog is enumerated
- THEN all 7 permissions are present with their canonical names

#### Scenario: No permission CRUD

- GIVEN an admin UI
- WHEN the available actions are inspected
- THEN no create/edit/delete permission operations are offered

### Requirement: RBAC-2 — Permissions stored as role claims

Each role-permission grant MUST be persisted as a role claim in `AspNetRoleClaims` with `ClaimType = "permission"` and `ClaimValue` equal to the permission name. Role membership MUST be persisted via `AspNetUserRoles`.

#### Scenario: Grant persists as a claim

- GIVEN a role granted `documents.upload`
- WHEN the role's claims are read back
- THEN a claim `(permission, documents.upload)` exists for that role

### Requirement: RBAC-3 — Claims factory materializes permissions on the principal

A custom `IUserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>` MUST project each role's `permission` claims onto the signed-in cookie principal so policies evaluate flat permission claims.

#### Scenario: Permissions present after sign-in

- GIVEN a user whose role grants `rag.ask` and `documents.upload`
- WHEN the user signs in
- THEN the cookie principal contains `permission: rag.ask` and `permission: documents.upload`

### Requirement: RBAC-4 — Authorization policies registered from the catalog

The system MUST register one authorization policy per catalog permission at startup (claim-based assertion on `permission` claims) and MUST enforce them with `[Authorize(Policy = "...")]` on the Ask, Upload, and Admin endpoints.

#### Scenario: User with rag.ask can ask

- GIVEN an authenticated principal with `permission: rag.ask`
- WHEN the user requests the Ask page
- THEN the request is authorized (200)

#### Scenario: User without documents.upload cannot upload

- GIVEN an authenticated principal without `permission: documents.upload`
- WHEN the user submits the upload form
- THEN the request is denied and routed to the access-denied page

#### Scenario: Anonymous user redirected to login

- GIVEN an anonymous principal
- WHEN the user requests an Ask or Upload endpoint
- THEN the response redirects to `/Account/Login`

#### Scenario: Admin pages require admin policies

- GIVEN a principal with only `rag.ask`
- WHEN the user requests `/Admin/Users`
- THEN the request is denied and routed to the access-denied page

### Requirement: RBAC-5 — Permission-denied handling

An authenticated principal lacking the required permission MUST be routed to the configured `AccessDeniedPath` (403 view), never to the login page and never to a bare 403.

#### Scenario: Access denied page shown

- GIVEN an authenticated user lacking a required permission
- WHEN a protected request is made
- THEN the response is a 403 with the access-denied view

## Assumptions

- The catalog is static and code-defined; permission CRUD is out of scope.
- Multi-tenant scoping (`documents.owner_id`) is a later change; permissions gate access only.
