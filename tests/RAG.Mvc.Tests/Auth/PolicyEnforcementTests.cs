using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Slice 4 policy enforcement tests (spec RBAC-4/RBAC-5, ASK-8, UPLOAD-9): the
/// [Authorize(Policy = ...)] gates on the Ask and Upload endpoints. No database —
/// every request is authenticated via the TestAuthHandler carrying fixed claims
/// (design "Policy routing" row). Three outcomes per endpoint:
///   anonymous principal          -> 302 redirect to /Account/Login (challenge)
///   authenticated + claim        -> 200, existing behavior applies
///   authenticated without claim  -> 302 -> /Account/AccessDenied (forbid, never login)
/// </summary>
public class PolicyEnforcementTests
{
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── ASK-8 / RBAC-4 / RBAC-5: Ask requires the rag.ask permission ──

    [Fact]
    public async Task Ask_Get_Anonymous_RedirectsToLogin()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Ask");

        // ASK-8: the 302 fires in the authorization middleware, before any RAG
        // pipeline call, and carries the returnUrl the login page will honor.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Contains("returnUrl", response.Headers.Location?.Query);
    }

    [Fact]
    public async Task Ask_Post_Anonymous_RedirectsToLogin()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.PostAsync(
            "/Ask/Ask",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Query"] = "What is the capital of France?"
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Contains("returnUrl", response.Headers.Location?.Query);
    }

    [Fact]
    public async Task Ask_Get_WithRagAskPermission_ReturnsOk()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.RagAsk], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Ask");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ask_Get_WithoutRagAskPermission_RoutesToAccessDenied()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], ["User"]);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Ask");

        // RBAC-5: an authenticated user lacking the permission is routed to the
        // access-denied page — never to the login page, never a bare 403.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);

        var denied = await client.GetAsync(response.Headers.Location!.AbsolutePath);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.Contains("Access denied", body);
    }

    [Fact]
    public async Task Ask_Post_WithoutRagAskPermission_RoutesToAccessDenied()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], ["User"]);
        using var client = CreateClient(factory);

        var response = await client.PostAsync(
            "/Ask/Ask",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Query"] = "Is this allowed?"
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }
}
