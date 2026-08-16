using RAG.Application.Services;

namespace rag.Models;

/// <summary>
/// Read-only presentation of the active, non-secret application settings.
/// Secret values (database password, seed admin credentials) are never exposed.
/// </summary>
public class SettingsViewModel
{
    public string Provider { get; set; } = "Ollama";

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Host chat model used to derive the default assistant (ASEL-1).</summary>
    public string ChatModel { get; set; } = "phi3:mini";

    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Catalog assistants selectable for chat, as shown in Settings (ASEL-1).</summary>
    public IReadOnlyList<AssistantDefinition> Assistants { get; set; } = [];

    /// <summary>Human-readable maximum upload size (e.g. "10 MB").</summary>
    public string MaxFileSizeHumanReadable { get; set; } = "";
}