using RAG.Application.Services;
using Xunit;

namespace RAG.Mvc.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AssistantCatalog"/> (spec assistant-selection
/// ASEL-1: config-driven assistant catalog with default fallback; ASEL-2:
/// per-request routing resolution known/blank/unknown at the catalog boundary).
/// </summary>
public class AssistantCatalogTests
{
    // ── ASEL-1: absent or empty catalog exposes a single default ──

    [Fact]
    public void CatalogAbsent_SingleDefaultDerivedFromChatModel()
    {
        var catalog = new AssistantCatalog("phi3:mini", null);

        Assert.Single(catalog.All);
        Assert.Equal("default", catalog.Default.Id);
        Assert.Equal("phi3:mini", catalog.Default.Model);
        Assert.Same(catalog.Default, catalog.All[0]);
    }

    [Fact]
    public void CatalogEmpty_SingleDefaultDerivedFromChatModel()
    {
        var catalog = new AssistantCatalog("phi3:mini", []);

        Assert.Single(catalog.All);
        Assert.Equal("default", catalog.Default.Id);
        Assert.Equal("phi3:mini", catalog.Default.Model);
    }

    // ── ASEL-1: configured catalog exposes every entry with full metadata ──

    [Fact]
    public void CatalogConfigured_ExposesEveryEntryWithFullMetadata()
    {
        var catalog = new AssistantCatalog("phi3:mini",
        [
            new AssistantDefinition("default", "Phi3 Mini", "phi3:mini", "Balanced quality and speed"),
            new AssistantDefinition("fast", "Qwen 1.5B", "qwen2.5:1.5b", "Fast answers"),
            new AssistantDefinition("tiny", "Llama 1B", "llama3.2:1b", "Fastest answers"),
        ]);

        Assert.Equal(3, catalog.All.Count);
        Assert.Equal("default", catalog.All[0].Id);
        Assert.Equal("Phi3 Mini", catalog.All[0].Label);
        Assert.Equal("phi3:mini", catalog.All[0].Model);
        Assert.Equal("Balanced quality and speed", catalog.All[0].Description);
        Assert.Equal("fast", catalog.All[1].Id);
        Assert.Equal("qwen2.5:1.5b", catalog.All[1].Model);
        Assert.Equal("tiny", catalog.All[2].Id);
        Assert.Equal("llama3.2:1b", catalog.All[2].Model);
    }

    [Fact]
    public void CatalogConfigured_DefaultPrefersEntryMatchingChatModel()
    {
        // The entry matching the host chat model stays the default, preserving
        // current behavior when the catalog is configured (proposal: "existing
        // ChatModel stays the default").
        var catalog = new AssistantCatalog("phi3:mini",
        [
            new AssistantDefinition("fast", "Qwen 1.5B", "qwen2.5:1.5b", "Fast answers"),
            new AssistantDefinition("default", "Phi3 Mini", "phi3:mini", "Balanced quality and speed"),
            new AssistantDefinition("tiny", "Llama 1B", "llama3.2:1b", "Fastest answers"),
        ]);

        Assert.Equal("default", catalog.Default.Id);
        Assert.Equal("phi3:mini", catalog.Default.Model);
    }

    // ── ASEL-2: Resolve known / blank / unknown ──

    [Fact]
    public void Resolve_KnownId_ReturnsMatchingAssistant()
    {
        var catalog = CatalogWithThreeAssistants();

        var resolved = catalog.Resolve("fast");

        Assert.Equal("fast", resolved.Id);
        Assert.Equal("qwen2.5:1.5b", resolved.Model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NullOrBlank_ReturnsDefault(string? modelId)
    {
        var catalog = CatalogWithThreeAssistants();

        var resolved = catalog.Resolve(modelId);

        Assert.Same(catalog.Default, resolved);
        Assert.Equal("phi3:mini", resolved.Model);
    }

    [Fact]
    public void Resolve_UnknownId_FallsBackToDefault()
    {
        var catalog = CatalogWithThreeAssistants();

        var resolved = catalog.Resolve("tampered-model");

        Assert.Same(catalog.Default, resolved);
        Assert.Equal("phi3:mini", resolved.Model);
    }

    // ── D1/D3/D4: TryResolve used by the HTTP boundaries ──

    [Fact]
    public void TryResolve_KnownId_ReturnsTrueWithMatchingAssistant()
    {
        var catalog = CatalogWithThreeAssistants();

        var found = catalog.TryResolve("tiny", out var assistant);

        Assert.True(found);
        Assert.Equal("tiny", assistant.Id);
        Assert.Equal("llama3.2:1b", assistant.Model);
    }

    [Fact]
    public void TryResolve_UnknownOrBlank_ReturnsTrueWithDefault()
    {
        var catalog = CatalogWithThreeAssistants();

        var fromUnknown = catalog.TryResolve("not-in-catalog", out var unknownResolved);
        var fromBlank = catalog.TryResolve("  ", out var blankResolved);

        Assert.True(fromUnknown);
        Assert.Same(catalog.Default, unknownResolved);
        Assert.True(fromBlank);
        Assert.Same(catalog.Default, blankResolved);
    }

    private static AssistantCatalog CatalogWithThreeAssistants() => new(
        "phi3:mini",
        [
            new AssistantDefinition("default", "Phi3 Mini", "phi3:mini", "Balanced quality and speed"),
            new AssistantDefinition("fast", "Qwen 1.5B", "qwen2.5:1.5b", "Fast answers"),
            new AssistantDefinition("tiny", "Llama 1B", "llama3.2:1b", "Fastest answers"),
        ]);
}
