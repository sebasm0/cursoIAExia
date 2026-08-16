namespace RAG.Application.Services;

/// <summary>
/// A source fragment that backed part of a RAG answer (citations, DocsChat-4):
/// exposes which document fragment the model could have used, with the source
/// file name when the chunk carries it (<c>Metadata["source"]</c>, set at
/// chunking time) and an optional page number when the extraction pipeline
/// tracks it. PDFs currently flatten pages, so <see cref="Page"/> stays null
/// until page-aware chunking exists.
/// </summary>
public sealed record SourceRef(string? FileName, string Snippet, int? Page);