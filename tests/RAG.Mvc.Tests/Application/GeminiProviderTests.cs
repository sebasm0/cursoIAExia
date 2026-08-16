using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RAG.Application.Services;
using RAG.Infrastructure.AI;
using Xunit;

namespace RAG.Mvc.Tests.Application;

/// <summary>
/// Host-level provider wiring (provider-selection): with <c>AI:Provider=Gemini</c>
/// the MVC host registers the Gemini client (via its OpenAI-compatible endpoint)
/// wrapped in the <see cref="RetryingChatClient"/> decorator, keeps embeddings
/// local (Ollama), and builds the assistant catalog around the Gemini default
/// model. A missing <c>AI:Gemini:ApiKey</c> fails fast at startup instead of
/// failing every request later with a confusing 502. The default provider (no
/// <c>AI:Provider</c>) must remain Ollama, unchanged.
/// </summary>
public class GeminiProviderTests
{
    [Fact]
    public void Host_GeminiProvider_RegistersChatClientWrappedInRetryingDecorator()
    {
        using var factory = new GeminiHostFactory();
        using var scope = factory.Services.CreateScope();

        var chat = scope.ServiceProvider.GetRequiredService<IChatClient>();

        // The decorator must wrap the OpenAI-compatible Gemini client
        // (Microsoft.Extensions.AI.OpenAIChatClient), NOT the Ollama client.
        var retrying = Assert.IsType<RetryingChatClient>(chat);
        var inner = Unwrap(retrying);
        Assert.Equal("Microsoft.Extensions.AI.OpenAIChatClient", inner.GetType().FullName);
    }

    [Fact]
    public void Host_GeminiProvider_WithoutApiKey_ThrowsInvalidOperationAtHostBuild()
    {
        // The rag host has user-secrets with the REAL AI:Gemini:ApiKey, and the
        // WAF host loads user-secrets — so "absent" is simulated with an
        // EXPLICIT empty override (higher precedence than user-secrets), which
        // production treats as missing. Never touches the real key.
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new GeminiHostFactory(apiKey: "");
            _ = factory.Services; // force host creation
        });

        // Fail-fast contract: booting without a key must surface a clear
        // InvalidOperationException naming the missing key — never a silent
        // host that 502s on every request afterwards.
        var invalid = FindInChain<InvalidOperationException>(ex);
        Assert.NotNull(invalid);
        Assert.Contains("AI:Gemini:ApiKey", invalid.Message);
    }

    [Fact]
    public void Host_OllamaProvider_StillDefault_WrapsOllamaChatClient()
    {
        using var factory = new DefaultOllamaHostFactory();
        using var scope = factory.Services.CreateScope();

        var chat = scope.ServiceProvider.GetRequiredService<IChatClient>();

        // Regression guard: with AI:Provider=Ollama the host keeps the
        // pre-change Ollama wiring (decorator wrapping the OllamaChatClient),
        // even when dev user-secrets select Gemini for the real app.
        var retrying = Assert.IsType<RetryingChatClient>(chat);
        var inner = Unwrap(retrying);
        Assert.Equal("Microsoft.Extensions.AI.OllamaChatClient", inner.GetType().FullName);
    }

    [Fact]
    public void Host_GeminiProvider_AssistantCatalogDefaultsToGeminiModel()
    {
        using var factory = new GeminiHostFactory();
        using var scope = factory.Services.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<AssistantCatalog>();

        // Catalog contract under Gemini: a single "default" assistant built from
        // the Gemini chat model — never the Ollama default model.
        Assert.Single(catalog.All);
        Assert.Equal("default", catalog.Default.Id);
        Assert.Equal("gemini-3.6-flash", catalog.Default.Model);
    }

    [Fact]
    public void Host_GeminiProvider_CustomChatModel_FlowsToCatalogDefault()
    {
        using var factory = new GeminiHostFactory(chatModel: "gemini-2.0-flash");
        using var scope = factory.Services.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<AssistantCatalog>();

        Assert.Equal("gemini-2.0-flash", catalog.Default.Model);
    }

    [Fact]
    public void Host_GeminiProvider_EmbeddingsStayLocalOllama()
    {
        using var factory = new GeminiHostFactory();
        using var scope = factory.Services.CreateScope();

        var embeddings = scope.ServiceProvider
            .GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        // Only final generation moves to Gemini; embeddings stay on the local
        // Ollama model so the stored vector space is not invalidated.
        Assert.Equal("Microsoft.Extensions.AI.OllamaEmbeddingGenerator", embeddings.GetType().FullName);
    }

    /// <summary>
    /// Reads the decorator's wrapped inner client. <see cref="RetryingChatClient"/>
    /// deliberately does not expose <c>Inner</c> publicly, so the composition is
    /// verified through the private readonly field (decorator composition guard).
    /// </summary>
    private static IChatClient Unwrap(RetryingChatClient retrying)
    {
        var field = typeof(RetryingChatClient).GetField(
            "_inner", BindingFlags.NonPublic | BindingFlags.Instance);
        return (IChatClient)field!.GetValue(retrying)!;
    }

    /// <summary>
    /// Walks the whole exception chain so WebApplicationFactory-wrapped startup
    /// failures still surface the original <typeparamref name="TException"/>.
    /// </summary>
    private static TException? FindInChain<TException>(Exception ex) where TException : Exception
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }
}

/// <summary>
/// Host factory booting the REAL MVC host with <c>AI:Provider=Gemini</c> (no
/// AI-service stubs), injecting the Gemini config via UseSetting so the
/// <c>Program</c> switch sees it, and disabling DB migrate/seed so no real
/// PostgreSQL is touched.
/// </summary>
public sealed class GeminiHostFactory(string? apiKey = "test-fake-key", string? chatModel = "gemini-3.6-flash")
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting flows into the host configuration BEFORE the entry point
        // runs (unlike ConfigureAppConfiguration, which applies after Program.cs
        // already read builder.Configuration) — so the AI:Provider switch sees it.
        builder.UseSetting("AI:Provider", "Gemini");
        if (apiKey is not null)
        {
            builder.UseSetting("AI:Gemini:ApiKey", apiKey);
        }

        builder.UseSetting("AI:Gemini:ChatModel", chatModel);

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:ApplyMigrationsOnStartup"] = "false",
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            });
        });
    }
}

/// <summary>
/// Ollama-provider factory: pins <c>AI:Provider=Ollama</c> explicitly so the
/// regression guard is deterministic even when the dev machine's user-secrets
/// set <c>AI:Provider=Gemini</c> (which the WAF host would otherwise inherit).
/// </summary>
public sealed class DefaultOllamaHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AI:Provider", "Ollama");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:ApplyMigrationsOnStartup"] = "false",
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            });
        });
    }
}