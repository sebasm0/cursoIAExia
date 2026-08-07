namespace rag.Models;

/// <summary>
/// Read-only presentation of the active, non-secret application settings.
/// Secret values (database password, seed admin credentials) are never exposed.
/// </summary>
public class SettingsViewModel
{
    public string Provider { get; set; } = "Ollama";

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    public string ChatModel { get; set; } = "llama3.2";

    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Human-readable maximum upload size (e.g. "10 MB").</summary>
    public string MaxFileSizeHumanReadable { get; set; } = "";
}