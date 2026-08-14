using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RAG.Application.Services;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;

namespace RAG.Api.Tests;

/// <summary>
/// WebApplicationFactory for the RAG API host: stubs the AI and infrastructure
/// services so tests never touch a real Ollama instance or PostgreSQL database
/// (same pattern as the MVC test factory in RAG.Mvc.Tests).
/// </summary>
public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// ChatOptions captured from the last IChatClient call, so tests can assert
    /// which model the routed request actually reached (ASEL-3/8).
    /// </summary>
    public ChatOptions? CapturedChatOptions { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never allow the lazy PgVectorStore to point at a real database.
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            });
        });

        builder.ConfigureServices(services =>
        {
            // ── Stub AI clients ──
            RemoveService<IChatClient>(services);
            RemoveService<IEmbeddingGenerator<string, Embedding<float>>>(services);

            var mockEmbedding = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
            mockEmbedding
                .Setup(g => g.GenerateAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<EmbeddingGenerationOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                    [new Embedding<float>(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]))]));

            // The chat client captures ChatOptions.ModelId per request (ASEL-8).
            // NOTE: the IChatClient interface signature is IEnumerable<ChatMessage>
            // (Microsoft.Extensions.AI 9.7); a typed Moq callback must use the
            // interface type or Moq throws ArgumentException.
            var mockChat = new Mock<IChatClient>();
            mockChat
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                    (_, options, _) => CapturedChatOptions = options)
                .ReturnsAsync(new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, "The capital of France is Paris.")));

            services.AddSingleton<IChatClient>(mockChat.Object);
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(mockEmbedding.Object);

            // ── Assistant catalog: default-only host catalog replaced with a
            //    multi-entry allow-list so known-vs-unknown routing is provable
            //    (ASEL-3/4) even though the API host ships default-only. ──
            RemoveService<AssistantCatalog>(services);
            services.AddSingleton(new AssistantCatalog(
                "llama3.2",
                [
                    new AssistantDefinition("default", "Default", "llama3.2", "Default assistant"),
                    new AssistantDefinition("fast", "Fast", "qwen2.5:1.5b", "Fast assistant"),
                ]));

            // ── Stub infrastructure ──
            RemoveService<IVectorStore>(services);
            RemoveService<IReranker>(services);

            var mockVectorStore = new Mock<IVectorStore>();
            mockVectorStore
                .Setup(v => v.HybridSearchAsync(
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            mockVectorStore
                .Setup(v => v.StoreChunksBatchAsync(
                    It.IsAny<Document>(),
                    It.IsAny<IList<(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding)>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            mockVectorStore
                .Setup(v => v.ListDocumentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Document>());
            mockVectorStore
                .Setup(v => v.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            mockVectorStore
                .Setup(v => v.GetDocumentWithContentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new Document { FileName = "sample.pdf", ContentType = "application/pdf", Size = 3 },
                    new byte[] { 1, 2, 3 }));
            services.AddSingleton<IVectorStore>(mockVectorStore.Object);

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

    protected static void RemoveService<T>(IServiceCollection services) where T : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
    }
}
