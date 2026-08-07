using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rag.Models;
using RAG.Application.Services;
using RAG.Domain.Abstractions;
using RAG.Domain.Entities;
using RAG.Infrastructure.Identity;

namespace rag.Controllers;

/// <summary>
/// Document management: upload, list and delete documents (specs UPLOAD-9,
/// LIST-1). The controller-wide gate requires an authenticated principal carrying
/// the <c>permission: documents.upload</c> claim, enforced by the policy registered
/// from the permission catalog (RBAC-4). The <see cref="Delete"/> action adds a
/// further <c>permission: documents.delete</c> gate on top of that. All gates run
/// before any storage call.
/// </summary>
[Authorize(Policy = Permissions.DocumentsUpload)]
public class DocumentsController : Controller
{
    private readonly IngestionService _ingestionService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<DocumentsController> _logger;
    private readonly long _maxFileSize;

    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "text/plain",
        [".md"] = "text/markdown",
        [".pdf"] = "application/pdf",
    };

    public DocumentsController(
        IngestionService ingestionService,
        IVectorStore vectorStore,
        IConfiguration configuration,
        ILogger<DocumentsController> logger)
    {
        _ingestionService = ingestionService;
        _vectorStore = vectorStore;
        _logger = logger;
        _maxFileSize = configuration.GetValue<long>("DocumentUpload:MaxFileSize", 10 * 1024 * 1024);
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        IReadOnlyList<Document> documents;

        ViewData["CanDeleteDocuments"] = User.HasClaim(Permissions.ClaimType, Permissions.DocumentsDelete);

        try
        {
            documents = await _vectorStore.ListDocumentsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents");
            ViewData["LoadFailed"] = true;
            TempData["Error"] = "No se pudo cargar la lista de documentos. El servicio puede estar temporalmente no disponible.";
            documents = [];
        }

        return View(documents);
    }

    /// <summary>
    /// Deletes a document and its chunks (cascade via FK). Gated by the
    /// <c>permission: documents.delete</c> claim, on top of the controller-wide
    /// <c>documents.upload</c> policy.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.DocumentsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            var deleted = await _vectorStore.DeleteDocumentAsync(id, ct);
            TempData["Message"] = deleted
                ? "Documento eliminado correctamente."
                : "El documento no existe o ya fue eliminado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document: {DocumentId}", id);
            TempData["Error"] = "Se produjo un error al eliminar el documento. Intente de nuevo.";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Serves the original uploaded file (e.g. the PDF) inline so the browser
    /// can render it. Gated by the <c>permission: documents.view</c> claim,
    /// which is seeded to the User and Admin roles (RBAC-4).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.DocumentsView)]
    public async Task<IActionResult> View(Guid id, CancellationToken ct)
    {
        try
        {
            var (document, content) = await _vectorStore.GetDocumentWithContentAsync(id, ct);
            if (document is null || content is null || content.Length == 0)
                return NotFound();

            // enableRangeProcessing lets the browser stream/seek the file and
            // return the content inline (Content-Disposition: inline), so PDFs
            // render in a browser tab instead of downloading.
            return File(content, document.ContentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error viewing document: {DocumentId}", id);
            TempData["Error"] = "Se produjo un error al cargar el documento. Intente de nuevo.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// UPLOAD-1: the upload form must be reachable via GET at /Documents/Upload
    /// (the Documents landing page links here). Render-only — all ingestion
    /// behavior lives in the POST action below.
    /// </summary>
    [HttpGet]
    public IActionResult Upload()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("file", "El archivo seleccionado está vacío. Elija un archivo con contenido.");
            return View();
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.ContainsKey(extension))
        {
            ModelState.AddModelError("file",
                $"Tipo de archivo no admitido '{(string.IsNullOrEmpty(extension) ? "(sin extensión)" : extension)}'. Formatos admitidos: .cs, .md, .pdf");
            return View();
        }

        if (file.Length > _maxFileSize)
        {
            ModelState.AddModelError("file",
                $"El archivo supera el tamaño máximo de subida de {_maxFileSize / (1024 * 1024)} MB.");
            return View();
        }

        var contentType = AllowedExtensions[extension];

        try
        {
            await using var stream = file.OpenReadStream();
            var document = await _ingestionService.IngestAsync(file.FileName, contentType, stream, ct);

            var viewModel = new UploadViewModel
            {
                FileName = document.FileName,
                FileSize = document.Size,
                ContentType = document.ContentType,
                Timestamp = document.CreatedAt,
            };

            return View("Result", viewModel);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Parser error uploading file: {FileName}", file.FileName);
            var viewModel = new UploadViewModel
            {
                FileName = file.FileName,
                ErrorMessage = $"No se pudo procesar el archivo: {ex.Message}",
            };
            return View("Result", viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {FileName}", file.FileName);
            var viewModel = new UploadViewModel
            {
                FileName = file.FileName,
                ErrorMessage = "Se produjo un error al procesar el archivo. El servicio puede estar temporalmente no disponible. Intente de nuevo.",
            };
            return View("Result", viewModel);
        }
    }
}
