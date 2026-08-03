using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// Design-time factory used by `dotnet ef migrations` so tooling never
/// executes the web app's startup pipeline (which would migrate + seed
/// against a real database). Only used by the CLI, never at runtime.
/// </summary>
public sealed class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        // `migrations add` only builds the model — no connection is opened,
        // so the fallback placeholder is safe. `database update` honours the
        // standard ConnectionStrings__PostgreSQL environment variable first.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? "Host=localhost;Database=rag;Username=postgres;Password=__SECRET__";

        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppIdentityDbContext(options);
    }
}
