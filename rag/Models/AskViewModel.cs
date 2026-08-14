using System.ComponentModel.DataAnnotations;
using RAG.Application.Services;

namespace rag.Models;

public class AskViewModel
{
    [Required(ErrorMessage = "Por favor, ingrese una pregunta.")]
    [Display(Name = "Su pregunta")]
    public string Query { get; set; } = string.Empty;

    public string? Answer { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Catalog assistants offered by the selector (ASK-14).</summary>
    public IReadOnlyList<AssistantDefinition> AvailableAssistants { get; set; } = [];

    /// <summary>Selected assistant id; blank/unknown resolve to the default (ASEL-2).</summary>
    public string SelectedModelId { get; set; } = "";

    /// <summary>Label of the assistant that generated the answer, for attribution (ASK-15).</summary>
    public string? UsedAssistant { get; set; }
}