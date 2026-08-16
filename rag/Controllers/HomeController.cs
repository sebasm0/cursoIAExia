using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using rag.Models;
using RAG.Application.Services;

namespace rag.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IConfiguration _configuration;
    private readonly AssistantCatalog _catalog;

    public HomeController(ILogger<HomeController> logger, IConfiguration configuration, AssistantCatalog catalog)
    {
        _logger = logger;
        _configuration = configuration;
        _catalog = catalog;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Settings()
    {
        var cfg = _configuration;
        var viewModel = new SettingsViewModel
        {
            Provider = cfg["AI:Provider"] ?? "Ollama",
            OllamaBaseUrl = cfg["AI:Ollama:BaseUrl"] ?? "http://localhost:11434",
            ChatModel = cfg["AI:Ollama:ChatModel"] ?? "phi3:mini",
            EmbeddingModel = cfg["AI:Ollama:EmbeddingModel"] ?? "nomic-embed-text",
            MaxFileSizeHumanReadable = FormatFileSize(cfg.GetValue<long>("DocumentUpload:MaxFileSize", 10485760)),
            // ASEL-1: Settings lists every catalog assistant, not a single model.
            Assistants = _catalog.All
        };
        return View(viewModel);
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.##} MB",
        >= 1024 => $"{bytes / 1024.0:0.##} KB",
        _ => $"{bytes} B",
    };

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
