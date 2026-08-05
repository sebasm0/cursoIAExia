using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Policy-only factory for the enforcement tests (spec RBAC-4/RBAC-5, ASK-8,
/// UPLOAD-9): authenticates every request via the TestAuthHandler with the given
/// permission/role claims. No database is touched — the Npgsql Identity context
/// is never resolved. The cookie handler stays the challenge/forbid scheme so
/// anonymous requests redirect to the LoginPath and denied requests to the
/// AccessDeniedPath, mirroring production (design D6).
/// </summary>
public sealed class PolicyTestWebApplicationFactory : RagWebApplicationFactoryBase
{
    private readonly string[] _permissions;
    private readonly string[] _roles;

    public PolicyTestWebApplicationFactory(string[] permissions, string[] roles)
    {
        _permissions = permissions;
        _roles = roles;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
            services.AddPolicyTestAuthentication(_permissions, _roles));
    }
}

/// <summary>
/// Anonymous factory for the enforcement tests: no test authentication is
/// registered, so requests carry no cookie and protected endpoints challenge
/// through the real cookie handler to /Account/Login (ASK-8 / UPLOAD-9).
/// </summary>
public sealed class AnonymousWebApplicationFactory : RagWebApplicationFactoryBase
{
}
