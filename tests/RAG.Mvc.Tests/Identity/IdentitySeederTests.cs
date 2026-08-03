using Microsoft.EntityFrameworkCore;
using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Identity;

/// <summary>
/// Spec AUTH-7: startup seeding creates the built-in roles with their catalog
/// permission claims and bootstraps the configured admin once, idempotently.
/// </summary>
public class IdentitySeederTests
{
    [Fact]
    public async Task SeedAsync_CreatesSeedRolesWithCatalogPermissionClaims()
    {
        // Arrange
        var (_, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        var seeder = IdentityTestHarness.CreateSeeder(userManager, roleManager, adminPassword: "Adm1n!Secret");

        // Act
        await seeder.SeedAsync();

        // Assert
        var adminRole = await roleManager.FindByNameAsync("Admin");
        var userRole = await roleManager.FindByNameAsync("User");
        var viewerRole = await roleManager.FindByNameAsync("Viewer");

        Assert.NotNull(adminRole);
        Assert.NotNull(userRole);
        Assert.NotNull(viewerRole);

        var adminGrants = (await roleManager.GetClaimsAsync(adminRole!))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            Permissions.All.OrderBy(p => p, StringComparer.Ordinal),
            adminGrants);

        var userGrants = (await roleManager.GetClaimsAsync(userRole!))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            new[] { Permissions.RagAsk, Permissions.DocumentsUpload, Permissions.DocumentsView }
                .OrderBy(v => v, StringComparer.Ordinal),
            userGrants.OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public async Task SeedAsync_CreatesBootstrapAdminWithAdminRoleWhenDatabaseIsEmpty()
    {
        // Arrange
        var (_, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        var seeder = IdentityTestHarness.CreateSeeder(userManager, roleManager, adminPassword: "Adm1n!Secret");

        // Act
        await seeder.SeedAsync();

        // Assert
        var admin = await userManager.FindByNameAsync("admin");
        Assert.NotNull(admin);
        Assert.Equal("admin@example.com", admin!.Email);
        Assert.True(await userManager.IsInRoleAsync(admin, "Admin"));
        Assert.False(string.IsNullOrEmpty(admin.PasswordHash), "admin must have a usable password hash");
        Assert.Single(await userManager.Users.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_NeverDuplicatesOrModifiesTheAdmin()
    {
        // Arrange
        var (context, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        var seeder = IdentityTestHarness.CreateSeeder(userManager, roleManager, adminPassword: "Adm1n!Secret");

        // Act — first seed
        await seeder.SeedAsync();
        var adminAfterFirst = await userManager.FindByNameAsync("admin");
        var passwordHashAfterFirst = adminAfterFirst!.PasswordHash;
        var userCountAfterFirst = await userManager.Users.CountAsync();
        var adminRoleAfterFirst = await roleManager.FindByNameAsync("Admin");
        var claimCountAfterFirst = (await roleManager.GetClaimsAsync(adminRoleAfterFirst!)).Count;

        // Act — second seed on the same database
        await seeder.SeedAsync();

        // Assert — nothing duplicated or modified
        Assert.Equal(userCountAfterFirst, await userManager.Users.CountAsync());
        var adminAfterSecond = await userManager.FindByNameAsync("admin");
        Assert.NotNull(adminAfterSecond);
        Assert.Equal(passwordHashAfterFirst, adminAfterSecond!.PasswordHash);
        Assert.Equal(claimCountAfterFirst, (await roleManager.GetClaimsAsync(adminRoleAfterFirst!)).Count);
        Assert.Equal(1, (await userManager.Users.ToListAsync()).Count);
        _ = context;
    }

    [Fact]
    public async Task SeedAsync_SkipsAdminBootstrapWhenUsersAlreadyExist()
    {
        // Arrange — a database that already contains users
        var (_, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        await IdentityTestHarness.CreateRoleWithPermissionsAsync(roleManager, "User", Permissions.RagAsk);

        var existing = new ApplicationUser { UserName = "existing", Email = "existing@example.com" };
        await userManager.CreateAsync(existing, "Str0ngPass!");
        await userManager.AddToRoleAsync(existing, "User");

        var seeder = IdentityTestHarness.CreateSeeder(userManager, roleManager, adminPassword: "Adm1n!Secret");

        // Act
        await seeder.SeedAsync();

        // Assert — configured admin must NOT be created into a populated database
        Assert.Null(await userManager.FindByNameAsync("admin"));
        Assert.NotNull(await userManager.FindByNameAsync("existing"));
    }

    [Fact]
    public async Task SeedAsync_ThrowsWhenSeedAdminPasswordIsTheUnsetPlaceholder()
    {
        // Arrange
        var (_, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        var seeder = IdentityTestHarness.CreateSeeder(userManager, roleManager, adminPassword: "__SECRET__");

        // Act + Assert — fail fast instead of booting with a placeholder password
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());
        Assert.Contains("Identity:SeedAdmin:Password", exception.Message);
    }
}
