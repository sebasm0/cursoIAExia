# Verify Report: aspnet-auth-rbac

- **Phase**: SDD VERIFY (independent validation)
- **Repo**: `D:\cursoIAExia-master` · **Suite**: `tests/RAG.Mvc.Tests` (59 tests)
- **Verdict**: **PASS** — 23/23 requirements implemented and tested; 0 CRITICAL, 0 WARNING, 4 SUGGESTION
- **Date**: 2026-08-03

## 1. Build & Test Results

| Command | Exit | Result | Output hash (SHA-256) |
|---|---|---|---|
| `dotnet test` (repo root, RAG.slnx) | 0 | **59 passed / 0 failed / 0 skipped** (8 s) | `4AF1C75B53F18541B77A8807CB51C6E6B73942BE8A6953D13391C6C8A0078924` |
| `dotnet build RAG.slnx` | 0 | 0 errors, 2 warnings (NU1510, pre-existing, out of scope) | `B6E73545E3881765E2909B0B3B1142F019AF8F4CDCDDD330EB72A66677055D22` |

Focused spot-checks (independent re-runs):
- `--filter ~PolicyEnforcementTests` → **10/10** (ASK-8 + UPLOAD-9 policy gates)
- `--filter ~AccountLoginFlowTests|~AccountPasswordRecoveryTests|~AdminUserFlowTests|~AdminRoleFlowTests` → **30/30** (login/logout/lockout/access-denied/admin CRUD/matrix)
- `--filter ~AskControllerTests|~DocumentsControllerTests` → **5/5** (legacy flows with authenticated clients)

Git: 15 auth commits on `main` (`64782c6..0c83897`), slice-split matches tasks 1.1–4.4; working tree clean except untracked `openspec/changes/` (by design).

## 2. Requirements Coverage Matrix

Impl = implementation evidence · Test = test evidence. **All requirements PASS.**

### user-auth (AUTH-1..9)

| Req | Implementation (file/line) | Test evidence | Result |
|---|---|---|---|
| AUTH-1 Login issues cookie; local returnUrl only | `rag/Controllers/AccountController.cs:44-77` — `PasswordSignInAsync` + `Url.IsLocalUrl` guard | `AccountLoginFlowTests.Login_Post_ValidCredentials_IssuesAuthCookieAndRedirectsToLocalReturnUrl`, `Login_Post_ValidCredentials_ExternalReturnUrlRedirectsToHome` | PASS |
| AUTH-2 Generic error + lockout | `AccountController.cs:51-76` (`lockoutOnFailure: true`); `IdentityServiceCollectionExtensions.cs:35-36` (5 attempts / 15 min) | `Login_Post_WrongPassword_RendersGenericErrorWithoutCookie`, `Login_Post_CorrectPasswordAfterFiveFailures_RefusedUntilLockoutExpires` | PASS |
| AUTH-3 Logout POST-only | `AccountController.cs:79-88` — POST action only; GET → 404 (no GET action) | `Logout_Get_DoesNotSignOut`, `Logout_Post_SignsOutClearsCookieAndRedirectsHome` | PASS |
| AUTH-4 Forgot password no existence leak | `AccountController.cs:101-127` — token only when user exists, identical generic confirmation | `ForgotPassword_Post_ExistingAccount_SendsResetLinkAndShowsGenericConfirmation`, `ForgotPassword_Post_UnknownAccount_SameConfirmationAndNothingSent` | PASS |
| AUTH-5 Reset validates token | `AccountController.cs:139-162` | `ResetPassword_Get_WithValidToken_RendersTheForm`, `ResetPassword_Post_ValidToken_ChangesPasswordAndAllowsSignInWithIt`, `ResetPassword_Post_InvalidToken_ShowsErrorAndKeepsOldPassword` | PASS |
| AUTH-6 No public signup | No Register action anywhere; create only in `AdminController.UsersCreate` under `admin.users` | `Register_Routes_NoAnonymousSignupPossible` | PASS |
| AUTH-7 Seeded bootstrap admin, idempotent | `IdentitySeeder.cs:86-126` — seeds only on empty DB, `__SECRET__` fail-fast | `SeedAsync_CreatesBootstrapAdminWithAdminRoleWhenDatabaseIsEmpty`, `SeedAsync_IsIdempotent_NeverDuplicatesOrModifiesTheAdmin`, `SeedAsync_SkipsAdminBootstrapWhenUsersAlreadyExist`, `SeedAsync_ThrowsWhenSeedAdminPasswordIsTheUnsetPlaceholder` | PASS |
| AUTH-8 Anonymous → login + returnUrl | `IdentityServiceCollectionExtensions.cs:43-52` (LoginPath, ReturnUrlParameter); challenge in auth middleware | `CookieConfig_LoginAndAccessDeniedPaths_AreWired` (config) + `Ask_Get_Anonymous_RedirectsToLogin`, `Upload_Get_Anonymous_RedirectsToLogin` (e2e 302 + returnUrl) | PASS |
| AUTH-9 Test auth handler w/ WebApplicationFactory | `tests/.../Auth/TestAuthHandler.cs` + `RagWebApplicationFactoryBase.cs` | `Ask_Get_WithRagAskPermission_ReturnsOk` (claims → 200, no DB) + entire PolicyEnforcementTests suite | PASS |

### user-rbac (RBAC-1..5)

| Req | Implementation | Test evidence | Result |
|---|---|---|---|
| RBAC-1 Catalog = exactly 7, no CRUD | `src/.../Identity/Permissions.cs:7-42`; no permission CRUD controller/actions exist | `PermissionsCatalogTests.All_ContainsExactlyTheSevenCanonicalPermissions`, `ClaimType_UsesPermissionConstant`, `SeedRoles_*` | PASS (see S-3) |
| RBAC-2 Grants persisted as `(permission, name)` role claims | `IdentitySeeder.cs:62-83`; `AdminController.Roles.cs:171-195` (AddClaim/RemoveClaim) | `SeedAsync_CreatesSeedRolesWithCatalogPermissionClaims`, `AdminRoleEdit_Post_ToggledMatrixPersistsPermissionClaims` (asserts claim set) | PASS |
| RBAC-3 Claims factory projects permissions | `AppUserClaimsPrincipalFactory.cs:27-54` | `AppUserClaimsPrincipalFactoryTests.CreateAsync_ProjectsRolePermissionClaimsOntoPrincipal`, `_MultipleRoles_AggregatesPermissionsFromAllRoles`, `_RoleWithoutPermissionClaims_YieldsNoPermissionClaims` | PASS |
| RBAC-4 One policy per catalog entry, enforced | `IdentityServiceCollectionExtensions.cs:66-78` (`RequireClaim(Permissions.ClaimType, p)`); `[Authorize(Policy=…)]` on Ask/Documents/Admin (all 10 admin actions) | `Ask_Get_WithRagAskPermission_ReturnsOk`, `Upload_Get_WithoutDocumentsUploadPermission_RoutesToAccessDenied`, `Ask_Get_Anonymous_RedirectsToLogin`, `AdminUsersIndex_UserWithoutAdminUsersPermission_RoutedToAccessDenied` | PASS |
| RBAC-5 Denied → AccessDeniedPath, never login/bare 403 | `IdentityServiceCollectionExtensions.cs:46` (`AccessDeniedPath=/Account/AccessDenied`) + `AccountController.AccessDenied` view | `Ask_Get_WithoutRagAskPermission_RoutesToAccessDenied`, `Upload_Get_WithoutDocumentsUploadPermission_RoutesToAccessDenied`, `AdminRolesIndex_UserWithoutAdminRolesPermission_RoutedToAccessDenied` (all assert redirect to `/Account/AccessDenied` + view body) | PASS |

### user-admin (ADMIN-1..7)

| Req | Implementation | Test evidence | Result |
|---|---|---|---|
| ADMIN-1 Users index + self-delete guard | `AdminController.cs:40-55` (`admin.users`), `:202-230` self-delete guard | `AdminUsersIndex_WithAdminUsersPermission_ListsUsersWithRoles`, `AdminUsersDelete_OwnAccount_RefusedAndUserRemains`, `AdminUsersIndex_UserWithoutAdminUsersPermission_RoutedToAccessDenied` | PASS |
| ADMIN-2 User create + duplicate rejection | `AdminController.cs:69-103` (`AddIdentityErrors`) | `AdminUserCreate_Post_CreatesUserWithRoles_AndNewUserCanSignIn`, `AdminUserCreate_Post_DuplicateEmail_ShowsValidationErrorAndCreatesNoAccount` | PASS |
| ADMIN-3 Edit roles + self admin.users lockout guard | `AdminController.cs:128-198` (`AddToRolesAsync`/`RemoveFromRolesAsync`, `GetEffectivePermissionsAsync` guard) | `AdminUserEdit_Post_AddsAndRemovesRoles`, `AdminUserEdit_Post_RemovingOwnLastAdminUsersGrant_Refused` | PASS |
| ADMIN-4 Roles index + delete guards | `AdminController.Roles.cs:25-45` (`admin.roles`), `:89-124` (built-in + members guards) | `AdminRolesIndex_WithAdminRolesPermission_ListsRolesWithCounts`, `AdminRoleDelete_RoleWithMembers_RefusedWithMessage`, `AdminRoleDelete_BuiltInAdminRole_Refused` | PASS |
| ADMIN-5 Role create + duplicate rejection | `AdminController.Roles.cs:56-85` | `AdminRoleCreate_Post_CreatesUniqueRole_AndAppearsInIndex`, `AdminRoleCreate_Post_DuplicateName_ShowsValidationError` | PASS |
| ADMIN-6 Permission matrix (checkbox + claims) | `AdminController.Roles.cs:128-199` (`admin.permissions`, diff → AddClaim/RemoveClaim) | `AdminRoleEdit_Get_RendersFullPermissionMatrix`, `AdminRoleEdit_Post_ToggledMatrixPersistsPermissionClaims`, `AdminRoleEdit_PermissionChange_ReflectedInNextSignIn` | PASS |
| ADMIN-7 Access-denied for admin endpoints | Cookie AccessDeniedPath + 403 view | `AdminRoleEdit_UserWithOnlyAdminUsersPermission_RoutedToAccessDenied`, `AdminRolesIndex_UserWithoutAdminRolesPermission_RoutedToAccessDenied` | PASS |

### Deltas (ASK-8, UPLOAD-9)

| Req | Implementation | Test evidence | Result |
|---|---|---|---|
| ASK-8 Ask gated by `rag.ask`, before pipeline | `rag/Controllers/AskController.cs:14` — class-level `[Authorize(Policy = Permissions.RagAsk)]` (gates GET + POST) | 5× `PolicyEnforcementTests.Ask_*`: anon GET/POST → 302 `/Account/Login?returnUrl=…`; with claim → 200; without → 302 `/Account/AccessDenied` | PASS |
| UPLOAD-9 Upload gated by `documents.upload`, before ingestion | `rag/Controllers/DocumentsController.cs:15` — class-level `[Authorize(Policy = Permissions.DocumentsUpload)]` | 5× `PolicyEnforcementTests.Upload_*`: anon GET/POST → 302 login; with claim → 200; without → 302 AccessDenied (no ingestion can occur — gate fires pre-action) | PASS |

## 3. Design Invariants

| Invariant | Evidence | Result |
|---|---|---|
| EF touches only `identity.*` | Migration `20260803230608_InitialIdentity.cs` + snapshot: `CreateSchema("identity")`, all 7 `AspNet*` tables `schema: "identity"`, FKs `principalSchema: "identity"` | PASS |
| Dapper touches only `public.documents`/`chunks` | `PgVectorStore.cs` — all SQL on `documents`/`chunks` only (CREATE/INSERT/COPY/SELECT/DELETE), zero `identity` references | PASS |
| No `search_path` to identity | `grep -r "search_path|SearchPath" src/` → **0 matches** | PASS |
| RAG.Api untouched | `git log -- src/RAG.Api` → only initial commit `86b04c9`; `git log -- src/RAG.Infrastructure/VectorStore scripts/init-db.sql` → no commits in this change | PASS |
| Secrets not in appsettings.json | `rag/appsettings.json:21,25-29` — connection password + SeedAdmin Email/Password all `__SECRET__`; `IdentitySeeder` fails fast on placeholder; `rag.csproj` has UserSecretsId | PASS |
| `[ValidateAntiForgeryToken]` on all new POSTs | Exactly 10: AccountController ×4 (Login, Logout, ForgotPassword, ResetPassword) + AdminController ×3 (Create/Edit/Delete) + AdminController.Roles ×3 (Create/Delete/Edit) | PASS |
| No global antiforgery filter | `Program.cs:10` plain `AddControllersWithViews()` (no options/filter); 0 `AutoValidateAntiforgeryToken` matches in `rag/` | PASS |

## 4. Findings

### CRITICAL
None.

### WARNING
None.

### SUGGESTION
- **S-1 (CSRF on pre-existing POSTs)**: `AskController.Ask` and `DocumentsController.Upload` POSTs have no `[ValidateAntiForgeryToken]`. This matches design D5 (scope = new Account/Admin POSTs) and the ASK-8/UPLOAD-9 specs do not demand it, so it is not a violation — but a future hardening change should add it (form tag helpers already emit tokens, so it is a low-cost win).
- **S-2 (Nu1510 warnings)**: 2× NU1510 on `Microsoft.Extensions.Configuration.Abstractions` in `RAG.Infrastructure.csproj`. Pre-existing and out of scope; remove the redundant PackageReference on the next csproj touch.
- **S-3 (RBAC-1 "no permission CRUD" scenario untested)**: the no-CRUD property is guaranteed structurally (code-only catalog, no permission controller) and by the exact-7 catalog tests, but there is no explicit test asserting the admin UI offers no create/edit/delete-permission actions. Add a guard test (e.g., assert no routes/actions named `Permission*`) if belt-and-braces coverage is desired.
- **S-4 (Denied-path body assertions vary)**: some deny tests assert the `/Account/AccessDenied` view body ("Access denied"), others only the redirect Location. Consistent body assertions across all deny tests would strengthen the RBAC-5 regression net (cosmetic).

## 5. Artifacts & Persistence

- **Topic**: `sdd/aspnet-auth-rbac/verify-report` (Engram, project `cursoiaexia-master`)
- **File**: `openspec/changes/aspnet-auth-rbac/verify-report.md`

**Next recommended: `archive`** — implementation verified complete and green; no blockers.
