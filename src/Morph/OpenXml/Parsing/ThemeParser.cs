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
        long[] defaults = [0, 6350, 12700, 19050];
        var formatScheme = themePart.Theme?.ThemeElements?.FormatScheme;
        var lnStyleLst = formatScheme?.LineStyleList;
        if (lnStyleLst == null)
        {
            return defaults;
        }

        List<long> widths = [0];
        foreach (var ln in lnStyleLst.Elements<A.Outline>())
        {
            widths.Add(ln.Width?.Value ?? 0);
        }
        if (widths.Count > 1)
        {
            return widths;
        }

        return defaults;
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

    /// <summary>
    /// Resolves a scheme color to an RGB hex value using the document theme.
    /// </summary>
    public static string? ResolveSchemeColor(A.SchemeColorValues schemeColor, ThemePart? themePart)
    {
        if (themePart?.Theme?.ThemeElements?.ColorScheme == null)
        {
            return null;
        }

        var colorScheme = themePart.Theme.ThemeElements.ColorScheme;

        // Map scheme color to theme element
        A.Color2Type? themeColor = null;
        if (schemeColor == A.SchemeColorValues.Accent1)
        {
            themeColor = colorScheme.Accent1Color;
        }
        else if (schemeColor == A.SchemeColorValues.Accent2)
        {
            themeColor = colorScheme.Accent2Color;
        }
        else if (schemeColor == A.SchemeColorValues.Accent3)
        {
            themeColor = colorScheme.Accent3Color;
        }
        else if (schemeColor == A.SchemeColorValues.Accent4)
        {
            themeColor = colorScheme.Accent4Color;
        }
        else if (schemeColor == A.SchemeColorValues.Accent5)
        {
            themeColor = colorScheme.Accent5Color;
        }
        else if (schemeColor == A.SchemeColorValues.Accent6)
        {
            themeColor = colorScheme.Accent6Color;
        }
        else if (schemeColor == A.SchemeColorValues.Dark1)
        {
            themeColor = colorScheme.Dark1Color;
        }
        else if (schemeColor == A.SchemeColorValues.Dark2)
        {
            themeColor = colorScheme.Dark2Color;
        }
        else if (schemeColor == A.SchemeColorValues.Light1)
        {
            themeColor = colorScheme.Light1Color;
        }
        else if (schemeColor == A.SchemeColorValues.Light2)
        {
            themeColor = colorScheme.Light2Color;
        }
        else if (schemeColor == A.SchemeColorValues.Background1)
        {
            themeColor = colorScheme.Light1Color;
        }
        else if (schemeColor == A.SchemeColorValues.Background2)
        {
            themeColor = colorScheme.Light2Color;
        }
        else if (schemeColor == A.SchemeColorValues.Text1)
        {
            themeColor = colorScheme.Dark1Color;
        }
        else if (schemeColor == A.SchemeColorValues.Text2)
        {
            themeColor = colorScheme.Dark2Color;
        }

        if (themeColor == null)
        {
            return null;
        }

        // Get RGB value from theme color
        var srgbClr = themeColor.RgbColorModelHex;
        if (srgbClr?.Val?.HasValue == true)
        {
            return srgbClr.Val.Value;
        }

        var sysClr = themeColor.SystemColor;
        if (sysClr?.LastColor?.HasValue == true)
        {
            return sysClr.LastColor.Value;
        }

        return null;
    }

    public static string? MapSchemeColor(A.SchemeColorValues? schemeColor)
    {
        if (schemeColor == null)
        {
            return null;
        }

        var val = schemeColor.Value;
        if (val == A.SchemeColorValues.Text1)
        {
            return "000000";
        }

        if (val == A.SchemeColorValues.Text2)
        {
            return "1F497D";
        }

        if (val == A.SchemeColorValues.Background1)
        {
            return "FFFFFF";
        }

        if (val == A.SchemeColorValues.Background2)
        {
            return "EEECE1";
        }

        if (val == A.SchemeColorValues.Accent1)
        {
            return "4F81BD";
        }

        if (val == A.SchemeColorValues.Accent2)
        {
            return "C0504D";
        }

        if (val == A.SchemeColorValues.Accent3)
        {
            return "9BBB59";
        }

        if (val == A.SchemeColorValues.Accent4)
        {
            return "8064A2";
        }

        if (val == A.SchemeColorValues.Accent5)
        {
            return "4BACC6";
        }

        if (val == A.SchemeColorValues.Accent6)
        {
            return "F79646";
        }

        if (val == A.SchemeColorValues.Hyperlink)
        {
            return "0000FF";
        }

        if (val == A.SchemeColorValues.FollowedHyperlink)
        {
            return "800080";
        }

        return null;
    }
}
