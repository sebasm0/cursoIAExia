using System.Text.Json;
using Xunit;

namespace RAG.Api.Tests;

/// <summary>
/// Config-contract guard (spec assistant-selection ASEL-5/6/10): the vector
/// store contract depends on the embedding model and the reranker default chat
/// model never changing. These tests fail the moment someone edits those values,
/// protecting the stored embeddings and reranking behavior from silent drift.
/// </summary>
public class ConfigPinTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Api_Host_PinsEmbeddingModelAndRerankerDefault()
    {
        using var doc = ReadJson(Path.Combine(RepoRoot, "src", "RAG.Api", "appsettings.json"));
        var ollama = doc.RootElement.GetProperty("Ollama");

        // ASEL-5: embeddings must stay nomic-embed-text or the vector store is invalidated.
        Assert.Equal("nomic-embed-text", ollama.GetProperty("EmbeddingModel").GetString());
        // ASEL-6: the reranker (OllamaReranker) uses the default chat client's
        // model; pinning the host chat model pins reranking behavior.
        Assert.Equal("llama3.2", ollama.GetProperty("ChatModel").GetString());
    }

    [Fact]
    public void Mvc_Host_PinsEmbeddingModelAndRerankerDefault()
    {
        using var doc = ReadJson(Path.Combine(RepoRoot, "rag", "appsettings.json"));
        var ollama = doc.RootElement.GetProperty("AI").GetProperty("Ollama");

        // ASEL-5: the MVC host writes the shared store with the same embedding model.
        Assert.Equal("nomic-embed-text", ollama.GetProperty("EmbeddingModel").GetString());
        // ASEL-6: the MVC reranker default chat model stays the pre-change value.
        Assert.Equal("phi3:mini", ollama.GetProperty("ChatModel").GetString());
    }

    private static JsonDocument ReadJson(string path) =>
        JsonDocument.Parse(File.ReadAllText(path));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RAG.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (RAG.slnx) not found above the test output directory.");
    }
}
