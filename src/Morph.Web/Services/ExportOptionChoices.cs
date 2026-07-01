namespace Morph.Web.Services;

/// <summary>
/// Dropdown choice lists shared by the per-format option editors, kept in one place so the editors and
/// their snapshot tests draw from the same source.
/// </summary>
public static class ExportOptionChoices
{
    public static readonly (int Value, string Label)[] Dpis =
    [
        (96, "96 DPI — screen"),
        (150, "150 DPI — default"),
        (300, "300 DPI — print"),
    ];
}
