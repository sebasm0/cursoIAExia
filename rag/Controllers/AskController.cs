using Microsoft.AspNetCore.Mvc;
using rag.Models;
using RAG.Application.Services;

namespace rag.Controllers;

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
    public async Task<IActionResult> Ask(AskViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Query))
        {
            ModelState.AddModelError(nameof(model.Query), "Please enter a question.");
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
            model.ErrorMessage = "The RAG service is temporarily unavailable. Please try again later.";
            return View("Result", model);
        }
    }
}
