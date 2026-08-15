/// <summary>
/// Theme color definitions from the document theme.
/// </summary>
sealed class ThemeColors
{
    /// <summary>Dark 1 color (typically black).</summary>
    public string Dark1 { get; init; } = "000000";

    /// <summary>Light 1 color (typically white).</summary>
    public string Light1 { get; init; } = "FFFFFF";

    /// <summary>Dark 2 color.</summary>
    public string Dark2 { get; init; } = "44546A";

    /// <summary>Light 2 color.</summary>
    public string Light2 { get; init; } = "E7E6E6";

    /// <summary>Accent color 1.</summary>
    public string Accent1 { get; init; } = "4472C4";

    /// <summary>Accent color 2.</summary>
    public string Accent2 { get; init; } = "ED7D31";

    /// <summary>Accent color 3.</summary>
    public string Accent3 { get; init; } = "A5A5A5";

    /// <summary>Accent color 4.</summary>
    public string Accent4 { get; init; } = "FFC000";

    /// <summary>Accent color 5.</summary>
    public string Accent5 { get; init; } = "5B9BD5";

    /// <summary>Accent color 6.</summary>
    public string Accent6 { get; init; } = "70AD47";

    /// <summary>Hyperlink color.</summary>
    public string Hyperlink { get; init; } = "0563C1";

    /// <summary>Followed hyperlink color.</summary>
    public string FollowedHyperlink { get; init; } = "954F72";

    /// <summary>
    /// Stroke widths (in EMU) from <c>theme/formatScheme/lnStyleLst</c>, indexed 1-based by
    /// <c>a:lnRef/@idx</c>. Defaults to Office's standard 0.5pt / 1pt / 1.5pt when the theme
    /// doesn't supply its own list. Index 0 in the list is unused (reserved for "no line" in
    /// some style refs).
    /// </summary>
    public IReadOnlyList<long> LineStyleWidthsEmu { get; init; } = [0, 6350, 12700, 19050];

    /// <summary>
    /// Resolves a theme color name to its hex value.
    /// </summary>
    /// <param name="themeColorName">Theme color name (e.g., "text1", "accent2", "hyperlink")</param>
    /// <param name="shade">Optional shade value (0-255 from WordprocessingML w:themeShade, darkens the color)</param>
    /// <param name="tint">Optional tint value (0-255 from WordprocessingML w:themeTint, lightens the color)</param>
    /// <returns>The resolved hex color value, or null if not found.</returns>
    public string? ResolveColor(string themeColorName, byte? shade = null, byte? tint = null) =>
        ResolveColor(
            themeColorName,
            new()
            {
                ThemeShade = shade, ThemeTint = tint
            });

    /// <summary>
    /// Resolves a theme color name to its hex value with full transform support.
    /// </summary>
    /// <param name="themeColorName">Theme color name (e.g., "text1", "accent2", "hyperlink")</param>
    /// <param name="transforms">Color transforms to apply (shade, tint, lumMod, satMod, etc.)</param>
    /// <returns>The resolved hex color value, or null if not found.</returns>
    public string? ResolveColor(string themeColorName, ColorTransforms transforms)
    {
        // Map theme color names to base colors
        var baseColor = themeColorName.ToLowerInvariant() switch
        {
            "text1" or "dark1" or "dk1" or "tx1" => Dark1,
            "text2" or "dark2" or "dk2" or "tx2" => Dark2,
            "background1" or "light1" or "lt1" or "bg1" => Light1,
            "background2" or "light2" or "lt2" or "bg2" => Light2,
            "accent1" => Accent1,
            "accent2" => Accent2,
            "accent3" => Accent3,
            "accent4" => Accent4,
            "accent5" => Accent5,
            "accent6" => Accent6,
            "hyperlink" or "hlink" => Hyperlink,
            "followedhyperlink" or "folhlink" => FollowedHyperlink,
            // phClr is the placeholder a theme style substitutes a caller's colour into, so a
            // document that names it as an actual colour has nothing to substitute. PowerPoint falls
            // back to DrawingML's default colour, black. Returning null instead discards whatever the
            // colour was for — for p:bgRef that is the entire slide background, so a deck PowerPoint
            // shows solid black rendered clean and hid the defect. Reaching here at all means a
            // literal phClr in the document: the style references (a:fillRef, a:lnRef, p:bgRef) each
            // take their own colour child, so a theme style's own phClr never resolves through here.
            "phclr" => "000000",
            _ => null
        };

        if (baseColor == null)
        {
            return null;
        }

        return transforms.ApplyTo(baseColor);
    }
}