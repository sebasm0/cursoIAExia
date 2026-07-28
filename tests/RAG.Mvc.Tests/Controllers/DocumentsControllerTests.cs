using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using rag.Controllers;
using rag.Models;
using RAG.Application.Services;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;
using Xunit;

namespace RAG.Mvc.Tests.Controllers;

/// <summary>
/// 5.2 Unit: POST Upload unsupported file type error.
/// 5.3 Unit: POST Upload 0-byte file error.
/// 5.5 Integration: POST Upload valid file renders success.
/// </summary>
public class DocumentsControllerTests
{
    // ── Helpers ──

    private static DocumentsController CreateController(
        IngestionService? ingestionService = null,
        IConfiguration? configuration = null,
        ILogger<DocumentsController>? logger = null)
    {
        var config = configuration ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentUpload:MaxFileSize"] = "10485760"
            }!)
            .Build();

        var controller = new DocumentsController(
            ingestionService!,
            config,
            logger ?? Mock.Of<ILogger<DocumentsController>>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static IFormFile CreateFormFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    // ── 5.2 Unit: Unsupported file type error ──

    [Fact]
    public async Task Upload_Post_UnsupportedFileType_ReturnsViewWithValidationError()
    {
        // Arrange — extension check happens BEFORE IngestAsync,
        // so we can pass null for IngestionService.
        var controller = CreateController();
        var file = CreateFormFile("malware.exe", "application/x-msdownload", [0x01, 0x02]);

        // Act
        var result = await controller.Upload(file, CancellationToken.None);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);

        Assert.False(controller.ModelState.IsValid);
        var error = Assert.Single(controller.ModelState["file"]?.Errors ?? []);
        Assert.Contains(".cs", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".md", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".pdf", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5.3 Unit: 0-byte file error ──

    [Fact]
    public async Task Upload_Post_EmptyFile_ReturnsViewWithValidationError()
    {
        // Arrange — empty check happens BEFORE IngestAsync call.
        var controller = CreateController();
        var file = CreateFormFile("empty.cs", "text/plain", []);

        // Act
        var result = await controller.Upload(file, CancellationToken.None);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);

        Assert.False(controller.ModelState.IsValid);
        var error = Assert.Single(controller.ModelState["file"]?.Errors ?? []);
        Assert.Contains("empty", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5.5 Integration: Upload valid file renders success ──

    [Fact]
    public async Task Upload_Post_ValidCsFile_ReturnsResultViewWithSuccess()
    {
        await using var factory = new CustomUploadWebApplicationFactory();
        using var client = factory.CreateClient();

        var fileContent = "public class Hello { }";
        using var fileStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        using var formContent = new MultipartFormDataContent
        {
            { new StreamContent(fileStream), "file", "Hello.cs" }
        };

        // Act
        var response = await client.PostAsync("/Documents/Upload", formContent);

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("success", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello.cs", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/plain", body, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Custom WebApplicationFactory that stubs AI + infrastructure services
/// for DocumentsController integration tests.
/// </summary>
public class CustomUploadWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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

            services.AddSingleton<IChatClient>(Mock.Of<IChatClient>());
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(mockEmbedding.Object);

            // ── Stub infrastructure (IDocumentParser, IChunker, IVectorStore) ──
            RemoveService<IVectorStore>(services);

            var mockVectorStore = new Mock<IVectorStore>();
            mockVectorStore
                .Setup(v => v.StoreChunksBatchAsync(
                    It.IsAny<IList<(DocumentChunk Chunk, ReadOnlyMemory<float> Embedding)>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            services.AddSingleton<IVectorStore>(mockVectorStore.Object);

            // Replace the real parsers with a stub parser that returns canned text
            RemoveServices<IDocumentParser>(services);

            var stubParser = new Mock<IDocumentParser>();
            stubParser
                .Setup(p => p.CanHandle(It.IsAny<string>()))
                .Returns(true);
            stubParser
                .Setup(p => p.ParseAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("stub parsed content");

            services.AddSingleton<IDocumentParser>(stubParser.Object);

            // Replace real chunker with a stub
            RemoveService<IChunker>(services);

            var stubChunker = new Mock<IChunker>();
            stubChunker
                .Setup(c => c.ChunkAsync(
                    It.IsAny<Document>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            services.AddSingleton<IChunker>(stubChunker.Object);
        });
    }

    private static void RemoveService<T>(IServiceCollection services) where T : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
            services.Remove(descriptor);
    }

    private static void RemoveServices<T>(IServiceCollection services) where T : class
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in descriptors)
            services.Remove(d);
    }
}
