using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using rag.Models;
using RAG.Domain.Abstractions;
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using Xunit;

namespace RAG.Mvc.Tests.Controllers;

/// <summary>
/// Integration tests for the per-user chat history endpoints (spec CH-5/CH-6/CH-7):
/// GET /Ask/History returns the caller's last 50 ascending; POST /Ask/History
/// validates + persists (JSON body, antiforgery header, 201 {id,createdAt} |
/// 400 {error} | 401); per-user isolation proven by two factories sharing ONE
/// in-memory store. The endpoints stay dormant until the frontend slice lands.
/// </summary>
public class ChatHistoryControllerTests
{
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>JSON POST with the antiforgery token as the RequestVerificationToken header (D9).</summary>
    private static HttpRequestMessage CreateJsonPost(string url, string antiforgeryToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", antiforgeryToken);
        return request;
    }

    private static async Task<(Guid Id, DateTime CreatedAt)> PostMessageAsync(
        HttpClient client, string token, string role, string content,
        string? modelId = null, object? sources = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["role"] = role,
            ["content"] = content,
            ["modelId"] = modelId,
            ["sources"] = sources,
        };

        var response = await client.SendAsync(CreateJsonPost("/Ask/History", token, body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var root = await ReadJsonAsync(response);
        return (root.GetProperty("id").GetGuid(), root.GetProperty("createdAt").GetDateTime());
    }

    private static async Task<List<JsonElement>> GetHistoryAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Ask/History");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadJsonAsync(response);
        return root.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    // ── CH-5: GET returns 200 with an empty array for a fresh user ──

    [Fact]
    public async Task History_Get_EmptyHistory_Returns200EmptyArray()
    {
        await using var factory = new ChatHistoryTestWebApplicationFactory(new InMemoryChatHistoryStore(), UserA.ToString(), "userA");
        using var client = factory.CreateClient();

        var history = await GetHistoryAsync(client);

        Assert.Empty(history);
    }

    // ── CH-5/CH-6: valid POST → 201 {id,createdAt}, then GET returns the item ──

    [Fact]
    public async Task History_Post_ValidMessage_Returns201WithIdAndCreatedAt_ThenGetReturnsIt()
    {
        await using var factory = new ChatHistoryTestWebApplicationFactory(new InMemoryChatHistoryStore(), UserA.ToString(), "userA");
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Act — a user message with model credit + one source, as the frontend sends it.
        var (id, createdAt) = await PostMessageAsync(client, token, "user", "  Hola, mundo.  ",
            sources: new[]
            {
                new { fileName = "francia.pdf", snippet = "Paris es la capital.", page = 3 },
            });

        Assert.NotEqual(Guid.Empty, id);
        Assert.True(createdAt > DateTime.MinValue);

        // GET now returns exactly that message, ascending, with the full CH-5 shape.
        var history = await GetHistoryAsync(client);
        var item = Assert.Single(history);
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("user", item.GetProperty("role").GetString());
        Assert.Equal("Hola, mundo.", item.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("modelId").ValueKind);

        var sources = item.GetProperty("sources");
        Assert.Equal(1, sources.GetArrayLength());
        Assert.Equal("francia.pdf", sources[0].GetProperty("fileName").GetString());
        Assert.Equal("Paris es la capital.", sources[0].GetProperty("snippet").GetString());
        Assert.Equal(3, sources[0].GetProperty("page").GetInt32());
    }

    // ── CH-6: invalid role / empty content → 400, store untouched ──

    [Fact]
    public async Task History_Post_InvalidRole_Returns400AndNothingPersisted()
    {
        var store = new InMemoryChatHistoryStore();
        await using var factory = new ChatHistoryTestWebApplicationFactory(store, UserA.ToString(), "userA");
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        var response = await client.SendAsync(CreateJsonPost("/Ask/History", token,
            new { role = "system", content = "Hola" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(root.GetProperty("error").GetString()));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task History_Post_EmptyContent_Returns400AndNothingPersisted()
    {
        var store = new InMemoryChatHistoryStore();
        await using var factory = new ChatHistoryTestWebApplicationFactory(store, UserA.ToString(), "userA");
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        var response = await client.SendAsync(CreateJsonPost("/Ask/History", token,
            new { role = "user", content = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.Count);
    }

    // ── CH-6: POST without antiforgery token → 400 before validation/persistence ──

    [Fact]
    public async Task History_Post_WithoutAntiforgeryToken_Returns400AndNothingPersisted()
    {
        var store = new InMemoryChatHistoryStore();
        await using var factory = new ChatHistoryTestWebApplicationFactory(store, UserA.ToString(), "userA");
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/Ask/History")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { role = "user", content = "Hola" }),
                Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.Count);
    }

    // ── CH-6: NameIdentifier that is not a Guid → 401 on both GET and POST ──
    // Gotcha: the TestAuthHandler default UserId is "test-user-id", which is not
    // a Guid — these factories intentionally keep it to prove the 401 path.

    [Fact]
    public async Task History_NonGuidNameIdentifier_Returns401()
    {
        await using var factory = new ChatHistoryTestWebApplicationFactory(new InMemoryChatHistoryStore());
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/Ask/History");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");
        var postResponse = await client.SendAsync(CreateJsonPost("/Ask/History", token,
            new { role = "user", content = "Hola" }));
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    // ── CH-7: two users, one shared store — each sees only its own messages ──

    [Fact]
    public async Task History_TwoUsers_AreIsolated()
    {
        var store = new InMemoryChatHistoryStore();
        await using var factoryA = new ChatHistoryTestWebApplicationFactory(store, UserA.ToString(), "userA");
        await using var factoryB = new ChatHistoryTestWebApplicationFactory(store, UserB.ToString(), "userB");
        using var clientA = factoryA.CreateClient();
        using var clientB = factoryB.CreateClient();

        var tokenA = await AccountTestHelpers.GetAntiforgeryTokenAsync(clientA, "/Ask");
        var tokenB = await AccountTestHelpers.GetAntiforgeryTokenAsync(clientB, "/Ask");

        var (idA1, _) = await PostMessageAsync(clientA, tokenA, "user", "Hola de A");
        var (idB, _) = await PostMessageAsync(clientB, tokenB, "assistant", "Respuesta de B");
        var (idA2, _) = await PostMessageAsync(clientA, tokenA, "assistant", "Respuesta de A");

        // A sees only A's two messages, ascending, none of B's.
        var historyA = await GetHistoryAsync(clientA);
        Assert.Equal(2, historyA.Count);
        Assert.Equal("Hola de A", historyA[0].GetProperty("content").GetString());
        Assert.Equal("Respuesta de A", historyA[1].GetProperty("content").GetString());
        var idsA = historyA.Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(idA1, idsA);
        Assert.Contains(idA2, idsA);
        Assert.DoesNotContain(idB, idsA);

        // B sees only B's single message.
        var historyB = await GetHistoryAsync(clientB);
        var itemB = Assert.Single(historyB);
        Assert.Equal("Respuesta de B", itemB.GetProperty("content").GetString());
        Assert.Equal(idB, itemB.GetProperty("id").GetGuid());
        Assert.DoesNotContain(itemB.GetProperty("id").GetGuid(), idsA);
    }

    // ── R2-001 correction: empty/null JSON body → 400, nothing persisted ──

    [Fact]
    public async Task History_Post_NullBody_Returns400AndNothingPersisted()
    {
        var store = new InMemoryChatHistoryStore();
        await using var factory = new ChatHistoryTestWebApplicationFactory(store, UserA.ToString(), "userA");
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");

        // Literal JSON null binds to a null request on a plain Controller (no
        // [ApiController]) — must be 400, not a 500 NullReferenceException.
        var request = new HttpRequestMessage(HttpMethod.Post, "/Ask/History")
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(root.GetProperty("error").GetString()));
        Assert.Equal(0, store.Count);
    }

    // ── R2-001 correction: store failure degrades to 502 JSON, not a raw 500 ──

    [Fact]
    public async Task History_StoreFailure_Returns502Json()
    {
        var store = new ThrowingChatHistoryStore();
        await using var factory = new ChatHistoryTestWebApplicationFactory(store, UserA.ToString(), "userA");
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/Ask/History");
        Assert.Equal(HttpStatusCode.BadGateway, getResponse.StatusCode);
        var getRoot = await ReadJsonAsync(getResponse);
        Assert.False(string.IsNullOrEmpty(getRoot.GetProperty("error").GetString()));

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Ask");
        var postResponse = await client.SendAsync(CreateJsonPost("/Ask/History", token,
            new { role = "user", content = "Hola" }));
        Assert.Equal(HttpStatusCode.BadGateway, postResponse.StatusCode);
        var postRoot = await ReadJsonAsync(postResponse);
        Assert.False(string.IsNullOrEmpty(postRoot.GetProperty("error").GetString()));
    }
}

/// <summary>
/// Factory for the chat history endpoint tests: authenticates with the test
/// handler using an explicit Guid NameIdentifier (the legacy default
/// "test-user-id" is not a Guid and would 401) and swaps the lazy real
/// PgChatHistoryStore for a shared in-memory fake (CH-7).
/// </summary>
public sealed class ChatHistoryTestWebApplicationFactory : RagWebApplicationFactoryBase
{
    private readonly IChatHistoryStore _store;
    private readonly string? _userId;
    private readonly string? _userName;

    public ChatHistoryTestWebApplicationFactory(IChatHistoryStore store, string? userId = null, string? userName = null)
    {
        _store = store;
        _userId = userId;
        _userName = userName;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication(
                [Permissions.RagAsk], [], userId: _userId, userName: _userName);

            // The real PgChatHistoryStore is lazy but would try to reach
            // PostgreSQL on first use; the shared fake keeps tests DB-free and
            // lets two factories prove isolation against one instance (CH-7).
            RemoveService<IChatHistoryStore>(services);
            services.AddSingleton(_store);
        });
    }
}