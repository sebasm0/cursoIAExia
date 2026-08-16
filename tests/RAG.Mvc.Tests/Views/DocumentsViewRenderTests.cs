using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RAG.Domain.Abstractions;
using RAG.Infrastructure.Identity;
using RAG.Mvc.Tests.Auth;
using RAG.Mvc.Tests.Controllers;
using Xunit;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// Slice B view-render tests for the Documents flow (spec UPLOAD-1, UPLOAD-10):
/// the landing page links to the now-reachable upload form, the upload form and
/// validation re-render per the design system, and the success/error result
/// views preserve the document details (name, size, timestamp; supported types
/// on error). Runs over the real WebApplicationFactory pipeline with the
/// documents.upload policy satisfied by the TestAuthHandler.
/// </summary>
public class DocumentsViewRenderTests
{
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── UPLOAD-13: floating chat renders the same assistant selector ──

    [Fact]
    public async Task Documents_Index_RendersAssistantSelectorInFloatingChat()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Dynamic catalog content is HTML-encoded by Razor; decode to assert the
        // copy the user actually sees.
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // The floating chat composer posts SelectedModelId through the same
        // AskController.Ask flow (UPLOAD-13) and lists the catalog options.
        Assert.Contains("name=\"SelectedModelId\"", body);
        Assert.Contains("Phi3 Mini", body);
        Assert.Contains("Qwen 2.5 1.5B", body);
        Assert.Contains("Llama 3.2 1B", body);
    }

    // ── DocsChat-2/3: floating chat form carries the AJAX hook for AskStream ──

    [Fact]
    public async Task Documents_Index_RendersFloatingChatFormWithAjaxHook()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // The form exposes the id site.js binds the fetch handler to, and the
        // SSE streaming endpoint it posts to (the answer renders in-place while
        // it is generated — no navigation).
        Assert.Contains("id=\"docs-chat-form\"", body);
        Assert.Contains("data-ask-stream-url=\"/Ask/AskStream\"", body);
        // The message panel the JS renders the bubbles into stays on the page.
        Assert.Contains("id=\"docsChatPanel\"", body);
        // Progressive enhancement preserved: with JavaScript disabled the native
        // action still targets the classic Ask POST flow.
        Assert.Contains("action=\"/Ask/Ask\"", body);
    }

    // ── UPLOAD-10: Documents landing links to the reachable upload route ──

    [Fact]
    public async Task Documents_Index_RendersLandingWithReachableUploadLink()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Breadcrumb + upload entry points on the current landing.
        Assert.Contains("Base de conocimiento", body);
        Assert.Contains("Agregar nuevo", body);
        Assert.Contains("Arrastre su archivo aquí o haga clic para seleccionar", body);
        Assert.Contains("Formatos admitidos: .cs, .md, .pdf", body);
        // UPLOAD-1: the landing Upload action targets the reachable route.
        Assert.Contains("href=\"/Documents/Upload\"", body);
    }

    // ── Documents listing: empty store renders the empty state ──

    [Fact]
    public async Task Documents_Index_EmptyStore_RendersEmptyState()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Documentos subidos", body);
        Assert.Contains("no ha subido documentos", body);
    }

    // ── UPLOAD-1 / UPLOAD-10: upload form renders per design system ──

    [Fact]
    public async Task Upload_Page_RendersDesignSystemForm()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents/Upload");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Subir un documento", body);
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("accept=\".cs,.md,.pdf\"", body);
        Assert.Contains("Formatos admitidos: .cs (C#), .md (Markdown), .pdf (PDF). Tamaño máximo: 10 MB.", body);
        Assert.Contains("Los archivos se procesan, se dividen en bloques y se indexan para la búsqueda semántica.", body);
        Assert.DoesNotContain("lorem", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── UPLOAD-1: unsupported file re-renders the form with a visible error ──

    [Fact]
    public async Task Upload_Post_UnsupportedFile_ReRendersFormWithSupportedTypesError()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Documents/Upload");
        var response = await client.SendAsync(AccountTestHelpers.CreateMultipartPost(
            "/Documents/Upload", token, "malware.exe", [0x01, 0x02, 0x03]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // The form re-renders with the server-side error listing the
        // supported types, and no ingestion pipeline runs.
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("Tipo de archivo no admitido", body);
        Assert.Contains(".cs, .md, .pdf", body);
        // UPLOAD-12: the server-side error renders bound to the file field
        // (field-validation-error span with data-valmsg-for="file"), so it is
        // visible with JavaScript disabled — not only via the client-side script.
        Assert.Contains("field-validation-error", body);
        Assert.Contains("data-valmsg-for=\"file\"", body);
    }

    // ── UPLOAD-10: success view shows document details ──

    [Fact]
    public async Task Upload_Result_Success_ShowsDocumentDetails()
    {
        await using var factory = new CustomUploadWebApplicationFactory();
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Documents/Upload");
        var response = await client.SendAsync(AccountTestHelpers.CreateMultipartPost(
            "/Documents/Upload", token, "Hello.cs",
            System.Text.Encoding.UTF8.GetBytes("public class Hello { }")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Subido correctamente", body);
        Assert.Contains("Hello.cs", body);
        Assert.Contains("text/plain", body);
        // UPLOAD-5/UPLOAD-6 data preserved: size + timestamp labels shown.
        Assert.Contains("Tamaño del archivo", body);
        Assert.Contains("Ingresado", body);
        Assert.Contains("Este documento ya es accesible mediante el buscador.", body);
    }

    // ── FIX-1: the per-row Delete control is gated on documents.delete ──

    [Fact]
    public async Task Documents_Index_NoDeletePermission_HidesDeleteAction()
    {
        await using var factory = new SeededDocumentsWebApplicationFactory([Permissions.DocumentsUpload]);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("hello.cs", body);
        Assert.DoesNotContain("Eliminar", body);
    }

    [Fact]
    public async Task Documents_Index_WithDeletePermission_RendersDeleteAction()
    {
        await using var factory = new SeededDocumentsWebApplicationFactory([Permissions.DocumentsUpload, Permissions.DocumentsDelete]);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("hello.cs", body);
        Assert.Contains("return confirm('¿Eliminar este documento y todos sus bloques indexados? Esta acción no se puede deshacer.');", body);
    }

    // ── ListDocumentsAsync throws: distinct error state, no empty-state text ──

    [Fact]
    public async Task Documents_Index_StoreThrows_ShowsLoadErrorNotEmptyState()
    {
        await using var factory = new FailingListDocumentsWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("No se pudo cargar la lista de documentos. Inténtelo más tarde.", body);
        Assert.DoesNotContain("Aún no ha subido documentos", body);
    }

    // ── UPLOAD-10: error view lists supported types, no stack trace ──

    [Fact]
    public async Task Upload_Result_Error_ListsSupportedTypesNoStackTrace()
    {
        await using var factory = new FailingParseUploadWebApplicationFactory();
        using var client = CreateClient(factory);

        var token = await AccountTestHelpers.GetAntiforgeryTokenAsync(client, "/Documents/Upload");
        var response = await client.SendAsync(AccountTestHelpers.CreateMultipartPost(
            "/Documents/Upload", token, "Broken.cs",
            System.Text.Encoding.UTF8.GetBytes("broken content")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Error al subir", body);
        Assert.Contains("Verifique que el archivo use un formato admitido: .cs, .md o .pdf.", body);
        Assert.DoesNotContain("Stack Trace", body);
        Assert.DoesNotContain("NotSupportedException", body);
    }
}

/// <summary>
/// Upload-flow factory where no parser can handle the content type:
/// IngestionService throws NotSupportedException and the controller renders
/// the upload error view (UPLOAD-10 error scenario).
/// </summary>
public class FailingParseUploadWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication([Permissions.DocumentsUpload], []);

            RemoveServices<IDocumentParser>(services);

            var stubParser = new Mock<IDocumentParser>();
            stubParser
                .Setup(p => p.CanHandle(It.IsAny<string>()))
                .Returns(false);

            services.AddSingleton<IDocumentParser>(stubParser.Object);
        });
    }
}

/// <summary>
/// Factory that seeds the vector store with one document so the list renders a
/// row. Permissions are configurable so the Delete gating can be tested both ways.
/// </summary>
public sealed class SeededDocumentsWebApplicationFactory : RagWebApplicationFactoryBase
{
    private readonly string[] _permissions;

    public SeededDocumentsWebApplicationFactory(string[] permissions)
    {
        _permissions = permissions;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication(_permissions, []);

            RemoveService<IVectorStore>(services);

            var mockVectorStore = new Mock<IVectorStore>();
            mockVectorStore
                .Setup(v => v.ListDocumentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new RAG.Domain.Entities.Document
                    {
                        Id = Guid.NewGuid(),
                        FileName = "hello.cs",
                        ContentType = "text/plain",
                        Size = 42,
                        CreatedAt = DateTime.UtcNow,
                    }
                ]);
            mockVectorStore
                .Setup(v => v.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            services.AddSingleton<IVectorStore>(mockVectorStore.Object);
        });
    }
}

/// <summary>
/// Factory whose vector store throws when listing documents, to exercise the
/// distinct "list could not be loaded" state (vs the plain empty state).
/// </summary>
public sealed class FailingListDocumentsWebApplicationFactory : RagWebApplicationFactoryBase
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddPolicyTestAuthentication([Permissions.DocumentsUpload], []);

            RemoveService<IVectorStore>(services);

            var mockVectorStore = new Mock<IVectorStore>();
            mockVectorStore
                .Setup(v => v.ListDocumentsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("db unavailable"));
            services.AddSingleton<IVectorStore>(mockVectorStore.Object);
        });
    }
}
