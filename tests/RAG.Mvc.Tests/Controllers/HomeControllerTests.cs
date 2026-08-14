using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using rag.Controllers;
using rag.Models;
using RAG.Application.Services;
using Xunit;

namespace RAG.Mvc.Tests.Controllers;

/// <summary>
/// Unit tests for <see cref="HomeController.Settings"/>: the Settings page must
/// expose the full multi-assistant catalog (ASEL-1) instead of a single chat
/// model, and the chat-model fallback must match the app bootstrap default
/// (<c>phi3:mini</c>, see Program.cs) rather than an uninstalled model.
/// </summary>
public class HomeControllerTests
{
    private static readonly AssistantDefinition[] TestAssistants =
    [
        new("default", "Phi3 Mini", "phi3:mini", "Equilibrio entre calidad y velocidad"),
        new("fast", "Qwen 2.5 1.5B", "qwen2.5:1.5b", "Más rápido manteniendo buena calidad"),
        new("tiny", "Llama 3.2 1B", "llama3.2:1b", "La opción más rápida"),
    ];

    private static HomeController CreateController(
        IConfiguration configuration,
        AssistantCatalog catalog)
        => new(Mock.Of<ILogger<HomeController>>(), configuration, catalog);

    private static IConfiguration BuildConfiguration(params (string Key, string? Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();

    // ── Settings exposes the full assistant catalog (ASEL-1) ──

    [Fact]
    public void Settings_ExposesEveryCatalogAssistant_WithLabelModelAndDescription()
    {
        var controller = CreateController(
            BuildConfiguration(("AI:Ollama:ChatModel", "phi3:mini")),
            new AssistantCatalog("phi3:mini", TestAssistants));

        var viewResult = Assert.IsType<ViewResult>(controller.Settings());
        var model = Assert.IsType<SettingsViewModel>(viewResult.Model);

        // All three catalog entries reach the view model.
        Assert.Equal(3, model.Assistants.Count);

        // The non-default assistants carry their full presentation data.
        var fast = Assert.Single(model.Assistants, a => a.Id == "fast");
        Assert.Equal("Qwen 2.5 1.5B", fast.Label);
        Assert.Equal("qwen2.5:1.5b", fast.Model);
        Assert.Contains("Más rápido", fast.Description);

        var tiny = Assert.Single(model.Assistants, a => a.Id == "tiny");
        Assert.Equal("Llama 3.2 1B", tiny.Label);
        Assert.Equal("llama3.2:1b", tiny.Model);
        Assert.Contains("La opción más rápida", tiny.Description);

        var def = Assert.Single(model.Assistants, a => a.Id == "default");
        Assert.Equal("Phi3 Mini", def.Label);
    }

    // ── Chat-model fallback matches the bootstrap default, not "llama3.2" ──

    [Fact]
    public void Settings_NoConfiguredChatModel_FallsBackToPhi3Mini()
    {
        var controller = CreateController(
            BuildConfiguration(),
            new AssistantCatalog(null, TestAssistants));

        var viewResult = Assert.IsType<ViewResult>(controller.Settings());
        var model = Assert.IsType<SettingsViewModel>(viewResult.Model);

        // Program.cs falls back to "phi3:mini"; the Settings view must agree.
        Assert.Equal("phi3:mini", model.ChatModel);
    }

    // ── Backward compat: empty catalog exposes the single derived assistant ──

    [Fact]
    public void Settings_EmptyCatalog_ExposesSingleAssistantDerivedFromChatModel()
    {
        var controller = CreateController(
            BuildConfiguration(("AI:Ollama:ChatModel", "phi3:mini")),
            new AssistantCatalog("phi3:mini", null));

        var viewResult = Assert.IsType<ViewResult>(controller.Settings());
        var model = Assert.IsType<SettingsViewModel>(viewResult.Model);

        var single = Assert.Single(model.Assistants);
        Assert.Equal("default", single.Id);
        Assert.Equal("phi3:mini", single.Model);
    }
}