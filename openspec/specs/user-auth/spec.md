# user-auth Specification

## Purpose

ASP.NET Core Identity authentication for the RAG MVC app: cookie sign-in backed by EF Core over an isolated `identity` schema, login/logout, password recovery delivered via a console email stub, admin-gated account creation (no public signup), seeded bootstrap admin, and lockout policy. All flows must be testable with `WebApplicationFactory`.

## Requirements

### Requirement: AUTH-1 — Successful login issues an authentication cookie

A POST `/Account/Login` form (antiforgery-protected) MUST authenticate valid credentials via `SignInManager.PasswordSignInAsync` and MUST issue an authentication cookie. Successful login MUST redirect to `returnUrl` when it is a safe local URL, otherwise to the home page.

#### Scenario: Valid credentials sign the user in

- GIVEN an active user with the correct password
- WHEN the user POSTs credentials with a local `returnUrl`
- THEN the response is a 302 redirect to `returnUrl`
- AND the response sets an authentication cookie

#### Scenario: External returnUrl rejected

- GIVEN a login POST with `returnUrl` pointing to an external host
- WHEN credentials are valid
- THEN the system redirects to the home page
- AND no external redirect occurs (open-redirect guard)

### Requirement: AUTH-2 — Failed login shows a generic error and enforces lockout

The system MUST NOT issue a cookie on invalid credentials, MUST increment the failed-attempt counter, and MUST lock the account after `MaxFailedAccessAttempts` (default 5) within `DefaultLockoutTimeSpan`. Login MUST show a generic invalid-credentials message without revealing which input was wrong.

#### Scenario: Wrong password rejected

- GIVEN a registered user submitting a wrong password
- WHEN the login POST is processed
- THEN the form re-renders with a generic error
- AND no authentication cookie is issued

#### Scenario: Lockout after repeated failures

- GIVEN 5 consecutive failed login attempts for one account
- WHEN the user attempts a valid password on the 6th attempt
- THEN login is refused until the lockout window expires

### Requirement: AUTH-3 — Logout is POST-only and clears the session

`GET /Account/Logout` MUST NOT sign the user out. A POST (antiforgery-protected) MUST call `SignInManager.SignOutAsync`, expire the cookie, and redirect to the home page.

#### Scenario: Logout form signs out

- GIVEN an authenticated session
- WHEN the user submits the logout POST form
- THEN the authentication cookie is cleared
- AND the response redirects to the home page

#### Scenario: GET logout does not sign out

- GIVEN an authenticated session
- WHEN the user navigates to `/Account/Logout` via GET
- THEN the session remains authenticated

### Requirement: AUTH-4 — Forgot password does not leak account existence

`POST /Account/ForgotPassword` MUST always show the same generic confirmation. When the account exists, the system MUST generate a reset token via `UserManager.GeneratePasswordResetTokenAsync` and MUST log the reset link/token to the console (email sender stub). When it does not exist, it MUST NOT log a token.

#### Scenario: Existing account — token logged to console

- GIVEN a registered user requesting a reset
- WHEN the forgot-password POST is processed
- THEN the system shows a generic confirmation
- AND a reset token/link is written to the console log

#### Scenario: Unknown account — generic response

- GIVEN an unregistered email address
- WHEN the forgot-password POST is processed
- THEN the system shows the same generic confirmation
- AND no token is generated or logged

### Requirement: AUTH-5 — Reset password validates token and changes password

`POST /Account/ResetPassword` MUST change the password only when `userId` + token are valid and unexpired. On success the user MUST be able to sign in with the new password. Invalid or expired tokens MUST surface an error and MUST NOT change the password.

#### Scenario: Valid token resets the password

- GIVEN a reset link with a valid, unexpired token
- WHEN the user submits a new password
- THEN the password is updated
- AND the user can sign in with the new password

#### Scenario: Invalid token rejected

- GIVEN a reset attempt with a tampered or expired token
- WHEN the user submits a new password
- THEN an error message is shown
- AND the existing password remains valid

### Requirement: AUTH-6 — Accounts are created only by administrators

There MUST be no public self-signup route. Account creation MUST be available only through admin pages protected by the `admin.users` policy.

#### Scenario: No public registration

- GIVEN an anonymous visitor
- WHEN they request a sign-up route
- THEN the system redirects to login or returns 404
- AND no account can be created anonymously

#### Scenario: Admin creates an account

- GIVEN an admin with `admin.users`
- WHEN the admin submits the user-create form
- THEN the account is created with the chosen roles

### Requirement: AUTH-7 — Seeded bootstrap admin

Startup seeding MUST create the configured bootstrap admin (from appsettings/User Secrets) with the Admin role (all permissions) when no admin exists. Seeding MUST be idempotent and MUST NOT recreate or reset the admin on subsequent boots.

#### Scenario: Fresh database bootstrap

- GIVEN an empty `identity` schema
- WHEN the application starts
- THEN the bootstrap admin account exists with the Admin role
- AND the admin can sign in with the configured credentials

#### Scenario: Idempotent reseed

- GIVEN a database already containing the bootstrap admin
- WHEN the application restarts
- THEN the admin account is not duplicated or modified

### Requirement: AUTH-8 — Unauthenticated access redirects to login

Any request to an `[Authorize]` endpoint by an anonymous principal MUST redirect to `/Account/Login` carrying the original URL as `returnUrl`. After login the user MUST land back on the originally requested page.

#### Scenario: Anonymous request to protected page

- GIVEN no authentication cookie
- WHEN the user requests a protected endpoint
- THEN the response is a 302 to `/Account/Login?returnUrl=...`
- AND after successful login the user lands on the original page

### Requirement: AUTH-9 — Test auth-handler fixture works with WebApplicationFactory

The test project MUST ship a test authentication handler that sets a claims principal (identity + permission claims) without a real database and MUST be usable through `WebApplicationFactory` to exercise `[Authorize(Policy=...)]` endpoints.

#### Scenario: Authenticated test client passes policy checks

- GIVEN a `WebApplicationFactory` configured with the test auth handler
- WHEN a test sends a request with a principal carrying `rag.ask`
- THEN the protected endpoint returns 200 instead of redirecting to login

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

- Identity + EF Core target only the isolated `identity` schema; Dapper continues to own RAG tables.
- No SMTP: password-recovery delivery is a console-logging stub.
- No public self-signup; admins create accounts.
- RAG.Api remains unauthenticated (out of scope for this change).
