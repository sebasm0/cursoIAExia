using System.ComponentModel.DataAnnotations;

namespace rag.Models;

/// <summary>
/// Admin user-create form (spec user-admin; Slice 3 consumes this model).
/// No public signup exists — accounts are created only through the admin flow.
/// </summary>
public class CreateUserViewModel
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Roles selected by the admin; the account is created with them.</summary>
    public IReadOnlyList<string> SelectedRoles { get; set; } = [];

    /// <summary>Role names available for assignment (populated by the admin page).</summary>
    public IReadOnlyList<string> AvailableRoles { get; set; } = [];
}
