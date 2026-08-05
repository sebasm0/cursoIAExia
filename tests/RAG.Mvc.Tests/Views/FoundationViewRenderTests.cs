using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using RAG.Mvc.Tests.Auth;
using Xunit;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// Slice A view-render tests (spec ui-design-system UDS-1..UDS-6, user-auth
/// AUTH-10..AUTH-13): every foundation screen renders 200 with the design-system
/// markers over the real WebApplicationFactory pipeline (WebApplicationFactory +
/// TestAuthHandler, design "Testing Strategy" row). No database — the identity
/// context is never resolved on these public pages.
/// </summary>
public class FoundationViewRenderTests
{
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── UDS-2 / UDS-3: layout shell + theme toggle, default light ──

    [Fact]
    public async Task Home_Anonymous_DefaultThemeIsLight()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-bs-theme=\"light\"", body);
    }

    [Fact]
    public async Task Layout_PreRendersThemeScript_WithLightFallback()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // UDS-2/UDS-3: the inline head script reads the persisted choice before
        // paint (no flash) and falls back to light when nothing is stored or JS
        // is disabled (server-rendered default).
        Assert.Contains("localStorage.getItem('rag-theme')", body);
        Assert.Contains("|| 'light'", body);
    }

    [Fact]
    public async Task Layout_RendersThemeToggleButton()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // UDS-2: a theme toggle control is present on every page.
        Assert.Contains("id=\"theme-toggle\"", body);
    }

    [Fact]
    public async Task Layout_Anonymous_ShowsLoginLink_NoLogoutForm()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("/Account/Login", body);
        Assert.DoesNotContain("Logout", body);
    }

    [Fact]
    public async Task Layout_SignedIn_ShowsLogoutPostFormAndUserName()
    {
        // AUTH-11 is asserted over the real Identity cookie pipeline (the same
        // flow the AUTH-3 tests use): a TestAuthHandler principal is not a
        // "signed in" session for SignInManager.IsSignedIn, so the real login
        // flow is the faithful layer for this assertion.
        const string password = "P@ssw0rd1!";
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        await factory.CreateUserAsync("alice", password, "alice@example.com");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");
        var login = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login", token,
            ("UserName", "alice"), ("Password", password)));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // AUTH-11: the layout renders the user name and a logout POST form when
        // signed in. AUTH-3 keeps logout POST-only — the form must POST.
        Assert.Contains("alice", body);
        Assert.Contains("Logout", body);
        var logoutFormIndex = body.IndexOf("Logout", StringComparison.Ordinal);
        Assert.True(logoutFormIndex > 0);
        var formContext = body.Substring(Math.Max(0, logoutFormIndex - 200), 400);
        Assert.Contains("method=\"post\"", formContext);
    }

    // ── UDS-1: token CSS variables + dark palette ──

    [Fact]
    public async Task SiteCss_ServesTokenVariables()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var css = await response.Content.ReadAsStringAsync();
        Assert.Contains("--bs-primary: #0d6efd", css);
        Assert.Contains("--bs-border-radius: .5rem", css);
        Assert.Contains("--bs-focus-ring-color: #258cfb", css);
    }

    [Fact]
    public async Task SiteCss_DarkPalette_OverridesBodyTokens()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var css = await (await client.GetAsync("/css/site.css")).Content.ReadAsStringAsync();

        // UDS-1: the dark variant exists and switches body background/color tokens.
        Assert.Contains("[data-bs-theme=\"dark\"]", css);
        Assert.Contains("--bs-body-bg", css);
        Assert.Contains("--bs-body-color", css);
    }

    // ── UDS-3: theme toggle JS persists via localStorage ──

    [Fact]
    public async Task SiteJs_ServesThemeToggle_WithLocalStoragePersistence()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/js/site.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var js = await response.Content.ReadAsStringAsync();
        Assert.Contains("'rag-theme'", js);
        Assert.Contains("localStorage", js);
    }

    // ── UDS-5: dashboard = hero + 3 action cards, real routes, no stats ──

    [Fact]
    public async Task Home_Index_RendersHeroAndThreeActionCards_ToRealRoutes()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // Hero.
        Assert.Contains("Semantic search over your documents", body);
        // Three action cards.
        Assert.Contains("Ask a Question", body);
        Assert.Contains("Upload a Document", body);
        Assert.Contains("Documents", body);
        // Each card links to a real route (Ask GET and the Documents landing).
        Assert.Contains("href=\"/Ask\"", body);
        Assert.Contains("href=\"/Documents\"", body);
    }

    [Fact]
    public async Task Home_Index_NoPlaceholderCopyOrFabricatedStats()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // UDS-4: no lorem/sample copy.
        Assert.DoesNotContain("lorem", body, StringComparison.OrdinalIgnoreCase);
        // UDS-5: no invented statistics/counts on the dashboard.
        Assert.DoesNotContain("documents indexed", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("queries answered", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total users", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── UDS-6: friendly global error page with request ID, no stack trace ──

    [Fact]
    public async Task Error_Page_RendersFriendlyMessageAndRequestId_NoStackTrace()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Home/Error");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Something went wrong", body);
        Assert.Contains("Request ID", body);
        Assert.DoesNotContain("Stack Trace", body);
        Assert.DoesNotContain("Development Mode", body);
    }

    // ── AUTH-10/12/13: account screens follow the design system ──

    [Fact]
    public async Task Login_Page_RendersDesignSystemForm()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Log in", body);
        Assert.Contains("name=\"UserName\"", body);
        Assert.Contains("name=\"Password\"", body);
        Assert.Contains("Forgot your password?", body);
    }

    [Fact]
    public async Task ForgotPassword_Page_RendersDesignSystemForm()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Account/ForgotPassword");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Forgot your password?", body);
        Assert.Contains("name=\"Email\"", body);
    }

    [Fact]
    public async Task ResetPassword_Page_RendersDesignSystemForm()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Account/ResetPassword");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Reset your password", body);
        Assert.Contains("name=\"Password\"", body);
        Assert.Contains("name=\"ConfirmPassword\"", body);
    }

    [Fact]
    public async Task AccessDenied_Page_RendersDesignSystemMessage()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Account/AccessDenied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Access denied", body);
    }
}
