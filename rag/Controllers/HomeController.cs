using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using rag.Models;

namespace rag.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IConfiguration _configuration;

    public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
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
            ChatModel = cfg["AI:Ollama:ChatModel"] ?? "llama3.2",
            EmbeddingModel = cfg["AI:Ollama:EmbeddingModel"] ?? "nomic-embed-text",
            MaxFileSizeHumanReadable = FormatFileSize(cfg.GetValue<long>("DocumentUpload:MaxFileSize", 10485760))
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
