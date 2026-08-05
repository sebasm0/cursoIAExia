# Delta for user-admin

## ADDED Requirements

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

- Requirement IDs ADMIN-1..ADMIN-9 are unchanged; this delta adds a no-JS fallback only.
- The `<noscript>` content is browser-suppressed when scripting is enabled, so the JS modal path is unaffected.
- Integration tests already exercising the modal POST path continue to pass; a no-JS submit test asserts the destructive POST succeeds without any modal interaction.