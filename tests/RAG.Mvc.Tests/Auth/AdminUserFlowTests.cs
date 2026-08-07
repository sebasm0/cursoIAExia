using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Slice 3 integration tests covering ADMIN-1 .. ADMIN-3 (user management,
/// spec user-admin.md) over the real cookie pipeline with an EF InMemory
/// Identity store.
/// </summary>
public class AdminUserFlowTests
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

    // ── ADMIN-1: Users index (admin.users) ──

    [Fact]
    public async Task AdminUsersIndex_WithAdminUsersPermission_ListsUsersWithRoles()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk, Permissions.DocumentsUpload);
        await factory.CreateUserWithRolesAsync("alice", Password, "alice@example.com", "User");
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("alice", body);
        Assert.Contains("alice@example.com", body);
        Assert.Contains("User", body);
    }

    [Fact]
    public async Task AdminUsersIndex_UserWithoutAdminUsersPermission_RoutedToAccessDenied()
    {
        await using var factory = new AdminPolicyWebApplicationFactory([Permissions.RagAsk], ["Viewer"]);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);

        var denied = await client.GetAsync(response.Headers.Location!.AbsolutePath);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.Contains("Acceso denegado", body);
    }

    // ── ADMIN-1: self-delete guard ──

    [Fact]
    public async Task AdminUsersDelete_OwnAccount_RefusedAndUserRemains()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var adminId = (await factory.FindByUserNameAsync("admin"))!.Id;

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Users/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            $"/Admin/Users/Delete/{adminId}", token));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var index = await client.GetAsync(response.Headers.Location!.OriginalString);
        var body = await index.Content.ReadAsStringAsync();
        Assert.Contains("No puede eliminar su propia cuenta", body);
        Assert.True(await factory.UserExistsAsync("admin"), "The admin account must survive the self-delete attempt.");
    }

    // ── ADMIN-2: User create (admin.users) ──

    [Fact]
    public async Task AdminUserCreate_Post_CreatesUserWithRoles_AndNewUserCanSignIn()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk);
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Users/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Admin/Users/Create", token,
            ("UserName", "carol"),
            ("Email", "carol@example.com"),
            ("Password", Password),
            ("SelectedRoles", "User")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await factory.UserExistsAsync("carol"), "The user must have been created.");
        Assert.Equal(["User"], await factory.GetUserRolesAsync("carol"));

        // ADMIN-2: the newly created account can actually sign in.
        using var newClient = CreateClient(factory);
        await SignInAsync(factory, newClient, "carol");
    }

    [Fact]
    public async Task AdminUserCreate_Post_DuplicateEmail_ShowsValidationErrorAndCreatesNoAccount()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.CreateUserAsync("alice", Password, "alice@example.com");
        var countBefore = await factory.CountUsersAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Users/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Admin/Users/Create", token,
            ("UserName", "bob"),
            ("Email", "alice@example.com"),
            ("Password", "Bob1!password")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("en uso", body);
        Assert.False(await factory.UserExistsAsync("bob"), "Duplicate email must not create an account.");
        Assert.Equal(countBefore, await factory.CountUsersAsync());
    }

    [Fact]
    public async Task AdminUserCreate_Post_ShortPassword_ShowsLocalizedValidationError()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Admin/Users/Create");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Admin/Users/Create", token,
            ("UserName", "dave"),
            ("Email", "dave@example.com"),
            ("Password", "Abcdef!")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Identity password policy (RequiredLength=8) surfaces through
        // TranslateIdentityError as Spanish, not the raw English description.
        Assert.Contains("al menos 8 caracteres", body);
        Assert.False(await factory.UserExistsAsync("dave"), "Short-password user must not be created.");
    }

    // ── ADMIN-3: User edit and role assignment (admin.users) ──

    [Fact]
    public async Task AdminUserEdit_Post_AddsAndRemovesRoles()
    {
        await using var factory = await CreateAdminFactoryAsync();
        await factory.EnsureRoleAsync("User", Permissions.RagAsk);
        await factory.EnsureRoleAsync("Viewer", Permissions.RagAsk);
        await factory.CreateUserWithRolesAsync("bob", Password, "bob@example.com", "User");
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var bobId = (await factory.FindByUserNameAsync("bob"))!.Id;

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, $"/Admin/Users/Edit/{bobId}");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            $"/Admin/Users/Edit/{bobId}", token,
            ("Email", "bob@example.com"),
            ("SelectedRoles", "Viewer")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(["Viewer"], await factory.GetUserRolesAsync("bob"));
    }

    [Fact]
    public async Task AdminUserEdit_Post_RemovingOwnLastAdminUsersGrant_Refused()
    {
        await using var factory = await CreateAdminFactoryAsync();
        using var client = CreateClient(factory);
        await SignInAsync(factory, client, "admin");
        var adminId = (await factory.FindByUserNameAsync("admin"))!.Id;

        // Deselect EVERY role for the admin's own account -> would strip admin.users.
        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, $"/Admin/Users/Edit/{adminId}");
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            $"/Admin/Users/Edit/{adminId}", token,
            ("Email", "admin@example.com")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("permiso de administra", body);
        Assert.Contains("Admin", await factory.GetUserRolesAsync("admin"));
    }
}
