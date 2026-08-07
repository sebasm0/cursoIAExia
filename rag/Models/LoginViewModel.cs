using System.ComponentModel.DataAnnotations;

namespace rag.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "El campo Usuario es obligatorio.")]
    [Display(Name = "Usuario")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El campo Contraseña es obligatorio.")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;
}
