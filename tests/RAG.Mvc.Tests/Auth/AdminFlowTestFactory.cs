using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Slice 3 test infrastructure (spec user-admin) over the real cookie pipeline
/// with an EF InMemory Identity store. The authenticated "admin" is a real user
/// whose cookie is materialized by the claims factory from role claims, so the
/// permission matrix changes are observable on the NEXT sign-in (ADMIN-6).
/// </summary>
public sealed class AdminFlowWebApplicationFactory : AccountFlowWebApplicationFactory
{
    /// <summary>Ensures a role exists and carries the given permission claims (RBAC-2).</summary>
    public async Task EnsureRoleAsync(string roleName, params string[] permissions)
    {
        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            var created = await roleManager.CreateAsync(new ApplicationRole { Name = roleName });
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            role = await roleManager.FindByNameAsync(roleName);
        }

        Assert.NotNull(role);
        foreach (var permission in permissions)
        {
            var already = (await roleManager.GetClaimsAsync(role!))
                .Any(c => c.Type == Permissions.ClaimType && c.Value == permission);
            if (!already)
            {
                var granted = await roleManager.AddClaimAsync(role!, new Claim(Permissions.ClaimType, permission));
                Assert.True(granted.Succeeded, string.Join("; ", granted.Errors.Select(e => e.Description)));
            }
        }
    }

    public async Task<ApplicationUser> CreateUserWithRolesAsync(
        string userName, string password, string email, params string[] roleNames)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, Email = email };
        var created = await userManager.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        if (roleNames.Length > 0)
        {
            var assigned = await userManager.AddToRolesAsync(user, roleNames);
            Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(e => e.Description)));
        }

        return user;
    }

    public async Task<Guid> GetRoleIdAsync(string roleName)
    {
        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = await roleManager.FindByNameAsync(roleName);
        Assert.NotNull(role);
        return role!.Id;
    }

    public async Task<bool> RoleExistsAsync(string roleName)
    {
        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        return await roleManager.FindByNameAsync(roleName) is not null;
    }

    public async Task<IReadOnlyList<string>> GetRolePermissionClaimsAsync(string roleName)
    {
        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = await roleManager.FindByNameAsync(roleName);
        Assert.NotNull(role);
        return (await roleManager.GetClaimsAsync(role!))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(string userName)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        Assert.NotNull(user);
        return (await userManager.GetRolesAsync(user!)).OrderBy(r => r, StringComparer.Ordinal).ToList();
    }

    public async Task<bool> UserExistsAsync(string userName)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByNameAsync(userName) is not null;
    }

    public async Task<ApplicationUser?> FindByUserNameAsync(string userName)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByNameAsync(userName);
    }
}

/// <summary>
/// Policy-only factory (no DB, spec RBAC-5 / ADMIN-7): authenticates every request
/// with the TestAuthHandler carrying fixed claims, and keeps the Identity cookie
/// handler as the forbid/challenge scheme so denied requests route to the
/// AccessDeniedPath instead of a bare 403.
/// </summary>
public sealed class AdminPolicyWebApplicationFactory : AccountFlowWebApplicationFactory
{
    private readonly string[] _permissions;
    private readonly string[] _roles;

    public AdminPolicyWebApplicationFactory(string[] permissions, string[] roles)
    {
        _permissions = permissions;
        _roles = roles;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddTestAuthentication(_permissions, _roles);

            // RBAC-5: a denied authenticated request must be routed to the cookie
            // AccessDeniedPath (302 -> /Account/AccessDenied), never a bare 403.
            // AddIdentity pins DefaultAuthenticateScheme to Identity.Application,
            // so the Test scheme must be set explicitly (not just DefaultScheme).
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultForbidScheme = IdentityConstants.ApplicationScheme;
            });
        });
    }
}
