/// <summary>
/// Provides region-based default page sizes matching Microsoft Word behavior.
/// Letter (8.5" x 11") in North America, A4 (210 x 297mm) elsewhere.
/// </summary>
static class DefaultPageSize
{
    // Letter: 8.5" x 11" = 612 x 792 points
    const double letterWidthPoints = 612.0;
    const double letterHeightPoints = 792.0;

    // A4: 210mm x 297mm = 595.28 x 841.89 points
    const double a4WidthPoints = 595.28;
    const double a4HeightPoints = 841.89;

    static HashSet<string> letterRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "US", // United States
        "CA", // Canada
        "MX", // Mexico
        "PH", // Philippines
        "CL", // Chile
        "CO", // Colombia
        "VE", // Venezuela
        "GT", // Guatemala
        "CR", // Costa Rica
        "PA", // Panama
    };

    static bool? useLetterSize;

    /// <summary>
    /// Gets or sets whether to use Letter size (true) or A4 size (false).
    /// When null, automatically determined from system region.
    /// </summary>
    public static bool UseLetterSize
    {
        get => useLetterSize ?? IsLetterRegion();
        set => useLetterSize = value;
    }

    /// <summary>
    /// Resets to automatic region-based detection.
    /// </summary>
    public static void ResetToAutoDetect() => useLetterSize = null;

    /// <summary>Default page width in points.</summary>
    public static double WidthPoints => UseLetterSize ? letterWidthPoints : a4WidthPoints;

    /// <summary>Default page height in points.</summary>
    public static double HeightPoints => UseLetterSize ? letterHeightPoints : a4HeightPoints;

    static bool IsLetterRegion()
    {
        var region = RegionInfo.CurrentRegion;
        return letterRegions.Contains(region.TwoLetterISORegionName);
    }
}

/// <summary>
/// Color transform parameters for theme colors.
/// Shade and tint use the Word/OpenXML 0-255 scale.
/// </summary>
internal sealed record ColorTransforms
{
    /// <summary>Shade value (0-255, darkens the color).</summary>
    public byte? Shade { get; init; }

    /// <summary>Tint value (0-255, lightens the color).</summary>
    public byte? Tint { get; init; }

    /// <summary>Luminance modulation percentage (e.g., 75 = 75% brightness).</summary>
    public double? LumMod { get; init; }

    /// <summary>Luminance offset percentage points.</summary>
    public double? LumOff { get; init; }

    /// <summary>Saturation modulation percentage (e.g., 50 = 50% saturation).</summary>
    public double? SatMod { get; init; }

    /// <summary>Saturation offset percentage points.</summary>
    public double? SatOff { get; init; }

    /// <summary>Returns true if any transform is specified.</summary>
    public bool HasTransforms =>
        Shade.HasValue || Tint.HasValue ||
        LumMod.HasValue || LumOff.HasValue ||
        SatMod.HasValue || SatOff.HasValue;
}

/// <summary>
/// Represents a parsed DOCX document.
/// </summary>
sealed class ParsedDocument
{
    public required PageSettings PageSettings { get; init; }
    public required IReadOnlyList<DocumentElement> Elements { get; init; }
    public HeaderFooterContent? Header { get; init; }
    public HeaderFooterContent? Footer { get; init; }

    /// <summary>
    /// Document-level hyphenation settings.
    /// </summary>
    public HyphenationSettings Hyphenation { get; init; } = new();

    /// <summary>
    /// Theme colors from the document theme.
    /// </summary>
    public ThemeColors? ThemeColors { get; init; }

    /// <summary>
    /// Theme fonts from the document theme.
    /// </summary>
    public ThemeFonts? ThemeFonts { get; init; }

    /// <summary>
    /// Word compatibility settings from the document.
    /// </summary>
    public CompatibilitySettings Compatibility { get; init; } = new();
}

/// <summary>
/// Word compatibility settings that affect layout behavior.
/// Based on settings from settings.xml w:compat section.
/// </summary>
sealed class CompatibilitySettings
{
    /// <summary>
    /// Word compatibility mode version.
    /// 11 = Word 2003, 12 = Word 2007 (ECMA-376), 14 = Word 2010, 15 = Word 2013+
    /// Default is 15 (modern Word behavior).
    /// </summary>
    public int CompatibilityMode { get; init; } = 15;

    /// <summary>
    /// Whether to use legacy line spacing in table cells.
    /// For compatibility mode 14 or lower, table cells may use different line spacing rules.
    /// </summary>
    public bool UseLegacyTableLineSpacing => CompatibilityMode <= 14;

    /// <summary>
    /// Whether to add extra line spacing to table cells (Word 2013+ behavior).
    /// In mode 15+, table cells may get additional line spacing for single-spaced text.
    /// </summary>
    public bool AddLineSpacingToTableCells => CompatibilityMode >= 15;
}

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
            Shade = shade, Tint = tint
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
            _ => null
        };

        if (baseColor == null)
        {
            return null;
        }

        return ApplyColorTransforms(baseColor, transforms);
    }

    /// <summary>
    /// Applies all color transforms to a base color.
    /// Order matters: lumMod/satMod first (HSL), then shade/tint (RGB).
    /// </summary>
    static string ApplyColorTransforms(string hexColor, ColorTransforms transforms)
    {
        if (!TryParseHexColor(hexColor, out var r, out var g, out var b))
        {
            return hexColor;
        }

        // Apply HSL-based transforms first (lumMod, satMod, lumOff, satOff)
        if (transforms.LumMod.HasValue || transforms.SatMod.HasValue ||
            transforms.LumOff.HasValue || transforms.SatOff.HasValue)
        {
            RgbToHsl(r, g, b, out var h, out var s, out var l);

            // Apply saturation modulation (percentage)
            if (transforms.SatMod.HasValue)
            {
                s *= transforms.SatMod.Value / 100.0;
            }

            // Apply saturation offset (percentage points)
            if (transforms.SatOff.HasValue)
            {
                s += transforms.SatOff.Value / 100.0;
            }

            // Apply luminance modulation (percentage)
            if (transforms.LumMod.HasValue)
            {
                l *= transforms.LumMod.Value / 100.0;
            }

            // Apply luminance offset (percentage points)
            if (transforms.LumOff.HasValue)
            {
                l += transforms.LumOff.Value / 100.0;
            }

            // Clamp values
            s = Math.Clamp(s, 0.0, 1.0);
            l = Math.Clamp(l, 0.0, 1.0);

            HslToRgb(h, s, l, out r, out g, out b);
        }

        // Apply RGB-based transforms (shade, tint)
        // Per ECMA-376: shade darkens the color, tint lightens it
        // Values are in 0-100 percentage scale
        if (transforms.Shade is > 0)
        {
            var shade = transforms.Shade.Value;
            r = (byte)(r * shade / 255);
            g = (byte)(g * shade / 255);
            b = (byte)(b * shade / 255);
        }

        if (transforms.Tint.HasValue)
        {
            // In OOXML, themeTint value is inverted: higher value = less tinting (closer to original)
            // 0xFF (255) = no change, 0x00 (0) = full white
            // So we use (255 - tint) as the amount of white to add
            var tintAmount = 255 - transforms.Tint.Value;
            r = (byte)(r + (255 - r) * tintAmount / 255);
            g = (byte)(g + (255 - g) * tintAmount / 255);
            b = (byte)(b + (255 - b) * tintAmount / 255);
        }

        return $"{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>
    /// Converts RGB to HSL color space.
    /// </summary>
    static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;

        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;

        l = (max + min) / 2.0;

        if (delta == 0)
        {
            h = 0;
            s = 0;
        }
        else
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == rd)
            {
                h = ((gd - bd) / delta + (gd < bd ? 6 : 0)) / 6.0;
            }
            else if (max == gd)
            {
                h = ((bd - rd) / delta + 2) / 6.0;
            }
            else
            {
                h = ((rd - gd) / delta + 4) / 6.0;
            }
        }
    }

    /// <summary>
    /// Converts HSL to RGB color space.
    /// </summary>
    static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double rd, gd, bd;

        if (s == 0)
        {
            rd = gd = bd = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;

            rd = HueToRgb(p, q, h + 1.0 / 3.0);
            gd = HueToRgb(p, q, h);
            bd = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        r = (byte)Math.Round(rd * 255);
        g = (byte)Math.Round(gd * 255);
        b = (byte)Math.Round(bd * 255);
    }

    static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6.0)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 1.0 / 2.0)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + (q - p) * (2.0 / 3.0 - t) * 6;
        }

        return p;
    }

    static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (hex.Length != 6)
        {
            return false;
        }

        return byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out r) &&
               byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out g) &&
               byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out b);
    }
}

/// <summary>
/// Theme font definitions from the document theme.
/// </summary>
sealed class ThemeFonts
{
    /// <summary>Major font for headings (e.g., "Calibri Light").</summary>
    public string MajorFont { get; init; } = "Calibri Light";

    /// <summary>Minor font for body text (e.g., "Calibri").</summary>
    public string MinorFont { get; init; } = "Calibri";

    /// <summary>
    /// Resolves a theme font reference to the actual font name.
    /// </summary>
    /// <param name="themeFontName">Theme font reference (e.g., "majorHAnsi", "minorHAnsi")</param>
    /// <returns>The resolved font name, or null if not a recognized theme font reference.</returns>
    public string? ResolveFont(string themeFontName) =>
        // OpenXML ThemeFontValues stores raw XML values: majorHAnsi, minorHAnsi, etc.
        themeFontName.ToLowerInvariant() switch
        {
            "majorhansi" or "majorascii" or "majorbidi" or "majoreastasia" => MajorFont,
            "minorhansi" or "minorascii" or "minorbidi" or "minoreastasia" => MinorFont,
            _ => null
        };
}

/// <summary>
/// Content for a header or footer.
/// </summary>
sealed class HeaderFooterContent
{
    public required IReadOnlyList<DocumentElement> Elements { get; init; }
}

/// <summary>
/// Page settings extracted from the document.
/// </summary>
internal sealed record PageSettings
{
    /// <summary>Page width in points (1/72 inch). Defaults to A4; use DefaultPageSize for region-based defaults.</summary>
    public double WidthPoints { get; init; } = 595.28;

    /// <summary>Page height in points (1/72 inch). Defaults to A4; use DefaultPageSize for region-based defaults.</summary>
    public double HeightPoints { get; init; } = 841.89;

    /// <summary>Top margin in points.</summary>
    // 1 inch
    public double MarginTop { get; init; } = 72;

    /// <summary>Bottom margin in points.</summary>
    public double MarginBottom { get; init; } = 72;

    /// <summary>Left margin in points.</summary>
    public double MarginLeft { get; init; } = 72;

    /// <summary>Right margin in points.</summary>
    public double MarginRight { get; init; } = 72;

    /// <summary>Distance from top edge to header in points.</summary>
    // 0.5 inch
    public double HeaderDistance { get; init; } = 36;

    /// <summary>Distance from bottom edge to footer in points.</summary>
    // 0.5 inch
    public double FooterDistance { get; init; } = 36;

    /// <summary>Number of columns (1 = single column layout).</summary>
    public int ColumnCount { get; init; } = 1;

    /// <summary>Space between columns in points.</summary>
    // 0.5 inch default
    public double ColumnSpacing { get; init; } = 36;

    /// <summary>Line numbering settings for this section. Null if line numbers are disabled.</summary>
    public LineNumberSettings? LineNumbers { get; init; }

    /// <summary>
    /// Document grid line pitch in points (from w:docGrid/@w:linePitch).
    /// </summary>
    public double DocumentGridLinePitchPoints { get; init; }

    /// <summary>
    /// Count of w:lastRenderedPageBreak markers in the source document.
    /// </summary>
    public int LastRenderedPageBreakCount { get; init; }

    /// <summary>
    /// Page background color (hex). Null for white/transparent.
    /// </summary>
    public string? BackgroundColorHex { get; init; }

    public double ContentWidth => WidthPoints - MarginLeft - MarginRight;

    /// <summary>Width of a single column in points.</summary>
    public double ColumnWidth => ColumnCount > 1
        ? (ContentWidth - ColumnSpacing * (ColumnCount - 1)) / ColumnCount
        : ContentWidth;
}

/// <summary>
/// Settings for line numbering in a document section.
/// </summary>
internal sealed record LineNumberSettings
{
    /// <summary>
    /// Starting line number. Default is 1.
    /// </summary>
    public int Start { get; init; } = 1;

    /// <summary>
    /// Line number increment (1 = every line, 5 = every 5th line, etc.). Default is 1.
    /// </summary>
    public int CountBy { get; init; } = 1;

    /// <summary>
    /// Distance from text to line numbers in points. Default is 18 points (0.25 inch).
    /// </summary>
    public double DistancePoints { get; init; } = 18;

    /// <summary>
    /// When to restart line numbering.
    /// </summary>
    public LineNumberRestart Restart { get; init; } = LineNumberRestart.NewPage;
}

/// <summary>
/// Specifies when line numbering should restart.
/// </summary>
internal enum LineNumberRestart
{
    /// <summary>Line numbers restart at the beginning of each page.</summary>
    NewPage,

    /// <summary>Line numbers restart at the beginning of each section.</summary>
    NewSection,

    /// <summary>Line numbers are continuous throughout the document.</summary>
    Continuous
}

/// <summary>
/// Document-level hyphenation settings.
/// </summary>
internal sealed record HyphenationSettings
{
    /// <summary>
    /// When true, automatic hyphenation is enabled for the document.
    /// </summary>
    public bool AutoHyphenation { get; init; }

    /// <summary>
    /// The hyphenation zone in points. Words within this distance from the right margin
    /// may be hyphenated. Default is 18 points (0.25 inch).
    /// </summary>
    public double HyphenationZonePoints { get; init; } = 18;

    /// <summary>
    /// Maximum number of consecutive lines that can end with a hyphen.
    /// 0 means unlimited. Default is 0.
    /// </summary>
    public int ConsecutiveHyphenLimit { get; init; }

    /// <summary>
    /// When true, words in all capital letters will not be hyphenated.
    /// </summary>
    public bool DoNotHyphenateCaps { get; init; }
}

/// <summary>
/// Base class for document elements.
/// </summary>
internal abstract class DocumentElement;

/// <summary>
/// Represents a paragraph in the document.
/// </summary>
sealed class ParagraphElement : DocumentElement
{
    public required IReadOnlyList<Run> Runs { get; init; }
    public ParagraphProperties Properties { get; init; } = new();
}

/// <summary>
/// Paragraph-level properties.
/// </summary>
internal sealed record ParagraphProperties
{
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public double SpacingBeforePoints { get; init; }
    public double SpacingAfterPoints { get; init; } // OpenXML default when not specified

    /// <summary>
    /// Line spacing multiplier for Auto mode (1.0 = single, 1.5 = 1.5 lines, 2.0 = double).
    /// Only used when LineSpacingRule is Auto.
    /// </summary>
    public double LineSpacingMultiplier { get; init; } = 1.08;

    /// <summary>
    /// Fixed line spacing in points for Exactly/AtLeast modes.
    /// Only used when LineSpacingRule is Exactly or AtLeast.
    /// </summary>
    public double LineSpacingPoints { get; init; }

    /// <summary>
    /// The line spacing rule to apply.
    /// </summary>
    public LineSpacingRule LineSpacingRule { get; init; } = LineSpacingRule.Auto;

    public double FirstLineIndentPoints { get; init; }
    public double LeftIndentPoints { get; init; }
    public double RightIndentPoints { get; init; }

    /// <summary>
    /// Hanging indent in points. When positive, the first line is at LeftIndent
    /// and subsequent lines are further indented by this amount.
    /// OpenXML: w:ind/@w:hanging
    /// </summary>
    public double HangingIndentPoints { get; init; }

    /// <summary>
    /// When true, spacing before/after is collapsed between this paragraph and adjacent
    /// paragraphs that also have contextual spacing enabled.
    /// </summary>
    public bool ContextualSpacing { get; init; }

    /// <summary>
    /// When true, line numbers are suppressed for this paragraph.
    /// </summary>
    public bool SuppressLineNumbers { get; init; }

    /// <summary>
    /// When true, automatic hyphenation is suppressed for this paragraph.
    /// </summary>
    public bool SuppressAutoHyphens { get; init; }

    /// <summary>
    /// Numbering/bullet information for this paragraph. Null if not a list item.
    /// </summary>
    public NumberingInfo? Numbering { get; init; }

    /// <summary>
    /// When true, all lines of this paragraph must be kept on the same page.
    /// If the paragraph doesn't fit, move the entire paragraph to the next page.
    /// </summary>
    public bool KeepLines { get; init; }

    /// <summary>
    /// When true, this paragraph must be kept on the same page as the next paragraph.
    /// Prevents page breaks between this paragraph and the following one.
    /// </summary>
    public bool KeepNext { get; init; }

    /// <summary>
    /// When true, prevents widow/orphan lines at page breaks.
    /// A widow is the last line of a paragraph appearing alone at the top of a page.
    /// An orphan is the first line of a paragraph appearing alone at the bottom of a page.
    /// </summary>
    // Default is true per OpenXML spec
    public bool WidowControl { get; init; } = true;

    /// <summary>
    /// When true, forces a page break before this paragraph.
    /// </summary>
    public bool PageBreakBefore { get; init; }

    /// <summary>
    /// Font size in points for the paragraph mark (used for empty paragraphs).
    /// Null means use the default 12pt.
    /// </summary>
    public double? ParagraphMarkFontSizePoints { get; init; }

    /// <summary>
    /// Background/shading color for the paragraph (from w:shd element in w:pPr).
    /// </summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>
    /// The style ID of this paragraph (e.g., "Heading1", "Normal").
    /// Used for contextual spacing which only collapses spacing between paragraphs of the same style.
    /// </summary>
    public string? StyleId { get; init; }
}

/// <summary>
/// Specifies how line spacing is calculated.
/// </summary>
internal enum LineSpacingRule
{
    /// <summary>
    /// Automatic/Multiple: Line spacing is a multiple of the line height (e.g., 1.0, 1.5, 2.0).
    /// </summary>
    Auto,

    /// <summary>
    /// Exact: Line spacing is exactly the specified value in points.
    /// </summary>
    Exactly,

    /// <summary>
    /// At Least: Line spacing is at least the specified value in points.
    /// </summary>
    AtLeast
}

/// <summary>
/// Numbering/bullet information for a paragraph.
/// </summary>
internal sealed record NumberingInfo
{
    /// <summary>
    /// The text to display before the paragraph content (e.g., "•", "1.", "A)").
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Font family for the numbering text. Null means use paragraph font.
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>
    /// The indent position for the number/bullet in points (from left margin).
    /// </summary>
    public double IndentPoints { get; init; }

    /// <summary>
    /// The hanging indent (space between number and text) in points.
    /// </summary>
    public double HangingIndentPoints { get; init; }
}

internal enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify
}

/// <summary>
/// A run of text with consistent formatting. Can also represent an inline image.
/// </summary>
sealed class Run
{
    public required string Text { get; init; }
    public RunProperties Properties { get; init; } = new();

    /// <summary>Inline image data (when the run represents an inline image).</summary>
    public byte[]? InlineImageData { get; init; }

    /// <summary>Width of inline image in points.</summary>
    public double InlineImageWidthPoints { get; init; }

    /// <summary>Height of inline image in points.</summary>
    public double InlineImageHeightPoints { get; init; }

    /// <summary>Content type of inline image (e.g., "image/png", "image/svg+xml").</summary>
    public string? InlineImageContentType { get; init; }
}

/// <summary>
/// Run-level text properties.
/// </summary>
internal sealed record RunProperties
{
    public string FontFamily { get; init; } = "Aptos";
    public double FontSizePoints { get; init; } = 11;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public bool AllCaps { get; init; }
    public string? ColorHex { get; init; } // null = black

    /// <summary>
    /// Background/shading color for text (from w:shd element).
    /// </summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>
    /// Vertical alignment for subscript/superscript text.
    /// </summary>
    public VerticalRunAlignment VerticalAlignment { get; init; } = VerticalRunAlignment.Baseline;
}

/// <summary>
/// Vertical text alignment for subscript and superscript.
/// </summary>
internal enum VerticalRunAlignment
{
    /// <summary>Normal baseline alignment.</summary>
    Baseline,

    /// <summary>Superscript - raised and typically smaller.</summary>
    Superscript,

    /// <summary>Subscript - lowered and typically smaller.</summary>
    Subscript
}

/// <summary>
/// Represents an explicit page break.
/// </summary>
sealed class PageBreakElement : DocumentElement;

/// <summary>
/// Represents a column break (moves content to next column in multi-column layouts).
/// </summary>
sealed class ColumnBreakElement : DocumentElement;

/// <summary>
/// Represents a line break (soft return) within a paragraph.
/// </summary>
sealed class LineBreakElement : DocumentElement;

/// <summary>
/// Represents a section break with various types.
/// </summary>
sealed class SectionBreakElement : DocumentElement
{
    public required SectionBreakType BreakType { get; init; }

    /// <summary>
    /// Optional new section properties (page size, margins, columns, etc.)
    /// </summary>
    public PageSettings? NewSectionSettings { get; init; }
}

/// <summary>
/// Types of section breaks.
/// </summary>
internal enum SectionBreakType
{
    /// <summary>Starts new section on the next page.</summary>
    NextPage,

    /// <summary>Starts new section on the same page (continuous).</summary>
    Continuous,

    /// <summary>Starts new section on the next even-numbered page.</summary>
    EvenPage,

    /// <summary>Starts new section on the next odd-numbered page.</summary>
    OddPage,

    /// <summary>Starts new section in the next column (for multi-column layouts).</summary>
    NextColumn
}

/// <summary>
/// Represents an inline image.
/// </summary>
sealed class ImageElement : DocumentElement
{
    public required byte[] ImageData { get; init; }
    public required double WidthPoints { get; init; }
    public required double HeightPoints { get; init; }
    public string? ContentType { get; init; }
}

/// <summary>
/// Represents a floating/anchored image positioned relative to page or paragraph.
/// </summary>
sealed class FloatingImageElement : DocumentElement
{
    public required byte[] ImageData { get; init; }
    public required double WidthPoints { get; init; }
    public required double HeightPoints { get; init; }
    public string? ContentType { get; init; }

    /// <summary>Horizontal position in points from the anchor reference.</summary>
    public double HorizontalPositionPoints { get; init; }

    /// <summary>Vertical position in points from the anchor reference.</summary>
    public double VerticalPositionPoints { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to.</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>How text wraps around this image.</summary>
    public WrapType WrapType { get; init; } = WrapType.None;

    /// <summary>Whether this image is behind text (vs in front).</summary>
    public bool BehindText { get; init; }
}

/// <summary>
/// Horizontal anchor reference for floating elements.
/// </summary>
internal enum HorizontalAnchor
{
    /// <summary>Position relative to page edge.</summary>
    Page,
    /// <summary>Position relative to page margins.</summary>
    Margin,
    /// <summary>Position relative to column.</summary>
    Column,
    /// <summary>Position relative to character.</summary>
    Character
}

/// <summary>
/// Vertical anchor reference for floating elements.
/// </summary>
internal enum VerticalAnchor
{
    /// <summary>Position relative to page edge.</summary>
    Page,
    /// <summary>Position relative to page margins.</summary>
    Margin,
    /// <summary>Position relative to paragraph.</summary>
    Paragraph,
    /// <summary>Position relative to line.</summary>
    Line
}

/// <summary>
/// Text wrapping type for floating elements.
/// </summary>
internal enum WrapType
{
    /// <summary>No wrapping - image floats over/under text.</summary>
    None,
    /// <summary>Text wraps in a square around the image.</summary>
    Square,
    /// <summary>Text wraps tightly around the image outline.</summary>
    Tight,
    /// <summary>Text wraps through the image.</summary>
    Through,
    /// <summary>Text appears above and below but not beside.</summary>
    TopAndBottom
}

/// <summary>
/// Represents a floating/positioned text box (shape with text content).
/// </summary>
sealed class FloatingTextBoxElement : DocumentElement
{
    /// <summary>Text content of the text box.</summary>
    public required IReadOnlyList<DocumentElement> Content { get; init; }

    /// <summary>Width in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Horizontal position in points from the anchor reference.</summary>
    public double HorizontalPositionPoints { get; init; }

    /// <summary>Vertical position in points from the anchor reference.</summary>
    public double VerticalPositionPoints { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to.</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>How text wraps around this text box.</summary>
    public WrapType WrapType { get; init; } = WrapType.None;

    /// <summary>Whether this text box is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Background color (hex). Null for transparent.</summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>Rotation in degrees (clockwise). 0 = no rotation.</summary>
    public double RotationDegrees { get; init; }
}

/// <summary>
/// Represents a floating shape (solid-fill or image-fill, typically used as background).
/// </summary>
sealed class FloatingShapeElement : DocumentElement
{
    /// <summary>Width in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Horizontal position in points from the anchor reference.</summary>
    public double HorizontalPositionPoints { get; init; }

    /// <summary>Vertical position in points from the anchor reference.</summary>
    public double VerticalPositionPoints { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to.</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>Whether this shape is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Fill color (hex RGB without #, e.g. "FF0000" for red). Null if using image fill.</summary>
    public string? FillColorHex { get; init; }

    /// <summary>Image data for image-filled shapes. Null if using solid color fill.</summary>
    public byte[]? ImageData { get; init; }

    /// <summary>Content type of the image (e.g., "image/jpeg"). Null if using solid color fill.</summary>
    public string? ImageContentType { get; init; }
}

/// <summary>
/// Represents a floating/positioned WordArt text element with special formatting.
/// Unlike WordArtElement (inline), this is positioned at absolute coordinates and doesn't consume flow space.
/// </summary>
sealed class FloatingWordArtElement : DocumentElement
{
    /// <summary>The text content of the WordArt.</summary>
    public required string Text { get; init; }

    /// <summary>Width in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Horizontal position in points from the anchor reference.</summary>
    public double HorizontalPositionPoints { get; init; }

    /// <summary>Vertical position in points from the anchor reference.</summary>
    public double VerticalPositionPoints { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to.</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>Whether this WordArt is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Font family for the text.</summary>
    public string FontFamily { get; init; } = "Aptos";

    /// <summary>Font size in points.</summary>
    public double FontSizePoints { get; init; } = 36;

    /// <summary>Whether the text is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether the text is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Text fill color (hex). Null for default black.</summary>
    public string? FillColorHex { get; init; }

    /// <summary>Text outline color (hex). Null for no outline.</summary>
    public string? OutlineColorHex { get; init; }

    /// <summary>Text outline width in points.</summary>
    public double OutlineWidthPoints { get; init; }

    /// <summary>Whether the text has a shadow effect.</summary>
    public bool HasShadow { get; init; }

    /// <summary>Whether the text has a reflection effect.</summary>
    public bool HasReflection { get; init; }

    /// <summary>Whether the text has a glow effect.</summary>
    public bool HasGlow { get; init; }

    /// <summary>The preset text transform/warp type.</summary>
    public WordArtTransform Transform { get; init; } = WordArtTransform.None;
}

/// <summary>
/// Represents a WordArt text element with special formatting.
/// </summary>
sealed class WordArtElement : DocumentElement
{
    /// <summary>The text content of the WordArt.</summary>
    public required string Text { get; init; }

    /// <summary>Width in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Font family for the text.</summary>
    public string FontFamily { get; init; } = "Aptos";

    /// <summary>Font size in points.</summary>
    public double FontSizePoints { get; init; } = 36;

    /// <summary>Whether the text is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether the text is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Text fill color (hex). Null for default black.</summary>
    public string? FillColorHex { get; init; }

    /// <summary>Text outline color (hex). Null for no outline.</summary>
    public string? OutlineColorHex { get; init; }

    /// <summary>Text outline width in points.</summary>
    public double OutlineWidthPoints { get; init; }

    /// <summary>Whether the text has a shadow effect.</summary>
    public bool HasShadow { get; init; }

    /// <summary>Whether the text has a reflection effect.</summary>
    public bool HasReflection { get; init; }

    /// <summary>Whether the text has a glow effect.</summary>
    public bool HasGlow { get; init; }

    /// <summary>The preset text transform/warp type.</summary>
    public WordArtTransform Transform { get; init; } = WordArtTransform.None;
}

/// <summary>
/// WordArt text transform/warp presets.
/// </summary>
internal enum WordArtTransform
{
    /// <summary>No transform applied.</summary>
    None,

    /// <summary>Text follows an arc path upward.</summary>
    ArchUp,

    /// <summary>Text follows an arc path downward.</summary>
    ArchDown,

    /// <summary>Text arranged in a circle.</summary>
    Circle,

    /// <summary>Text with wave effect.</summary>
    Wave,

    /// <summary>Text with chevron pointing up.</summary>
    ChevronUp,

    /// <summary>Text with chevron pointing down.</summary>
    ChevronDown,

    /// <summary>Text slanted upward.</summary>
    SlantUp,

    /// <summary>Text slanted downward.</summary>
    SlantDown,

    /// <summary>Text in a triangle shape.</summary>
    Triangle,

    /// <summary>Text with fade effect to right.</summary>
    FadeRight,

    /// <summary>Text with fade effect to left.</summary>
    FadeLeft
}

/// <summary>
/// Represents an ink drawing (pen/handwriting annotation).
/// </summary>
sealed class InkElement : DocumentElement
{
    /// <summary>Width of the ink drawing in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height of the ink drawing in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Collection of ink strokes/traces.</summary>
    public required IReadOnlyList<InkStroke> Strokes { get; init; }
}

/// <summary>
/// Represents a single ink stroke (trace).
/// </summary>
sealed class InkStroke
{
    /// <summary>Points that make up this stroke.</summary>
    public required IReadOnlyList<InkPoint> Points { get; init; }

    /// <summary>Stroke color in hex format.</summary>
    public string ColorHex { get; init; } = "000000";

    /// <summary>Stroke width in points.</summary>
    public double WidthPoints { get; init; } = 1.5;

    /// <summary>Stroke transparency (0 = opaque, 255 = fully transparent).</summary>
    public byte Transparency { get; init; }

    /// <summary>Pen tip shape.</summary>
    public InkPenTip PenTip { get; init; } = InkPenTip.Ellipse;

    /// <summary>Whether the stroke represents a highlighter (semi-transparent).</summary>
    public bool IsHighlighter { get; init; }
}

/// <summary>
/// Represents a point in an ink stroke.
/// </summary>
internal sealed record InkPoint
{
    /// <summary>X coordinate in points.</summary>
    public required double X { get; init; }

    /// <summary>Y coordinate in points.</summary>
    public required double Y { get; init; }

    /// <summary>Optional pressure value (0.0 to 1.0).</summary>
    public double? Pressure { get; init; }
}

/// <summary>
/// Pen tip shapes for ink strokes.
/// </summary>
internal enum InkPenTip
{
    /// <summary>Elliptical pen tip (default).</summary>
    Ellipse,

    /// <summary>Rectangular pen tip.</summary>
    Rectangle
}

/// <summary>
/// Base class for form field elements.
/// </summary>
internal abstract class FormFieldElement : DocumentElement
{
    /// <summary>Name/bookmark of the form field.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the field is enabled for user input.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Represents a text input form field.
/// </summary>
sealed class TextFormFieldElement : FormFieldElement
{
    /// <summary>The current text value.</summary>
    public string Value { get; init; } = "";

    /// <summary>Default/placeholder text.</summary>
    public string? DefaultText { get; init; }

    /// <summary>Maximum character length (0 = unlimited).</summary>
    public int MaxLength { get; init; }

    /// <summary>The type of text input.</summary>
    public TextFormFieldType TextType { get; init; } = TextFormFieldType.Regular;

    /// <summary>Width of the field in points (for rendering).</summary>
    public double WidthPoints { get; init; } = 100;
}

/// <summary>
/// Types of text form fields.
/// </summary>
internal enum TextFormFieldType
{
    /// <summary>Regular text input.</summary>
    Regular,

    /// <summary>Number input.</summary>
    Number,

    /// <summary>Date input.</summary>
    Date,

    /// <summary>Current date (auto-filled).</summary>
    CurrentDate,

    /// <summary>Current time (auto-filled).</summary>
    CurrentTime,

    /// <summary>Calculated field.</summary>
    Calculated
}

/// <summary>
/// Represents a checkbox form field.
/// </summary>
sealed class CheckBoxFormFieldElement : FormFieldElement
{
    /// <summary>Whether the checkbox is checked.</summary>
    public bool Checked { get; init; }

    /// <summary>Default checked state.</summary>
    public bool DefaultChecked { get; init; }

    /// <summary>Size of the checkbox in points (0 = auto).</summary>
    public double SizePoints { get; init; }
}

/// <summary>
/// Represents a drop-down list form field.
/// </summary>
sealed class DropDownFormFieldElement : FormFieldElement
{
    /// <summary>Available options in the drop-down.</summary>
    public required IReadOnlyList<string> Items { get; init; }

    /// <summary>Index of the currently selected item (0-based).</summary>
    public int SelectedIndex { get; init; }

    /// <summary>Width of the field in points (for rendering).</summary>
    public double WidthPoints { get; init; } = 100;
}

/// <summary>
/// Represents a content control (structured document tag).
/// </summary>
sealed class ContentControlElement : DocumentElement
{
    /// <summary>The type of content control.</summary>
    public ContentControlType ControlType { get; init; } = ContentControlType.RichText;

    /// <summary>Tag name for the control.</summary>
    public string? Tag { get; init; }

    /// <summary>Title/label for the control.</summary>
    public string? Title { get; init; }

    /// <summary>Placeholder text when empty.</summary>
    public string? PlaceholderText { get; init; }

    /// <summary>Current text content (plain text for backward compatibility).</summary>
    public string Content { get; init; } = "";

    /// <summary>Styled runs within the content control (preserves formatting).</summary>
    public IReadOnlyList<Run>? Runs { get; init; }

    /// <summary>For checkbox controls, whether it's checked.</summary>
    public bool? Checked { get; init; }

    /// <summary>For drop-down/combo controls, the list items.</summary>
    public IReadOnlyList<string>? ListItems { get; init; }

    /// <summary>For date controls, the selected date.</summary>
    public DateTime? DateValue { get; init; }

    /// <summary>Width hint in points (for rendering).</summary>
    public double WidthPoints { get; init; } = 100;
}

/// <summary>
/// Types of content controls.
/// </summary>
internal enum ContentControlType
{
    /// <summary>Rich text content control (allows formatting).</summary>
    RichText,

    /// <summary>Plain text content control.</summary>
    PlainText,

    /// <summary>Checkbox content control.</summary>
    CheckBox,

    /// <summary>Combo box (editable drop-down).</summary>
    ComboBox,

    /// <summary>Drop-down list (select only).</summary>
    DropDownList,

    /// <summary>Date picker.</summary>
    Date,

    /// <summary>Picture content control.</summary>
    Picture
}

/// <summary>
/// Represents a table in the document.
/// </summary>
sealed class TableElement : DocumentElement
{
    public required IReadOnlyList<TableRow> Rows { get; init; }
    public TableProperties Properties { get; init; } = new();
}

/// <summary>
/// Table-level properties.
/// </summary>
internal sealed record TableProperties
{
    /// <summary>Whether this table is a floating table with absolute positioning (w:tblpPr).</summary>
    public bool IsFloating { get; init; }

    /// <summary>Default borders for cells (from w:tblBorders). Null means no borders.</summary>
    public CellBorders? DefaultBorders { get; init; }

    /// <summary>Default cell padding (used when cell doesn't specify its own).</summary>
    public CellSpacing DefaultCellPadding { get; init; } = new();

    /// <summary>Default cell margins (used when cell doesn't specify its own).</summary>
    public CellSpacing DefaultCellMargin { get; init; } = new();

    /// <summary>Table indent from left margin (can be negative).</summary>
    public double IndentPoints { get; init; }

    /// <summary>Column widths from the table grid (w:tblGrid), in points. Null if not specified.</summary>
    public IReadOnlyList<double>? GridColumnWidths { get; init; }
}

/// <summary>
/// Represents a row in a table.
/// </summary>
sealed class TableRow
{
    public required IReadOnlyList<TableCell> Cells { get; init; }

    /// <summary>
    /// Explicit row height in points, if specified in the document.
    /// Null means the height should be calculated from content.
    /// </summary>
    public double? HeightPoints { get; init; }

    /// <summary>
    /// Whether the row height is exact (true) or minimum (false).
    /// When exact, the row will be exactly HeightPoints tall.
    /// When minimum, the row will be at least HeightPoints tall.
    /// </summary>
    public bool IsExactHeight { get; init; }
}

/// <summary>
/// Represents a cell in a table row.
/// </summary>
sealed class TableCell
{
    public required IReadOnlyList<DocumentElement> Content { get; init; }
    public TableCellProperties Properties { get; init; } = new();
}

/// <summary>
/// Vertical alignment options for table cells.
/// </summary>
internal enum CellVerticalAlignment
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// Vertical merge state for table cells.
/// </summary>
internal enum VerticalMergeType
{
    /// <summary>Cell is not part of a vertical merge.</summary>
    None,
    /// <summary>Cell starts a vertical merge (spans downward).</summary>
    Restart,
    /// <summary>Cell continues a vertical merge from above (should not be rendered separately).</summary>
    Continue
}

/// <summary>
/// Cell-level properties.
/// </summary>
internal sealed record TableCellProperties
{
    public double? WidthPoints { get; init; }
    public string? BackgroundColorHex { get; init; }

    /// <summary>Cell padding (inset from border to content). Null means use table default.</summary>
    public CellSpacing? Padding { get; init; }

    /// <summary>Cell margin (space outside the border). Null means use table default.</summary>
    public CellSpacing? Margin { get; init; }

    /// <summary>Per-edge border specifications. Null means use table default borders.</summary>
    public CellBorders? Borders { get; init; }

    /// <summary>Number of grid columns this cell spans. Default is 1.</summary>
    public int GridSpan { get; init; } = 1;

    /// <summary>Vertical alignment of content within the cell. Default is Top.</summary>
    public CellVerticalAlignment VerticalAlignment { get; init; } = CellVerticalAlignment.Top;

    /// <summary>Vertical merge state for this cell. Default is None.</summary>
    public VerticalMergeType VerticalMerge { get; init; } = VerticalMergeType.None;
}

/// <summary>
/// Represents spacing (padding or margin) with individual values for each side.
/// </summary>
internal sealed record CellSpacing
{
    // Word's default cell margins are 0
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }
    public double Left { get; init; }

    public CellSpacing() { }

    public CellSpacing(double all) =>
        Top = Right = Bottom = Left = all;

    public CellSpacing(double vertical, double horizontal)
    {
        Top = Bottom = vertical;
        Left = Right = horizontal;
    }

    public CellSpacing(double top, double right, double bottom, double left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    /// <summary>Total horizontal spacing (left + right).</summary>
    public double Horizontal => Left + Right;

    /// <summary>Total vertical spacing (top + bottom).</summary>
    public double Vertical => Top + Bottom;
}

/// <summary>
/// Represents a single border edge (top, right, bottom, or left).
/// </summary>
internal sealed record BorderEdge
{
    /// <summary>Whether this border edge should be rendered.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Border width in points.</summary>
    public double WidthPoints { get; init; } = 0.5;

    /// <summary>Border color as hex string (e.g., "000000").</summary>
    public string? ColorHex { get; init; } = "000000";

    public static BorderEdge None => new() { IsVisible = false };
    public static BorderEdge Default => new() { IsVisible = true, WidthPoints = 0.5, ColorHex = "000000" };
}

/// <summary>
/// Represents borders for all four edges of a cell.
/// </summary>
internal sealed record CellBorders
{
    public BorderEdge Top { get; init; } = BorderEdge.None;
    public BorderEdge Right { get; init; } = BorderEdge.None;
    public BorderEdge Bottom { get; init; } = BorderEdge.None;
    public BorderEdge Left { get; init; } = BorderEdge.None;

    /// <summary>Returns true if any border edge is visible.</summary>
    public bool HasAnyBorder => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;

    public static CellBorders All => new()
    {
        Top = BorderEdge.Default,
        Right = BorderEdge.Default,
        Bottom = BorderEdge.Default,
        Left = BorderEdge.Default
    };
}
