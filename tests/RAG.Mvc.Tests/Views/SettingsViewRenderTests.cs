using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using RAG.Mvc.Tests.Auth;
using Xunit;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// View-render tests for the Settings page (/Home/Settings, public): the page
/// lists every assistant from the catalog (label, model, description) instead
/// of a single chat model, while keeping the remaining configuration rows.
/// Runs over the real WebApplicationFactory pipeline with the production
/// appsettings catalog (default/fast/tiny), no database touched.
/// </summary>
public class SettingsViewRenderTests
{
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Settings_Page_RendersEveryCatalogAssistant_WithLabelModelAndDescription()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Home/Settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Razor HTML-encodes dynamic content; decode to assert the user-visible copy.
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // ASEL-1: every catalog assistant renders label + model + description.
        Assert.Contains("Phi3 Mini", body);
        Assert.Contains("Equilibrio entre calidad y velocidad", body);
        Assert.Contains("phi3:mini", body);

        Assert.Contains("Qwen 2.5 1.5B", body);
        Assert.Contains("Más rápido manteniendo buena calidad", body);
        Assert.Contains("qwen2.5:1.5b", body);

        Assert.Contains("Llama 3.2 1B", body);
        Assert.Contains("La opción más rápida", body);
        Assert.Contains("llama3.2:1b", body);
    }

    [Fact]
    public async Task Settings_Page_ReplacesSingleChatModelRowWithCatalogSection()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var body = await (await client.GetAsync("/Home/Settings")).Content.ReadAsStringAsync();

        // The single "Modelo de chat" row is gone; a catalog section header exists.
        Assert.DoesNotContain("Modelo de chat", body);
        Assert.Contains("Asistentes de chat", body);
    }

    [Fact]
    public async Task Settings_Page_KeepsRemainingConfigurationRows()
    {
        await using var factory = new AnonymousWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Home/Settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // The other configuration rows are preserved with their active values.
        Assert.Contains("Proveedor de IA", body);
        Assert.Contains("Ollama", body);
        Assert.Contains("URL de Ollama", body);
        Assert.Contains("http://localhost:11434", body);
        Assert.Contains("Modelo de embeddings", body);
        Assert.Contains("nomic-embed-text", body);
        Assert.Contains("Tamaño máximo de archivo", body);
        Assert.Contains("10 MB", body);
    }
}