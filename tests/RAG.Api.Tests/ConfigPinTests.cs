using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RAG.Application.Services;
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
        // Aligned config schema: the API host reads AI:Ollama:* like the MVC host.
        var ollama = doc.RootElement.GetProperty("AI").GetProperty("Ollama");

        // ASEL-5: embeddings must stay nomic-embed-text or the vector store is invalidated.
        Assert.Equal("nomic-embed-text", ollama.GetProperty("EmbeddingModel").GetString());
        // ASEL-6: the reranker (OllamaReranker) uses the default chat client's
        // model; pinning the host chat model pins reranking behavior. Aligned
        // with the MVC host and the installed Ollama models: plain "llama3.2"
        // has no tag and is not pullable, phi3:mini is the installed default.
        Assert.Equal("phi3:mini", ollama.GetProperty("ChatModel").GetString());
    }

    [Fact]
    public void Api_Host_ExposesAssistantCatalog()
    {
        // (a) Config contract: the API appsettings ships the same multi-assistant
        // allow-list as the MVC host (design D1, ASEL-1), so fast/tiny are
        // selectable through the API exactly like the MVC app.
        using var doc = ReadJson(Path.Combine(RepoRoot, "src", "RAG.Api", "appsettings.json"));
        var assistants = doc.RootElement
            .GetProperty("AI")
            .GetProperty("Ollama")
            .GetProperty("Assistants");
        var ids = assistants.EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet();
        Assert.Contains("default", ids);
        Assert.Contains("fast", ids);
        Assert.Contains("tiny", ids);

        // (b) Runtime: Program registers a non-empty catalog resolved from that
        // section (no longer the empty [] catalog), so /api/rag/ask routing can
        // select the same assistants the MVC host offers.
        using var factory = new ApiHostFactory();
        var catalog = factory.Services.GetRequiredService<AssistantCatalog>();
        Assert.True(catalog.All.Count >= 3, $"Expected at least 3 catalog entries, got {catalog.All.Count}.");
        Assert.Contains(catalog.All, a => a.Id == "default");
        Assert.Contains(catalog.All, a => a.Id == "fast");
        Assert.Contains(catalog.All, a => a.Id == "tiny");
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

    /// <summary>
    /// WAF variant that boots the real API host WITHOUT replacing the
    /// <see cref="AssistantCatalog"/> (unlike <see cref="ApiWebApplicationFactory"/>,
    /// which swaps in a two-entry catalog for the routing tests), so DI reflects
    /// exactly what Program registers from AI:Ollama:Assistants.
    /// </summary>
    private sealed class ApiHostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Never allow the lazy PgVectorStore to point at a real database.
                    ["ConnectionStrings:PostgreSQL"] =
                        "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
                });
            });
        }
    }
}