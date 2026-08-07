using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Slice 2 RED integration tests (spec user-auth) for password recovery over the
/// real cookie pipeline with an EF InMemory Identity store:
///   AUTH-4  forgot password never leaks account existence (token via console stub)
///   AUTH-5  reset password validates the token and changes the password
/// </summary>
public class AccountPasswordRecoveryTests
{
    private const string Password = "P@ssw0rd1!";
    private const string NewPassword = "NewP@ssw0rd2!";

    private static HttpClient CreateClient(AccountFlowWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── AUTH-4: Forgot password does not leak account existence ──

    [Fact]
    public async Task ForgotPassword_Post_ExistingAccount_SendsResetLinkAndShowsGenericConfirmation()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        var user = await factory.CreateUserAsync("alice", Password, "alice@example.com");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/ForgotPassword");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/ForgotPassword", token,
            ("Email", "alice@example.com")));

        // Generic confirmation shown regardless of account existence (AUTH-4).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("se ha enviado un enlace de restablecimiento", body);

        // The console email stub was asked to deliver a reset link with the token.
        var email = Assert.Single(factory.SentEmails);
        Assert.Equal("alice@example.com", email.Email);
        Assert.Contains("Restablecimiento de contraseña", email.Subject);
        Assert.Contains($"/Account/ResetPassword?userId={user.Id}", email.HtmlBody);
        Assert.Contains("token=", email.HtmlBody);
    }

    [Fact]
    public async Task ForgotPassword_Post_UnknownAccount_SameConfirmationAndNothingSent()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/ForgotPassword");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/ForgotPassword", token,
            ("Email", "nobody@example.com")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("se ha enviado un enlace de restablecimiento", body);
        Assert.Empty(factory.SentEmails);
    }

    // ── AUTH-5: Reset password validates token and changes password ──

    [Fact]
    public async Task ResetPassword_Get_WithValidToken_RendersTheForm()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        var user = await factory.CreateUserAsync("alice", Password, "alice@example.com");
        var resetToken = await factory.GenerateResetTokenAsync(user);

        var response = await client.GetAsync(
            $"/Account/ResetPassword?userId={user.Id}&token={Uri.EscapeDataString(resetToken)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Restablecer su contraseña", body);
    }

    [Fact]
    public async Task ResetPassword_Post_ValidToken_ChangesPasswordAndAllowsSignInWithIt()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        var user = await factory.CreateUserAsync("alice", Password, "alice@example.com");
        var resetToken = await factory.GenerateResetTokenAsync(user);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/ResetPassword", token,
            ("UserId", user.Id.ToString()), ("Token", resetToken),
            ("Password", NewPassword), ("ConfirmPassword", NewPassword)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ha sido restablecida", body);

        // AUTH-5: the user can now sign in with the NEW password.
        var loginToken = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");
        var login = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login", loginToken,
            ("UserName", "alice"), ("Password", NewPassword)));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.True(AccountTestHelpers.HasAuthCookie(login),
            "After a valid reset the new password must authenticate.");
    }

    [Fact]
    public async Task ResetPassword_Post_InvalidToken_ShowsErrorAndKeepsOldPassword()
    {
        await using var factory = new AccountFlowWebApplicationFactory();
        using var client = CreateClient(factory);
        var user = await factory.CreateUserAsync("alice", Password, "alice@example.com");

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/ResetPassword", token,
            ("UserId", user.Id.ToString()), ("Token", "tampered-invalid-token"),
            ("Password", NewPassword), ("ConfirmPassword", NewPassword)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Token de restablecimiento", body);

        // AUTH-5: the existing password must remain valid.
        var loginToken = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Account/Login");
        var login = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Account/Login", loginToken,
            ("UserName", "alice"), ("Password", Password)));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.True(AccountTestHelpers.HasAuthCookie(login),
            "A failed reset must not invalidate the existing password.");
    }
}
