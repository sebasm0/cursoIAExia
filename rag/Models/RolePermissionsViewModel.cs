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

    /// <summary>
    /// Permission names posted back from the checked checkboxes (ADMIN-6): the
    /// controller diffs this set against the role's current permission claims.
    /// <see cref="List{T}"/> because read-only interface collections do not bind
    /// from form data.
    /// </summary>
    public List<string> SelectedPermissions { get; set; } = [];
}

/// <summary>One row of the permission matrix.</summary>
public class PermissionCheckbox
{
    public string Permission { get; set; } = string.Empty;

    public bool IsChecked { get; set; }
}
