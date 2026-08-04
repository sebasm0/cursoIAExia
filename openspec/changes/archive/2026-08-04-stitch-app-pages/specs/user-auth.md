# Delta for user-auth

## ADDED Requirements

### Requirement: AUTH-10 — Login screen follows the design system

The login view MUST follow the design system (UDS-1..UDS-4) and MUST preserve AUTH-1/AUTH-2 behavior: generic invalid-credentials error, lockout messaging, safe `returnUrl` handling.

#### Scenario: Login renders per design system

- GIVEN an anonymous visitor on the login page
- WHEN the page renders
- THEN the login form uses token-based styling, the shared layout, and real copy

#### Scenario: Invalid credentials keep the generic error

- GIVEN a registered user submitting a wrong password
- WHEN the login POST is processed
- THEN the form re-renders with the generic styled error
- AND no authentication cookie is issued (AUTH-2 unchanged)

### Requirement: AUTH-11 — Logout control in the shared layout

The shared layout MUST render a logout control (POST form) when the user is signed in, styled per the design system. Logout MUST remain POST-only (AUTH-3 unchanged).

#### Scenario: Signed-in user sees logout

- GIVEN an authenticated session
- WHEN the layout renders
- THEN a logout control is visible in the navbar
- AND submitting it clears the session and redirects home (AUTH-3 unchanged)

### Requirement: AUTH-12 — Recover screens follow the design system

The forgot-password and reset-password views MUST follow the design system and MUST preserve AUTH-4/AUTH-5 behavior: generic confirmation without leaking account existence; token validation before a password change.

#### Scenario: Forgot password shows generic confirmation

- GIVEN a registered or unregistered email
- WHEN the forgot-password POST is processed
- THEN a token-styled generic confirmation renders
- AND no account-existence signal is revealed (AUTH-4 unchanged)

#### Scenario: Reset success and invalid-token states

- GIVEN a reset attempt
- WHEN the reset view renders after processing
- THEN success and invalid/expired-token outcomes display per the design system
- AND a password change occurs only for a valid token (AUTH-5 unchanged)

### Requirement: AUTH-13 — Access-denied screen follows the design system

The access-denied view (403) MUST follow the design system and MUST remain the routing target for missing permissions (RBAC-5, AUTH-8 unchanged).

#### Scenario: Access-denied styled

- GIVEN an authenticated user lacking a required permission
- WHEN a protected request is made
- THEN the token-styled access-denied view renders (403)
- AND the user is never redirected to login or a bare 403

## Assumptions

- Requirement IDs AUTH-1..AUTH-9 are unchanged; this delta adds UI presentation requirements only.
- No controller, service, or model changes.