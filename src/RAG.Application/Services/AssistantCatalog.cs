namespace RAG.Application.Services;

/// <summary>
/// A selectable chat assistant (spec assistant-selection ASEL-1). <see cref="Model"/>
/// is the Ollama model identifier used only for final answer generation; the
/// embedding generator and reranker never use it (ASEL-5/6).
/// </summary>
public sealed record AssistantDefinition(
    string Id,
    string Label,
    string Model,
    string Description);

/// <summary>
/// Config-driven allow-list of chat assistants (design D1). Loaded once per host
/// from <c>AI:Ollama:Assistants</c> (MVC) or the default-only fallback (API).
/// When the catalog is absent or empty, a single default assistant is derived
/// from the host chat model, keeping behavior backward compatible with the
/// pre-catalog app.
/// </summary>
public sealed class AssistantCatalog
{
    public IReadOnlyList<AssistantDefinition> All { get; }

    public AssistantDefinition Default { get; }

    public AssistantCatalog(string? defaultModel, IReadOnlyList<AssistantDefinition>? assistants)
    {
        if (assistants is { Count: > 0 })
        {
            All = assistants.ToList();
            // The entry matching the host chat model stays the default; fall back
            // to an explicit "default" id, then to the first entry (ASEL-1).
            Default = All.FirstOrDefault(a => a.Model == defaultModel)
                ?? All.FirstOrDefault(a => string.Equals(a.Id, "default", StringComparison.Ordinal))
                ?? All[0];
        }
        else
        {
            Default = new AssistantDefinition(
                "default", "Default", defaultModel ?? string.Empty, "Default assistant");
            All = [Default];
        }
    }

    /// <summary>
    /// Resolves a model id against the allow-list (ASEL-2). Null/whitespace and
    /// unknown ids fall back to <see cref="Default"/> without error, so a value
    /// outside the allow-list never reaches the chat client (ASEL-4).
    /// </summary>
    public AssistantDefinition Resolve(string? modelId)
    {
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            var match = All.FirstOrDefault(a => string.Equals(a.Id, modelId, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return Default;
    }

    /// <summary>
    /// Try-style alias over <see cref="Resolve"/> for the HTTP boundaries. The
    /// allow-list contract never rejects: a value outside the list resolves to
    /// the default assistant (design D1/D3/D4).
    /// </summary>
    public bool TryResolve(string? modelId, out AssistantDefinition assistant)
    {
        assistant = Resolve(modelId);
        return true;
    }
}