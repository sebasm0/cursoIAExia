using System.Text;
using RAG.Domain.Abstractions;
using UglyToad.PdfPig;

namespace RAG.Infrastructure.Parsing;

/// <summary>
/// Parser for PDF documents. Extracts the text content of every page.
/// </summary>
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
        var sb = new StringBuilder();

        using (var pdf = PdfDocument.Open(content))
        {
            foreach (var page in pdf.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                sb.AppendLine(page.Text);
            }
        }

        return Task.FromResult(sb.ToString().Trim());
    }
}
