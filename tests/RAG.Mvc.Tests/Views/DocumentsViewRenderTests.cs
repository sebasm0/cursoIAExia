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

    // ── UPLOAD-10: Documents landing links to the reachable upload route ──

    [Fact]
    public async Task Documents_Index_RendersLandingWithReachableUploadLink()
    {
        await using var factory = new PolicyTestWebApplicationFactory([Permissions.DocumentsUpload], []);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Manage documents in the RAG system.", body);
        Assert.Contains("Supported formats: .cs, .md, .pdf", body);
        // UPLOAD-1: the landing Upload action targets the reachable route.
        Assert.Contains("href=\"/Documents/Upload\"", body);
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
        Assert.Contains("Upload a Document", body);
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("accept=\".cs,.md,.pdf\"", body);
        Assert.Contains("Accepted formats: .cs (C#), .md (Markdown), .pdf (PDF). Maximum size: 10 MB.", body);
        Assert.Contains("Files are parsed, chunked and indexed for semantic search.", body);
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
        Assert.Contains("Unsupported file type", body);
        Assert.Contains(".cs, .md, .pdf", body);
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
        Assert.Contains("success", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello.cs", body);
        Assert.Contains("text/plain", body);
        // UPLOAD-5/UPLOAD-6 data preserved: size + timestamp labels shown.
        Assert.Contains("File Size", body);
        Assert.Contains("Ingested At", body);
        Assert.Contains("This document is now searchable through the Ask interface.", body);
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
        Assert.Contains("Upload failed", body);
        Assert.Contains("Check that the file uses a supported format: .cs, .md, or .pdf.", body);
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
