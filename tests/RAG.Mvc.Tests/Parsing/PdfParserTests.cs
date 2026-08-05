using RAG.Infrastructure.Parsing;
using Xunit;

namespace RAG.Mvc.Tests.Parsing;

public class PdfParserTests
{
    // 586-byte one-page PDF (PDF-1.4, Helvetica "Hello World" content stream).
    private const string HelloWorldPdfBase64 =
        "JVBERi0xLjQKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyAvUGFnZXMgMiAwIFIgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL1R5cGUgL1BhZ2VzIC9LaWRzIFszIDAgUl0gL0NvdW50IDEgPj4KZW5kb2JqCjMgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSAvQ29udGVudHMgNCAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNSAwIFIgPj4gPj4gPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA0MiA+PgpzdHJlYW0KQlQgL0YxIDI0IFRmIDcyIDcyMCBUZCAoSGVsbG8gV29ybGQpIFRqIEVUCmVuZHN0cmVhbQplbmRvYmoKNSAwIG9iago8PCAvVHlwZSAvRm9udCAvU3VidHlwZSAvVHlwZTEgL0Jhc2VGb250IC9IZWx2ZXRpY2EgPj4KZW5kb2JqCnhyZWYKMCA2CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDAwOSAwMDAwMCBuIAowMDAwMDAwMDU4IDAwMDAwIG4gCjAwMDAwMDAxMTUgMDAwMDAgbiAKMDAwMDAwMDI0MSAwMDAwMCBuIAowMDAwMDAwMzMzIDAwMDAwIG4gCnRyYWlsZXIKPDwgL1NpemUgNiAvUm9vdCAxIDAgUiA+PgpzdGFydHhyZWYKNDAzCiUlRU9GCg==";

    // 548-byte one-page PDF (PDF-1.4) with an empty "BT ET" content stream.
    private const string EmptyPagePdfBase64 =
        "JVBERi0xLjQKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyAvUGFnZXMgMiAwIFIgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL1R5cGUgL1BhZ2VzIC9LaWRzIFszIDAgUl0gL0NvdW50IDEgPj4KZW5kb2JqCjMgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSAvQ29udGVudHMgNCAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNSAwIFIgPj4gPj4gPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA1ID4+CnN0cmVhbQpCVCBFVAplbmRzdHJlYW0KZW5kb2JqCjUgMCBvYmoKPDwgL1R5cGUgL0ZvbnQgL1N1YnR5cGUgL1R5cGUxIC9CYXNlRm9udCAvSGVsdmV0aWNhID4+CmVuZG9iagp4cmVmCjAgNgowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMDkgMDAwMDAgbiAKMDAwMDAwMDA1OCAwMDAwMCBuIAowMDAwMDAwMTE1IDAwMDAwIG4gCjAwMDAwMDAyNDEgMDAwMDAgbiAKMDAwMDAwMDI5NSAwMDAwMCBuIAp0cmFpbGVyCjw8IC9TaXplIDYgL1Jvb3QgMSAwIFIgPj4Kc3RhcnR4cmVmCjM2NQolJUVPRgo=";

    [Theory]
    [InlineData("application/pdf")]
    [InlineData(".pdf")]
    public void CanHandle_SupportedPdfTypes_ReturnsTrue(string contentType)
    {
        var parser = new PdfParser();

        Assert.True(parser.CanHandle(contentType));
    }

    [Theory]
    [InlineData("text/markdown")]
    [InlineData(".md")]
    public void CanHandle_UnsupportedTypes_ReturnsFalse(string contentType)
    {
        var parser = new PdfParser();

        Assert.False(parser.CanHandle(contentType));
    }

    [Fact]
    public async Task ParseAsync_ValidPdf_ExtractsPageText()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(HelloWorldPdfBase64));

        var text = await new PdfParser().ParseAsync(stream);

        Assert.Contains("Hello World", text);
    }

    [Fact]
    public async Task ParseAsync_PdfWithoutText_ReturnsEmptyString()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(EmptyPagePdfBase64));

        var text = await new PdfParser().ParseAsync(stream);

        Assert.Equal(string.Empty, text);
    }
}
