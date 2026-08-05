using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RAG.Infrastructure.Identity;
using Xunit;

namespace RAG.Mvc.Tests.Auth;

/// <summary>Email captured by <see cref="RecordingEmailSender"/> (AUTH-4).</summary>
public sealed record SentEmail(string Email, string Subject, string HtmlBody);

/// <summary>
/// Test double for the console email stub: records what <c>SendAsync</c> was
/// asked to deliver instead of writing to the log (AUTH-4 no-leak assertion).
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly List<SentEmail> _emails = [];

    public IReadOnlyList<SentEmail> Emails => _emails;

    public Task SendAsync(string email, string subject, string htmlMessage)
    {
        _emails.Add(new SentEmail(email, subject, htmlMessage));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Factory for the Account integration tests (spec user-auth): swaps the real
/// PostgreSQL Identity DbContext for an isolated EF InMemory store and captures
/// outgoing "emails", keeping the real Identity cookie + SignInManager pipeline.
/// </summary>
public class AccountFlowWebApplicationFactory : RagWebApplicationFactoryBase
{
    private static readonly InMemoryDatabaseRoot InMemoryRoot = new();
    private readonly string _dbName = $"account-identity-{Guid.NewGuid():N}";
    private readonly RecordingEmailSender _recorder = new();

    public IReadOnlyList<SentEmail> SentEmails => _recorder.Emails;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Swap the Npgsql Identity context for an isolated in-memory store so
            // SignInManager/UserManager work end-to-end without a database (design D3).
            // EF 9+ AddDbContext registers all three descriptors; both option
            // configurations must go or the context sees two providers.
            services.RemoveAll(typeof(AppIdentityDbContext));
            services.RemoveAll(typeof(DbContextOptions<AppIdentityDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppIdentityDbContext>));
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseInMemoryDatabase(_dbName, InMemoryRoot));

            // Capture what the console email stub would log (AUTH-4).
            RemoveService<IEmailSender>(services);
            services.AddSingleton<IEmailSender>(_recorder);
        });
    }

    public async Task<ApplicationUser> CreateUserAsync(string userName, string password, string email)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, Email = email };
        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    public async Task<string> GenerateResetTokenAsync(ApplicationUser user)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<int> CountUsersAsync()
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.Users.CountAsync();
    }
}

/// <summary>Shared helpers for antiforgery-protected form POSTs.</summary>
public static class AccountTestHelpers
{
    /// <summary>
    /// GETs a form page, asserts it renders, and returns the antiforgery token
    /// the form tag helper embedded (cookie auto-managed by the client).
    /// </summary>
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(match.Success, $"Antiforgery token not rendered on {url}.");
        return match.Groups[1].Value;
    }

    public static HttpRequestMessage CreatePost(
        string url,
        string antiforgeryToken,
        params (string Key, string Value)[] fields)
    {
        var pairs = new List<KeyValuePair<string, string>>(fields.Length + 1);
        foreach (var field in fields)
        {
            pairs.Add(new KeyValuePair<string, string>(field.Key, field.Value));
        }

        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", antiforgeryToken));

        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(pairs),
        };
    }

    /// <summary>
    /// Builds a multipart/form-data POST that mirrors <see cref="CreatePost"/> but
    /// carries a real file part bound to the field <c>file</c>, so file-bound
    /// actions can be exercised with the antiforgery token present in the multipart
    /// body. Used by the Document Upload CSRF and validation-visibility tests.
    /// </summary>
    public static HttpRequestMessage CreateMultipartPost(
        string url, string antiforgeryToken, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(content), "file", fileName },
            { new StringContent(antiforgeryToken), "__RequestVerificationToken" }
        };

        return new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
    }

    /// <summary>
    /// The Identity application cookie name: scheme "Identity.Application" maps
    /// to the ".AspNetCore.Identity.Application" cookie (default cookie prefix).
    /// </summary>
    public const string IdentityCookieName = ".AspNetCore.Identity.Application";

    /// <summary>True when the response carries a Set-Cookie for the Identity auth cookie.</summary>
    public static bool HasAuthCookie(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.Contains(IdentityCookieName, StringComparison.OrdinalIgnoreCase));
}
