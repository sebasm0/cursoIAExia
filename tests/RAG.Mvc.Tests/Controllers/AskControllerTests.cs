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
        // so we can pass null for the service.
        var controller = new AskController(null!, Mock.Of<ILogger<AskController>>());
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
        Assert.Contains("question", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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

        var formData = new Dictionary<string, string>
        {
            { "Query", "What is the capital of France?" }
        };

        using var content = new FormUrlEncodedContent(formData);

        // Act
        var response = await client.PostAsync("/Ask/Ask", content);

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Answer", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paris", body, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Custom WebApplicationFactory that stubs AI and infrastructure services
/// so integration tests can run without Ollama, PostgreSQL, etc.
/// </summary>
public class CustomRagWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real AI client registrations
            RemoveService<IChatClient>(services);
            RemoveService<IEmbeddingGenerator<string, Embedding<float>>>(services);

            // Add stubbed AI clients
            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IList<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "The capital of France is Paris.")));

            var mockEmbedding = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
            mockEmbedding
                .Setup(g => g.GenerateAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<EmbeddingGenerationOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                    [new Embedding<float>(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]))]));

            services.AddSingleton<IChatClient>(mockChat.Object);
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(mockEmbedding.Object);

            // Stub infrastructure services that need real DB/Ollama
            RemoveService<IVectorStore>(services);

            var mockVectorStore = new Mock<IVectorStore>();
            mockVectorStore
                .Setup(v => v.HybridSearchAsync(
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            services.AddSingleton<IVectorStore>(mockVectorStore.Object);

            RemoveService<IReranker>(services);

            var mockReranker = new Mock<IReranker>();
            mockReranker
                .Setup(r => r.RerankAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<SearchResult>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            services.AddSingleton<IReranker>(mockReranker.Object);
        });
    }

    private static void RemoveService<T>(IServiceCollection services) where T : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
            services.Remove(descriptor);
    }
}
