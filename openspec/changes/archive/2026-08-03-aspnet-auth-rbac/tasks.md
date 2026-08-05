# Tasks: ASP.NET Auth + RBAC (aspnet-auth-rbac)

## Review Workload Forecast

Est. changed lines ~1600 (S1≈500, S2≈450, S3≈400, S4≈250).
Delivery strategy: ask-on-risk.

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending (choose stacked-to-main or feature-branch-chain before apply)
400-line budget risk: High

**BLOCKER**: commit initial migration (1.7) before Slice 1 closes. `dotnet ef` NOT installed: local manifest + `dotnet ef migrations add InitialIdentity -p src/RAG.Infrastructure -s rag`.

### Work Units

| Unit | PR | Est | Focused test | Runtime harness | Rollback |
|------|----|-----|--------------|-----------------|----------|
| 1 Foundation | 1 | ~500 | `dotnet test --filter Identity` | fresh-DB `dotnet run` → identity schema | revert PR-1 |
| 2 Account pages | 2 | ~450 | `dotnet test --filter Account` | login/logout/reset round-trip | revert PR-2 |
| 3 Admin pages | 3 | ~400 | `dotnet test --filter Admin` | create user/role, toggle matrix | revert PR-3 |
| 4 Enforcement | 4 | ~250 | `dotnet test` (full) | anon /Ask → login redirect | revert PR-4 |

Deps: slice N ← N-1; numbered order; RED before production (strict_tdd).

## Slice 1 — Foundation
- [x] 1.1 Add Identity.EntityFrameworkCore 10.0.10 + Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 to `rag/rag.csproj` + `src/RAG.Infrastructure/RAG.Infrastructure.csproj`
- [x] 1.2 Create `rag/.config/dotnet-tools.json` (dotnet-ef manifest)
- [x] 1.3 Create `src/RAG.Infrastructure/Identity/` {ApplicationUser, ApplicationRole, AppIdentityDbContext}.cs — Guid IDs, identity schema
- [x] 1.4 Create `Permissions.cs` (7 + All + SeedRoles), `AppUserClaimsPrincipalFactory.cs`, `ConsoleEmailSender.cs`
- [x] 1.5 Create `IdentitySeeder.cs` (idempotent admin) + `DependencyInjection.cs` (AddRagIdentity/AddRagAuthorization)
- [x] 1.6 [RED] Unit tests `tests/RAG.Mvc.Tests/Identity/`: catalog=7; claims-factory projection; seeder idempotency
- [x] 1.7 Generate + commit `src/RAG.Infrastructure/Identity/Migrations/*` — BLOCKER
- [x] 1.8 Wire `rag/Program.cs`: AddRagIdentity, UseAuthentication, startup Migrate+Seed; appsettings Identity section (secrets only)
- [x] 1.9 Create `tests/RAG.Mvc.Tests/Auth/` {TestAuthHandler, RagWebApplicationFactoryBase}.cs; refactor both factories to inherit base

## Slice 2 — Account pages
- [x] 2.1 [RED] Account tests (InMemory + real cookie): AUTH-1..8 — cookie + safe returnUrl, external rejected, lockout, POST-only logout, no leak, reset, no public signup, idempotent seed
- [x] 2.2 Create `rag/Models/` ViewModels: Login, ForgotPassword, ResetPassword, CreateUser, EditUser, RolePermissions
- [x] 2.3 Create `rag/Controllers/AccountController.cs` + `rag/Views/Account/` (Login, ForgotPassword, ResetPassword, AccessDenied); antiforgery on POSTs
- [x] 2.4 Cookie config: LoginPath, AccessDeniedPath, ReturnUrlParameter, SecurePolicy prod; password ≥8 + lockout
- [x] 2.5 `_Layout.cshtml` login/logout navbar; `_ViewImports.cshtml` `@using RAG.Infrastructure.Identity`
- [x] 2.6 [GREEN] `dotnet test` green (slice exit)

## Slice 3 — Admin pages
- [x] 3.1 [RED] Admin tests: ADMIN-1..7 — list, self-delete, create, duplicates, role edit, guards, matrix, denied
- [x] 3.2 Create `rag/Controllers/AdminController.cs` (Users/Roles CRUD; antiforgery) + 6 `rag/Views/Admin/**` views
- [x] 3.3 Wire AddToRoles/RemoveFromRoles + matrix diff (AddClaim/RemoveClaim)
- [x] 3.4 Nav: permission-conditional Admin link

## Slice 4 — Enforcement + legacy tests
- [x] 4.1 [RED] Policy tests (TestAuthHandler, no DB): RBAC-1..5, ASK-8, UPLOAD-9 — 200 / denied / anon redirect
- [x] 4.2 `[Authorize]` on `rag/Controllers/{AskController,DocumentsController}.cs` (rag.ask, documents.upload)
- [x] 4.3 Authenticate legacy clients: `tests/RAG.Mvc.Tests/Controllers/{AskControllerTests,DocumentsControllerTests}.cs`
- [x] 4.4 [GREEN] Full `dotnet test`; RAG.Api + Dapper untouched
