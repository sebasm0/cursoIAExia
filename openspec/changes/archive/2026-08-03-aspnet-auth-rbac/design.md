# Design: ASP.NET Authentication + RBAC

## Technical Approach

ASP.NET Core Identity + EF Core over a dedicated `identity` PostgreSQL schema, cookie auth, static code-defined permission catalog enforced via per-permission authorization policies, custom claims factory, Razor Account/Admin pages, startup `Migrate()` + idempotent seeding. Identity lives in `src/RAG.Infrastructure/Identity/`; only `rag/` registers it — `src/RAG.Api` stays untouched. Covers specs user-auth, user-rbac, user-admin, ASK-8, UPLOAD-9.

## Architecture Decisions

### D1: Package set (verified on NuGet; SDK 10.0.302)

| Package (version) | Project | Why |
|---|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore **10.0.10** | rag + RAG.Infrastructure | EF Identity stores; matches repo's 10.0.10 Microsoft-package convention |
| Npgsql.EntityFrameworkCore.PostgreSQL **10.0.3** | rag + RAG.Infrastructure | Matches existing Npgsql 10.0.3; latest stable 10.x (11.x is preview) |
| Microsoft.EntityFrameworkCore.Design **10.0.10** (PrivateAssets=all) | rag only | Needed only by `dotnet ef` at dev time to generate the migration |
| Microsoft.EntityFrameworkCore.InMemory **10.0.10** | tests only | Real-cookie account-flow tests without Postgres |

Rejected: AddDefaultIdentity scaffolding (we build plain Razor per spec); global dotnet-ef tool (repo-local tool manifest is deterministic/CI-able).

### D2: Migration strategy — startup Migrate(), not EnsureCreated

| Option | Tradeoff | Decision |
|---|---|---|
| `Database.Migrate()` at startup | Needs a committed migration; idempotent; evolvable | **Chosen** — matches self-healing `EnsureSchemaAsync` pattern |
| `EnsureCreated()` | **No-op on existing DBs** (DB already has documents/chunks); can't mix with later migrations | **Rejected** |

Initial migration is generated ONCE at dev time via repo-scoped local tool (`dotnet new tool-manifest` + `dotnet tool install dotnet-ef`), committed as an artifact; runtime needs no tool. Guarded by `Identity:ApplyMigrationsOnStartup` (default true). Fallback if tooling forbidden: hand-write migration (riskier).

### D3: Schema isolation (dual-stack safety)

`AppIdentityDbContext` uses `modelBuilder.HasDefaultSchema("identity")` (all 7 `AspNet*` tables land there). EF emits schema-qualified `identity."AspNet*"` SQL; Dapper's `PgVectorStore` keeps unqualified `documents`/`chunks` → public schema via default search_path. Invariant: EF never touches public.*, Dapper never touches identity.*. **Never set search_path to identity** (would break Dapper). DB user needs CREATE on the database (already implied by `EnsureSchemaAsync`).

### D4: Permission model

Static `Permissions` class in `RAG.Infrastructure.Identity`: 7 constants + `All` + `SeedRoles` (`Admin`=all, `User`=rag.ask/documents.upload/documents.view, `Viewer`=rag.ask). Grants persist as role claims `(ClaimType="permission", Value=<name>)` in `AspNetRoleClaims`. `AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>` projects each role's permission claims onto the cookie principal. One policy per catalog entry via `RequireClaim(Permissions.ClaimType, p)`, registered from the catalog. No permission CRUD — catalog is code.

### D5: No global antiforgery filter

Existing Upload integration test POSTs without an antiforgery token; a global `AutoValidateAntiforgeryToken` filter would break it. Apply `[ValidateAntiForgeryToken]` explicitly on all 4 Account + all Admin POST actions; form tag helpers auto-embed tokens.

### D6: Registration and security config

`AddRagIdentity(config)` in Infrastructure: `AddDbContext<AppIdentityDbContext>(UseNpgsql)` → `AddIdentity<ApplicationUser, ApplicationRole>` (both Guid) → `AddEntityFrameworkStores` → `AddDefaultTokenProviders` (reset tokens) → `ConfigureApplicationCookie` (LoginPath=/Account/Login, AccessDeniedPath=/Account/AccessDenied, ReturnUrlParameter=returnUrl, SlidingExpiration, SecurePolicy=Always in prod) → policy registration → claims factory → `ConsoleEmailSender` (IEmailSender stub). Program.cs adds `UseAuthentication()` between `UseRouting()` and `UseAuthorization()`. Password: ≥8 chars, digit+upper+lower; Lockout 5 attempts / 15 min; `RequireConfirmedAccount=false` (admin-created users).

## Data Flow

```
POST /Account/Login → SignInManager.PasswordSignInAsync → cookie (claims via factory) → returnUrl if Url.IsLocalUrl else Home
[Authorize(Policy)] → CookieAuthHandler → Challenge → /Account/Login?returnUrl=…   |   Forbid → /Account/AccessDenied (403)
GET /Admin/Roles/Edit/{id} → RoleManager.GetClaimsAsync → checkbox matrix → POST diff → AddClaimAsync / RemoveClaimAsync
```

## File Changes

| File | Action | Description |
|---|---|---|
| `src/RAG.Infrastructure/Identity/{ApplicationUser,ApplicationRole,AppIdentityDbContext,Permissions,AppUserClaimsPrincipalFactory,ConsoleEmailSender,IdentitySeeder,DependencyInjection}.cs` | Create | Entities, catalog, factory, seeder, `AddRagIdentity` + `AddRagAuthorization` |
| `src/RAG.Infrastructure/Identity/Migrations/*` | Create | Initial identity migration (tool-generated) |
| `src/RAG.Infrastructure/RAG.Infrastructure.csproj` | Modify | +Identity.EntityFrameworkCore, +Npgsql.EFCore |
| `rag/rag.csproj` | Modify | Same 2 packages + Design (PrivateAssets=all) |
| `rag/.config/dotnet-tools.json` | Create | Local dotnet-ef manifest |
| `rag/Program.cs` | Modify | AddRagIdentity/AddRagAuthorization, UseAuthentication, startup Migrate+Seed |
| `rag/Controllers/{Account,Admin}Controller.cs` | Create | Pages per user-auth/user-admin specs |
| `rag/Models/{Login,ForgotPassword,ResetPassword,CreateUser,EditUser,RolePermissions}ViewModel.cs` | Create | Form/result models |
| `rag/Controllers/{Ask,Documents}Controller.cs` | Modify | `[Authorize(Policy=…)]` (batch 4) |
| `rag/Views/Account/*` (4), `rag/Views/Admin/**` (6) | Create | Plain Razor views |
| `rag/Views/Shared/_Layout.cshtml`, `_ViewImports.cshtml` | Modify | Navbar login/logout + permission-conditional links; `@using RAG.Infrastructure.Identity` |
| `rag/appsettings*.json` + User Secrets | Modify | `Identity` section; SeedAdmin creds in secrets only (`__SECRET__` placeholder convention) |
| `tests/RAG.Mvc.Tests/{Auth/TestAuthHandler.cs,RagWebApplicationFactoryBase.cs}` | Create | Auth fixture; base factory for both existing factories |
| `tests/RAG.Mvc.Tests/Controllers/*.cs` | Modify | Authenticate integration clients |
| `scripts/init-db.sql` | Modify (optional) | `CREATE SCHEMA IF NOT EXISTS identity;` parity line |

## Interfaces / Contracts

```csharp
public static class Permissions {
    public const string RagAsk = "rag.ask", DocumentsUpload = "documents.upload",
        DocumentsView = "documents.view", DocumentsDelete = "documents.delete",
        AdminUsers = "admin.users", AdminRoles = "admin.roles", AdminPermissions = "admin.permissions";
    public const string ClaimType = "permission";
    public static IReadOnlyList<string> All { get; }      // 7 entries
    public static IReadOnlyDictionary<string, string[]> SeedRoles { get; }
}
// AddRagIdentity(this IServiceCollection, IConfiguration)   → Identity + cookie + policies + factory
// AddRagAuthorization(services)                             → one policy per Permissions.All
// IEmailSender.SendAsync(email, subject, htmlMessage)       → ConsoleEmailSender logs to ILogger
// TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>  // claims from TestAuthOptions{ Permissions, Roles }
```

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit | Catalog = exactly 7 entries; claims-factory projection; guards (self-delete, role-with-members, IsLocalUrl) | Moq UserManager/RoleManager over mocked stores |
| Integration | Login/logout cookie round-trip; forgot/reset (token logged, no existence leak); admin create + role assign; permission matrix | TestAuthHandler + EF InMemory, seeded per-test user |
| Integration | Policy routing: /Ask 200 with rag.ask, 403 → AccessDenied without, login-redirect anonymous; /Admin/* per admin.* policy | TestAuthHandler only, no DB |
| Regression | Existing Ask/Upload integration tests (currently break once [Authorize] lands) | Factories inherit base + authenticate with needed permissions |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary (MVC routes, cookie auth, and policies are application code, not agent/automation surfaces).

## Migration / Rollout

DB: initial migration creates `identity` schema + 7 tables; startup `Migrate()` is idempotent; seeder idempotent (never resets admin password). Rollback: revert Identity registration + `[Authorize]` attributes, drop identity schema. RAG.Api and `documents`/`chunks` untouched. Chained-PR slices: **1** foundation (packages, entities, catalog, migration, seeder, pipeline) → **2** account pages + auth fixture (suite green) → **3** admin pages + guards → **4** [Authorize] enforcement + legacy-test fix (suite green).

## Open Questions

- [ ] SeedAdmin credential source in Production (env vars vs secrets store) — fail fast when missing
- [ ] Password policy strictness (8 vs 12 chars) — course-project balance
- [ ] Delete-user action in Admin UI: include now or defer (proposal only mandates the self-delete guard)
