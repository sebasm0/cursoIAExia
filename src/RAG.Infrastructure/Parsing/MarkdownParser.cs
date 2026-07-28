using System.Text.RegularExpressions;
using RAG.Domain.Abstractions;

namespace RAG.Infrastructure.Parsing;

public sealed partial class MarkdownParser : IDocumentParser
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "text/markdown", "text/x-markdown", "text/plain",
        ".md", ".mdx", ".markdown",
    ];

    public bool CanHandle(string contentType)
        => SupportedTypes.Contains(contentType.ToLowerInvariant()) ||
           SupportedTypes.Contains(Path.GetExtension(contentType).ToLowerInvariant());

    public Task<string> ParseAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content);
        var text = reader.ReadToEnd();

        // Strip YAML frontmatter
        text = FrontMatterRegex().Replace(text, "");

        // Strip code blocks (keep their content — it's still valuable context)
        // Strip HTML tags
        text = HtmlTagRegex().Replace(text, " ");

        // Normalize whitespace
        text = MultipleNewlinesRegex().Replace(text, "\n\n");

        return Task.FromResult(text.Trim());
    }

    [GeneratedRegex(@"^---[\s\S]*?---\n?", RegexOptions.Multiline)]
    private static partial Regex FrontMatterRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.NonBacktracking)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.NonBacktracking)]
    private static partial Regex MultipleNewlinesRegex();
}
