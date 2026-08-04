namespace rag.Models;

/// <summary>One row of the admin users index (ADMIN-1).</summary>
public sealed record UserRow(
    Guid Id,
    string UserName,
    string Email,
    IReadOnlyList<string> Roles);

/// <summary>Model for the admin users index page (ADMIN-1).</summary>
public class UsersIndexViewModel
{
    public IReadOnlyList<UserRow> Users { get; set; } = [];
}
