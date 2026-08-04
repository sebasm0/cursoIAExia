using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Slice 3 integration tests covering ADMIN-4 .. ADMIN-7 (roles and the
/// permission matrix, spec user-admin.md) over the real cookie pipeline with
/// an EF InMemory Identity store.
/// </summary>
public class AdminRoleFlowTests
{
    private const string Password = "P@ssw0rd1!";

    private static HttpClient CreateClient(AccountFlowWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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

    /// <summary>Factory pre-seeded with an Admin role (all permissions) + admin user.</summary>
    private static async Task<AdminFlowWebApplicationFactory> CreateAdminFactoryAsync()
    {
        var factory = new AdminFlowWebApplicationFactory();
        await factory.EnsureRoleAsync("Admin", Permissions.All.ToArray());
        await factory.CreateUserWithRolesAsync("admin", Password, "admin@example.com", "Admin");
        return factory;
    }

    private static void AssertCheckbox(string html, string permission, bool expectedChecked)
    {
        var pattern = $@"<input[^>]*name=""SelectedPermissions""[^>]*value=""{Regex.Escape(permission)}""[^>]*>";
        var match = Regex.Match(html, pattern);
        Assert.True(match.Success, $"Permission checkbox for '{permission}' not rendered.");
        var isChecked = match.Value.Contains("checked", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedChecked, isChecked);
    }

    // ── ADMIN-4: Roles index (admin.roles) ──

    [Fact]
    public async Task AdminRolesIndex_WithAdminRolesPermission_ListsRolesWithCounts()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk, Permissions.DocumentsUpload, Permissions.DocumentsView);
        await factory.CreateUserWithRolesAsync("alice", Password, "alice@example.com", "User");
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var response = await client.GetAsync("/Admin/Roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        var adminRow = GetRoleRow(body, "Admin");
        Assert.Contains("7", ExtractCells(adminRow), StringComparer.Ordinal);
        Assert.Contains("1", ExtractCells(adminRow), StringComparer.Ordinal);

        var userRow = GetRoleRow(body, "User");
        Assert.Contains("3", ExtractCells(userRow), StringComparer.Ordinal);
        Assert.Contains("1", ExtractCells(userRow), StringComparer.Ordinal);
    }

    [Fact]
    public async Task AdminRoleDelete_RoleWithMembers_RefusedWithMessage()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("Operator", Permissions.RagAsk);
        await factory.CreateUserWithRolesAsync("alice", Password, "alice@example.com", "Operator");
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var operatorRoleId = await factory.GetRoleIdAsync("Operator");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Roles/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            $"/Admin/Roles/Delete/{operatorRoleId}", token));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var index = await client.GetAsync(response.Headers.Location!.OriginalString);
        var body = await index.Content.ReadAsStringAsync();
        Assert.Contains("still has members", body);
        Assert.True(await factory.RoleExistsAsync("Operator"), "A role with members must survive deletion.");
    }

    [Fact]
    public async Task AdminRoleDelete_BuiltInAdminRole_Refused()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var adminRoleId = await factory.GetRoleIdAsync("Admin");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Roles/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            $"/Admin/Roles/Delete/{adminRoleId}", token));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var index = await client.GetAsync(response.Headers.Location!.OriginalString);
        var body = await index.Content.ReadAsStringAsync();
        Assert.Contains("cannot be deleted", body);
        Assert.True(await factory.RoleExistsAsync("Admin"), "The built-in Admin role must never be deleted.");
    }

    // ── ADMIN-5: Role create (admin.roles) ──

    [Fact]
    public async Task AdminRoleCreate_Post_CreatesUniqueRole_AndAppearsInIndex()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Roles/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Admin/Roles/Create", token,
            ("roleName", "Operator")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await factory.RoleExistsAsync("Operator"), "The role must have been created.");

        var index = await client.GetAsync("/Admin/Roles");
        var body = await index.Content.ReadAsStringAsync();
        Assert.Contains("Operator", body);
    }

    [Fact]
    public async Task AdminRoleCreate_Post_DuplicateName_ShowsValidationError()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Roles/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Admin/Roles/Create", token,
            ("roleName", "Admin")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", body);
    }

    // ── ADMIN-6: Role edit permission matrix (admin.permissions) ──

    [Fact]
    public async Task AdminRoleEdit_Get_RendersFullPermissionMatrix()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("Viewer", Permissions.RagAsk);
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var viewerId = await factory.GetRoleIdAsync("Viewer");

        var response = await client.GetAsync($"/Admin/Roles/Edit/{viewerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var permission in Permissions.All)
        {
            Assert.Contains(permission, body);
        }

        AssertCheckbox(body, Permissions.RagAsk, expectedChecked: true);
        AssertCheckbox(body, Permissions.AdminUsers, expectedChecked: false);
    }

    [Fact]
    public async Task AdminRoleEdit_Post_ToggledMatrixPersistsPermissionClaims()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("Viewer", Permissions.RagAsk);
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var viewerId = await factory.GetRoleIdAsync("Viewer");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, $"/Admin/Roles/Edit/{viewerId}");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            $"/Admin/Roles/Edit/{viewerId}", token,
            ("SelectedPermissions", Permissions.RagAsk),
            ("SelectedPermissions", Permissions.DocumentsView),
            ("SelectedPermissions", Permissions.AdminUsers)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            ["admin.users", "documents.view", "rag.ask"],
            await factory.GetRolePermissionClaimsAsync("Viewer"));
    }

    [Fact]
    public async Task AdminRoleEdit_PermissionChange_ReflectedInNextSignIn()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("Viewer", Permissions.RagAsk);
        await factory.CreateUserWithRolesAsync("bob", Password, "bob@example.com", "Viewer");

        // Bob's first session: Viewer grants only rag.ask -> no Admin navigation link.
        using var bobClient1 = CreateClient(factory);
        await SignInAsync(factory, bobClient1, "bob");
        var home1 = await bobClient1.GetAsync("/");
        var body1 = await home1.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/Admin/Users", body1);

        // Admin grants admin.users to the Viewer role via the permission matrix.
        using var adminClient = CreateClient(factory);
        await SignInAsync(factory, adminClient, "admin");
        var viewerId = await factory.GetRoleIdAsync("Viewer");
        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(adminClient, $"/Admin/Roles/Edit/{viewerId}");
        var post = await adminClient.SendAsync(AccountTestHelpers.CreatePost(
            $"/Admin/Roles/Edit/{viewerId}", token,
            ("SelectedPermissions", Permissions.RagAsk),
            ("SelectedPermissions", Permissions.AdminUsers)));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        // Bob signs in again: the fresh cookie materializes admin.users -> link appears.
        using var bobClient2 = CreateClient(factory);
        await SignInAsync(factory, bobClient2, "bob");
        var home2 = await bobClient2.GetAsync("/");
        var body2 = await home2.Content.ReadAsStringAsync();
        Assert.Contains("/Admin/Users", body2);
    }

    // ── ADMIN-7: Access denied for unauthorized admin access ──

    [Fact]
    public async Task AdminRoleEdit_UserWithOnlyAdminUsersPermission_RoutedToAccessDenied()
    {
        await using var factory = new AdminPolicyWebApplicationFactory([Permissions.AdminUsers], ["Operator"]);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/Admin/Roles/Edit/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);

        var denied = await client.GetAsync(response.Headers.Location!.AbsolutePath);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.Contains("Access denied", body);
    }

    [Fact]
    public async Task AdminRolesIndex_UserWithoutAdminRolesPermission_RoutedToAccessDenied()
    {
        await using var factory = new AdminPolicyWebApplicationFactory([Permissions.RagAsk], ["Viewer"]);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Admin/Roles");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    // ── helpers ──

    private static string GetRoleRow(string html, string roleName)
    {
        var match = Regex.Match(
            html,
            $@"<tr[^>]*>(?:(?!</tr>).)*> {Regex.Escape(roleName)} <(?:(?!</tr>).)*</tr>",
            RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace);
        Assert.True(match.Success, $"Table row for role '{roleName}' not rendered.");
        return match.Value;
    }

    private static IReadOnlyList<string> ExtractCells(string row)
    {
        return Regex.Matches(row, @"<td[^>]*>\s*([^<]*?)\s*</td>")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }
}
