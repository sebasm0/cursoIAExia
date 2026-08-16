using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using rag.Controllers;
using rag.Models;
using RAG.Application.Services;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;
using Microsoft.Extensions.AI;
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using RAG.Mvc.Tests.Views;
using Xunit;

namespace RAG.Mvc.Tests.Controllers;

/// <summary>
/// 5.1 Unit: POST Ask empty query returns validation error.
/// 5.4 Integration: POST Ask valid question renders answer.
/// </summary>
public class AskControllerTests
{
    /// <summary>
    /// Parses the response body as JSON and returns a detached root element, so
    /// the <see cref="JsonDocument"/> is disposed but the assertions keep a
    /// usable snapshot of the payload.
    /// </summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    // ── 5.1 Unit: Empty query returns validation error ──

    [Fact]
    public async Task Ask_Post_EmptyQuery_ReturnsViewWithValidationError()
    {
        // Arrange — empty query hits ModelState check BEFORE service call,
        // so we can pass null for the service. The catalog is real (design D3).
        var controller = new AskController(
            null!,
            Mock.Of<ILogger<AskController>>(),
            new AssistantCatalog("phi3:mini", null));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var model = new AskViewModel { Query = "   " };

        // Act
        var result = await controller.Ask(model, CancellationToken.None);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);

        Assert.False(controller.ModelState.IsValid);
        var error = Assert.Single(controller.ModelState["Query"]?.Errors ?? []);
        Assert.Contains("pregunta", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5.4 Integration: Valid question renders answer ──

    /// <summary>
    /// Full integration test using WebApplicationFactory with stubbed AI + infrastructure services.
    /// </summary>
    [Fact]
    public async Task Ask_Post_ValidQuestion_ReturnsResultViewWithAnswer()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        // Harness the token the form tag helper embeds, then submit with it (ASK-12).
        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token, ("Query", "What is the capital of France?")));

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Respuesta", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paris", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── ASK-12: POST without antiforgery token is rejected ──

    [Fact]
    public async Task Ask_Post_WithoutToken_ReturnsBadRequest()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var formData = new Dictionary<string, string>
        {
            { "Query", "What is the capital of France?" }
        };

        using var content = new FormUrlEncodedContent(formData);

        // Act — ASK-12: no __RequestVerificationToken in the body.
        var response = await client.PostAsync("/Ask/Ask", content);

        // Assert — the server must reject with HTTP 400 BEFORE any pipeline call.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── ASK-15 / ASEL-9: POST with a catalog assistant attributes the answer ──

    [Fact]
    public async Task Ask_Post_SelectedAssistant_RendersResultAttributedToThatAssistant()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — select the "fast" assistant (qwen2.5:1.5b) on the form.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "fast")));

        // Assert — the answer renders attributed to the selected assistant.
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Paris", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Generado por Qwen 2.5 1.5B", body);
    }

    // ── ASEL-2/ASEL-4: invalid selection falls back to default without error ──

    [Fact]
    public async Task Ask_Post_InvalidSelectedModelId_FallsBackToDefaultWithoutError()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — tampered model id outside the catalog allow-list.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "not-in-catalog")));

        // Assert — the request completes and the default assistant is attributed.
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Paris", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Generado por Phi3 Mini", body);
    }

    // ── ASEL-2: blank selection uses the default assistant ──

    [Fact]
    public async Task Ask_Post_BlankSelectedModelId_UsesDefaultWithoutError()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — no SelectedModelId submitted (existing clients, ASK-2 regression).
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/Ask", token, ("Query", "What is the capital of France?")));

        // Assert — the request completes and the default assistant is attributed.
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Paris", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Generado por Phi3 Mini", body);
    }

    // ── DocsChat-1: AskJson JSON endpoint for the Documents floating chat ──
    // The floating chat in Documents/Index posts over fetch to this endpoint
    // and renders the JSON answer in place, instead of navigating to the
    // Ask/Result view. Contract: 200 { answer, usedModel } | 400 { error } |
    // 502 { error } — always application/json, never a view.

    [Fact]
    public async Task AskJson_Post_ValidQuery_ReturnsJsonAnswerAndUsedModel()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — select the "fast" assistant (qwen2.5:1.5b) on the form.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskJson", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "fast")));

        // Assert — a JSON payload (not a view) with the answer and attribution.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ReadJsonAsync(response);
        Assert.Equal("The capital of France is Paris.", root.GetProperty("answer").GetString());
        Assert.Equal("Qwen 2.5 1.5B", root.GetProperty("usedModel").GetString());
    }

    [Fact]
    public async Task AskJson_Post_UnknownModelId_FallsBackToDefaultAssistant()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — tampered model id outside the catalog allow-list (ASEL-4).
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskJson", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "not-in-catalog")));

        // Assert — resolves to the default assistant without error.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadJsonAsync(response);
        Assert.Equal("Phi3 Mini", root.GetProperty("usedModel").GetString());
    }

    [Fact]
    public async Task AskJson_Post_BlankModelId_UsesDefaultAssistant()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — no SelectedModelId submitted (existing clients, ASK-2 regression).
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskJson", token, ("Query", "What is the capital of France?")));

        // Assert — blank selection uses the default assistant.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadJsonAsync(response);
        Assert.Equal("Phi3 Mini", root.GetProperty("usedModel").GetString());
    }

    [Fact]
    public async Task AskJson_Post_EmptyQuery_ReturnsBadRequestJsonError()
    {
        await using var factory = new CustomRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — blank query hits the guard BEFORE any pipeline call.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskJson", token, ("Query", "   ")));

        // Assert — HTTP 400 with a JSON error, not a re-rendered view.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ReadJsonAsync(response);
        var error = root.GetProperty("error").GetString();
        Assert.Contains("pregunta", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskJson_Post_RagFailure_ReturnsBadGatewayJsonError()
    {
        await using var factory = new FailingRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — the chat client throws; RagService.AskAsync fails.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskJson", token, ("Query", "Is the service up?")));

        // Assert — HTTP 502 with a JSON error; no error view, no stack trace.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ReadJsonAsync(response);
        var error = root.GetProperty("error").GetString();
        Assert.Contains("no disponible", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── DocsChat-3: AskStream SSE streaming endpoint for the Documents chat ──
    // Wire contract: Content-Type text/event-stream; one data event per delta
    // ({"delta":"text"}), a terminal {"done":true,"usedModel":"label"} event, and
    // a terminal {"error":"message"} event when the pipeline fails mid-stream.
    // An empty query is rejected with 400 JSON BEFORE the stream starts.

    /// <summary>
    /// Parses an SSE body into its <c>data:</c> payloads (one JSON string per
    /// event), ignoring blank heartbeats.
    /// </summary>
    private static async Task<List<string>> ReadSseEventsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var events = new List<string>();
        foreach (var block in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var data = string.Join("\n", block.Split('\n')
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line["data:".Length..].Trim()));
            if (data.Length > 0)
            {
                events.Add(data);
            }
        }

        return events;
    }

    [Fact]
    public async Task AskStream_Post_ValidQuery_StreamsDeltasThenDoneEvent()
    {
        await using var factory = new StreamingRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — select the "fast" assistant (qwen2.5:1.5b) on the form.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskStream", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "fast")));

        // Assert — an event stream (never a view) carrying the streamed answer.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = await ReadSseEventsAsync(response);

        // The deltas arrive in order and concatenate to the full answer.
        var deltaEvents = events.Where(e => e.Contains("\"delta\"")).ToList();
        Assert.Equal(2, deltaEvents.Count);
        var answer = string.Concat(deltaEvents.Select(e =>
            JsonDocument.Parse(e).RootElement.GetProperty("delta").GetString()));
        Assert.Equal("The capital of France is Paris.", answer);

        // The stream terminates with the done event carrying the attribution.
        var doneEvent = Assert.Single(events.Where(e => e.Contains("\"done\"")));
        var done = JsonDocument.Parse(doneEvent).RootElement;
        Assert.True(done.GetProperty("done").GetBoolean());
        Assert.Equal("Qwen 2.5 1.5B", done.GetProperty("usedModel").GetString());
    }

    [Fact]
    public async Task AskStream_Post_EmptyQuery_ReturnsBadRequestJsonError()
    {
        await using var factory = new StreamingRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — blank query hits the guard BEFORE any stream starts.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskStream", token, ("Query", "   ")));

        // Assert — HTTP 400 with a JSON error, never an event stream.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ReadJsonAsync(response);
        var error = root.GetProperty("error").GetString();
        Assert.Contains("pregunta", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskStream_Post_UnknownModelId_DoneEventUsesDefaultAssistant()
    {
        await using var factory = new StreamingRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — tampered model id outside the catalog allow-list (ASEL-4).
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskStream", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "not-in-catalog")));

        // Assert — resolves to the default assistant and attributes it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await ReadSseEventsAsync(response);
        var doneEvent = Assert.Single(events.Where(e => e.Contains("\"done\"")));
        var done = JsonDocument.Parse(doneEvent).RootElement;
        Assert.Equal("Phi3 Mini", done.GetProperty("usedModel").GetString());
    }

    [Fact]
    public async Task AskStream_Post_RagFailure_StreamsErrorEventAndCloses()
    {
        await using var factory = new FailingStreamingRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — the streaming chat client throws after the first delta.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskStream", token, ("Query", "Is the service up?")));

        // Assert — the stream stays text/event-stream and terminates with an
        // error event; the page never navigates and no stack trace leaks.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = await ReadSseEventsAsync(response);
        var errorEvent = Assert.Single(events.Where(e => e.Contains("\"error\"")));
        var error = JsonDocument.Parse(errorEvent).RootElement.GetProperty("error").GetString();
        Assert.Contains("no disponible", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", error);
    }

    // ── DocsChat-4: AskJson/AskStream expose the sources that backed the ──
    // answer. The sources array is ADDITIVE: answer/usedModel/delta/done keep
    // their existing wire contract untouched, and the fragments arrive in
    // rerank order with the file name from the chunk's "source" metadata.

    [Fact]
    public async Task AskJson_Post_ValidQuery_ReturnsSourcesArray()
    {
        await using var factory = new SourcesRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — select the "fast" assistant (qwen2.5:1.5b) on the form.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskJson", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "fast")));

        // Assert — additive contract: the pre-existing fields survive, and the
        // sources array mirrors the reranked fragments with their file names.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var root = await ReadJsonAsync(response);
        Assert.Equal("The capital of France is Paris.", root.GetProperty("answer").GetString());
        Assert.Equal("Qwen 2.5 1.5B", root.GetProperty("usedModel").GetString());

        var sources = root.GetProperty("sources");
        Assert.Equal(2, sources.GetArrayLength());
        Assert.Equal("francia.pdf", sources[0].GetProperty("fileName").GetString());
        Assert.Equal("Paris is the capital of France.", sources[0].GetProperty("snippet").GetString());
        Assert.Equal("espana.pdf", sources[1].GetProperty("fileName").GetString());
        Assert.Equal("France borders Spain.", sources[1].GetProperty("snippet").GetString());
    }

    [Fact]
    public async Task AskStream_Post_ValidQuery_DoneEventCarriesSources()
    {
        await using var factory = new StreamingSourcesRagWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — select the "fast" assistant (qwen2.5:1.5b) on the form.
        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskStream", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "fast")));

        // Assert — the stream keeps its delta contract...
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = await ReadSseEventsAsync(response);

        var deltaEvents = events.Where(e => e.Contains("\"delta\"")).ToList();
        Assert.Equal(2, deltaEvents.Count);
        var answer = string.Concat(deltaEvents.Select(e =>
            JsonDocument.Parse(e).RootElement.GetProperty("delta").GetString()));
        Assert.Equal("The capital of France is Paris.", answer);

        // ...and the terminal done event now also carries the sources used.
        var doneEvent = Assert.Single(events.Where(e => e.Contains("\"done\"")));
        var done = JsonDocument.Parse(doneEvent).RootElement;
        Assert.True(done.GetProperty("done").GetBoolean());
        Assert.Equal("Qwen 2.5 1.5B", done.GetProperty("usedModel").GetString());

        var sources = done.GetProperty("sources");
        Assert.Equal(2, sources.GetArrayLength());
        Assert.Equal("Paris is the capital of France.", sources[0].GetProperty("snippet").GetString());
        Assert.Equal("France borders Spain.", sources[1].GetProperty("snippet").GetString());
    }

    // ── DocsChat-4 security gate: raw document fragments only travel to ──
    // principals holding documents.view. A user with just rag.ask (e.g. the
    // seeded Viewer role) still gets the answer, but sources degrade to [].

    [Fact]
    public async Task AskJson_Post_WithoutDocumentsView_ReturnsEmptySources()
    {
        await using var factory = new SourcesRagWebApplicationFactory([Permissions.RagAsk]);
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskJson", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "fast")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadJsonAsync(response);
        // The answer still flows...
        Assert.Equal("The capital of France is Paris.", root.GetProperty("answer").GetString());
        // ...but the raw fragments are withheld without documents.view.
        var sources = root.GetProperty("sources");
        Assert.Equal(0, sources.GetArrayLength());
    }

    [Fact]
    public async Task AskStream_Post_WithoutDocumentsView_DoneEventHasEmptySources()
    {
        await using var factory = new StreamingSourcesRagWebApplicationFactory([Permissions.RagAsk]);
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        var response = await client.SendAsync(AccountTestHelpers.CreatePost(
            "/Ask/AskStream", token,
            ("Query", "What is the capital of France?"),
            ("SelectedModelId", "fast")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await ReadSseEventsAsync(response);

        // Streaming contract intact...
        var deltaEvents = events.Where(e => e.Contains("\"delta\"")).ToList();
        Assert.Equal(2, deltaEvents.Count);
        var answer = string.Concat(deltaEvents.Select(e =>
            JsonDocument.Parse(e).RootElement.GetProperty("delta").GetString()));
        Assert.Equal("The capital of France is Paris.", answer);

        // ...but the done event withholds the fragments without documents.view.
        var doneEvent = Assert.Single(events.Where(e => e.Contains("\"done\"")));
        var done = JsonDocument.Parse(doneEvent).RootElement;
        var sources = done.GetProperty("sources");
        Assert.Equal(0, sources.GetArrayLength());
    }
}

/// <summary>
/// Custom WebApplicationFactory for the Ask flow: inherits the shared AI +
/// infrastructure stubs from <see cref="RagWebApplicationFactoryBase"/> and
/// overrides only the chat client with the canned answer used by the assertion.
/// </summary>
public class CustomRagWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // The Ask endpoint is gated by rag.ask (ASK-8) — authenticate the
            // integration client with that permission.
            services.AddPolicyTestAuthentication([Permissions.RagAsk], []);

            // The Ask flow calls the chat client last — replace the base stub
            // with the canned answer the test asserts on.
            RemoveService<IChatClient>(services);

            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "The capital of France is Paris.")));

            services.AddSingleton<IChatClient>(mockChat.Object);
        });
    }
}

/// <summary>
/// Factory whose chat client streams the canned answer in deltas, exercising the
/// AskStream SSE endpoint contract (DocsChat-3).
/// </summary>
public class StreamingRagWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // The Ask endpoints are gated by rag.ask (ASK-8) — authenticate the
            // integration client with that permission.
            services.AddPolicyTestAuthentication([Permissions.RagAsk], []);

            RemoveService<IChatClient>(services);

            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(StreamAnswerUpdates());

            services.AddSingleton<IChatClient>(mockChat.Object);
        });
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamAnswerUpdates()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "The capital of ");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "France is Paris.");
    }
}

/// <summary>
/// Factory whose streaming chat client yields one delta and then throws, so the
/// mid-stream RAG failure path of AskStream can be exercised.
/// </summary>
public class FailingStreamingRagWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication([Permissions.RagAsk], []);

            RemoveService<IChatClient>(services);

            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(FailingUpdates());

            services.AddSingleton<IChatClient>(mockChat.Object);
        });
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FailingUpdates()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Partial");
        throw new InvalidOperationException("ollama unavailable");
    }
}

/// <summary>
/// Factory whose vector store/reranker return two known fragments — carrying
/// "source" metadata so the citations expose file names — and whose chat
/// client answers with the canned text the assertions expect (DocsChat-4).
/// Authenticates with <c>documents.view</c> by default (the sources gate only
/// releases raw fragments to principals that may view documents); pass a
/// reduced permission set to exercise the gate itself.
/// </summary>
public class SourcesRagWebApplicationFactory(string[]? permissions = null) : RagWebApplicationFactoryBase
{
    private readonly string[] _permissions =
        permissions ?? [Permissions.RagAsk, Permissions.DocumentsView];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // The Ask endpoints are gated by rag.ask (ASK-8); the sources gate
            // additionally requires documents.view (DocsChat-4 security fix).
            services.AddPolicyTestAuthentication(_permissions, []);

            // DocsChat-4: the sources come from the retrieval results — stub the
            // store and reranker with two known fragments instead of the base
            // empty set, then answer with the canned text.
            RemoveService<IChatClient>(services);
            RemoveService<IVectorStore>(services);
            RemoveService<IReranker>(services);

            var results = new List<SearchResult>
            {
                new()
                {
                    Chunk = new DocumentChunk
                    {
                        Content = "Paris is the capital of France.",
                        Metadata = new() { ["source"] = "francia.pdf" },
                    },
                    RrfScore = 0.9,
                },
                new()
                {
                    Chunk = new DocumentChunk
                    {
                        Content = "France borders Spain.",
                        Metadata = new() { ["source"] = "espana.pdf" },
                    },
                    RrfScore = 0.8,
                },
            };

            var mockVectorStore = new Mock<IVectorStore>();
            mockVectorStore
                .Setup(v => v.HybridSearchAsync(
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(results);
            services.AddSingleton<IVectorStore>(mockVectorStore.Object);

            var mockReranker = new Mock<IReranker>();
            mockReranker
                .Setup(r => r.RerankAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<SearchResult>>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(results);
            services.AddSingleton<IReranker>(mockReranker.Object);

            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "The capital of France is Paris.")));
            services.AddSingleton<IChatClient>(mockChat.Object);
        });
    }
}

/// <summary>
/// Same known fragments as <see cref="SourcesRagWebApplicationFactory"/>, but
/// the chat client streams the canned answer in deltas, exercising the SSE
/// done-event-with-sources contract (DocsChat-4). Authenticates with
/// <c>documents.view</c> by default; pass a reduced permission set to exercise
/// the sources gate on the streaming path.
/// </summary>
public class StreamingSourcesRagWebApplicationFactory(string[]? permissions = null) : RagWebApplicationFactoryBase
{
    private readonly string[] _permissions =
        permissions ?? [Permissions.RagAsk, Permissions.DocumentsView];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication(_permissions, []);

            RemoveService<IChatClient>(services);
            RemoveService<IVectorStore>(services);
            RemoveService<IReranker>(services);

            var results = new List<SearchResult>
            {
                new() { Chunk = new DocumentChunk { Content = "Paris is the capital of France." }, RrfScore = 0.9 },
                new() { Chunk = new DocumentChunk { Content = "France borders Spain." }, RrfScore = 0.8 },
            };

            var mockVectorStore = new Mock<IVectorStore>();
            mockVectorStore
                .Setup(v => v.HybridSearchAsync(
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(results);
            services.AddSingleton<IVectorStore>(mockVectorStore.Object);

            var mockReranker = new Mock<IReranker>();
            mockReranker
                .Setup(r => r.RerankAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<SearchResult>>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(results);
            services.AddSingleton<IReranker>(mockReranker.Object);

            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(StreamAnswerUpdates());

            services.AddSingleton<IChatClient>(mockChat.Object);
        });
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamAnswerUpdates()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "The capital of ");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "France is Paris.");
    }
}
