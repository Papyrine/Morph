namespace Morph;

/// <summary>
/// What the editor reads of CheckFileInfo ([MS-WOPI] 3.3.5.1.1.2): the file's name, what the user may do
/// with it, and the host's links.
/// </summary>
/// <param name="BaseFileName">The file's name, with its extension, which says whether it is edited.</param>
/// <param name="UserFriendlyName">The user's name, which comments and changes are made in.</param>
/// <param name="UserCanWrite">Whether the user may save the file.</param>
/// <param name="ReadOnly">Whether no one may save the file, as on a read-only site.</param>
/// <param name="SupportsLocks">Whether the host takes Lock, RefreshLock and Unlock.</param>
/// <param name="SupportsUpdate">Whether the host takes PutFile.</param>
/// <param name="DisablePrint">Takes Print away (DisablePrint or HidePrintOption).</param>
/// <param name="DisableExport">Takes Download away (DisableExport or HideExportOption).</param>
/// <param name="CloseUrl">Where Close goes; there is no Close without it.</param>
/// <param name="BrandName">The host's name, first in the breadcrumb (BreadcrumbBrandName).</param>
/// <param name="BrandUrl">Where the host's name links to (BreadcrumbBrandUrl).</param>
/// <param name="FolderName">The file's folder, before its name in the breadcrumb (BreadcrumbFolderName).</param>
/// <param name="FolderUrl">Where the folder links to (BreadcrumbFolderUrl).</param>
public sealed record WopiFileInfo(
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
    internal static WopiFileInfo Parse(string json)
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