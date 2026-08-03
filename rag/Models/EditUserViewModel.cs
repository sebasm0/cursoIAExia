namespace rag.Models;

/// <summary>
/// Admin user-edit form (spec user-admin; Slice 3 consumes this model): identity
/// info plus the role assignment for one user.
/// </summary>
public class EditUserViewModel
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Roles the user currently has (checked in the form).</summary>
    public IReadOnlyList<string> SelectedRoles { get; set; } = [];

    /// <summary>Role names available for assignment (populated by the admin page).</summary>
    public IReadOnlyList<string> AvailableRoles { get; set; } = [];
}
