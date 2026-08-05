using Microsoft.AspNetCore.Identity;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// Application user for cookie authentication. Guid keys match the
/// repository-wide Guid identity convention (documents/chunks).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
}
