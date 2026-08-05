using System.Security.Claims;
using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Identity;

/// <summary>
/// Spec RBAC-3: the claims factory projects each role's permission claims onto
/// the signed-in principal so policies evaluate flat permission claims.
/// </summary>
public class AppUserClaimsPrincipalFactoryTests
{
    [Fact]
    public async Task CreateAsync_ProjectsRolePermissionClaimsOntoPrincipal()
    {
        // Arrange
        var (_, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        var factory = IdentityTestHarness.CreateClaimsFactory(userManager, roleManager);

        await IdentityTestHarness.CreateRoleWithPermissionsAsync(
            roleManager, "User", Permissions.RagAsk, Permissions.DocumentsUpload);

        var user = new ApplicationUser { UserName = "alice", Email = "alice@example.com" };
        await userManager.CreateAsync(user, "Str0ngPass!");
        await userManager.AddToRoleAsync(user, "User");

        // Act
        var principal = await factory.CreateAsync(user);

        // Assert — only permission claims from the role, nothing else
        var permissions = principal
            .FindAll(Permissions.ClaimType)
            .Select(c => c.Value)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { Permissions.DocumentsUpload, Permissions.RagAsk },
            permissions);
        Assert.Equal(user.UserName, principal.Identity?.Name);
    }

    [Fact]
    public async Task CreateAsync_RoleWithoutPermissionClaims_YieldsNoPermissionClaims()
    {
        // Arrange
        var (_, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        var factory = IdentityTestHarness.CreateClaimsFactory(userManager, roleManager);

        var role = new ApplicationRole { Name = "Viewer" };
        await roleManager.CreateAsync(role);
        // A role with claims, but none of type "permission".
        await roleManager.AddClaimAsync(role, new Claim("some-other-type", "unrelated"));

        var user = new ApplicationUser { UserName = "bob", Email = "bob@example.com" };
        await userManager.CreateAsync(user, "Str0ngPass!");
        await userManager.AddToRoleAsync(user, "Viewer");

        // Act
        var principal = await factory.CreateAsync(user);

        // Assert — production code ran (role iterated) and filtered correctly
        Assert.Empty(principal.FindAll(Permissions.ClaimType));
    }

    [Fact]
    public async Task CreateAsync_MultipleRoles_AggregatesPermissionsFromAllRoles()
    {
        // Arrange
        var (_, userManager, roleManager) = IdentityTestHarness.CreateManagers();
        var factory = IdentityTestHarness.CreateClaimsFactory(userManager, roleManager);

        await IdentityTestHarness.CreateRoleWithPermissionsAsync(roleManager, "User", Permissions.DocumentsUpload);
        await IdentityTestHarness.CreateRoleWithPermissionsAsync(roleManager, "Admin", Permissions.AdminUsers);

        var user = new ApplicationUser { UserName = "carol", Email = "carol@example.com" };
        await userManager.CreateAsync(user, "Str0ngPass!");
        await userManager.AddToRolesAsync(user, ["User", "Admin"]);

        // Act
        var principal = await factory.CreateAsync(user);

        // Assert — aggregation across both roles
        var permissions = principal
            .FindAll(Permissions.ClaimType)
            .Select(c => c.Value)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { Permissions.AdminUsers, Permissions.DocumentsUpload }, permissions);
    }
}
