namespace rag.Models;

/// <summary>
/// UDS-7: parameters for the shared <c>_ConfirmModal</c> partial. The row-level
/// destructive trigger opens the modal (data-bs-toggle/data-bs-target); Cancel
/// dismisses without submitting (data-bs-dismiss); Confirm is the only submit
/// wired to the target form (form attribute), so the action blocks until the
/// user makes a choice.
/// </summary>
public class ConfirmModalViewModel
{
    public string ModalId { get; set; } = "confirmModal";

    public string FormId { get; set; } = string.Empty;

    public string Title { get; set; } = "Confirm action";

    public string Message { get; set; } = "This action cannot be undone.";

    public string ConfirmLabel { get; set; } = "Confirm";

    public string ConfirmClass { get; set; } = "btn-danger";
}
