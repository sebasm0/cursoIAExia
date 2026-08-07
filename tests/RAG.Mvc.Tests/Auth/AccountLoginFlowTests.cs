using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Slice 2 RED integration tests (spec user-auth) over the real cookie pipeline
/// with an EF InMemory Identity store:
///   AUTH-1  successful login issues cookie + safe local returnUrl / external rejected
///   AUTH-2  wrong password -> generic error, no cookie; lockout after 5 failures
///   AUTH-3  logout is POST-only; GET does not sign out, POST clears the session
///   AUTH-6  no public signup route exists
///   AUTH-8  cookie challenge config (LoginPath / AccessDeniedPath / returnUrl)
/// </summary>
public class AccountLoginFlowTests
{
    private const string Password = "P@ssw0rd1!";

    private static HttpClient CreateClient(AccountFlowWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── AUTH-1: Successful login issues an authentication cookie ──

    [Fact]
    public async Task Login_Post_ValidCredentials_IssuesAuthCookieAndRedirectsToLocalReturnUrl()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("alice", Password, "alice@example.com");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login?returnUrl=/Ask", token,
            ("UserName", "alice"), ("Password", Password)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Ask", response.Headers.Location?.OriginalString);
        Assert.True(AccountTestHelpers.HasAuthCookie(response),
            "Successful login must issue the Identity authentication cookie.");
    }

    [Fact]
    public async Task Login_Post_ValidCredentials_ExternalReturnUrlRedirectsToHome()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("alice", Password, "alice@example.com");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login?returnUrl=https%3A%2F%2Fevil.example.com%2Fsteal", token,
            ("UserName", "alice"), ("Password", Password)));

        // Open-redirect guard: never redirect to an external host (AUTH-1).
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.False(response.Headers.Location!.IsAbsoluteUri,
            "External returnUrl must be rejected (open-redirect guard).");
        Assert.Equal("/", response.Headers.Location.OriginalString);
    }

    // ── AUTH-2: Failed login shows a generic error and enforces lockout ──

    [Fact]
    public async Task Login_Post_WrongPassword_RendersGenericErrorWithoutCookie()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("alice", Password, "alice@example.com");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login", token,
            ("UserName", "alice"), ("Password", "WrongPass1!")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Intento de inicio de sesi", body);
        Assert.False(AccountTestHelpers.HasAuthCookie(response),
            "Failed login must not issue an authentication cookie.");
    }

    [Fact]
    public async Task Login_Post_CorrectPasswordAfterFiveFailures_RefusedUntilLockoutExpires()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("alice", Password, "alice@example.com");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");

        // 5 consecutive failed attempts (MaxFailedAccessAttempts = 5, design D6).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.SendAsync(AccountTestHelpers.CreatePost(
                "/Account/Login", token,
                ("UserName", "alice"), ("Password", "WrongPass1!")));
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        // The 6th attempt with the CORRECT password must be refused (locked out).
        var sixth = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login", token,
            ("UserName", "alice"), ("Password", Password)));

        Assert.Equal(HttpStatusCode.OK, sixth.StatusCode);
        var body = await sixth.Content.ReadAsStringAsync();
        Assert.Contains("bloqueada temporalmente", body);
        Assert.False(AccountTestHelpers.HasAuthCookie(sixth),
            "Locked-out account must not receive an authentication cookie.");
    }

    // ── AUTH-3: Logout is POST-only and clears the session ──

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

    [Fact]
    public async Task Logout_Get_DoesNotSignOut()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("alice", Password, "alice@example.com");
        await SignInAsync(factory, client, "alice");

        // AUTH-3: GET /Account/Logout must NOT sign the user out (POST-only).
        var getLogout = await client.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.NotFound, getLogout.StatusCode);

        // The session is still authenticated: the navbar still shows the user + logout form.
        var home = await client.GetAsync("/");
        var body = await home.Content.ReadAsStringAsync();
        Assert.Contains("alice", body);
        Assert.Contains("Logout", body);
    }

    [Fact]
    public async Task Logout_Post_SignsOutClearsCookieAndRedirectsHome()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("alice", Password, "alice@example.com");
        await SignInAsync(factory, client, "alice");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost("/Account/Logout", token));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.True(AccountTestHelpers.HasAuthCookie(response),
            "Logout must send a Set-Cookie clearing the auth cookie.");

        // The session is gone: the navbar shows the anonymous Login link instead.
        var home = await client.GetAsync("/");
        var body = await home.Content.ReadAsStringAsync();
        Assert.Contains("Login", body);
        Assert.DoesNotContain("Logout", body);
        Assert.DoesNotContain("alice", body);
    }

    // ── AUTH-6: Accounts are created only by administrators ──

    [Fact]
    public async Task Register_Routes_NoAnonymousSignupPossible()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);

        // AUTH-6: no public sign-up route exists — GET returns 404.
        var getResponse = await client.GetAsync("/Account/Register");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // POST is hard-denied too (framework returns 404/405 for a missing action)
        // — never a redirect or a success that could create an account.
        var postResponse = await client.PostAsync("/Account/Register", new FormUrlEncodedContent([]));
        Assert.True(
            postResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"POST /Account/Register must be denied, got {postResponse.StatusCode}.");
        Assert.NotEqual(HttpStatusCode.Redirect, postResponse.StatusCode);

        // And the anonymous visitor could not create an account.
        Assert.Equal(0, await factory.CountUsersAsync());
    }

    // ── AUTH-8: Unauthenticated access redirects to login (cookie challenge config) ──

    [Fact]
    public async Task CookieConfig_LoginAndAccessDeniedPaths_AreWired()
    {
        await using var factory = new AccountFlowWebApplicationFactory();

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("/Account/Login", options.LoginPath);
        Assert.Equal("/Account/AccessDenied", options.AccessDeniedPath);
        Assert.Equal("returnUrl", options.ReturnUrlParameter);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }
}
