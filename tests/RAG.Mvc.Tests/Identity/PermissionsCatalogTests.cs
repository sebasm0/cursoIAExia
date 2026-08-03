using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Identity;

/// <summary>
/// Spec RBAC-1: static, code-defined permission catalog — exactly 7 entries,
/// no UI CRUD, built-in seed roles carry the documented grants.
/// </summary>
public class PermissionsCatalogTests
{
    private static readonly string[] CanonicalPermissions =
    [
        Permissions.RagAsk,
        Permissions.DocumentsUpload,
        Permissions.DocumentsView,
        Permissions.DocumentsDelete,
        Permissions.AdminUsers,
        Permissions.AdminRoles,
        Permissions.AdminPermissions,
    ];

    [Fact]
    public void All_ContainsExactlyTheSevenCanonicalPermissions()
    {
        var expected = CanonicalPermissions.OrderBy(p => p).ToArray();
        var actual = Permissions.All.OrderBy(p => p).ToArray();

        Assert.Equal(7, Permissions.All.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ClaimType_UsesPermissionConstant()
    {
        Assert.Equal("permission", Permissions.ClaimType);
    }

    [Fact]
    public void SeedRoles_ContainsOnlyTheThreeBuiltInRoles()
    {
        Assert.Equal(
            new[] { "Admin", "User", "Viewer" },
            Permissions.SeedRoles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void SeedRoles_Admin_GrantsEveryCatalogPermission()
    {
        var grants = Permissions.SeedRoles["Admin"];

        Assert.Equal(7, grants.Length);
        Assert.Equal(
            Permissions.All.OrderBy(p => p),
            grants.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void SeedRoles_User_GrantsAskUploadAndView()
    {
        var grants = Permissions.SeedRoles["User"];

        Assert.Equal(
            new[] { Permissions.RagAsk, Permissions.DocumentsUpload, Permissions.DocumentsView }
                .OrderBy(p => p, StringComparer.Ordinal),
            grants.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void SeedRoles_Viewer_GrantsOnlyAsk()
    {
        var grants = Permissions.SeedRoles["Viewer"];

        Assert.Equal(new[] { Permissions.RagAsk }, grants);
    }
}
