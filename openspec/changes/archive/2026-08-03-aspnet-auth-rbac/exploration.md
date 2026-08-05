# Exploration: ASP.NET Authentication + RBAC with Granular Permissions

> Change: `aspnet-auth-rbac` — Add ASP.NET auth to the RAG MVC app: users, roles, granular
> permissions (permission-based authorization, not just role membership), plus Razor pages
> for login/logout/password recovery/sign-up and user/role/permission administration.
> Exploration only — no code written.

## Current State

- **Zero authentication.** `rag/Program.cs` calls `UseAuthorization()` but there is **no
  `UseAuthentication()`** and no authentication/authorization services are registered —
  `UseAuthorization()` is a no-op today. Controllers (`AskController`, `DocumentsController`,
  `HomeController`) are fully open.
- **Data access is 100% Dapper + Npgsql raw SQL.** `src/RAG.Infrastructure/VectorStore/PgVectorStore.cs`
  owns `documents` / `chunks` tables via self-healing `EnsureSchemaAsync` (`CREATE TABLE IF NOT EXISTS`,
  `CREATE EXTENSION IF NOT EXISTS vector`, ivfflat index). `scripts/init-db.sql` mirrors the same DDL.
  **No EF Core anywhere** (no `Microsoft.EntityFrameworkCore` / `Npgsql.EntityFrameworkCore.PostgreSQL`
  package references in any `.csproj`), **no migrations infrastructure** (`dotnet ef` tool is NOT installed;
  SDK is 10.0.302).
- **Two entry points.** `src/RAG.Api` (Minimal API: `POST /api/rag/ingest`, `POST /api/rag/ask`,
  `GET /api/rag/health` — no auth, out of scope for this change) and `rag/` MVC app (the target).
  Clean Architecture: Domain → Application → Infrastructure → Api/MVC. `rag/` references Application +
  Domain + Infrastructure; `InternalsVisibleTo RAG.Mvc.Tests`.
- **DI pattern:** `AddApplication()` registers `IngestionService` + `RagService` (singletons);
  `AddRagInfrastructure(config)` registers `IVectorStore` (singleton `PgVectorStore`),
  `IChunker`, `IReranker`, `IDocumentParser` collection. Connection string read from
  `ConnectionStrings:PostgreSQL`; sensitive values via **User Secrets** (`__SECRET__` placeholder in
  appsettings.json, `UserSecretsId` present in `rag.csproj`).
- **Existing flows:** `DocumentsController.Upload` (IFormFile, extension whitelist .cs/.md/.pdf, size
  limit, MVC antiforgery default) and `AskController.Ask` (query → `RagService.AskAsync`). Both return
  views; upload result view shows name/size/timestamp. `_Layout.cshtml` navbar has Home/Privacy/Ask/Upload
  links — no auth affordances.
- **Multi-tenancy is SHOULD-only** (ASK-6, UPLOAD-7) and explicitly deferred in the archived design:
  `IngestionService.IngestAsync` / `RagService.AskAsync` have no `userId`; `documents` has no owner
  column. Auth is the foundation for that future change, but this change must NOT implement it.
- **Tests:** xUnit 2.9.3 + Moq + `Microsoft.AspNetCore.Mvc.Testing` 10.0.10 (tests/RAG.Mvc.Tests).
  Two custom factories (`CustomRagWebApplicationFactory`, `CustomUploadWebApplicationFactory`) extend
  `WebApplicationFactory<Program>` and stub AI clients + `IVectorStore`/`IChunker`/`IDocumentParser`
  via `ConfigureServices` removal/replacement. Unit tests instantiate controllers directly with Moq.
- **Downstream change:** `stitch-app-pages` (Google Stitch UI redesign) is planned AFTER this change
  and explicitly excludes auth screens from its design — Razor auth pages must stay simple/functional,
  visual polish deferred.

## Affected Areas

- `rag/Program.cs` — register Identity + cookie auth + authorization policies; add `UseAuthentication()`
  before `UseAuthorization()`; migrate identity schema at startup.
- `rag/rag.csproj` — add `Microsoft.AspNetCore.Identity.EntityFrameworkCore`,
  `Npgsql.EntityFrameworkCore.PostgreSQL` (v10.x) package references.
- `src/RAG.Infrastructure/` (new `Identity/` folder) — `ApplicationUser : IdentityUser<Guid>`,
  `ApplicationRole : IdentityRole<Guid>`, `AppIdentityDbContext : IdentityDbContext<...>`,
  `AddRagIdentity(config)` extension (called only by `rag/`). Keeps Clean Architecture layering;
  `RAG.Api` never calls it.
- `rag/Controllers/AccountController.cs` (new) — login, logout, forgot/reset password, register,
  access denied.
- `rag/Controllers/AdminController.cs` (new) — user list/create/edit/delete, role list/create/edit,
  role→permission matrix, user→role assignment.
- `rag/Controllers/AskController.cs`, `rag/Controllers/DocumentsController.cs` — add
  `[Authorize(Policy = ...)]` (breaking change for existing integration tests).
- `rag/Views/Account/*`, `rag/Views/Admin/*` (new Razor views) + `rag/Models/` ViewModels.
- `rag/Views/Shared/_Layout.cshtml` — login/logout/username in navbar; permission-conditional nav items.
- `tests/RAG.Mvc.Tests/` — new auth test fixtures (test auth handler); update existing factories
  and integration tests that now hit `[Authorize]` endpoints.
- `rag/appsettings.json` / User Secrets — password policy, cookie, lockout, seed-admin config.
- `scripts/init-db.sql` — optional: append Identity schema DDL for manual provisioning parity.

## Approaches

### 1. ASP.NET Core Identity + EF Core (RECOMMENDED)

Full `Microsoft.AspNetCore.Identity` with `IdentityDbContext`, EF Core 10 migrations, Npgsql provider,
cookie authentication.

- **Pros:**
  - Battle-tested security: PBKDF2 password hashing, lockout, security stamps, anti-forgery, cookie
    hardening, token providers for password recovery/reset — all built-in.
  - **Identity's schema maps 1:1 to the granular permission model**: `AspNetRoleClaims`
    (`ClaimType`/`ClaimValue`) is exactly a role→permission store; `AspNetUserClaims` covers per-user
    grants; `AspNetUserRoles` for role membership.
  - `UserManager`/`RoleManager`/`SignInManager` give the admin pages their data access for free.
  - Standard, documented, well-understood; fits a teaching/course project.
- **Cons:**
  - Introduces EF Core as a second data-access stack alongside Dapper (must be kept strictly isolated
    to Identity tables/schema).
  - First migration system in the repo (requires `dotnet ef` tool install or startup `Migrate()`).
  - Extra package weight; `Microsoft.AspNetCore.Identity.EntityFrameworkCore` + Npgsql provider (10.x).
- **Effort: Medium** (largest of the three, but mostly configuration + scaffolding, not custom logic).

### 2. Identity core + custom Dapper-backed stores (REJECTED)

Use `Microsoft.AspNetCore.Identity` core only; implement `IUserStore`, `IRoleStore`,
`IUserPasswordStore`, `IUserClaimStore`, `IUserRoleStore`, `IRoleClaimStore`, etc. over Dapper.

- **Pros:** keeps a single data-access stack (Dapper).
- **Cons:** ~9 store interfaces / ~40 methods to implement correctly (normalization, concurrency tokens,
  token stores); third-party Dapper Identity providers are stale/unmaintained; high risk of subtle
  security bugs; large test burden. Reimplements what EF stores give for free.
- **Effort: High** with the worst risk profile.

### 3. Fully custom cookie auth + own user tables (REJECTED)

Own users/roles/permissions tables via Dapper, own password hashing, own cookie middleware,
own reset tokens.

- **Pros:** total control, minimal dependencies, single stack.
- **Cons:** reimplements security-critical machinery (hashing, lockout, cookie security, CSRF interplay,
  token generation/expiry, security stamps) — the classic source of auth vulnerabilities; no scaffolding;
  pedagogical anti-pattern for a course project. Multi-tenancy seam (userId) doesn't need this freedom.
- **Effort: High** and security-risky for zero functional gain.

## Recommendation

**Approach 1 — ASP.NET Core Identity with EF Core, scoped to Identity tables only.**

- Register via `AddIdentity<ApplicationUser, ApplicationRole>(...)` (NOT `AddDefaultIdentity`'s UI
  scaffolding — we build our own simple Razor pages) + `.AddEntityFrameworkStores<AppIdentityDbContext>()`
  + `.AddDefaultTokenProviders()` (needed for password recovery/reset) + `ConfigureApplicationCookie`.
- **Isolate the two stacks:** Identity entities/DbContext live in `src/RAG.Infrastructure/Identity/`,
  configured to a dedicated `identity` schema (e.g. `modelBuilder.HasDefaultSchema("identity")`) so EF
  never touches `documents`/`chunks`; Dapper never touches `AspNet*` tables. `PgVectorStore` keeps
  owning the RAG tables via raw SQL.
- **Migrations:** first migration infrastructure in the repo. Either install `dotnet ef` tool
  (`dotnet tool install --global dotnet-ef`) and use `db.Database.Migrate()` at startup (matches the
  self-healing `EnsureSchemaAsync` pattern), or generate schema SQL into `scripts/init-db.sql`.
  Recommend: EF migrations + startup `Migrate()`; pin `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.2+**
  (verified compatible with EF Core 10 / net10.0).
- **IDs:** `IdentityUser<Guid>` / `IdentityRole<Guid>` — consistent with the codebase's UUID convention.
- **Pipeline:** add `UseAuthentication()` between `UseRouting()` and `UseAuthorization()` in `rag/Program.cs`.
- **Credentials:** keep connection string in User Secrets (existing convention), never appsettings.json.

## Permission Model (concrete scheme)

- **Permissions are a static, code-defined catalog** (not DB CRUD — avoids the self-referential
  "who grants admin.permissions" trap). Dotted convention, ~7 entries:
  - `rag.ask` — query the RAG system
  - `documents.upload` — upload documents
  - `documents.view` — view document list/details (future feature seam)
  - `documents.delete` — delete documents (future feature seam)
  - `admin.users` — manage users (create/edit/delete, assign roles)
  - `admin.roles` — manage roles
  - `admin.permissions` — edit the role→permission matrix
- **Storage:** each permission is a role claim → `AspNetRoleClaims(ClaimType = "permission",
  ClaimValue = "documents.upload")`. Role membership via `AspNetUserRoles`. Optional per-user grants
  via `AspNetUserClaims`.
- **Enforcement:** authorization policies built at startup from the catalog —
  `options.AddPolicy("documents.upload", p => p.RequireAssertion(ctx => ctx.User.HasClaim("permission",
  "documents.upload")))` — applied with `[Authorize(Policy = "...")]` on controllers/actions.
- **Claim issuance:** custom `IUserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>` that
  materializes each role's `permission` claims onto the cookie principal (standard pattern), so policies
  see flat `permission` claims. Role claims remain for coarse UI checks.
- **Seed roles:** `Admin` (all permissions), `User` (`rag.ask`, `documents.upload`, `documents.view`),
  optional `Viewer` (`rag.ask` only).
- **Admin UI mapping:** role edit page = role→permission checkbox matrix (gated by `admin.permissions`);
  user edit page = user→role checkbox list (gated by `admin.users`).

## Page Inventory

Account (Razor controllers/views in `rag/`, plain/semantic markup — Stitch redesign comes later):

| Page | Route | Purpose | Key UI elements / data |
|---|---|---|---|
| Login | `GET/POST /Account/Login` | Authenticate | email/username + password + remember-me; redirect to `returnUrl` (validate open redirect); link to register + forgot password; `SignInManager.PasswordSignInAsync` |
| Logout | `POST /Account/Logout` | End session | CSRF-protected form (never GET); `SignInManager.SignOutAsync()` → Home |
| Forgot Password | `GET/POST /Account/ForgotPassword` | Request reset token | email input; always show generic confirmation (no user-existence leak); `GeneratePasswordResetTokenAsync` → email sender stub |
| Reset Password | `GET/POST /Account/ResetPassword` | Set new password | userId + token (query) + new password + confirm; `ResetPasswordAsync`; token via `AddDefaultTokenProviders` |
| Register (Sign up) | `GET/POST /Account/Register` | Self-service account creation | email, username, password, confirm; assigns default `User` role. **Policy decision required: open vs admin-created** |
| Access Denied | `GET /Account/AccessDenied` | 403 page | friendly "you lack permission" view |

Admin (`rag/Controllers/AdminController.cs` + views):

| Page | Route / Policy | Purpose | Key UI elements / data |
|---|---|---|---|
| Users Index | `GET /Admin/Users` (`admin.users`) | List users | table: username, email, roles; actions: edit/delete/create; prevent self-delete |
| User Create | `GET/POST /Admin/Users/Create` (`admin.users`) | Create user | username, email, password, roles checkboxes; `UserManager.CreateAsync` |
| User Edit | `GET/POST /Admin/Users/Edit/{id}` (`admin.users`) | Edit user + roles | email + roles checkbox list; `AddToRolesAsync`/`RemoveFromRolesAsync` |
| Roles Index | `GET /Admin/Roles` (`admin.roles`) | List roles | table: name, permission count, user count; actions; guard: cannot delete built-in `Admin` or role with members |
| Role Create | `GET/POST /Admin/Roles/Create` (`admin.roles`) | Create role | name input |
| Role Edit | `GET/POST /Admin/Roles/Edit/{id}` (`admin.permissions`) | **Permission matrix** | name + checkbox grid of the full permission catalog → `role claims` via `RoleManager.AddClaimAsync/RemoveClaimAsync` |

Layout: `_Layout.cshtml` — logged-in name + Logout (POST form), Login link when anonymous; nav items
conditionally rendered by permission (`rag.ask` → Ask, `documents.upload` → Upload, any `admin.*` → Admin).

## Test Strategy

- **Unit (Moq + direct controller instantiation, existing pattern):**
  - `AccountController` / `AdminController` with a **real `UserManager`/`RoleManager` constructed over
    mocked stores** (e.g. `Mock<IUserStore<ApplicationUser>>` + `IUserPasswordStore` + `IUserRoleStore`
    + `IUserClaimStore` + `IRoleStore`/`IRoleClaimStore`) — exercises real Identity logic
    (normalization, hashing, token generation) deterministically.
  - Policy-requirement tests: `RequirePermission("x")` evaluates a claims principal correctly.
  - Guard-rule unit tests (self-delete prevention, role-with-members deletion, open-redirect check).
- **Integration (`WebApplicationFactory<Program>`, existing pattern):**
  - New **test auth handler** (custom `AuthenticationHandler<AuthenticationSchemeOptions>` that sets a
    claims principal — pattern from MS docs) so policy tests can run as admin / user / anonymous
    without a real DB. Use it to verify `[Authorize(Policy="...")]` → 200 / 403-redirect.
  - Account flows end-to-end: register → login (real cookie issued) → access protected page; logout
    clears it. Requires replacing identity store with an in-memory/EF SQLite provider in test config,
    OR testing against the stubbed-claims handler only. Recommend: real cookie path for Account flows,
    claims handler for the rest.
- **BREAKING for existing tests:** once `[Authorize]` lands on `Ask`/`Documents`, the current
  `CustomRagWebApplicationFactory` / `CustomUploadWebApplicationFactory` integration tests will
  redirect to login → **must be updated in the same change** (add test auth handler + authenticated
  client). strict_tdd means these updates are part of the tasks, not an afterthought.

## Risks

- **CRITICAL — Dual data-access stacks:** EF Core (Identity) + Dapper (RAG) in one DB. EF must be
  confined to a dedicated `identity` schema; Dapper must never touch `AspNet*`. A misconfigured
  `HasDefaultSchema` or a Dapper query touching identity tables = silent data-corruption risk.
- **CRITICAL — Sign-up policy decision needed:** open self-signup vs admin-created users. If open,
  decide the **bootstrap admin** rule (seeded from config vs first-user-is-admin — recommend seeded
  config admin to avoid a race/privilege escalation). Orchestrator must surface this to the user before
  spec.
- **CRITICAL — Migration risk on existing PostgreSQL DB:** no migration tooling exists; `dotnet ef`
  global tool not installed; first migration must run against a DB that already has `documents`/`chunks`
  + `vector` extension. `Migrate()` at startup is the safest (matches self-healing pattern), but requires
  the DB user to have DDL rights on the `identity` schema.
- **WARNING — Existing integration tests break** when auth enforcement lands (see Test Strategy) —
  plan the auth-handler test fixture as part of this change.
- **WARNING — Password recovery email:** no SMTP configured. Need an `IEmailSender` abstraction with a
  log-based/dev implementation; flow must not leak whether an email exists. Dev UX decision needed
  (log the reset token to console?).
- **WARNING — Cookie/account security defaults:** configure lockout, password policy
  (`Password.RequiredLength`, complexity), `requireConfirmedAccount` decision, secure cookies in prod;
  set `AccessDeniedPath`.
- **WARNING — Multi-tenancy stays OUT of scope:** auth gives the `User.Id` seam, but threading userId
  through the pipeline + `documents.owner_id` is a separate change. Do not scope documents by user here;
  document the seam only.
- **WARNING — RAG.Api remains unauthenticated:** Minimal API has its own `Program.cs`; protecting
  `/api/rag/*` (cookie or API-key) is out of scope — flag explicitly so nobody assumes the API is
  secured by this change.
- **WARNING — Stitch redesign follows this change:** keep Razor auth pages plain/semantic; do not invest
  in visual polish that `stitch-app-pages` will replace.
- **LOW — Permission granularity tradeoff:** catalog is static and small (~7 permissions); adding
  permissions is a code change (new catalog entry + policy), not an admin operation — document this so
  admins don't expect permission CRUD.
- **LOW — Package/version pinning:** `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.x +
  `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.2+** (verified available, net10.0-compatible); pin
  exact versions at design time.

## Ready for Proposal

**Yes.** Orchestrator should tell the user:
1. Recommended approach: **ASP.NET Core Identity + EF Core** (Identity-only, isolated `identity` schema)
   with cookie auth — not custom auth or Dapper-backed Identity.
2. Permission model: code-defined permission catalog (`rag.ask`, `documents.upload`, `documents.view`,
   `documents.delete`, `admin.users`, `admin.roles`, `admin.permissions`) stored as role claims,
   enforced via authorization policies + `[Authorize(Policy=...)]`.
3. **Decision needed: sign-up policy** — open self-registration (default `User` role) vs
   admin-created users only, and the bootstrap-admin strategy (seeded config admin recommended).
4. Password-recovery delivery: email sender is a stub (no SMTP) — confirm log-based dev delivery is OK.
5. The Minimal API (`src/RAG.Api`) is intentionally NOT secured by this change.
