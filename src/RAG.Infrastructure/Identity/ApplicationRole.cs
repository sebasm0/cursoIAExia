using Microsoft.AspNetCore.Identity;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// Application role. Permission grants are persisted as role claims
/// with the <see cref="Permissions.ClaimType"/> claim type.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
}
