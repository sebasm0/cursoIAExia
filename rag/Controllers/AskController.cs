using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using rag.Models;
using RAG.Application.Services;
using RAG.Domain.Chat;
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
    private readonly ChatHistoryService _chatHistoryService;

    public AskController(RagService ragService, ILogger<AskController> logger, AssistantCatalog catalog, ChatHistoryService chatHistoryService)
    {
        _ragService = ragService;
        _logger = logger;
        _catalog = catalog;
        _chatHistoryService = chatHistoryService;
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

    /// <summary>
    /// DocsChat: per-user chat history (spec CH-5). Returns the caller's 50 most
    /// recent messages ascending by <c>created_at</c>, each shaped
    /// <c>{id, role, content, createdAt, modelId, sources}</c> — sources as
    /// <c>[]</c> when absent, modelId as <c>null</c> when absent. The user id
    /// always comes from the NameIdentifier claim; a non-Guid claim yields 401.
    /// Dormant until the frontend slice lands (no consumer yet).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        var userId = UserIdFromPrincipal();
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var messages = await _chatHistoryService.GetRecentAsync(userId.Value, ct);

            // D7: mapping ChatSource → SourceRef lives only here, so the wire shape
            // stays identical to the AskStream done event (fileName/snippet/page).
            // DocsChat-4 gate: source fragments only cross the wire for principals
            // with documents.view, mirroring AskJson/AskStream (R1-001 correction).
            var canViewSources = CanViewDocumentSources;
            var items = messages
                .Select(m => new ChatHistoryItem(
                    m.Id,
                    m.Role,
                    m.Content,
                    m.CreatedAt,
                    m.ModelId,
                    canViewSources
                        ? m.Sources.Select(s => new SourceRef(s.FileName, s.Snippet, s.Page)).ToList()
                        : []))
                .ToList();

            return Json(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading chat history for user {UserId}", userId.Value);
            return new JsonResult(new { error = "El historial de chat está temporalmente no disponible. Intente de nuevo más tarde." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
    }

    /// <summary>
    /// DocsChat: persist a chat message (spec CH-6, design D9). JSON body
    /// <c>{role, content, modelId?, sources?}</c> with the antiforgery token in
    /// the <c>RequestVerificationToken</c> header — a missing token yields 400
    /// BEFORE any validation or persistence (per-action posture). Success: 201
    /// <c>{id, createdAt}</c> with the id and the DB-clock timestamp; invalid
    /// role/content: 400 <c>{error}</c>, nothing persisted; non-Guid
    /// NameIdentifier claim: 401. Dormant until the frontend slice lands.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> History([FromBody] ChatHistoryRequest request, CancellationToken ct)
    {
        var userId = UserIdFromPrincipal();
        if (userId is null)
        {
            return Unauthorized();
        }

        // CH-6: a null body (empty JSON or literal null) is invalid input, not a
        // server failure — 400 before any dereference (R2-001 correction).
        if (request is null)
        {
            return new JsonResult(new { error = "Cuerpo de mensaje inválido." })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        try
        {
            var result = await _chatHistoryService.AddAsync(
                userId.Value, request.Role, request.Content, request.ModelId, request.Sources, ct);

            if (!result.IsValid || result.Message is null)
            {
                return new JsonResult(new { error = result.ErrorMessage ?? "Mensaje inválido." })
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                };
            }

            return new JsonResult(new { id = result.Message.Id, createdAt = result.Message.CreatedAt })
            {
                StatusCode = StatusCodes.Status201Created,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting chat history for user {UserId}", userId.Value);
            return new JsonResult(new { error = "El historial de chat está temporalmente no disponible. Intente de nuevo más tarde." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
    }

    /// <summary>
    /// The authenticated user id: parsed from the NameIdentifier claim (CH-3);
    /// null when the claim is missing or not a Guid, which callers turn into 401.
    /// </summary>
    private Guid? UserIdFromPrincipal()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
