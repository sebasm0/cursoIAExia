using RAG.Domain.Abstractions;

namespace RAG.Infrastructure.Parsing;

/// <summary>
/// Parser for source code files. Returns content as-is since code IS text.
/// Handles common text-based source extensions.
/// </summary>
public sealed class CodeParser : IDocumentParser
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".cs", ".fs", ".vb",           // .NET
        ".ts", ".tsx", ".js", ".jsx",  // JavaScript/TypeScript
        ".py",                          // Python
        ".go",                          // Go
        ".rs",                          // Rust
        ".java", ".kt",                 // JVM
        ".swift",                       // Swift
        ".rb",                          // Ruby
        ".php",                         // PHP
        ".c", ".h", ".cpp", ".hpp",    // C/C++
        ".sql",                         // SQL
        ".yaml", ".yml", ".json", ".xml", ".toml", // Data formats
        ".sh", ".ps1", ".bat",          // Scripts
        ".css", ".scss", ".less",       // Styles
        ".html", ".htm",                // HTML
        ".config", ".props", ".targets", // MSBuild
    ];

    private static readonly HashSet<string> SupportedMimeTypes =
    [
        "text/x-csharp", "text/x-java", "text/x-python",
        "text/x-go", "text/x-rust", "text/x-php",
        "text/x-typescript", "text/x-javascript",
        "text/xml", "text/x-yaml", "text/x-json",
        "text/x-script", "text/x-sh",
    ];

    public bool CanHandle(string contentType)
    {
        var lower = contentType.ToLowerInvariant();

        if (SupportedMimeTypes.Contains(lower))
            return true;

        if (lower.StartsWith("text/"))
            return true;

        var ext = Path.GetExtension(lower);
        return SupportedExtensions.Contains(ext);
    }

    public Task<string> ParseAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content);
        return Task.FromResult(reader.ReadToEnd());
    }
}
