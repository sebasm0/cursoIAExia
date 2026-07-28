namespace rag.Models;

public class AskViewModel
{
    public string Query { get; set; } = string.Empty;

    public string? Answer { get; set; }

    public string? ErrorMessage { get; set; }
}
