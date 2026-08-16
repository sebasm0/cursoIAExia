using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RAG.Mvc.Tests.Application;

/// <summary>
/// Host-level timeout configuration for the Ollama HTTP client: the app must
/// register a keyed HttpClient named "ollama" whose timeout comes from
/// AI:Ollama:TimeoutSeconds (default 300s) so long local-model generations do
/// not get cancelled by the .NET default 100s HttpClient timeout.
/// </summary>
public class HostTimeoutTests
{
    [Fact]
    public void Host_RegistersOllamaHttpClient_WithDefault300SecondTimeout()
    {
        using var factory = new TimeoutHostFactory();
        using var scope = factory.Services.CreateScope();

        var client = scope.ServiceProvider.GetKeyedService<HttpClient>("ollama");

        Assert.NotNull(client);
        Assert.Equal(TimeSpan.FromSeconds(300), client.Timeout);
    }

    [Fact]
    public void Host_RegistersOllamaHttpClient_WithConfiguredTimeout()
    {
        using var factory = new TimeoutHostFactory("120");
        using var scope = factory.Services.CreateScope();

        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var raw = config["AI:Ollama:TimeoutSeconds"];

        var client = scope.ServiceProvider.GetKeyedService<HttpClient>("ollama");

        Assert.True(!string.IsNullOrEmpty(raw), $"raw TimeoutSeconds='{raw ?? "<null>"}'");
        Assert.NotNull(client);
        Assert.Equal(TimeSpan.FromSeconds(120), client.Timeout);
    }
}

/// <summary>
/// Minimal host factory: keeps the real AI service registrations (so the
/// keyed ollama HttpClient exists) but disables DB migrate/seed. The optional
/// timeoutSeconds override injects AI:Ollama:TimeoutSeconds.
/// </summary>
public sealed class TimeoutHostFactory(string? timeoutSeconds = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (timeoutSeconds is not null)
        {
            // UseSetting flows into the host configuration BEFORE the entry
            // point runs, unlike ConfigureAppConfiguration which applies after
            // Program.cs already read builder.Configuration.
            builder.UseSetting("AI:Ollama:TimeoutSeconds", timeoutSeconds);
        }

        // Pin the provider so the Ollama keyed HttpClient exists regardless of
        // dev user-secrets selecting Gemini.
        builder.UseSetting("AI:Provider", "Ollama");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Identity:ApplyMigrationsOnStartup"] = "false",
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            };
            config.AddInMemoryCollection(values);
        });
    }
}