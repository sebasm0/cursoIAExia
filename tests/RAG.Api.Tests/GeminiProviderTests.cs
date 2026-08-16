using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RAG.Infrastructure.AI;
using Xunit;

namespace RAG.Api.Tests;

/// <summary>
/// Provider wiring for the API host — same contract as the MVC host
/// (provider-selection): with <c>AI:Provider=Gemini</c> the API registers the
/// Gemini client via its OpenAI-compatible endpoint wrapped in
/// <see cref="RetryingChatClient"/>, and a missing <c>AI:Gemini:ApiKey</c> fails
/// fast at startup. The default provider (no <c>AI:Provider</c>) stays Ollama —
/// approval guard for the switch refactor of <c>RAG.Api.Program</c>.
/// </summary>
public class GeminiProviderTests
{
    [Fact]
    public void Api_Host_GeminiProvider_RegistersChatClientWrappedInRetryingDecorator()
    {
        using var factory = new ApiGeminiHostFactory();
        using var scope = factory.Services.CreateScope();

        var chat = scope.ServiceProvider.GetRequiredService<IChatClient>();

        var retrying = Assert.IsType<RetryingChatClient>(chat);
        var inner = Unwrap(retrying);
        Assert.Equal("Microsoft.Extensions.AI.OpenAIChatClient", inner.GetType().FullName);
    }

    [Fact]
    public void Api_Host_GeminiProvider_WithoutApiKey_ThrowsInvalidOperationAtHostBuild()
    {
        // "Absent" is simulated with an explicit empty override; production
        // treats empty/whitespace keys as missing (fail-fast contract).
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var factory = new ApiGeminiHostFactory(apiKey: "");
            _ = factory.Services; // force host creation
        });

        var invalid = FindInChain<InvalidOperationException>(ex);
        Assert.NotNull(invalid);
        Assert.Contains("AI:Gemini:ApiKey", invalid.Message);
    }

    [Fact]
    public void Api_Host_OllamaProvider_StillDefault_WrapsOllamaChatClient()
    {
        using var factory = new ApiDefaultOllamaHostFactory();
        using var scope = factory.Services.CreateScope();

        var chat = scope.ServiceProvider.GetRequiredService<IChatClient>();

        // Approval guard for the switch refactor: the API host's default path
        // must keep the exact pre-change wiring (RetryingChatClient over Ollama).
        var retrying = Assert.IsType<RetryingChatClient>(chat);
        var inner = Unwrap(retrying);
        Assert.Equal("Microsoft.Extensions.AI.OllamaChatClient", inner.GetType().FullName);
    }

    private static IChatClient Unwrap(RetryingChatClient retrying)
    {
        var field = typeof(RetryingChatClient).GetField(
            "_inner", BindingFlags.NonPublic | BindingFlags.Instance);
        return (IChatClient)field!.GetValue(retrying)!;
    }

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
/// Host factory booting the REAL API host with <c>AI:Provider=Gemini</c> (no
/// AI-service stubs), with a fake key injected via UseSetting and a fake
/// PostgreSQL connection string so the lazy vector store can never touch a real
/// database.
/// </summary>
public sealed class ApiGeminiHostFactory(string? apiKey = "test-fake-key")
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AI:Provider", "Gemini");
        if (apiKey is not null)
        {
            builder.UseSetting("AI:Gemini:ApiKey", apiKey);
        }

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            });
        });
    }
}

/// <summary>
/// Default-provider factory for the API host: no <c>AI:Provider</c> set, so
/// <c>RAG.Api.Program</c> falls back to Ollama (pre-change behavior).
/// </summary>
public sealed class ApiDefaultOllamaHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            });
        });
    }
}