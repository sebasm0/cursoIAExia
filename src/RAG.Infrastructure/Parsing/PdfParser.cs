using RAG.Domain.Abstractions;

namespace RAG.Infrastructure.Parsing;

/// <summary>
/// PDF parser. Requires a PDF library to be added to the project.
/// Recommended options:
///   - UglyToad.PdfPig (MIT, lightweight, no System.Drawing dependency)
///   - iText 7 (AGPL for open source, commercial license otherwise)
///   - Docnet.Core (Apache 2.0, uses PDFium)
/// </summary>
/// <remarks>
/// This is a placeholder implementation. Add a PDF parsing library and
/// replace the logic below. The interface contract stays the same.
/// </remarks>
public sealed class PdfParser : IDocumentParser
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "application/pdf",
        ".pdf",
    ];

    public bool CanHandle(string contentType)
        => SupportedTypes.Contains(contentType.ToLowerInvariant()) ||
           SupportedTypes.Contains(Path.GetExtension(contentType).ToLowerInvariant());

    public Task<string> ParseAsync(Stream content, CancellationToken ct = default)
    {
        // Placeholder: reads raw text from the stream.
        // Replace with PdfPig or your chosen library:
        //
        //   using var pdf = PdfDocument.Open(content);
        //   var sb = new StringBuilder();
        //   foreach (var page in pdf.GetPages())
        //       sb.AppendLine(page.Text);
        //   return sb.ToString();

        throw new NotImplementedException(
            "PDF parsing is not implemented. Add a PDF library (e.g., UglyToad.PdfPig) " +
            "and implement PdfParser.ParseAsync. See the comment in this file.");
    }
}
