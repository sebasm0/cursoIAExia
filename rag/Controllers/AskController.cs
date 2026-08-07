using Microsoft.AspNetCore.Authorization;
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

    public AskController(RagService ragService, ILogger<AskController> logger)
    {
        _ragService = ragService;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask(AskViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Query))
        {
            ModelState.AddModelError(nameof(model.Query), "Por favor, ingrese una pregunta.");
            return View("Index", model);
        }

        try
        {
            var answer = await _ragService.AskAsync(model.Query, ct: ct);
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
}
