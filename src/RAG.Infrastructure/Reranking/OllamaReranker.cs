using Microsoft.Extensions.AI;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;

namespace RAG.Infrastructure.Reranking;

/// <summary>
/// Reranker basado en LLM: envía query + candidatos al modelo y pide una
/// puntuación de relevancia para cada fragmento.
/// </summary>
public sealed class OllamaReranker(IChatClient chatClient) : IReranker
{
    private const int MaxRerankItems = 20;

    public async Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        string? modelId = null,
        CancellationToken ct = default)
    {
        if (results.Count == 0)
            return results;

        var candidates = results.Take(MaxRerankItems).ToList();

        var prompt = BuildRerankPrompt(query, candidates);

        // Route the rerank chat call to the selected model (latency fix): pass
        // ChatOptions with ModelId when resolved; when null/blank, omit options
        // so Ollama uses the chat client's default model.
        ChatOptions? options = string.IsNullOrWhiteSpace(modelId) ? null : new ChatOptions { ModelId = modelId };
        var response = await chatClient.GetResponseAsync(prompt, options, ct);
        var scores = ParseScores(response.Text ?? "", candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            candidates[i].RerankScore = i < scores.Count ? scores[i] : 0;
        }

        return [.. candidates.OrderByDescending(r => r.RerankScore)];
    }

    private static string BuildRerankPrompt(string query, List<SearchResult> candidates)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a relevance scorer. For each document fragment below, rate its relevance to the query.");
        sb.AppendLine("Return ONLY a comma-separated list of scores from 0.0 (completely irrelevant) to 10.0 (highly relevant).");
        sb.AppendLine("Do NOT include any other text, explanation, or formatting.");
        sb.AppendLine();
        sb.AppendLine($"Query: {query}");
        sb.AppendLine();

        for (int i = 0; i < candidates.Count; i++)
        {
            var content = candidates[i].Chunk.Content;
            var preview = content.Length > 500 ? content[..500] + "..." : content;
            sb.AppendLine($"--- Fragment {i + 1} ---");
            sb.AppendLine(preview);
            sb.AppendLine();
        }

        sb.Append("Scores: ");
        return sb.ToString();
    }

    private static List<double> ParseScores(string response, int expectedCount)
    {
        var scores = new List<double>();

        // Try comma-separated numbers
        var parts = response.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            // Extract first number from each part
            var cleaned = new string(part.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var score))
            {
                scores.Add(Math.Clamp(score, 0, 10));
            }
        }

        // Fill missing with 0
        while (scores.Count < expectedCount)
            scores.Add(0);

        return scores;
    }
}
