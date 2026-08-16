using RAG.Application.Services;

namespace RAG.Api.Endpoints;

public static class RagEndpoints
{
    public static void MapRagEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/rag");

        group.MapPost("/ingest", async (
            IFormFile file,
            IngestionService ingestion,
            CancellationToken ct) =>
        {
            if (file.Length is 0)
                return Results.BadRequest(new { error = "File is empty" });

            await using var stream = file.OpenReadStream();
            var document = await ingestion.IngestAsync(
                file.FileName,
                file.ContentType,
                stream,
                ct);

            return Results.Ok(new
            {
                documentId = document.Id,
                fileName = document.FileName,
                size = document.Size,
                createdAt = document.CreatedAt,
            });
        })
        .WithName("IngestDocument")
        .DisableAntiforgery(); // required for multipart form uploads in API-only mode

        group.MapPost("/ask", async (
            AskRequest request,
            RagService rag,
            AssistantCatalog catalog,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.BadRequest(new { error = "Query is required" });

            // ASEL-3/4: resolve the optional ModelId against the catalog
            // allow-list at the HTTP boundary — omitted, blank or unknown ids
            // fall back to the default assistant, and a tampered value never
            // reaches the chat client (only the resolved assistant id is passed).
            var assistant = catalog.Resolve(request.ModelId);

            var answer = await rag.AskAsync(
                request.Query,
                request.TopKRetrieve,
                request.TopKRank,
                assistant.Id,
                ct);

            return Results.Ok(new { answer });
        })
        .WithName("AskRag");

        group.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithName("HealthCheck");
    }
}

// ── DTOs ──

public record AskRequest(
    string Query,
    int TopKRetrieve = 20,
    int TopKRank = 5,
    string? ModelId = null);
