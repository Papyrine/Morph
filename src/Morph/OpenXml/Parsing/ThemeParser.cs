using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Parses theme information (colors and fonts) out of a <see cref="ThemePart"/>.
///
/// A theme part is pure DrawingML and identical across the three OOXML packages, so both entry
/// points take the part itself rather than the format-specific part that owns it: Word reaches it
/// through <c>MainDocumentPart.ThemePart</c>, PowerPoint through <c>SlideMasterPart.ThemePart</c>
/// and Excel through <c>WorkbookPart.ThemePart</c>.
/// </summary>
static class ThemeParser
{
    /// <summary>
    /// Extracts theme fonts. Null when the part is absent or declares no font scheme.
    /// </summary>
    public static ThemeFonts? ExtractThemeFonts(ThemePart? themePart)
    {
        if (themePart?.Theme?.ThemeElements?.FontScheme == null)
        {
            return null;
        }

        var fontScheme = themePart.Theme.ThemeElements.FontScheme;

        // Get major font (for headings) - latin typeface
        var majorFont = "Calibri Light";
        var majorFontElement = fontScheme.MajorFont?.LatinFont;
        if (majorFontElement?.Typeface?.HasValue == true)
        {
            majorFont = majorFontElement.Typeface.Value!.Trim();
        }

        // Get minor font (for body text) - latin typeface
        var minorFont = "Calibri";
        var minorFontElement = fontScheme.MinorFont?.LatinFont;
        if (minorFontElement?.Typeface?.HasValue == true)
        {
            minorFont = minorFontElement.Typeface.Value!.Trim();
        }

        return new()
        {
            MajorFont = majorFont,
            MinorFont = minorFont
        };
    }

    /// <summary>
    /// Extracts theme colors. Null when the part is absent or declares no color scheme.
    /// </summary>
    public static ThemeColors? ExtractThemeColors(ThemePart? themePart)
    {
        if (themePart?.Theme?.ThemeElements?.ColorScheme == null)
        {
            return null;
        }

        var colorScheme = themePart.Theme.ThemeElements.ColorScheme;

        return new()
        {
            Dark1 = ExtractColorFromSchemeElement(colorScheme.Dark1Color),
            Light1 = ExtractColorFromSchemeElement(colorScheme.Light1Color),
            Dark2 = ExtractColorFromSchemeElement(colorScheme.Dark2Color),
            Light2 = ExtractColorFromSchemeElement(colorScheme.Light2Color),
            Accent1 = ExtractColorFromSchemeElement(colorScheme.Accent1Color),
            Accent2 = ExtractColorFromSchemeElement(colorScheme.Accent2Color),
            Accent3 = ExtractColorFromSchemeElement(colorScheme.Accent3Color),
            Accent4 = ExtractColorFromSchemeElement(colorScheme.Accent4Color),
            Accent5 = ExtractColorFromSchemeElement(colorScheme.Accent5Color),
            Accent6 = ExtractColorFromSchemeElement(colorScheme.Accent6Color),
            Hyperlink = ExtractColorFromSchemeElement(colorScheme.Hyperlink),
            FollowedHyperlink = ExtractColorFromSchemeElement(colorScheme.FollowedHyperlinkColor),
            LineStyleWidthsEmu = ExtractLineStyleWidths(themePart)
        };
    }

    /// <summary>
    /// Reads <c>theme/formatScheme/lnStyleLst</c> widths (EMU) for resolving <c>a:lnRef/@idx</c>
    /// on shapes. The first entry of the returned list is a 0 sentinel so callers can index
    /// 1-based directly with the <c>idx</c> attribute. Falls back to Office defaults if the
    /// theme omits the list.
    /// </summary>
    static IReadOnlyList<long> ExtractLineStyleWidths(ThemePart themePart)
    {
        var defaults = new long[] { 0, 6350, 12700, 19050 };
        var formatScheme = themePart.Theme?.ThemeElements?.FormatScheme;
        var lnStyleLst = formatScheme?.LineStyleList;
        if (lnStyleLst == null)
        {
            return defaults;
        }

        var widths = new List<long> { 0 };
        foreach (var ln in lnStyleLst.Elements<A.Outline>())
        {
            widths.Add(ln.Width?.Value ?? 0);
        }
        return widths.Count > 1 ? widths : defaults;
    }

    /// <summary>
    /// Extracts a color value from a theme color scheme element.
    /// </summary>
    public static string ExtractColorFromSchemeElement(A.Color2Type? colorElement)
    {
        if (colorElement == null)
        {
            return "000000";
        }

        // Try srgbClr (direct RGB value)
        var srgb = colorElement.RgbColorModelHex;
        if (srgb?.Val?.HasValue == true)
        {
            return srgb.Val.Value!;
        }

        // Try sysClr (system color with lastClr attribute storing the actual value)
        var sysClr = colorElement.SystemColor;
        if (sysClr?.LastColor?.HasValue == true)
        {
            return sysClr.LastColor.Value!;
        }

        return "000000";
    }
}
