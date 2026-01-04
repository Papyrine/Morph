using A = DocumentFormat.OpenXml.Drawing;

namespace WordRender;

/// <summary>
/// Parses theme information (colors and fonts) from Word documents.
/// </summary>
public static class ThemeParser
{
    /// <summary>
    /// Extracts theme fonts from the document.
    /// </summary>
    public static ThemeFonts? ExtractThemeFonts(MainDocumentPart mainPart)
    {
        var themePart = mainPart.ThemePart;
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
    /// Extracts theme colors from the document.
    /// </summary>
    public static ThemeColors? ExtractThemeColors(MainDocumentPart mainPart)
    {
        var themePart = mainPart.ThemePart;
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
            FollowedHyperlink = ExtractColorFromSchemeElement(colorScheme.FollowedHyperlinkColor)
        };
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
