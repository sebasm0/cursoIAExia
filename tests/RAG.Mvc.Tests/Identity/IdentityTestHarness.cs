using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RAG.Infrastructure.Identity;

namespace RAG.Mvc.Tests.Identity;

/// <summary>
/// Shared in-memory Identity stack for the Identity unit tests: EF InMemory store
/// with real UserManager/RoleManager, claims factory and seeder — no database.
/// </summary>
internal static class IdentityTestHarness
{
    public static IdentityOptions CreateOptions() => new()
    {
        Password = new PasswordOptions
        {
            RequiredLength = 8,
            RequireDigit = true,
            RequireUppercase = true,
            RequireLowercase = true,
            RequireNonAlphanumeric = false,
        },
        User = new UserOptions { RequireUniqueEmail = true },
    };

    public static (AppIdentityDbContext Context, UserManager<ApplicationUser> UserManager,
        RoleManager<ApplicationRole> RoleManager) CreateManagers()
    {
        var context = new AppIdentityDbContext(new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var options = Options.Create(CreateOptions());

        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, ApplicationRole, AppIdentityDbContext, Guid>(context),
            options,
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var roleManager = new RoleManager<ApplicationRole>(
            new RoleStore<ApplicationRole, AppIdentityDbContext, Guid>(context),
            null!,
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<ApplicationRole>>.Instance);

        return (context, userManager, roleManager);
    }

    public static AppUserClaimsPrincipalFactory CreateClaimsFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
        => new(userManager, roleManager, Options.Create(CreateOptions()));

    public static IdentitySeeder CreateSeeder(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        string? adminPassword)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:SeedAdmin:UserName"] = "admin",
                ["Identity:SeedAdmin:Email"] = "admin@example.com",
                ["Identity:SeedAdmin:Password"] = adminPassword,
            })
            .Build();

        return new IdentitySeeder(
            userManager,
            roleManager,
            configuration,
            NullLogger<IdentitySeeder>.Instance);
    }

    public static async Task<ApplicationRole> CreateRoleWithPermissionsAsync(
        RoleManager<ApplicationRole> roleManager,
        string name,
        params string[] permissions)
    {
        var role = new ApplicationRole { Name = name };
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        foreach (var permission in permissions)
        {
            var claimResult = await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
            if (!claimResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to add claim '{permission}' to role '{name}'.");
            }
        }

        return role;
    }
}
