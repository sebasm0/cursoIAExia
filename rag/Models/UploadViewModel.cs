namespace rag.Models;

public class UploadViewModel
{
    public string FileName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public string? ErrorMessage { get; set; }
}
