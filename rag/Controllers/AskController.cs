using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using rag.Models;
using RAG.Application.Services;
using RAG.Infrastructure.Identity;

namespace rag.Controllers;

/// <summary>
/// Ask flow (spec ASK-8): requires an authenticated principal carrying the
/// <c>permission: rag.ask</c> claim, enforced by the policy registered from the
/// permission catalog (RBAC-4). The gate runs before any RAG pipeline call.
/// </summary>
[Authorize(Policy = Permissions.RagAsk)]
public class AskController : Controller
{
    private readonly RagService _ragService;
    private readonly ILogger<AskController> _logger;
    private readonly AssistantCatalog _catalog;

    public AskController(RagService ragService, ILogger<AskController> logger, AssistantCatalog catalog)
    {
        _ragService = ragService;
        _logger = logger;
        _catalog = catalog;
    }

    /// <summary>
    /// DocsChat-4 security gate: source fragments (verbatim document chunk text)
    /// are only exposed to principals that can view documents (RBAC:
    /// <c>documents.view</c>). Users holding only <c>rag.ask</c> — e.g. the
    /// seeded Viewer role — get an empty sources array; the answer itself still
    /// streams, but raw document snippets never cross the wire for them.
    /// </summary>
    private bool CanViewDocumentSources =>
        User.HasClaim(Permissions.ClaimType, Permissions.DocumentsView);

    public IActionResult Index()
    {
        // ASK-14: the composer renders the catalog with the default preselected.
        return View(new AskViewModel
        {
            AvailableAssistants = _catalog.All,
            SelectedModelId = _catalog.Default.Id,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask(AskViewModel model, CancellationToken ct)
    {
        // ASK-14/ASEL-4: validate the selection against the catalog allow-list;
        // blank/unknown resolve to the default assistant without error, and the
        // re-rendered form always shows a valid preselected option.
        _catalog.TryResolve(model.SelectedModelId, out var assistant);
        model.SelectedModelId = assistant.Id;
        model.UsedAssistant = assistant.Label;
        model.AvailableAssistants = _catalog.All;

        if (string.IsNullOrWhiteSpace(model.Query))
        {
            ModelState.AddModelError(nameof(model.Query), "Por favor, ingrese una pregunta.");
            return View("Index", model);
        }

        try
        {
            var answer = await _ragService.AskAsync(model.Query, modelId: assistant.Id, ct: ct);
            model.Answer = answer;
            return View("Result", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing question: {Query}", model.Query);
            model.ErrorMessage = "El servicio RAG está temporalmente no disponible. Intente de nuevo más tarde.";
            return View("Result", model);
        }
    }

    /// <summary>
    /// DocsChat: JSON endpoint for the floating chat in Documents/Index. Runs the
    /// same RAG pipeline and catalog validation as <see cref="Ask"/> (ASK-14,
    /// ASEL-4) but returns JSON, so the chat panel renders the answer in place
    /// instead of navigating to the Result view. The antiforgery token travels
    /// as the form's <c>__RequestVerificationToken</c> field, submitted by the
    /// site.js fetch handler (same validation contract as every other POST).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AskJson(AskViewModel model, CancellationToken ct)
    {
        _catalog.TryResolve(model.SelectedModelId, out var assistant);

        if (string.IsNullOrWhiteSpace(model.Query))
        {
            return new JsonResult(new { error = "Por favor, ingrese una pregunta." })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        try
        {
            var (answer, sources) = await _ragService.AskWithSourcesAsync(
                model.Query, modelId: assistant.Id, ct: ct);
            return Json(new { answer, usedModel = assistant.Label, sources = CanViewDocumentSources ? sources : [] });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing question (JSON): {Query}", model.Query);
            return new JsonResult(new { error = "El servicio RAG está temporalmente no disponible. Intente de nuevo más tarde." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
    }

    /// <summary>
    /// DocsChat: SSE streaming endpoint for the floating chat in Documents/Index
    /// (DocsChat-3). Runs the same RAG pipeline and catalog validation as
    /// <see cref="AskJson"/> (ASK-14, ASEL-4) but delivers the answer
    /// incrementally as server-sent events, so the chat renders text as it is
    /// generated instead of waiting for the full response.
    ///
    /// Wire contract:
    /// <code>
    /// Content-Type: text/event-stream
    /// data: {"delta":"text"}                                  (one per generated text chunk)
    /// data: {"done":true,"usedModel":"label","sources":[...]} (terminal success event;
    ///                                                            sources = [{fileName,snippet,page},...]
    ///                                                            of the top-ranked fragments, DocsChat-4)
    /// data: {"error":"message"}                               (terminal failure event)
    /// </code>
    /// An empty query is rejected with a 400 JSON error BEFORE the stream starts;
    /// blank/unknown model ids resolve to the default assistant (ASEL-2/4).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task AskStream(AskViewModel model, CancellationToken ct)
    {
        _catalog.TryResolve(model.SelectedModelId, out var assistant);

        if (string.IsNullOrWhiteSpace(model.Query))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "application/json";
            await Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "Por favor, ingrese una pregunta." }), ct);
            return;
        }

        Response.ContentType = "text/event-stream";
        // Live delivery: disable server output buffering so each event reaches
        // the client as soon as it is flushed, not when the action completes.
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // Web defaults (camelCase) keep the SSE payloads consistent with the
        // AskJson JSON contract — SourceRef record properties (fileName, snippet,
        // page) must not leak as PascalCase on the wire.
        var sseJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        async Task WriteEventAsync(object payload)
        {
            await Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(payload, sseJsonOptions)}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        try
        {
            // Retrieval runs once, up front; its topResults become the sources
            // carried by the terminal done event, while the answer still streams.
            var (deltas, sources) = await _ragService.AskStreamWithSourcesAsync(
                model.Query, modelId: assistant.Id);
            await foreach (var delta in deltas.WithCancellation(ct))
            {
                await WriteEventAsync(new { delta });
            }

            await WriteEventAsync(new { done = true, usedModel = assistant.Label, sources = CanViewDocumentSources ? sources : [] });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming question: {Query}", model.Query);
            await WriteEventAsync(new { error = "El servicio RAG está temporalmente no disponible. Intente de nuevo más tarde." });
        }
    }
}
