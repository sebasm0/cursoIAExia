using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using Xunit;

namespace RAG.Mvc.Tests.Controllers;

/// <summary>
/// 5.2 Unit: POST Upload unsupported file type error.
/// 5.3 Unit: POST Upload 0-byte file error.
/// 5.5 Integration: POST Upload valid file renders success.
/// UPLOAD-1: GET Upload renders the form; POST validation re-renders the form.
/// </summary>
public class DocumentsControllerTests
{
    // ── Helpers ──

    private static DocumentsController CreateController(
        IngestionService? ingestionService = null,
        IConfiguration? configuration = null,
        ILogger<DocumentsController>? logger = null,
        IVectorStore? vectorStore = null)
    {
        var config = configuration ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentUpload:MaxFileSize"] = "10485760"
            }!)
            .Build();

        var controller = new DocumentsController(
            ingestionService!,
            vectorStore ?? new Mock<IVectorStore>().Object,
            config,
            logger ?? Mock.Of<ILogger<DocumentsController>>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());

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

        // Assert — UPLOAD-1: validation errors re-render the upload form
        // (View() with no explicit name resolves to the Upload action's view),
        // NOT the Documents landing page.
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Null(viewResult.ViewName);

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

        // Assert — UPLOAD-1: validation errors re-render the upload form.
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Null(viewResult.ViewName);

        Assert.False(controller.ModelState.IsValid);
        var error = Assert.Single(controller.ModelState["file"]?.Errors ?? []);
        Assert.Contains("empty", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── UPLOAD-1: GET Upload renders the form (reachable route) ──

    [Fact]
    public async Task Upload_Get_RendersUploadForm()
    {
        // UPLOAD-1: /Documents/Upload must be reachable via GET and render the
        // form (the landing page links here). 404/405 before the GET action
        // existed; 200 + form after.
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Documents/Upload");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"file\"", body);
    }

    // ── UPLOAD-1: size-limit validation re-renders the form too ──

    [Fact]
    public async Task Upload_Post_FileExceedsSizeLimit_ReRendersFormWithMaxSize()
    {
        // Triangulation of the third validation branch (file too large): a
        // distinct input (over-limit file) takes a different code path but
        // must produce the same re-render contract as the other two.
        var controller = CreateController(configuration: new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentUpload:MaxFileSize"] = "100"
            }!)
            .Build());
        var file = CreateFormFile("big.cs", "text/plain", new byte[101]);

        var result = await controller.Upload(file, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Null(viewResult.ViewName);

        Assert.False(controller.ModelState.IsValid);
        var error = Assert.Single(controller.ModelState["file"]?.Errors ?? []);
        Assert.Contains("maximum upload size", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5.5 Integration: Upload valid file renders success ──

    [Fact]
    public async Task Upload_Post_ValidCsFile_ReturnsResultViewWithSuccess()
    {
        await using var factory = new CustomUploadWebApplicationFactory();
        using var client = factory.CreateClient();

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Documents/Upload");

        // Act — UPLOAD-11: multipart POST with the harvested token.
        var response = await client.SendAsync(AccountTestHelpers.CreateMultipartPost(
            "/Documents/Upload", token, "Hello.cs",
            System.Text.Encoding.UTF8.GetBytes("public class Hello { }")));

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("success", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello.cs", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/plain", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── UPLOAD-11: multipart POST without antiforgery token is rejected ──

    [Fact]
    public async Task Upload_Post_WithoutToken_ReturnsBadRequest()
    {
        await using var factory = new CustomUploadWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var formContent = new MultipartFormDataContent
        {
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("public class Hello { }")), "file", "Hello.cs" }
        };

        // Act — UPLOAD-11: no __RequestVerificationToken in the multipart body.
        var response = await client.PostAsync("/Documents/Upload", formContent);

        // Assert — the server must reject with HTTP 400 BEFORE any file validation
        // or ingestion call.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Index: loads the document list from the store ──

    [Fact]
    public async Task Index_GetsDocumentList_PassesListToView()
    {
        var documents = new List<Document>
        {
            new() { FileName = "a.cs", ContentType = "text/plain", Size = 100 },
            new() { FileName = "b.md", ContentType = "text/markdown", Size = 2048 },
        };

        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.ListDocumentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents);

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(documents, viewResult.Model);
    }

    // ── Index: store failure renders the view with the LoadFailed flag ──

    [Fact]
    public async Task Index_StoreThrows_SetsLoadFailedAndEmptyModel()
    {
        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.ListDocumentsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Empty((IReadOnlyList<Document>)viewResult.Model!);
        Assert.True(controller.ViewData["LoadFailed"] is true);
        Assert.NotNull(controller.TempData["Error"]);
    }

    // ── Delete: removes the document and redirects back to the list ──

    [Fact]
    public async Task Delete_Post_CallsDeleteDocumentAsyncAndRedirectsToIndex()
    {
        var id = Guid.NewGuid();
        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.Delete(id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DocumentsController.Index), redirect.ActionName);
        mockVectorStore.Verify(v => v.DeleteDocumentAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Document deleted successfully.", controller.TempData["Message"]);
    }

    [Fact]
    public async Task Delete_Post_StoreThrows_StillRedirectsToIndex()
    {
        var id = Guid.NewGuid();
        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.Delete(id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DocumentsController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Delete_Post_NonexistentDocument_ReportsAlreadyDeleted()
    {
        var id = Guid.NewGuid();
        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.Delete(id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DocumentsController.Index), redirect.ActionName);
        Assert.Equal("The document does not exist or was already deleted.", controller.TempData["Message"]);
    }

    // ── View: serves the original file inline ──

    [Fact]
    public async Task View_Get_WithContent_ReturnsFileResult()
    {
        var id = Guid.NewGuid();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.GetDocumentWithContentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new Document { FileName = "doc.pdf", ContentType = "application/pdf", Size = bytes.Length }, bytes));

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.View(id, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.Equal(bytes, fileResult.FileContents);
        Assert.True(fileResult.EnableRangeProcessing);
    }

    [Fact]
    public async Task View_Get_MissingContent_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.GetDocumentWithContentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Document?)null, (byte[]?)null));

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.View(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task View_Get_StoreThrows_RedirectsToIndexWithError()
    {
        var id = Guid.NewGuid();
        var mockVectorStore = new Mock<IVectorStore>();
        mockVectorStore
            .Setup(v => v.GetDocumentWithContentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var controller = CreateController(vectorStore: mockVectorStore.Object);

        var result = await controller.View(id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DocumentsController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
    }
}

/// <summary>
/// Custom WebApplicationFactory for the Upload flow: inherits the shared AI +
/// infrastructure stubs from <see cref="RagWebApplicationFactoryBase"/> and
/// overrides only the parsers/chunker used by the ingest pipeline.
/// </summary>
public class CustomUploadWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // The Upload endpoint is gated by documents.upload (UPLOAD-9) —
            // authenticate the integration client with that permission.
            services.AddPolicyTestAuthentication([Permissions.DocumentsUpload], []);

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
}
