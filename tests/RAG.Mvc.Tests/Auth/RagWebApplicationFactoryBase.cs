using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;

namespace RAG.Mvc.Tests.Auth;

/// <summary>
/// Base <c>WebApplicationFactory</c> for the RAG MVC app: stubs the AI and
/// infrastructure services and disables Identity startup migrate/seed so tests
/// never touch a real PostgreSQL database (design D2/D3).
/// </summary>
public abstract class RagWebApplicationFactoryBase : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting flows into the host configuration BEFORE the entry point
        // runs (unlike ConfigureAppConfiguration, which applies after Program.cs
        // already read builder.Configuration). Pinning the provider here keeps
        // the WAF deterministic even when dev user-secrets select Gemini.
        builder.UseSetting("AI:Provider", "Ollama");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never migrate + seed a real database during tests (design D2).
                ["Identity:ApplyMigrationsOnStartup"] = "false",
                ["ConnectionStrings:PostgreSQL"] =
                    "Host=localhost;Database=rag_tests;Username=postgres;Password=__SECRET__",
            });
        });

        builder.ConfigureServices(services =>
        {
            // ── Stub AI clients ──
            RemoveService<IChatClient>(services);
            RemoveService<IEmbeddingGenerator<string, Embedding<float>>>(services);

            // Default embedding (single vector) serves both the Ask query path
            // and the Upload per-chunk path. Ask subclasses override the chat
            // client with the canned answer they assert on.
            var mockEmbedding = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
            mockEmbedding
                .Setup(g => g.GenerateAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<EmbeddingGenerationOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                    [new Embedding<float>(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]))]));

            services.AddSingleton<IChatClient>(Mock.Of<IChatClient>());
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(mockEmbedding.Object);

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
                    It.IsAny<string?>(),
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

    protected static void RemoveServices<T>(IServiceCollection services) where T : class
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
