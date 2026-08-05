using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// EF Core context for ASP.NET Core Identity, isolated in the "identity" schema.
/// Dapper-owned tables (documents/chunks) stay in the public schema and are never
/// touched here — EF never writes to public.* and Dapper never writes to identity.*
/// (design decision D3).
/// </summary>
public sealed class AppIdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // All 7 AspNet* tables land in the identity schema.
        builder.HasDefaultSchema("identity");
    }
}
