using System.Net;
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
using Xunit;

namespace RAG.Mvc.Tests.Controllers;

/// <summary>
/// 5.1 Unit: POST Ask empty query returns validation error.
/// 5.4 Integration: POST Ask valid question renders answer.
/// </summary>
public class AskControllerTests
{
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
