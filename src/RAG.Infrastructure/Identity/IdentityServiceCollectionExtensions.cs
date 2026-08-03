using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// Registration entry point for ASP.NET Core Identity over the isolated
/// "identity" schema (design D6): DbContext, cookie auth, password/lockout
/// policy, permission policies, claims factory, email stub and seeder.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddRagIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSQL is required");

        services.AddDbContext<AppIdentityDbContext>(options => options.UseNpgsql(connectionString));

        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // Design D6: ≥8 chars with digit + upper + lower (no special-char
                // requirement); lockout 5 attempts / 15 min; admin-created accounts.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ReturnUrlParameter = "returnUrl";
            options.SlidingExpiration = true;
            options.Cookie.SecurePolicy = IsProduction(configuration)
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        });

        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AppUserClaimsPrincipalFactory>();
        services.AddScoped<IEmailSender, ConsoleEmailSender>();
        services.AddScoped<IdentitySeeder>();
        services.AddRagAuthorization();

        return services;
    }

    /// <summary>
    /// One authorization policy per catalog entry (spec RBAC-4): the policy
    /// asserts a flat <see cref="Permissions.ClaimType"/> claim on the principal.
    /// </summary>
    public static IServiceCollection AddRagAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireClaim(Permissions.ClaimType, permission));
            }
        });

        return services;
    }

    private static bool IsProduction(IConfiguration configuration)
        => string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"],
            "Production",
            StringComparison.OrdinalIgnoreCase);
}
