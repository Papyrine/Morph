namespace Morph;

/// <summary>
/// What the editor reads of CheckFileInfo ([MS-WOPI] 3.3.5.1.1.2): the file's name, what the user may do
/// with it, and the host's links.
/// </summary>
sealed record WopiFileInfo(
    string BaseFileName,
    string? UserFriendlyName,
    bool UserCanWrite,
    bool ReadOnly,
    bool SupportsLocks,
    bool SupportsUpdate,
    bool DisablePrint,
    bool DisableExport,
    string? CloseUrl,
    string? BrandName,
    string? BrandUrl,
    string? FolderName,
    string? FolderUrl)
{
    public static WopiFileInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(
            Text(root, "BaseFileName") ?? "Document",
            Text(root, "UserFriendlyName"),
            Flag(root, "UserCanWrite"),
            Flag(root, "ReadOnly"),
            Flag(root, "SupportsLocks"),
            Flag(root, "SupportsUpdate"),
            Flag(root, "DisablePrint") || Flag(root, "HidePrintOption"),
            Flag(root, "DisableExport") || Flag(root, "HideExportOption"),
            Text(root, "CloseUrl"),
            Text(root, "BreadcrumbBrandName"),
            Text(root, "BreadcrumbBrandUrl"),
            Text(root, "BreadcrumbFolderName"),
            Text(root, "BreadcrumbFolderUrl"));
    }

    static string? Text(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString() is {Length: > 0} text)
        {
            return text;
        }

        return null;
    }

    static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;

    /// <summary>Whether the user may edit and save the file over itself, under a lock.</summary>
    public bool Editable =>
        UserCanWrite &&
        !ReadOnly &&
        SupportsUpdate &&
        SupportsLocks;
}