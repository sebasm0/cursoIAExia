using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RAG.Infrastructure.Identity;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Options for <see cref="TestAuthHandler"/>: the roles and permission claims
/// the test principal carries.
/// </summary>
public class TestAuthOptions : AuthenticationSchemeOptions
{
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public IReadOnlyList<string> Roles { get; set; } = [];
    public string UserName { get; set; } = "test-user";
    public string UserId { get; set; } = "test-user-id";
}

/// <summary>
/// Test authentication handler (spec AUTH-9): issues a claims principal with
/// identity + role + permission claims — no database required. Used with
/// <c>WebApplicationFactory</c> to exercise <c>[Authorize(Policy=...)]</c>
/// endpoints.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<TestAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Options.UserId),
            new(ClaimTypes.Name, Options.UserName),
        };

        foreach (var role in Options.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in Options.Permissions)
        {
            claims.Add(new Claim(Permissions.ClaimType, permission));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Replaces the app's default (Identity cookie) authentication with the test
/// handler carrying the given permission + role claims (spec AUTH-9).
/// </summary>
public static class TestAuthExtensions
{
    public static IServiceCollection AddTestAuthentication(
        this IServiceCollection services,
        string[] permissions,
        string[] roles)
    {
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<TestAuthOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                options =>
                {
                    options.Permissions = permissions;
                    options.Roles = roles;
                });

        return services;
    }

    /// <summary>
    /// Policy-enforcement wiring (design D6): the Test handler authenticates every
    /// request while the real Identity cookie handler stays the challenge/forbid
    /// scheme, so anonymous requests redirect to the LoginPath and denied requests
    /// to the AccessDeniedPath exactly like production. AddIdentity pins
    /// DefaultAuthenticateScheme to Identity.Application, so it must be overridden
    /// to the Test scheme here.
    /// </summary>
    public static IServiceCollection AddPolicyTestAuthentication(
        this IServiceCollection services,
        string[] permissions,
        string[] roles)
    {
        services.AddTestAuthentication(permissions, roles);

        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultForbidScheme = IdentityConstants.ApplicationScheme;
        });

        return services;
    }
}
