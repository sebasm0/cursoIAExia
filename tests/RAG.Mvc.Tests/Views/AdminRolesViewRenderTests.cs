using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using Xunit;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// Slice C view-render tests for the Admin Roles screens (spec user-admin
/// ADMIN-9): the roles index, create form and the permission-matrix edit screen
/// render per the design system (UDS-1..UDS-4) over the real cookie pipeline
/// with an EF InMemory Identity store. The matrix must display the full
/// permission catalog as checkboxes (ADMIN-6 unchanged — persistence is covered
/// by AdminRoleFlowTests); these tests assert the rendered contract: catalog
/// completeness, checked state from role claims, and the narrow-viewport
/// responsive mechanism on the tables.
/// </summary>
public class AdminRolesViewRenderTests
{
    private const string Password = "P@ssw0rd1!";

    private static HttpClient CreateClient(AccountFlowWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Factory pre-seeded with an Admin role (all permissions) + admin user.</summary>
    private static async Task<AdminFlowWebApplicationFactory> CreateAdminFactoryAsync()
    {
        var factory = new AdminFlowWebApplicationFactory();
        await factory.EnsureRoleAsync("Admin", Permissions.All.ToArray());
        await factory.CreateUserWithRolesAsync("admin", Password, "admin@example.com", "Admin");
        return factory;
    }

    private static async Task SignInAsync(
        AccountFlowWebApplicationFactory factory, HttpClient client, string userName)
    {
        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");
        var login = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login", token,
            ("UserName", userName), ("Password", Password)));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.True(AccountTestHelpers.HasAuthCookie(login), "Precondition: login must succeed.");
    }

    private static void AssertMatrixCheckbox(string html, string permission, bool expectedChecked)
    {
        var pattern = $@"<input[^>]*name=""SelectedPermissions""[^>]*value=""{Regex.Escape(permission)}""[^>]*>";
        var match = Regex.Match(html, pattern);
        Assert.True(match.Success, $"Permission checkbox for '{permission}' not rendered.");
        var isChecked = match.Value.Contains("checked", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedChecked, isChecked);
    }

    // ── ADMIN-9: Roles index styled and responsive ──

    [Fact]
    public async Task RolesIndex_AdminRoles_RendersResponsiveTableWithRealCopy()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("Viewer", Permissions.RagAsk);
        await factory.CreateUserWithRolesAsync("alice", Password, "alice@example.com", "Viewer");
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var response = await client.GetAsync("/Admin/Roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Roles", body);
        Assert.Contains("Admin", body);
        Assert.Contains("Viewer", body);
        Assert.Contains("Crear rol", body);
        Assert.DoesNotContain("lorem", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RolesIndex_AdminRoles_NarrowViewport_TableHasResponsiveWrapper()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var body = await (await client.GetAsync("/Admin/Roles")).Content.ReadAsStringAsync();

        // ADMIN-9 (via ADMIN-8): the roles table is usable on narrow viewports
        // without horizontal page overflow — rendered inside the responsive
        // container (overflow-prevention mechanism).
        Assert.Contains("table-responsive", body);
        Assert.Contains("<table", body);
    }

    // ── ADMIN-10: role delete form stays submit-able without JavaScript ──

    [Fact]
    public async Task RolesIndex_AdminRoles_DeleteFormHasNoScriptSubmitFallback()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("temp-role", Permissions.RagAsk);
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var roleId = await factory.GetRoleIdAsync("temp-role");

        var body = await (await client.GetAsync("/Admin/Roles")).Content.ReadAsStringAsync();

        // ADMIN-10: a deletable role (non built-in, no members) renders a
        // <noscript> submit fallback inside its delete form for JS-off admins.
        var deleteForm = Regex.Match(
            body, $@"<form id=""delete-role-{roleId}"".*?</form>", RegexOptions.Singleline);
        Assert.True(deleteForm.Success, "Delete form for deletable role not rendered.");
        Assert.Contains("<noscript>", deleteForm.Value);
        Assert.Contains("<button type=\"submit\"", deleteForm.Value);
        // The JS modal path stays the interactive flow for scripting-enabled users.
        Assert.Contains($"data-bs-toggle=\"modal\" data-bs-target=\"#confirmModal-{roleId}\"", body);
    }

    // ── ADMIN-5: Role create form per design system ──

    [Fact]
    public async Task RolesCreate_AdminRoles_RendersTokenForm()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var response = await client.GetAsync("/Admin/Roles/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Crear rol", body);
        Assert.Contains("name=\"roleName\"", body);
    }

    // ── ADMIN-9: Permission matrix per design system ──

    [Fact]
    public async Task RolesEdit_AdminPermissions_RendersFullCatalogMatrixResponsive()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("Viewer", Permissions.RagAsk);
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var viewerId = await factory.GetRoleIdAsync("Viewer");

        var response = await client.GetAsync($"/Admin/Roles/Edit/{viewerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Full permission catalog rendered as checkboxes (ADMIN-9, ADMIN-6 unchanged).
        foreach (var permission in Permissions.All)
        {
            Assert.Contains(permission, body);
        }
        Assert.Contains("name=\"SelectedPermissions\"", body);
        Assert.Contains("Guardar permisos", body);
        Assert.Contains("table-responsive", body);
        Assert.Contains("<table", body);

        // Checked state reflects the role's current claims (RBAC-2).
        AssertMatrixCheckbox(body, Permissions.RagAsk, expectedChecked: true);
        AssertMatrixCheckbox(body, Permissions.AdminUsers, expectedChecked: false);
        Assert.DoesNotContain("lorem", body, StringComparison.OrdinalIgnoreCase);
    }
}
