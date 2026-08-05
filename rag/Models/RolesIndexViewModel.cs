namespace rag.Models;

/// <summary>
/// One row of the admin roles index (ADMIN-4): the role name, its permission
/// claim count, its member count, and whether it is a built-in seed role
/// (built-in roles and roles with members are not deletable).
/// </summary>
public sealed record RoleRow(
    Guid Id,
    string Name,
    int PermissionCount,
    int MemberCount,
    bool IsBuiltIn);

/// <summary>Model for the admin roles index page (ADMIN-4).</summary>
public class RolesIndexViewModel
{
    public IReadOnlyList<RoleRow> Roles { get; set; } = [];
}
