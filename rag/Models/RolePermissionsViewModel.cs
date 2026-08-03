namespace rag.Models;

/// <summary>
/// Admin role→permission matrix (spec user-admin; Slice 3 consumes this model):
/// each catalog entry rendered as a checkbox, POSTed back as the diff.
/// </summary>
public class RolePermissionsViewModel
{
    public Guid RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public IReadOnlyList<PermissionCheckbox> Permissions { get; set; } = [];
}

/// <summary>One row of the permission matrix.</summary>
public class PermissionCheckbox
{
    public string Permission { get; set; } = string.Empty;

    public bool IsChecked { get; set; }
}
