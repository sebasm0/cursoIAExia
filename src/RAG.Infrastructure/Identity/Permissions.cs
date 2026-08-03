namespace RAG.Infrastructure.Identity;

/// <summary>
/// Static, code-defined permission catalog (spec RBAC-1). Adding a permission is
/// a code change — the catalog is never editable through the UI.
/// </summary>
public static class Permissions
{
    public const string RagAsk = "rag.ask";
    public const string DocumentsUpload = "documents.upload";
    public const string DocumentsView = "documents.view";
    public const string DocumentsDelete = "documents.delete";
    public const string AdminUsers = "admin.users";
    public const string AdminRoles = "admin.roles";
    public const string AdminPermissions = "admin.permissions";

    /// <summary>Claim type used for every role-permission grant (RBAC-2).</summary>
    public const string ClaimType = "permission";

    /// <summary>The complete catalog — exactly 7 entries (RBAC-1).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RagAsk,
        DocumentsUpload,
        DocumentsView,
        DocumentsDelete,
        AdminUsers,
        AdminRoles,
        AdminPermissions,
    ];

    /// <summary>
    /// Built-in roles and the permission grants each one carries (design D4).
    /// Used by the startup seeder to persist grants as role claims.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> SeedRoles { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = All.ToArray(),
            ["User"] = [RagAsk, DocumentsUpload, DocumentsView],
            ["Viewer"] = [RagAsk],
        };
}
