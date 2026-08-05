using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// Idempotent startup seeding (spec AUTH-7): creates the built-in roles with
/// their catalog permission claims and bootstraps the configured admin account
/// when the identity database has no users yet.
/// </summary>
public sealed class IdentitySeeder
{
    private const string PlaceholderPassword = "__SECRET__";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IConfiguration configuration,
        ILogger<IdentitySeeder> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await SeedRolesAsync(ct);
        await SeedBootstrapAdminAsync(ct);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        foreach (var (roleName, permissions) in Permissions.SeedRoles)
        {
            ct.ThrowIfCancellationRequested();

            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new ApplicationRole { Name = roleName };
                var createResult = await _roleManager.CreateAsync(role);
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed role '{roleName}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
                }

                _logger.LogInformation("Seeded role {RoleName}", roleName);
            }

            // Idempotent: only add missing permission claims, never touch the rest.
            var existingClaims = (await _roleManager.GetClaimsAsync(role))
                .Where(c => c.Type == Permissions.ClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var permission in permissions)
            {
                if (!existingClaims.Add(permission))
                {
                    continue;
                }

                var claimResult = await _roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                if (!claimResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to add permission claim '{permission}' to role '{roleName}': " +
                        string.Join("; ", claimResult.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    private async Task SeedBootstrapAdminAsync(CancellationToken ct)
    {
        // Bootstrap only an empty identity database: once any user exists
        // (including the seeded admin), the admin is never recreated or
        // modified (AUTH-7 idempotency scenario).
        if (await _userManager.Users.AnyAsync(ct))
        {
            _logger.LogDebug("Identity database already has users — skipping bootstrap admin seeding");
            return;
        }

        var userName = _configuration["Identity:SeedAdmin:UserName"] ?? "admin";
        var email = _configuration["Identity:SeedAdmin:Email"];
        var password = _configuration["Identity:SeedAdmin:Password"];

        if (string.IsNullOrWhiteSpace(password) || password == PlaceholderPassword)
        {
            throw new InvalidOperationException(
                "Identity:SeedAdmin:Password must be configured via User Secrets or environment " +
                $"variables — the appsettings placeholder '{PlaceholderPassword}' is not a valid password.");
        }

        var admin = new ApplicationUser { UserName = userName, Email = email };

        var createResult = await _userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed bootstrap admin '{userName}': {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        var roleResult = await _userManager.AddToRoleAsync(admin, "Admin");
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign the Admin role to the bootstrap admin: " +
                string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        _logger.LogInformation("Seeded bootstrap admin {UserName} with the Admin role", userName);
    }
}
