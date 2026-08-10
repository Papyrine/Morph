/// <summary>
/// The legacy 56-entry colour palette a workbook can reference by index instead of by value
/// (ECMA-376 §18.8.27). Modern files use theme or RGB colours, but indexed entries survive in
/// styles carried forward from older workbooks — and index 64/65, the "automatic" foreground and
/// background, appear in nearly every file as the placeholder inside a solid fill.
/// </summary>
static class IndexedPalette
{
    static readonly string[] entries =
    [
        "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
        "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
        "800000", "008000", "000080", "808000", "800080", "008080", "C0C0C0", "808080",
        "9999FF", "993366", "FFFFCC", "CCFFFF", "660066", "FF8080", "0066CC", "CCCCFF",
        "000080", "FF00FF", "FFFF00", "00FFFF", "800080", "800000", "008080", "0000FF",
        "00CCFF", "CCFFFF", "CCFFCC", "FFFF99", "99CCFF", "FF99CC", "CC99FF", "FFCC99",
        "3366FF", "33CCCC", "99CC00", "FFCC00", "FF9900", "FF6600", "666699", "969696",
        "003366", "339966", "003300", "333300", "993300", "993366", "333399", "333333"
    ];

    /// <summary>
    /// The colour for an index, or null for the two "automatic" sentinels (64 = foreground,
    /// 65 = background) and anything out of range. Null means "no explicit colour", which lets the
    /// caller fall back rather than painting a wrong black.
    /// </summary>
    public static string? Resolve(uint index) =>
        index < entries.Length ? entries[index] : null;
}
