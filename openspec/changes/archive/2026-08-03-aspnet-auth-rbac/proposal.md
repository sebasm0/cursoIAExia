# Proposal: ASP.NET Authentication + RBAC

## Intent

- **Problem**: no auth today — `UseAuthorization()` without `UseAuthentication()`; every controller open.
- **Business problem & users**: internal government-style system; admins manage access; users ask RAG questions and upload documents per their permissions.
- **Outcome**: cookie auth (ASP.NET Core Identity), code-defined permission catalog enforced via policies, Razor account/admin pages.

## Scope

### In Scope
- Identity + EF Core in isolated `identity` schema; cookie auth; startup `Migrate()` (no `dotnet ef`).
- Permission catalog (rag.ask, documents.upload, documents.view, documents.delete, admin.users, admin.roles, admin.permissions) as role claims; policies + `[Authorize(Policy=...)]`; claims-principal factory.
- Account pages: login, logout (POST-only), forgot/reset password (console-stub email), access-denied; no public signup — admins create accounts.
- Admin pages: users index/create/edit, roles index/create, role→permission matrix, user→role assignment.
- Seeded admin (all permissions); test auth-handler fixture; fix existing integration tests.

### Out of Scope
- RAG.Api auth (flagged: stays open); multi-tenancy; SMTP; self-signup; permission-catalog CRUD; visual redesign (Stitch follows); document list/detail/delete backend.

## Business Rules

- Only admins create users. Catalog is code-defined/static (new permission = code change).
- Roles→permissions as role claims; users→roles via assignment; grants materialize on cookie principal.
- Seeded admin owns all permissions; not deletable. Self-delete forbidden; built-in roles with members not deletable.
- Password reset via log-based email stub; generic confirmation (no existence leak).

## Capabilities

### New Capabilities
- `user-auth`: Identity setup, cookie auth, account pages.
- `user-rbac`: permission catalog, policies, claims factory, MVC enforcement.
- `user-admin`: user/role admin pages, permission matrix, seed admin.

### Modified Capabilities
- `mvc-rag-ask`: ASK requires `rag.ask` policy; anonymous → login redirect.
- `mvc-document-upload`: Upload requires `documents.upload` policy; anonymous → login redirect.

## Approach

AddIdentity + EF stores + default token providers + cookie config over AppIdentityDbContext (identity schema); UseAuthentication() before UseAuthorization(); startup Migrate(); Npgsql EF PostgreSQL 10.0.2+.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `rag/` Program.cs + csproj | Modified | Identity+auth+policies; EF packages; pipeline |
| `src/RAG.Infrastructure/Identity/` | New | Entities, DbContext, AddRagIdentity |
| `rag/Controllers` + Views | New/Modified | Account+Admin pages; [Authorize(Policy=...)] |
| `tests/RAG.Mvc.Tests/` | Modified | Auth fixture; fix factories/tests |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Dual stacks (EF/Dapper) corruption | Med | Strict schema isolation; Dapper never touches AspNet* |
| First migration on live pgvector DB | Med | Startup Migrate() + identity-schema DDL rights; idempotent; seed-guard |
| Existing integration tests break | High | Auth-handler fixture ships in-change |
| Review size > 400 lines | High | Forecast at tasks; chained PRs |
| Lockout/password/cookie misconfig | Med | Explicit policy; secure cookies; access-denied path |
| Permission drift | Low | Catalog is code; not admin-editable |

## Rollback Plan

Revert commit: remove Identity registration + [Authorize] attributes; drop identity schema tables (migration down). documents/chunks + RAG.Api untouched.

## Dependencies

- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2+ (net10.0).
- DB user with identity-schema DDL rights.

## Success Criteria

- [ ] Anonymous Ask/Upload → login; authorized pass; denied see access-denied.
- [ ] Admin creates users/roles, assigns permissions; login issues cookie; logout clears it.
- [ ] Seeded admin boots on fresh DB; reset logs token and changes password.
- [ ] Full dotnet test green; RAG.Api unchanged; only identity schema touched.
