namespace Morph;

/// <summary>
/// Dropdown choice lists shared by the per-format option editors, kept in one place so the editors and
/// their snapshot tests draw from the same source.
/// </summary>
public static class ExportOptionChoices
{
    /// <summary>The render resolutions the PNG option panel offers, as (value, label) pairs.</summary>
    public static readonly (int Value, string Label)[] Dpis =
    [
        (96, "96 DPI — screen"),
        (150, "150 DPI — default"),
        (300, "300 DPI — print"),
    ];

    /// <summary>The page-crop choices the PNG option panel offers, as (value, label) pairs.</summary>
    public static readonly (PageCrop Value, string Label)[] Crops =
    [
        (PageCrop.FullPage, "Whole page — default"),
        (PageCrop.ContentBox, "Content only — margins cropped"),
        (PageCrop.ContentBoxWithHeaderFooter, "Content with header and footer"),
    ];
}
