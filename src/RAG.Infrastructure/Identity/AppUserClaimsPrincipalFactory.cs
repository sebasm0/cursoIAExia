using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// Claims factory contract (spec RBAC-3, design D4): guarantees each role's
/// permission claims (type <see cref="Permissions.ClaimType"/>) are projected
/// onto the signed-in cookie principal so policies evaluate flat permission claims.
///
/// NOTE: since .NET 8/10 the base <see cref="UserClaimsPrincipalFactory{TUser, TRole}"/>
/// already projects every role claim (permission claims included). This subclass
/// therefore only adds a permission claim when the base projection missed it,
/// keeping the principal duplicate-free regardless of base behavior.
/// </summary>
public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public AppUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var present = identity
            .FindAll(Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var roleName in await UserManager.GetRolesAsync(user))
        {
            var role = await RoleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            foreach (var claim in await RoleManager.GetClaimsAsync(role))
            {
                if (claim.Type == Permissions.ClaimType && present.Add(claim.Value))
                {
                    identity.AddClaim(new Claim(Permissions.ClaimType, claim.Value, claim.ValueType));
                }
            }
        }

        return identity;
    }
}
