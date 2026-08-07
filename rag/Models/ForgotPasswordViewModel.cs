using System.ComponentModel.DataAnnotations;

namespace rag.Models;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "El campo Correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "El campo debe ser una dirección de correo electrónico válida.")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = string.Empty;
}
