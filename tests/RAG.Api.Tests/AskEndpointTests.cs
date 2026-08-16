using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RAG.Api.Tests;

/// <summary>
/// POST /api/rag/ask routing (spec assistant-selection ASEL-3/4): the endpoint
/// accepts an optional ModelId, validates it against the catalog allow-list and
/// routes only catalog models to the chat client. Omitted or unknown ids fall
/// back to the default assistant without breaking existing clients.
/// </summary>
public class AskEndpointTests
{
    private const string DefaultModel = "llama3.2";
    private const string FastModel = "qwen2.5:1.5b";

    [Fact]
    public async Task Post_WithoutModelId_ReturnsDefaultAssistantAnswer()
    {
        // Regression: omitting ModelId behaves exactly as before the change
        // (ASEL-3 "POST without ModelId").
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/rag/ask", new { query = "What is the capital of France?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("The capital of France is Paris.", ReadAnswer(body));
        Assert.Equal(DefaultModel, factory.CapturedChatOptions?.ModelId);
    }

    [Fact]
    public async Task Post_WithKnownModelId_RoutesToThatModel()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/rag/ask",
            new { query = "What is the capital of France?", modelId = "fast" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("The capital of France is Paris.", ReadAnswer(body));
        // The known id is routed: the chat client receives qwen2.5:1.5b, NOT the default.
        Assert.Equal(FastModel, factory.CapturedChatOptions?.ModelId);
    }

    [Fact]
    public async Task Post_WithUnknownModelId_FallsBackToDefaultWithoutError()
    {
        // A tampered/unknown id must never reach the chat client (ASEL-4): the
        // default model answers and the response stays 200.
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/rag/ask",
            new { query = "What is the capital of France?", modelId = "not-in-catalog" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("The capital of France is Paris.", ReadAnswer(body));
        Assert.Equal(DefaultModel, factory.CapturedChatOptions?.ModelId);
    }

    [Fact]
    public async Task Post_WithBlankModelId_UsesDefault()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/rag/ask",
            new { query = "What is the capital of France?", modelId = "  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DefaultModel, factory.CapturedChatOptions?.ModelId);
    }

    private static string? ReadAnswer(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("answer", out var answer)
            ? answer.GetString()
            : null;
    }
}
