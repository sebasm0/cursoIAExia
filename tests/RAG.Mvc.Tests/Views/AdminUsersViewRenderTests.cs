using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using Xunit;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// Slice C view-render tests for the Admin Users screens (spec user-admin
/// ADMIN-8): the users index, create form and edit form render per the design
/// system (UDS-1..UDS-4) over the real cookie pipeline with an EF InMemory
/// Identity store. The index table must be usable on narrow viewports without
/// horizontal page overflow (ADMIN-8 scenario 1), so the table is wrapped in a
/// Bootstrap responsive container — the overflow-prevention mechanism, asserted
/// here as the functional marker of that behavior (not as a visual style).
/// </summary>
public class AdminUsersViewRenderTests
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

    // ── ADMIN-8: Users index styled and responsive ──

    [Fact]
    public async Task UsersIndex_AdminUsers_RendersResponsiveTableWithRealCopy()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk);
        await factory.CreateUserWithRolesAsync("alice", Password, "alice@example.com", "User");
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // Real data rows (ADMIN-8): users listed with roles.
        Assert.Contains("alice", body);
        Assert.Contains("alice@example.com", body);
        Assert.Contains("User", body);
        // Primary action per the reference screen.
        Assert.Contains("Create user", body);
        // UDS-4: no placeholder copy.
        Assert.DoesNotContain("lorem", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsersIndex_AdminUsers_NarrowViewport_TableHasResponsiveWrapper()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var body = await (await client.GetAsync("/Admin/Users")).Content.ReadAsStringAsync();

        // ADMIN-8 scenario 1: the users table is usable on narrow viewports
        // without horizontal page overflow — the table must render inside the
        // Bootstrap responsive container (overflow-prevention mechanism).
        Assert.Contains("table-responsive", body);
        Assert.Contains("<table", body);
    }

    // ── ADMIN-8: Create form per design system ──

    [Fact]
    public async Task UsersCreate_AdminUsers_RendersTokenFormFields()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk);
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var response = await client.GetAsync("/Admin/Users/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Create user", body);
        Assert.Contains("name=\"UserName\"", body);
        Assert.Contains("name=\"Email\"", body);
        Assert.Contains("name=\"Password\"", body);
        // Role assignment checkboxes render from the available roles.
        Assert.Contains("name=\"SelectedRoles\"", body);
        Assert.Contains(">User<", body);
    }

    [Fact]
    public async Task UsersCreate_Post_EmptyRequiredFields_ReRendersFormWithValidationErrors()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk);
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Users/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Admin/Users/Create", token));

        // ADMIN-2 unchanged: a validation failure re-renders the create form
        // with design-system-styled validation messages, no account created.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("input-validation-error", body);
        Assert.Contains("name=\"UserName\"", body);
        Assert.Contains("field-validation-error", body);
    }

    // ── ADMIN-8: Edit form per design system ──

    [Fact]
    public async Task UsersEdit_AdminUsers_RendersDisabledUsernameAndCheckedRoles()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk);
        await factory.CreateUserWithRolesAsync("alice", Password, "alice@example.com", "User");
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var aliceId = (await factory.FindByUserNameAsync("alice"))!.Id;

        var response = await client.GetAsync($"/Admin/Users/Edit/{aliceId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Username is immutable on edit (ADMIN-3 form contract) — rendered disabled.
        Assert.Contains("disabled", body);
        Assert.Contains("alice@example.com", body);
        Assert.Contains("name=\"SelectedRoles\"", body);
        Assert.Contains("name=\"Email\"", body);

        // The User role checkbox renders checked for a member of that role.
        var pattern = $@"<input[^>]*name=""SelectedRoles""[^>]*value=""{Regex.Escape("User")}""[^>]*>";
        var match = Regex.Match(body, pattern);
        Assert.True(match.Success, "Role checkbox for 'User' not rendered.");
        Assert.Contains("checked", match.Value, StringComparison.OrdinalIgnoreCase);
    }
}
