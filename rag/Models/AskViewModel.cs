using System.ComponentModel.DataAnnotations;

namespace rag.Models;

public class AskViewModel
{
    [Required(ErrorMessage = "Por favor, ingrese una pregunta.")]
    [Display(Name = "Su pregunta")]
    public string Query { get; set; } = string.Empty;

    public string? Answer { get; set; }

    public string? ErrorMessage { get; set; }
}
