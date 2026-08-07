using System.ComponentModel.DataAnnotations;

namespace rag.Models;

/// <summary>
/// Admin user-create form (spec user-admin; Slice 3 consumes this model).
/// No public signup exists — accounts are created only through the admin flow.
/// </summary>
public class CreateUserViewModel
{
    [Required(ErrorMessage = "El campo Nombre de usuario es obligatorio.")]
    [Display(Name = "Nombre de usuario")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El campo Correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "El campo debe ser una dirección de correo electrónico válida.")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El campo Contraseña es obligatorio.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Roles selected by the admin; the account is created with them. Posted by
    /// the role checkboxes — <see cref="List{T}"/> because read-only interface
    /// collections do not bind from form data.
    /// </summary>
    public List<string> SelectedRoles { get; set; } = [];

    /// <summary>Role names available for assignment (populated by the admin page).</summary>
    public IReadOnlyList<string> AvailableRoles { get; set; } = [];
}
