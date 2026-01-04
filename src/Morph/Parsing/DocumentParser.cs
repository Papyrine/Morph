using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableProperties = DocumentFormat.OpenXml.Wordprocessing.TableProperties;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

namespace WordRender;

/// <summary>
/// Parses DOCX files using OpenXML.
/// </summary>
public sealed class DocumentParser
{
    // Conversion constants
    const double twipsPerPoint = 20.0;
    const double emusPerPoint = 914400.0 / 72.0; // EMUs per point

    // Theme colors for the current document being parsed
    ThemeColors? currentThemeColors;

    // Theme fonts for the current document being parsed
    ThemeFonts? currentThemeFonts;

    // Section transitions: for a given sectPr (end of a section), what settings apply to the next section?
    Dictionary<SectionProperties, PageSettings?>? nextSectionSettings;

    int lastRenderedPageBreakCount;

    // Style definitions cached during parsing (styleId -> full run properties)
    Dictionary<string, RunProperties>? styleRunProperties;

    // Style paragraph properties cached during parsing (styleId -> paragraph properties)
    Dictionary<string, ParagraphProperties>? styleParagraphProperties;

    // Numbering definitions: numId -> ilvl -> NumberingLevelDefinition
    Dictionary<int, Dictionary<int, NumberingLevelDefinition>>? numberingDefinitions;

    // Style numbering: styleId -> (numId, ilvl) for styles that define numbering
    Dictionary<string, (int numId, int ilvl)>? styleNumbering;

    // Table style borders cached during parsing (styleId -> CellBorders)
    Dictionary<string, CellBorders>? tableStyleBorders;

    // Document-level background color (applies to all pages)
    string? documentBackgroundColor;

    // Document default paragraph spacing (from docDefaults/pPrDefault or Word built-in defaults)
    double defaultSpacingAfterPoints = 8; // Word's built-in default when no styles.xml

    public ParsedDocument Parse(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Parse(stream);
    }

    public ParsedDocument Parse(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        return ParseDocument(doc);
    }

    ParsedDocument ParseDocument(WordprocessingDocument doc)
    {
        var mainPart = doc.MainDocumentPart
                       ?? throw new InvalidOperationException("Document has no main part");

        var body = mainPart.Document.Body
                   ?? throw new InvalidOperationException("Document has no body");

        lastRenderedPageBreakCount = body.Descendants<LastRenderedPageBreak>().Count();

        // Extract and store theme colors early (needed for background color and other theme-resolved values)
        currentThemeColors = ThemeParser.ExtractThemeColors(mainPart);

        // Extract and store theme fonts for use during parsing
        currentThemeFonts = ThemeParser.ExtractThemeFonts(mainPart);

        // Extract document-level background color (w:background element)
        documentBackgroundColor = ExtractDocumentBackgroundColor(mainPart.Document);

        // Extract document default spacing from pPrDefault
        defaultSpacingAfterPoints = ExtractDefaultSpacingAfter(mainPart);

        // SectionProperties (sectPr) describes the section it belongs to, and the section break is stored
        // on the last paragraph of the section. The next section's properties are stored in the next sectPr.
        var sectionPropsList = body.Descendants<SectionProperties>().ToList();
        nextSectionSettings = new();
        for (var i = 0; i < sectionPropsList.Count; i++)
        {
            var current = sectionPropsList[i];
            var next = i + 1 < sectionPropsList.Count
                ? ExtractPageSettings(sectionPropsList[i + 1])
                : null;
            nextSectionSettings[current] = next;
        }

        var pageSettings = sectionPropsList.Count > 0
            ? ExtractPageSettings(sectionPropsList[0])
            : new();

        // Extract style run properties (with theme color resolution)
        styleRunProperties = ExtractStyleRunProperties(mainPart);

        // Extract style paragraph properties (line spacing, spacing before/after, etc.)
        styleParagraphProperties = ExtractStyleParagraphProperties(mainPart);
        // Extract numbering definitions from numbering.xml
        numberingDefinitions = ExtractNumberingDefinitions(mainPart);

        // Extract style numbering (styles that have numPr)
        styleNumbering = ExtractStyleNumbering(mainPart);

        // Extract table style borders
        tableStyleBorders = ExtractTableStyleBorders(mainPart);

        var elements = ParseElements(body, mainPart);
        var header = ExtractHeader(body, mainPart);
        var footer = ExtractFooter(body, mainPart);
        var hyphenation = ExtractHyphenationSettings(mainPart);
        var compatibility = ExtractCompatibilitySettings(mainPart);

        return new()
        {
            PageSettings = pageSettings,
            Elements = elements,
            Header = header,
            Footer = footer,
            Hyphenation = hyphenation,
            ThemeColors = currentThemeColors,
            ThemeFonts = currentThemeFonts,
            Compatibility = compatibility
        };
    }

    Dictionary<string, RunProperties> ExtractStyleRunProperties(MainDocumentPart mainPart)
    {
        var styleProps = new Dictionary<string, RunProperties>(StringComparer.OrdinalIgnoreCase);

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return styleProps;
        }

        // Extract docDefaults run properties as the base defaults
        var defaultFontFamily = "Aptos";
        var defaultFontSize = 11.0;

        var docDefaults = stylesPart.Styles.DocDefaults;
        if (docDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle != null)
        {
            var rPrDefault = docDefaults.RunPropertiesDefault.RunPropertiesBaseStyle;

            // Font family from docDefaults
            var defaultFonts = rPrDefault.GetFirstChild<RunFonts>();
            if (defaultFonts != null)
            {
                // Try theme font reference first
                if (defaultFonts.AsciiTheme?.HasValue == true && currentThemeFonts != null)
                {
                    var themeValue = ((IEnumValue) defaultFonts.AsciiTheme.Value).Value;
                    var resolvedFont = currentThemeFonts.ResolveFont(themeValue);
                    if (resolvedFont != null)
                    {
                        defaultFontFamily = resolvedFont;
                    }
                }
                // Fall back to direct font name
                else if (defaultFonts.Ascii?.HasValue == true)
                {
                    defaultFontFamily = defaultFonts.Ascii.Value!;
                }
            }

            // Font size from docDefaults
            var defaultSz = rPrDefault.GetFirstChild<FontSize>();
            if (defaultSz?.Val?.HasValue == true)
            {
                defaultFontSize = double.Parse(defaultSz.Val.Value!) / 2.0;
            }
        }

        // First pass: collect all styles and their basedOn references
        var styles = stylesPart.Styles.Elements<Style>().ToList();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build a set of all style IDs that exist in the document
        var existingStyleIds = new HashSet<string>(
            styles.Select(s => s.StyleId?.Value).Where(id => id != null)!,
            StringComparer.OrdinalIgnoreCase);

        // Process styles with proper inheritance - may need multiple passes
        // to handle chains like: Title -> Normal -> (base)
        int lastCount;
        do
        {
            lastCount = processed.Count;
            foreach (var style in styles)
            {
                var styleId = style.StyleId?.Value;
                if (styleId == null || processed.Contains(styleId))
                {
                    continue;
                }

                // Check if base style needs to be processed first
                var basedOnId = style.BasedOn?.Val?.Value;
                // Only wait for the base style if it actually exists in this document
                // If basedOn references a non-existent style, process without waiting
                if (basedOnId != null && existingStyleIds.Contains(basedOnId) && !processed.Contains(basedOnId))
                {
                    // Base style not yet processed, skip for now
                    continue;
                }

                // Get base style properties if available
                RunProperties? baseProps = null;
                if (basedOnId != null)
                {
                    styleProps.TryGetValue(basedOnId, out baseProps);
                }

                // Check for run properties in the style
                var runProps = style.StyleRunProperties;

                // Start with base style properties or docDefaults
                var fontFamily = baseProps?.FontFamily ?? defaultFontFamily;
                var fontSize = baseProps?.FontSizePoints ?? defaultFontSize;
                var bold = baseProps?.Bold ?? false;
                var italic = baseProps?.Italic ?? false;
                var underline = baseProps?.Underline ?? false;
                var strikethrough = baseProps?.Strikethrough ?? false;
                var allCaps = baseProps?.AllCaps ?? false;
                var color = baseProps?.ColorHex;
                var backgroundColor = baseProps?.BackgroundColorHex;

                // If no run properties, still save inherited properties
                if (runProps == null)
                {
                    styleProps[styleId] = new()
                    {
                        FontFamily = fontFamily,
                        FontSizePoints = fontSize,
                        Bold = bold,
                        Italic = italic,
                        Underline = underline,
                        Strikethrough = strikethrough,
                        AllCaps = allCaps,
                        ColorHex = color,
                        BackgroundColorHex = backgroundColor
                    };
                    processed.Add(styleId);
                    continue;
                }

                // Font
                var runFonts = runProps.GetFirstChild<RunFonts>();
                if (runFonts != null)
                {
                    // First try theme font reference
                    if (runFonts.AsciiTheme?.HasValue == true && currentThemeFonts != null)
                    {
                        // ThemeFontValues implements IEnumValue - access Value property through interface
                        var themeValue = ((IEnumValue) runFonts.AsciiTheme.Value).Value;
                        var resolvedFont = currentThemeFonts.ResolveFont(themeValue);
                        if (resolvedFont != null)
                        {
                            fontFamily = resolvedFont;
                        }
                    }
                    // Fall back to direct font name
                    else if (runFonts.Ascii?.HasValue == true)
                    {
                        fontFamily = runFonts.Ascii.Value!;
                    }
                }

                // Font size (in half-points)
                var fontSizeElement = runProps.GetFirstChild<FontSize>();
                if (fontSizeElement?.Val?.HasValue == true)
                {
                    fontSize = double.Parse(fontSizeElement.Val.Value!) / 2.0;
                }

                // Bold
                var boldElement = runProps.GetFirstChild<Bold>();
                if (boldElement != null)
                {
                    bold = boldElement.Val?.Value != false;
                }

                // Italic
                var italicElement = runProps.GetFirstChild<Italic>();
                if (italicElement != null)
                {
                    italic = italicElement.Val?.Value != false;
                }

                // Underline
                var underlineElement = runProps.GetFirstChild<Underline>();
                if (underlineElement != null && underlineElement.Val?.Value != UnderlineValues.None)
                {
                    underline = true;
                }

                // Strikethrough
                var strikeElement = runProps.GetFirstChild<Strike>();
                if (strikeElement != null)
                {
                    strikethrough = strikeElement.Val?.Value != false;
                }

                // All caps
                var capsElement = runProps.GetFirstChild<Caps>();
                if (capsElement != null)
                {
                    allCaps = capsElement.Val?.Value != false;
                }

                // Color - check for theme color first, then direct value as fallback
                var colorElement = runProps.GetFirstChild<Color>();
                if (colorElement != null)
                {
                    var themeColor = colorElement.ThemeColor?.Value;
                    if (themeColor != null && currentThemeColors != null)
                    {
                        byte? shade = null;
                        byte? tint = null;

                        if (colorElement.ThemeShade?.HasValue == true)
                        {
                            if (byte.TryParse(colorElement.ThemeShade.Value, NumberStyles.HexNumber, null, out var shadeVal))
                            {
                                shade = shadeVal;
                            }
                        }

                        if (colorElement.ThemeTint?.HasValue == true)
                        {
                            if (byte.TryParse(colorElement.ThemeTint.Value, NumberStyles.HexNumber, null, out var tintVal))
                            {
                                tint = tintVal;
                            }
                        }

                        // Use IEnumValue.Value instead of ToString() to get actual enum value string
                        var themeColorValue = ((IEnumValue) themeColor).Value;
                        color = currentThemeColors.ResolveColor(themeColorValue ?? "", shade, tint);
                    }

                    // Fall back to direct value if theme resolution failed or no theme color
                    if (color == null && colorElement.Val?.HasValue == true && colorElement.Val.Value != "auto")
                    {
                        color = colorElement.Val.Value;
                    }
                }

                // Background/shading color (w:shd element)
                var shadingElement = runProps.GetFirstChild<Shading>();
                if (shadingElement != null)
                {
                    // Check for theme fill color first, then direct fill value
                    var themeFill = shadingElement.ThemeFill?.Value;
                    if (themeFill != null && currentThemeColors != null)
                    {
                        var themeFillValue = ((IEnumValue) themeFill).Value;
                        backgroundColor = currentThemeColors.ResolveColor(themeFillValue ?? "", null, null);
                    }

                    // Fall back to direct fill value
                    if (backgroundColor == null && shadingElement.Fill?.HasValue == true &&
                        shadingElement.Fill.Value != "auto" && shadingElement.Fill.Value != "none")
                    {
                        backgroundColor = shadingElement.Fill.Value;
                    }
                }

                styleProps[styleId] = new()
                {
                    FontFamily = fontFamily,
                    FontSizePoints = fontSize,
                    Bold = bold,
                    Italic = italic,
                    Underline = underline,
                    Strikethrough = strikethrough,
                    AllCaps = allCaps,
                    ColorHex = color,
                    BackgroundColorHex = backgroundColor
                };
                processed.Add(styleId);
            }
        } while (processed.Count > lastCount);

        return styleProps;
    }

    Dictionary<string, ParagraphProperties> ExtractStyleParagraphProperties(MainDocumentPart mainPart)
    {
        var styleProps = new Dictionary<string, ParagraphProperties>(StringComparer.OrdinalIgnoreCase);

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return styleProps;
        }

        // First pass: collect all styles and their basedOn references
        var styles = stylesPart.Styles.Elements<Style>().ToList();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build a set of all style IDs that exist in the document
        var existingStyleIds = new HashSet<string>(
            styles.Select(s => s.StyleId?.Value).Where(id => id != null)!,
            StringComparer.OrdinalIgnoreCase);

        // Process styles with proper inheritance - may need multiple passes
        // to handle chains like: Title -> Normal -> (base)
        int lastCount;
        do
        {
            lastCount = processed.Count;
            foreach (var style in styles)
            {
                var styleId = style.StyleId?.Value;
                if (styleId == null || processed.Contains(styleId))
                {
                    continue;
                }

                // Check if base style needs to be processed first
                var basedOnId = style.BasedOn?.Val?.Value;
                // Only wait for the base style if it actually exists in this document
                // If basedOn references a non-existent style, process without waiting
                if (basedOnId != null && existingStyleIds.Contains(basedOnId) && !processed.Contains(basedOnId))
                {
                    // Base style not yet processed, skip for now
                    continue;
                }

                // Get base style properties if available
                ParagraphProperties? baseProps = null;
                if (basedOnId != null)
                {
                    styleProps.TryGetValue(basedOnId, out baseProps);
                }

                // Check for paragraph properties in the style
                var paraProps = style.StyleParagraphProperties;

                // Start with base style properties or defaults
                var alignment = baseProps?.Alignment ?? TextAlignment.Left;
                var spacingBefore = baseProps?.SpacingBeforePoints ?? 0;
                var spacingAfter = baseProps?.SpacingAfterPoints ?? defaultSpacingAfterPoints;
                var lineSpacingMultiplier = baseProps?.LineSpacingMultiplier ?? 1.08;
                var lineSpacingPoints = baseProps?.LineSpacingPoints ?? 0;
                var lineSpacingRule = baseProps?.LineSpacingRule ?? LineSpacingRule.Auto;
                var firstLineIndent = baseProps?.FirstLineIndentPoints ?? 0;
                var leftIndent = baseProps?.LeftIndentPoints ?? 0;
                var rightIndent = baseProps?.RightIndentPoints ?? 0;
                var hangingIndent = baseProps?.HangingIndentPoints ?? 0;
                var contextualSpacing = baseProps?.ContextualSpacing ?? false;

                // Pagination properties from base style
                // Note: pageBreakBefore is NOT inherited from styles to match Word's typical behavior
                // where it's only applied when explicitly set on the paragraph
                var keepLines = baseProps?.KeepLines ?? false;
                var keepNext = baseProps?.KeepNext ?? false;
                var widowControl = baseProps?.WidowControl ?? true;
                var backgroundColor = baseProps?.BackgroundColorHex;

                // If no paragraph properties, still save inherited properties
                if (paraProps == null)
                {
                    styleProps[styleId] = new()
                    {
                        Alignment = alignment,
                        SpacingBeforePoints = spacingBefore,
                        SpacingAfterPoints = spacingAfter,
                        LineSpacingMultiplier = lineSpacingMultiplier,
                        LineSpacingPoints = lineSpacingPoints,
                        LineSpacingRule = lineSpacingRule,
                        FirstLineIndentPoints = firstLineIndent,
                        LeftIndentPoints = leftIndent,
                        RightIndentPoints = rightIndent,
                        HangingIndentPoints = hangingIndent,
                        ContextualSpacing = contextualSpacing,
                        KeepLines = keepLines,
                        KeepNext = keepNext,
                        WidowControl = widowControl,
                        BackgroundColorHex = backgroundColor
                        // PageBreakBefore intentionally not inherited from styles
                    };
                    processed.Add(styleId);
                    continue;
                }

                // Parse alignment
                var justification = paraProps.GetFirstChild<Justification>();
                if (justification?.Val?.HasValue == true)
                {
                    var justVal = justification.Val.Value;
                    if (justVal == JustificationValues.Center)
                    {
                        alignment = TextAlignment.Center;
                    }
                    else if (justVal == JustificationValues.Right)
                    {
                        alignment = TextAlignment.Right;
                    }
                    else if (justVal == JustificationValues.Both || justVal == JustificationValues.Distribute)
                    {
                        alignment = TextAlignment.Justify;
                    }
                    else
                    {
                        alignment = TextAlignment.Left;
                    }
                }

                // Parse spacing
                var spacing = paraProps.GetFirstChild<SpacingBetweenLines>();
                if (spacing != null)
                {
                    if (spacing.Before?.HasValue == true)
                    {
                        spacingBefore = double.Parse(spacing.Before.Value!) / twipsPerPoint;
                    }

                    if (spacing.After?.HasValue == true)
                    {
                        spacingAfter = double.Parse(spacing.After.Value!) / twipsPerPoint;
                    }

                    if (spacing.Line?.HasValue == true)
                    {
                        var ruleValue = spacing.LineRule?.Value ?? LineSpacingRuleValues.Auto;

                        if (ruleValue == LineSpacingRuleValues.Auto)
                        {
                            // Line spacing in 240ths of a line
                            lineSpacingMultiplier = double.Parse(spacing.Line.Value!) / 240.0;
                            lineSpacingRule = LineSpacingRule.Auto;
                        }
                        else if (ruleValue == LineSpacingRuleValues.Exact)
                        {
                            // Line spacing in twips (1/20 of a point)
                            lineSpacingPoints = double.Parse(spacing.Line.Value!) / twipsPerPoint;
                            lineSpacingRule = LineSpacingRule.Exactly;
                        }
                        else if (ruleValue == LineSpacingRuleValues.AtLeast)
                        {
                            // Line spacing in twips (1/20 of a point)
                            lineSpacingPoints = double.Parse(spacing.Line.Value!) / twipsPerPoint;
                            lineSpacingRule = LineSpacingRule.AtLeast;
                        }
                    }
                }

                // Parse indentation
                var indentation = paraProps.GetFirstChild<Indentation>();
                if (indentation != null)
                {
                    if (indentation.FirstLine?.HasValue == true)
                    {
                        firstLineIndent = double.Parse(indentation.FirstLine.Value!) / twipsPerPoint;
                    }

                    if (indentation.Left?.HasValue == true)
                    {
                        leftIndent = double.Parse(indentation.Left.Value!) / twipsPerPoint;
                    }

                    if (indentation.Right?.HasValue == true)
                    {
                        rightIndent = double.Parse(indentation.Right.Value!) / twipsPerPoint;
                    }

                    if (indentation.Hanging?.HasValue == true)
                    {
                        hangingIndent = double.Parse(indentation.Hanging.Value!) / twipsPerPoint;
                    }
                }

                // Parse contextual spacing
                if (paraProps.GetFirstChild<ContextualSpacing>() != null)
                {
                    contextualSpacing = true;
                }

                // Parse pagination properties
                // Note: pageBreakBefore is NOT parsed from styles - only from inline paragraph properties
                if (paraProps.GetFirstChild<KeepLines>() != null)
                {
                    keepLines = true;
                }

                if (paraProps.GetFirstChild<KeepNext>() != null)
                {
                    keepNext = true;
                }

                var widowControlEl = paraProps.GetFirstChild<WidowControl>();
                if (widowControlEl != null)
                {
                    var valAttr = widowControlEl.Val;
                    if (valAttr != null && valAttr.HasValue)
                    {
                        widowControl = valAttr.Value;
                    }
                    else
                    {
                        widowControl = true;
                    }
                }

                // Parse paragraph shading/background color (w:shd element)
                var shadingElement = paraProps.GetFirstChild<Shading>();
                if (shadingElement != null)
                {
                    // Check for theme fill color first, then direct fill value
                    var themeFill = shadingElement.ThemeFill?.Value;
                    if (themeFill != null && currentThemeColors != null)
                    {
                        var themeFillValue = ((IEnumValue) themeFill).Value;
                        backgroundColor = currentThemeColors.ResolveColor(themeFillValue ?? "", null, null);
                    }

                    // Fall back to direct fill value
                    if (backgroundColor == null && shadingElement.Fill?.HasValue == true &&
                        shadingElement.Fill.Value != "auto" && shadingElement.Fill.Value != "none")
                    {
                        backgroundColor = shadingElement.Fill.Value;
                    }
                }

                styleProps[styleId] = new()
                {
                    Alignment = alignment,
                    SpacingBeforePoints = spacingBefore,
                    SpacingAfterPoints = spacingAfter,
                    LineSpacingMultiplier = lineSpacingMultiplier,
                    LineSpacingPoints = lineSpacingPoints,
                    LineSpacingRule = lineSpacingRule,
                    FirstLineIndentPoints = firstLineIndent,
                    LeftIndentPoints = leftIndent,
                    RightIndentPoints = rightIndent,
                    HangingIndentPoints = hangingIndent,
                    ContextualSpacing = contextualSpacing,
                    KeepLines = keepLines,
                    KeepNext = keepNext,
                    WidowControl = widowControl,
                    BackgroundColorHex = backgroundColor
                    // PageBreakBefore intentionally not inherited from styles
                };
                processed.Add(styleId);
            }
        } while (processed.Count > lastCount);

        return styleProps;
    }

    /// <summary>
    /// Internal class to store numbering level definitions.
    /// </summary>
    sealed class NumberingLevelDefinition
    {
        public string LevelText { get; init; } = "";
        public string? FontFamily { get; init; }
        public double LeftIndentPoints { get; init; }
        public double HangingIndentPoints { get; init; }
        public bool IsBullet { get; init; }
        public int StartNumber { get; init; } = 1;
    }

    static Dictionary<int, Dictionary<int, NumberingLevelDefinition>> ExtractNumberingDefinitions(MainDocumentPart mainPart)
    {
        var result = new Dictionary<int, Dictionary<int, NumberingLevelDefinition>>();
        var numberingPart = mainPart.NumberingDefinitionsPart;
        if (numberingPart?.Numbering == null)
        {
            return result;
        }

        var numbering = numberingPart.Numbering;

        // First, collect abstract numbering definitions (abstractNumId -> levels)
        var abstractNums = new Dictionary<int, Dictionary<int, NumberingLevelDefinition>>();
        foreach (var abstractNum in numbering.Elements<AbstractNum>())
        {
            var abstractNumId = abstractNum.AbstractNumberId?.Value ?? 0;
            var levels = new Dictionary<int, NumberingLevelDefinition>();

            foreach (var level in abstractNum.Elements<Level>())
            {
                var ilvl = level.LevelIndex?.Value ?? 0;
                var levelText = level.LevelText?.Val?.Value ?? "";
                var numFmt = level.NumberingFormat?.Val?.Value;

                // Determine if this is a bullet or numbered list
                var isBullet = numFmt == NumberFormatValues.Bullet;

                // Get font for bullet character
                string? fontFamily = null;
                var runProps = level.NumberingSymbolRunProperties;
                if (runProps != null)
                {
                    var fonts = runProps.GetFirstChild<RunFonts>();
                    if (fonts?.Ascii?.HasValue == true)
                    {
                        fontFamily = fonts.Ascii.Value;
                    }
                    else if (fonts?.HighAnsi?.HasValue == true)
                    {
                        fontFamily = fonts.HighAnsi.Value;
                    }
                }

                // Get indentation
                double leftIndent = 0;
                double hangingIndent = 0;
                var pPr = level.GetFirstChild<PreviousParagraphProperties>();
                var indentation = pPr?.GetFirstChild<Indentation>() ?? level.GetFirstChild<Indentation>();
                if (indentation != null)
                {
                    if (indentation.Left?.HasValue == true)
                    {
                        leftIndent = double.Parse(indentation.Left.Value!) / twipsPerPoint;
                    }

                    if (indentation.Hanging?.HasValue == true)
                    {
                        hangingIndent = double.Parse(indentation.Hanging.Value!) / twipsPerPoint;
                    }
                }

                // Get start number
                var startNumber = level.StartNumberingValue?.Val?.Value ?? 1;

                levels[ilvl] = new()
                {
                    LevelText = levelText,
                    FontFamily = fontFamily,
                    LeftIndentPoints = leftIndent,
                    HangingIndentPoints = hangingIndent,
                    IsBullet = isBullet,
                    StartNumber = startNumber
                };
            }

            abstractNums[abstractNumId] = levels;
        }

        // Now map numId to abstractNumId
        foreach (var numInstance in numbering.Elements<NumberingInstance>())
        {
            var numId = numInstance.NumberID?.Value ?? 0;
            var abstractNumIdRef = numInstance.AbstractNumId?.Val?.Value ?? 0;

            if (abstractNums.TryGetValue(abstractNumIdRef, out var levels))
            {
                result[numId] = levels;
            }
        }

        return result;
    }

    static Dictionary<string, (int numId, int ilvl)> ExtractStyleNumbering(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, (int numId, int ilvl)>(StringComparer.OrdinalIgnoreCase);

        // Method 1: Extract from numbering.xml pStyle links (numbering definitions that link TO styles)
        var numberingPart = mainPart.NumberingDefinitionsPart;
        if (numberingPart?.Numbering != null)
        {
            var numbering = numberingPart.Numbering;

            // Build abstractNumId -> List of (ilvl, styleId)
            var abstractStyleLinks = new Dictionary<int, List<(int ilvl, string styleId)>>();
            foreach (var abstractNum in numbering.Elements<AbstractNum>())
            {
                var abstractNumId = abstractNum.AbstractNumberId?.Value ?? 0;
                foreach (var level in abstractNum.Elements<Level>())
                {
                    var pStyle = level.GetFirstChild<ParagraphStyleIdInLevel>();
                    if (pStyle?.Val?.Value != null)
                    {
                        if (!abstractStyleLinks.TryGetValue(abstractNumId, out var value))
                        {
                            value = new List<(int, string)>();
                            abstractStyleLinks[abstractNumId] = value;
                        }

                        var ilvl = level.LevelIndex?.Value ?? 0;
                        value.Add((ilvl, pStyle.Val.Value));
                    }
                }
            }

            // Map numId -> abstractNumId, then look up style links
            foreach (var numInstance in numbering.Elements<NumberingInstance>())
            {
                var numId = numInstance.NumberID?.Value ?? 0;
                var abstractNumIdRef = numInstance.AbstractNumId?.Val?.Value ?? 0;

                if (abstractStyleLinks.TryGetValue(abstractNumIdRef, out var styleLinks))
                {
                    foreach (var (ilvl, styleId) in styleLinks)
                    {
                        if (!result.ContainsKey(styleId))
                        {
                            result[styleId] = (numId, ilvl);
                        }
                    }
                }
            }
        }

        // Method 2: Extract from styles that have numPr directly
        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return result;
        }

        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            var styleId = style.StyleId?.Value;
            if (styleId == null)
            {
                continue;
            }

            // Check for numPr in paragraph properties
            var pPr = style.StyleParagraphProperties;
            if (pPr == null)
            {
                continue;
            }

            var numPr = pPr.GetFirstChild<NumberingProperties>();
            if (numPr == null)
            {
                continue;
            }

            var numId = numPr.NumberingId?.Val?.Value ?? 0;
            var ilvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;

            if (numId > 0)
            {
                result[styleId] = (numId, ilvl);
            }
        }

        return result;
    }

    Dictionary<string, CellBorders> ExtractTableStyleBorders(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, CellBorders>(StringComparer.OrdinalIgnoreCase);

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return result;
        }

        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            var styleId = style.StyleId?.Value;
            if (styleId == null)
            {
                continue;
            }

            // Only look at table styles
            if (style.Type?.Value != StyleValues.Table)
            {
                continue;
            }

            // Look for table properties in the style
            var tblPr = style.StyleTableProperties;
            if (tblPr == null)
            {
                continue;
            }

            // Look for tblBorders in the table properties
            var borders = tblPr.GetFirstChild<TableBorders>();
            if (borders == null)
            {
                continue;
            }

            var cellBorders = new CellBorders
            {
                Top = ParseBorderEdge(borders.GetFirstChild<TopBorder>()),
                Right = ParseBorderEdge(borders.GetFirstChild<RightBorder>()),
                Bottom = ParseBorderEdge(borders.GetFirstChild<BottomBorder>()),
                Left = ParseBorderEdge(borders.GetFirstChild<LeftBorder>())
            };

            // Also check InsideHorizontalBorder and InsideVerticalBorder for internal cell borders
            var insideH = ParseBorderEdge(borders.GetFirstChild<InsideHorizontalBorder>());
            var insideV = ParseBorderEdge(borders.GetFirstChild<InsideVerticalBorder>());

            // If inside borders are specified, they apply to internal cell edges
            if (insideH.IsVisible || insideV.IsVisible)
            {
                cellBorders = new()
                {
                    Top = cellBorders.Top.IsVisible ? cellBorders.Top : insideH,
                    Right = cellBorders.Right.IsVisible ? cellBorders.Right : insideV,
                    Bottom = cellBorders.Bottom.IsVisible ? cellBorders.Bottom : insideH,
                    Left = cellBorders.Left.IsVisible ? cellBorders.Left : insideV
                };
            }

            // Only add if at least one border is visible
            if (cellBorders.Top.IsVisible || cellBorders.Right.IsVisible ||
                cellBorders.Bottom.IsVisible || cellBorders.Left.IsVisible)
            {
                result[styleId] = cellBorders;
            }
        }

        return result;
    }

    NumberingInfo? GetNumberingInfo(OoxmlParagraphProperties? paraProps, string? styleId)
    {
        if (numberingDefinitions == null || numberingDefinitions.Count == 0)
        {
            return null;
        }

        var numId = 0;
        var ilvl = 0;

        // First check for direct numPr on paragraph
        var numPr = paraProps?.GetFirstChild<NumberingProperties>();
        if (numPr != null)
        {
            numId = numPr.NumberingId?.Val?.Value ?? 0;
            ilvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;
        }
        // Fall back to style numbering
        else if (styleId != null && styleNumbering != null && styleNumbering.TryGetValue(styleId, out var styleNumInfo))
        {
            numId = styleNumInfo.numId;
            ilvl = styleNumInfo.ilvl;
        }

        if (numId == 0)
        {
            return null;
        }

        // Look up the numbering definition
        if (!numberingDefinitions.TryGetValue(numId, out var levels))
        {
            return null;
        }

        if (!levels.TryGetValue(ilvl, out var levelDef))
        {
            return null;
        }

        // Generate the bullet/number text
        string text;
        if (levelDef.IsBullet)
        {
            // For bullets, the level text IS the bullet character
            text = levelDef.LevelText;

            // Map Symbol/Wingdings font Private Use Area characters to Unicode equivalents
            if (!string.IsNullOrEmpty(text) && text.Length == 1)
            {
                var c = text[0];
                text = c switch
                {
                    '\uF0B7' => "•", // Symbol bullet -> BULLET
                    '\uF0A7' => "•", // Symbol alternative bullet
                    '\uF06C' => "●", // Symbol filled circle
                    '\uF0FC' => "✓", // Wingdings checkmark
                    '\uF0A8' => "○", // Symbol circle
                    '\uF0D8' => "◆", // Symbol diamond
                    '\uF076' => "■", // Wingdings square
                    >= '\uF000' and <= '\uF0FF' => "•", // Other PUA -> fallback to bullet
                    _ => text // Keep as-is
                };
            }

            if (string.IsNullOrEmpty(text))
            {
                // Default bullet character
                text = "•";
            }
        }
        else
        {
            // For numbered lists, we'd need to track counters - for now just use placeholder
            // Level text like "%1." means use the counter for level 1
            text = levelDef.LevelText.Replace("%1", "1").Replace("%2", "2").Replace("%3", "3");
        }

        return new()
        {
            Text = text,
            FontFamily = levelDef.FontFamily,
            IndentPoints = levelDef.LeftIndentPoints,
            HangingIndentPoints = levelDef.HangingIndentPoints
        };
    }

    static HyphenationSettings ExtractHyphenationSettings(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.DocumentSettingsPart;
        if (settingsPart?.Settings == null)
        {
            return new();
        }

        var settings = settingsPart.Settings;

        var autoHyphenation = false;
        double hyphenationZonePoints = 18; // Default 0.25 inch
        var consecutiveHyphenLimit = 0;
        var doNotHyphenateCaps = false;

        // Parse autoHyphenation
        var autoHyphen = settings.GetFirstChild<AutoHyphenation>();
        if (autoHyphen != null)
        {
            autoHyphenation = autoHyphen.Val?.Value != false;
        }

        // Parse hyphenationZone
        var hyphenZone = settings.GetFirstChild<HyphenationZone>();
        if (hyphenZone?.Val?.HasValue == true)
        {
            hyphenationZonePoints = double.Parse(hyphenZone.Val.Value!) / twipsPerPoint;
        }

        // Parse consecutiveHyphenLimit
        var consecutiveLimit = settings.GetFirstChild<ConsecutiveHyphenLimit>();
        if (consecutiveLimit?.Val?.HasValue == true)
        {
            consecutiveHyphenLimit = consecutiveLimit.Val.Value;
        }

        // Parse doNotHyphenateCaps
        var doNotHyphenCaps = settings.GetFirstChild<DoNotHyphenateCaps>();
        if (doNotHyphenCaps != null)
        {
            doNotHyphenateCaps = doNotHyphenCaps.Val?.Value != false;
        }

        return new()
        {
            AutoHyphenation = autoHyphenation,
            HyphenationZonePoints = hyphenationZonePoints,
            ConsecutiveHyphenLimit = consecutiveHyphenLimit,
            DoNotHyphenateCaps = doNotHyphenateCaps
        };
    }

    static CompatibilitySettings ExtractCompatibilitySettings(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.DocumentSettingsPart;
        if (settingsPart?.Settings == null)
        {
            return new();
        }

        var settings = settingsPart.Settings;
        var compat = settings.GetFirstChild<Compatibility>();
        if (compat == null)
        {
            return new();
        }

        // Look for compatibilityMode in CompatSetting elements
        var compatMode = 15; // Default to Word 2013+ mode

        foreach (var compatSetting in compat.Elements<CompatibilitySetting>())
        {
            // Use InnerText to get the raw attribute value since the SDK doesn't have enum values for all settings
            var name = compatSetting.Name?.InnerText;
            var uri = compatSetting.Uri?.Value;
            var val = compatSetting.Val?.Value;

            if (string.Equals(name, "compatibilityMode", StringComparison.OrdinalIgnoreCase) && uri == "http://schemas.microsoft.com/office/word" && val != null)
            {
                if (int.TryParse(val, out var mode))
                {
                    compatMode = mode;
                }
            }
        }

        return new()
        {
            CompatibilityMode = compatMode
        };
    }

    PageSettings ExtractPageSettings(Body body)
    {
        var sectionProps = body.Descendants<SectionProperties>().LastOrDefault();
        if (sectionProps == null)
        {
            return new();
        }

        return ExtractPageSettings(sectionProps);
    }

    PageSettings ExtractPageSettings(SectionProperties sectionProps)
    {
        var pageSize = sectionProps.GetFirstChild<PageSize>();
        var pageMargin = sectionProps.GetFirstChild<PageMargin>();

        var width = DefaultPageSize.WidthPoints;
        var height = DefaultPageSize.HeightPoints;
        double marginTop = 72;
        double marginBottom = 72;
        double marginLeft = 72;
        double marginRight = 72;
        double headerDistance = 36;
        double footerDistance = 36;
        var columnCount = 1;
        double columnSpacing = 36;

        if (pageSize != null)
        {
            if (pageSize.Width?.HasValue == true)
            {
                width = pageSize.Width.Value / twipsPerPoint;
            }

            if (pageSize.Height?.HasValue == true)
            {
                height = pageSize.Height.Value / twipsPerPoint;
            }
        }

        if (pageMargin != null)
        {
            if (pageMargin.Top?.HasValue == true)
            {
                marginTop = pageMargin.Top.Value / twipsPerPoint;
            }

            if (pageMargin.Bottom?.HasValue == true)
            {
                marginBottom = pageMargin.Bottom.Value / twipsPerPoint;
            }

            if (pageMargin.Left?.HasValue == true)
            {
                marginLeft = pageMargin.Left.Value / twipsPerPoint;
            }

            if (pageMargin.Right?.HasValue == true)
            {
                marginRight = pageMargin.Right.Value / twipsPerPoint;
            }

            if (pageMargin.Header?.HasValue == true)
            {
                headerDistance = pageMargin.Header.Value / twipsPerPoint;
            }

            if (pageMargin.Footer?.HasValue == true)
            {
                footerDistance = pageMargin.Footer.Value / twipsPerPoint;
            }
        }

        // Parse column settings
        var columns = sectionProps.GetFirstChild<Columns>();
        if (columns != null)
        {
            if (columns.ColumnCount?.HasValue == true)
            {
                columnCount = columns.ColumnCount.Value;
            }

            if (columns.Space?.HasValue == true)
            {
                columnSpacing = double.Parse(columns.Space.Value!) / twipsPerPoint;
            }
        }

        // Parse line numbering settings
        var lineNumbers = ParseLineNumberSettings(sectionProps);

        // Parse document grid settings (used by Word to align text to a baseline grid)
        double documentGridLinePitchPoints = 0;
        var docGrid = sectionProps.GetFirstChild<DocGrid>();
        if (docGrid?.LinePitch?.HasValue == true)
        {
            documentGridLinePitchPoints = docGrid.LinePitch.Value / twipsPerPoint;
        }

        return new()
        {
            WidthPoints = width,
            HeightPoints = height,
            MarginTop = marginTop,
            MarginBottom = marginBottom,
            MarginLeft = marginLeft,
            MarginRight = marginRight,
            HeaderDistance = headerDistance,
            FooterDistance = footerDistance,
            ColumnCount = columnCount,
            ColumnSpacing = columnSpacing,
            LineNumbers = lineNumbers,
            DocumentGridLinePitchPoints = documentGridLinePitchPoints,
            LastRenderedPageBreakCount = lastRenderedPageBreakCount,
            BackgroundColorHex = documentBackgroundColor
        };
    }

    static LineNumberSettings? ParseLineNumberSettings(SectionProperties sectionProps)
    {
        var lnNumType = sectionProps.GetFirstChild<LineNumberType>();
        if (lnNumType == null)
        {
            return null;
        }

        var start = 1;
        var countBy = 1;
        double distancePoints = 18; // Default 0.25 inch
        var restart = LineNumberRestart.NewPage;

        if (lnNumType.Start?.HasValue == true)
        {
            start = lnNumType.Start.Value;
        }

        if (lnNumType.CountBy?.HasValue == true)
        {
            countBy = lnNumType.CountBy.Value;
        }

        if (lnNumType.Distance?.HasValue == true)
        {
            distancePoints = double.Parse(lnNumType.Distance.Value!) / twipsPerPoint;
        }

        if (lnNumType.Restart?.HasValue == true)
        {
            var restartValue = lnNumType.Restart.Value;
            if (restartValue == LineNumberRestartValues.Continuous)
            {
                restart = LineNumberRestart.Continuous;
            }
            else if (restartValue == LineNumberRestartValues.NewSection)
            {
                restart = LineNumberRestart.NewSection;
            }
            // else keep default (NewPage)
        }

        return new()
        {
            Start = start,
            CountBy = countBy,
            DistancePoints = distancePoints,
            Restart = restart
        };
    }

    string? ExtractDocumentBackgroundColor(Document document)
    {
        // Look for w:background element (child of w:document)
        var background = document.GetFirstChild<DocumentBackground>();
        if (background == null)
        {
            return null;
        }

        // Try explicit color first
        if (background.Color?.HasValue == true)
        {
            var colorValue = background.Color.Value;
            if (!string.IsNullOrEmpty(colorValue) && colorValue != "auto")
            {
                return colorValue;
            }
        }

        // Try theme color
        if (background.ThemeColor?.HasValue == true && currentThemeColors != null)
        {
            var themeColorName = background.ThemeColor.Value.ToString();

            // Parse tint/shade values (hex strings to bytes)
            byte? tint = null;
            byte? shade = null;

            if (background.ThemeTint?.HasValue == true)
            {
                var tintHex = background.ThemeTint.Value;
                if (!string.IsNullOrEmpty(tintHex) && byte.TryParse(tintHex, NumberStyles.HexNumber, null, out var tintByte))
                {
                    tint = tintByte;
                }
            }

            if (background.ThemeShade?.HasValue == true)
            {
                var shadeHex = background.ThemeShade.Value;
                if (!string.IsNullOrEmpty(shadeHex) && byte.TryParse(shadeHex, NumberStyles.HexNumber, null, out var shadeByte))
                {
                    shade = shadeByte;
                }
            }

            return currentThemeColors.ResolveColor(themeColorName, shade, tint);
        }

        return null;
    }

    static double ExtractDefaultSpacingAfter(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.StyleDefinitionsPart;

        // No styles.xml at all - use Word's built-in defaults (8pt spacing after)
        if (stylesPart?.Styles == null)
        {
            return 8;
        }

        // Look for docDefaults/pPrDefault
        var docDefaults = stylesPart.Styles.DocDefaults;
        if (docDefaults == null)
        {
            return 0; // styles.xml exists but no docDefaults - use 0
        }

        var pPrDefault = docDefaults.ParagraphPropertiesDefault;
        if (pPrDefault?.ParagraphPropertiesBaseStyle == null)
        {
            return 0; // empty pPrDefault - use 0
        }

        // Check for spacing in pPrDefault
        var spacing = pPrDefault.ParagraphPropertiesBaseStyle.SpacingBetweenLines;
        if (spacing?.After?.HasValue == true)
        {
            return double.Parse(spacing.After.Value!) / twipsPerPoint;
        }

        return 0; // pPrDefault exists but no spacing defined - use 0
    }

    HeaderFooterContent? ExtractHeader(Body body, MainDocumentPart mainPart)
    {
        // Try to find header from section properties reference
        var sectionProps = body.Descendants<SectionProperties>().LastOrDefault();
        HeaderPart? headerPart = null;

        if (sectionProps != null)
        {
            var headerRef = sectionProps.Descendants<HeaderReference>()
                .FirstOrDefault(h => h.Type?.Value == HeaderFooterValues.Default);

            if (headerRef?.Id?.Value != null)
            {
                headerPart = mainPart.GetPartById(headerRef.Id.Value) as HeaderPart;
            }
        }

        // Fallback: try to get first header part directly
        if (headerPart == null)
        {
            headerPart = mainPart.HeaderParts.FirstOrDefault();
        }

        if (headerPart?.Header == null)
        {
            return null;
        }

        var elements = new List<DocumentElement>();
        foreach (var element in headerPart.Header.ChildElements)
        {
            if (element is Paragraph para)
            {
                elements.AddRange(ParseParagraph(para, mainPart));
            }
            else if (element is Table table)
            {
                var parsedTable = ParseTable(table, mainPart);
                if (parsedTable != null)
                {
                    elements.Add(parsedTable);
                }
            }
        }

        return elements.Count > 0
            ? new HeaderFooterContent
            {
                Elements = elements
            }
            : null;
    }

    HeaderFooterContent? ExtractFooter(Body body, MainDocumentPart mainPart)
    {
        // Try to find footer from section properties reference
        var sectionProps = body.Descendants<SectionProperties>().LastOrDefault();
        FooterPart? footerPart = null;

        if (sectionProps != null)
        {
            var footerRef = sectionProps.Descendants<FooterReference>()
                .FirstOrDefault(f => f.Type?.Value == HeaderFooterValues.Default);

            if (footerRef?.Id?.Value != null)
            {
                footerPart = mainPart.GetPartById(footerRef.Id.Value) as FooterPart;
            }
        }

        // Fallback: try to get first footer part directly
        footerPart ??= mainPart.FooterParts.FirstOrDefault();

        if (footerPart?.Footer == null)
        {
            return null;
        }

        var elements = new List<DocumentElement>();
        foreach (var element in footerPart.Footer.ChildElements)
        {
            if (element is Paragraph para)
            {
                elements.AddRange(ParseParagraph(para, mainPart));
            }
            else if (element is Table table)
            {
                var parsedTable = ParseTable(table, mainPart);
                if (parsedTable != null)
                {
                    elements.Add(parsedTable);
                }
            }
        }

        return elements.Count > 0
            ? new HeaderFooterContent
            {
                Elements = elements
            }
            : null;
    }

    List<DocumentElement> ParseElements(Body body, MainDocumentPart mainPart)
    {
        var elements = new List<DocumentElement>();

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph para:
                    var parsedElements = ParseParagraph(para, mainPart);
                    elements.AddRange(parsedElements);
                    break;

                case Table table:
                    var parsedTable = ParseTable(table, mainPart);
                    if (parsedTable != null)
                    {
                        elements.Add(parsedTable);
                    }

                    break;

                case AltChunk altChunk:
                    var altChunkElements = ParseAltChunk(altChunk, mainPart);
                    elements.AddRange(altChunkElements);
                    break;

                case SdtBlock sdtBlock:
                    // Block-level content control at document body level - extract and parse its content
                    foreach (var sdtChild in sdtBlock.SdtContentBlock?.ChildElements ?? [])
                    {
                        if (sdtChild is Paragraph sdtPara)
                        {
                            elements.AddRange(ParseParagraph(sdtPara, mainPart));
                        }
                        else if (sdtChild is Table sdtTable)
                        {
                            var parsedSdtTable = ParseTable(sdtTable, mainPart);
                            if (parsedSdtTable != null)
                            {
                                elements.Add(parsedSdtTable);
                            }
                        }
                    }

                    break;
            }
        }

        return elements;
    }

    static List<DocumentElement> ParseAltChunk(AltChunk altChunk, MainDocumentPart mainPart)
    {
        if (altChunk.Id?.Value == null)
        {
            return [];
        }

        var part = mainPart.GetPartById(altChunk.Id.Value);
        if (part is AlternativeFormatImportPart altPart)
        {
            using var stream = altPart.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = reader.ReadToEnd();

            return HtmlParser.Parse(html);
        }

        return [];
    }

    TableElement? ParseTable(Table table, MainDocumentPart mainPart)
    {
        var rows = new List<TableRow>();
        var tableProps = table.GetFirstChild<OoxmlTableProperties>();

        // Parse table grid (column widths)
        List<double>? gridColumnWidths = null;
        var tableGrid = table.GetFirstChild<TableGrid>();
        if (tableGrid != null)
        {
            gridColumnWidths = new();
            foreach (var gridCol in tableGrid.Elements<GridColumn>())
            {
                if (gridCol.Width?.HasValue == true &&
                    double.TryParse(gridCol.Width.Value, out var widthTwips))
                {
                    gridColumnWidths.Add(widthTwips / twipsPerPoint);
                }
            }

            if (gridColumnWidths.Count == 0)
            {
                gridColumnWidths = null;
            }
        }

        // Parse table-level default cell margins and floating table positioning
        CellSpacing? defaultCellMargin = null;
        CellSpacing? defaultCellPadding = null;
        var isFloating = false;
        if (tableProps != null)
        {
            var tblCellMar = tableProps.GetFirstChild<TableCellMarginDefault>();
            if (tblCellMar != null)
            {
                defaultCellPadding = ParseTableCellMargin(tblCellMar);
            }

            // Check for floating table positioning (tblpPr)
            var tblpPr = tableProps.GetFirstChild<TablePositionProperties>();
            isFloating = tblpPr != null;
        }

        foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
        {
            var cells = new List<TableCell>();

            foreach (var cell in row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
            {
                var cellContent = new List<DocumentElement>();
                var cellProps = cell.GetFirstChild<OoxmlTableCellProperties>();

                // Parse cell content (paragraphs, nested tables, SdtBlocks, etc.)
                foreach (var cellChild in cell.ChildElements)
                {
                    switch (cellChild)
                    {
                        case Paragraph para:
                            cellContent.AddRange(ParseParagraph(para, mainPart));
                            break;
                        case Table nestedTable:
                            var nested = ParseTable(nestedTable, mainPart);
                            if (nested != null)
                            {
                                cellContent.Add(nested);
                            }

                            break;
                        case SdtBlock sdtBlock:
                            // Block-level content control - extract and parse paragraphs from its content
                            foreach (var sdtChild in sdtBlock.SdtContentBlock?.ChildElements ?? [])
                            {
                                if (sdtChild is Paragraph sdtPara)
                                {
                                    cellContent.AddRange(ParseParagraph(sdtPara, mainPart));
                                }
                                else if (sdtChild is Table sdtTable)
                                {
                                    var nestedSdt = ParseTable(sdtTable, mainPart);
                                    if (nestedSdt != null)
                                    {
                                        cellContent.Add(nestedSdt);
                                    }
                                }
                            }

                            break;
                    }
                }

                // Get cell properties
                double? width = null;
                string? bgColor = null;
                CellSpacing? cellPadding = null;
                CellSpacing? cellMargin = null;

                if (cellProps != null)
                {
                    var cellWidth = cellProps.GetFirstChild<TableCellWidth>();
                    if (cellWidth?.Width?.HasValue == true && cellWidth.Type?.Value == TableWidthUnitValues.Dxa)
                    {
                        width = double.Parse(cellWidth.Width.Value!) / twipsPerPoint;
                    }

                    var shading = cellProps.GetFirstChild<Shading>();
                    if (shading?.Fill?.HasValue == true && shading.Fill.Value != "auto")
                    {
                        bgColor = shading.Fill.Value;
                    }

                    // Parse cell-level margins (which act as padding in Word)
                    var tcMar = cellProps.GetFirstChild<TableCellMargin>();
                    if (tcMar != null)
                    {
                        cellPadding = ParseCellMargin(tcMar);
                    }
                }

                // Parse cell-level borders
                CellBorders? cellBorders = null;
                var tcBorders = cellProps?.GetFirstChild<TableCellBorders>();
                if (tcBorders != null)
                {
                    cellBorders = new()
                    {
                        Top = ParseBorderEdge(tcBorders.GetFirstChild<TopBorder>()),
                        Right = ParseBorderEdge(tcBorders.GetFirstChild<RightBorder>()),
                        Bottom = ParseBorderEdge(tcBorders.GetFirstChild<BottomBorder>()),
                        Left = ParseBorderEdge(tcBorders.GetFirstChild<LeftBorder>())
                    };
                }

                // Parse grid span (number of columns this cell spans)
                var gridSpan = 1;
                var gridSpanElement = cellProps?.GetFirstChild<GridSpan>();
                if (gridSpanElement?.Val?.HasValue == true)
                {
                    gridSpan = gridSpanElement.Val.Value;
                }

                // Parse vertical alignment (w:vAlign)
                var verticalAlign = CellVerticalAlignment.Top;
                var vAlignElement = cellProps?.GetFirstChild<TableCellVerticalAlignment>();
                if (vAlignElement?.Val?.HasValue == true)
                {
                    var vAlignVal = vAlignElement.Val.Value;
                    if (vAlignVal == TableVerticalAlignmentValues.Center)
                    {
                        verticalAlign = CellVerticalAlignment.Center;
                    }
                    else if (vAlignVal == TableVerticalAlignmentValues.Bottom)
                    {
                        verticalAlign = CellVerticalAlignment.Bottom;
                    }
                }

                // Parse vertical merge (w:vMerge)
                var verticalMerge = VerticalMergeType.None;
                var vMergeElement = cellProps?.GetFirstChild<VerticalMerge>();
                if (vMergeElement != null)
                {
                    // If val="restart", this cell starts a vertical merge
                    // If val is missing or val="continue", this cell continues a merge from above
                    if (vMergeElement.Val?.Value == MergedCellValues.Restart)
                    {
                        verticalMerge = VerticalMergeType.Restart;
                    }
                    else
                    {
                        verticalMerge = VerticalMergeType.Continue;
                    }
                }

                cells.Add(new()
                {
                    Content = cellContent,
                    Properties = new()
                    {
                        WidthPoints = width,
                        BackgroundColorHex = bgColor,
                        Padding = cellPadding,
                        Margin = cellMargin,
                        Borders = cellBorders,
                        GridSpan = gridSpan,
                        VerticalAlignment = verticalAlign,
                        VerticalMerge = verticalMerge
                    }
                });
            }

            // Parse row properties for height
            double? rowHeight = null;
            var isExactHeight = false;
            var rowProps = row.GetFirstChild<TableRowProperties>();
            if (rowProps != null)
            {
                var trHeight = rowProps.GetFirstChild<TableRowHeight>();
                if (trHeight?.Val?.HasValue == true)
                {
                    rowHeight = trHeight.Val.Value / twipsPerPoint;
                    // hRule="exact" means exact height, otherwise it's minimum height
                    isExactHeight = trHeight.HeightType?.Value == HeightRuleValues.Exact;
                }
            }

            rows.Add(new()
            {
                Cells = cells,
                HeightPoints = rowHeight,
                IsExactHeight = isExactHeight
            });
        }

        if (rows.Count == 0)
        {
            return null;
        }

        // Parse table properties
        CellBorders? defaultBorders = null;
        double indentPoints = 0;

        if (tableProps != null)
        {
            // Parse table-level borders (w:tblBorders)
            var borders = tableProps.GetFirstChild<TableBorders>();
            if (borders != null)
            {
                defaultBorders = new()
                {
                    Top = ParseBorderEdge(borders.GetFirstChild<TopBorder>()),
                    Right = ParseBorderEdge(borders.GetFirstChild<RightBorder>()),
                    Bottom = ParseBorderEdge(borders.GetFirstChild<BottomBorder>()),
                    Left = ParseBorderEdge(borders.GetFirstChild<LeftBorder>())
                };

                // Also check InsideHorizontalBorder and InsideVerticalBorder for internal cell borders
                var insideH = ParseBorderEdge(borders.GetFirstChild<InsideHorizontalBorder>());
                var insideV = ParseBorderEdge(borders.GetFirstChild<InsideVerticalBorder>());

                // If inside borders are specified, they apply to internal cell edges
                // For now, we'll use these as defaults for all cells if they're more visible
                if (insideH.IsVisible || insideV.IsVisible)
                {
                    // Combine outside and inside borders
                    defaultBorders = new()
                    {
                        Top = defaultBorders.Top.IsVisible ? defaultBorders.Top : insideH,
                        Right = defaultBorders.Right.IsVisible ? defaultBorders.Right : insideV,
                        Bottom = defaultBorders.Bottom.IsVisible ? defaultBorders.Bottom : insideH,
                        Left = defaultBorders.Left.IsVisible ? defaultBorders.Left : insideV
                    };
                }
            }

            // If no inline borders, try to get borders from the table style
            if (defaultBorders == null)
            {
                var tableStyle = tableProps.GetFirstChild<TableStyle>();
                if (tableStyle?.Val?.Value != null && tableStyleBorders != null &&
                    tableStyleBorders.TryGetValue(tableStyle.Val.Value, out var styleBorders))
                {
                    defaultBorders = styleBorders;
                }
            }

            // Parse table indent
            var tblInd = tableProps.GetFirstChild<TableIndentation>();
            if (tblInd?.Width?.HasValue == true)
            {
                indentPoints = tblInd.Width.Value / twipsPerPoint;
            }
        }

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                IsFloating = isFloating,
                DefaultBorders = defaultBorders,
                DefaultCellPadding = defaultCellPadding ?? new CellSpacing(),
                DefaultCellMargin = defaultCellMargin ?? new CellSpacing(0),
                IndentPoints = indentPoints,
                GridColumnWidths = gridColumnWidths
            }
        };
    }

    static CellSpacing? ParseTableCellMargin(TableCellMarginDefault margin)
    {
        double top = 0, right = 0, bottom = 0, left = 0;
        var hasAny = false;

        var topMargin = margin.TopMargin;
        if (topMargin?.Width?.HasValue == true)
        {
            top = double.Parse(topMargin.Width.Value!) / twipsPerPoint;
            hasAny = true;
        }

        var rightMargin = margin.TableCellRightMargin;
        if (rightMargin?.Width?.HasValue == true)
        {
            right = rightMargin.Width.Value / twipsPerPoint;
            hasAny = true;
        }

        var bottomMargin = margin.BottomMargin;
        if (bottomMargin?.Width?.HasValue == true)
        {
            bottom = double.Parse(bottomMargin.Width.Value!) / twipsPerPoint;
            hasAny = true;
        }

        var leftMargin = margin.TableCellLeftMargin;
        if (leftMargin?.Width?.HasValue == true)
        {
            left = leftMargin.Width.Value / twipsPerPoint;
            hasAny = true;
        }

        return hasAny ? new CellSpacing(top, right, bottom, left) : null;
    }

    static CellSpacing? ParseCellMargin(TableCellMargin margin)
    {
        double top = 0, right = 0, bottom = 0, left = 0;
        var hasAny = false;

        var topMargin = margin.TopMargin;
        if (topMargin?.Width?.HasValue == true)
        {
            top = double.Parse(topMargin.Width.Value!) / twipsPerPoint;
            hasAny = true;
        }

        var rightMargin = margin.RightMargin;
        if (rightMargin?.Width?.HasValue == true)
        {
            right = double.Parse(rightMargin.Width.Value!) / twipsPerPoint;
            hasAny = true;
        }

        var bottomMargin = margin.BottomMargin;
        if (bottomMargin?.Width?.HasValue == true)
        {
            bottom = double.Parse(bottomMargin.Width.Value!) / twipsPerPoint;
            hasAny = true;
        }

        var leftMargin = margin.LeftMargin;
        if (leftMargin?.Width?.HasValue == true)
        {
            left = double.Parse(leftMargin.Width.Value!) / twipsPerPoint;
            hasAny = true;
        }

        return hasAny ? new CellSpacing(top, right, bottom, left) : null;
    }

    BorderEdge ParseBorderEdge(BorderType? border)
    {
        if (border == null)
        {
            return BorderEdge.None;
        }

        // Check if border is explicitly set to none/nil
        if (border.Val?.Value == BorderValues.None || border.Val?.Value == BorderValues.Nil)
        {
            return BorderEdge.None;
        }

        // If no Val specified, treat as no border
        if (!border.Val?.HasValue ?? true)
        {
            return BorderEdge.None;
        }

        // Parse border properties
        var width = 0.5;
        if (border.Size?.HasValue == true)
        {
            width = border.Size.Value / 8.0; // Size is in eighths of a point
        }

        var color = "000000";
        if (border.Color?.HasValue == true && border.Color.Value != "auto")
        {
            color = border.Color.Value;
        }
        else if (border.ThemeColor?.HasValue == true && currentThemeColors != null)
        {
            // Try to resolve theme color - use IEnumValue.Value instead of ToString()
            var themeColorValue = ((IEnumValue) border.ThemeColor.Value).Value;
            color = currentThemeColors.ResolveColor(themeColorValue ?? "");
        }

        return new()
        {
            IsVisible = true,
            WidthPoints = width,
            ColorHex = color
        };
    }

    List<DocumentElement> ParseParagraph(Paragraph para, MainDocumentPart mainPart)
    {
        var result = new List<DocumentElement>();

        // Note: PageBreakBefore is now handled via paragraph properties in RenderParagraph
        // to avoid double page breaks (the property is parsed in ParseParagraphProperties)

        // Check for section break in paragraph properties
        var paraProps = para.ParagraphProperties;
        var sectionProps = paraProps?.GetFirstChild<SectionProperties>();
        SectionBreakElement? sectionBreak = null;
        if (sectionProps != null)
        {
            sectionBreak = ParseSectionBreak(sectionProps);
        }

        var runs = new List<Run>();

        // Get paragraph style ID for style-based property resolution
        var paragraphStyleId = paraProps?.ParagraphStyleId?.Val?.Value;
        var props = ParseParagraphProperties(paraProps, paragraphStyleId);

        // Check for numbering (direct on paragraph or from style)
        var numberingInfo = GetNumberingInfo(paraProps, paragraphStyleId);
        if (numberingInfo != null)
        {
            props = props with
            {
                Numbering = numberingInfo
            };
        }

        foreach (var child in para.ChildElements)
        {
            switch (child)
            {
                case SdtRun sdtRun:
                    // Content control (structured document tag)
                    // Check if this is a specific control type that should be rendered as a ContentControlElement
                    if (IsContentControlType(sdtRun))
                    {
                        // Emit current paragraph content before the content control
                        if (runs.Count > 0)
                        {
                            result.Add(new ParagraphElement
                            {
                                Runs = new List<Run>(runs),
                                Properties = props
                            });
                            runs.Clear();
                        }

                        var contentControl = ParseSdtRun(sdtRun, mainPart, paragraphStyleId);
                        if (contentControl != null)
                        {
                            result.Add(contentControl);
                        }

                        break;
                    }

                    // SdtRun is an inline (run-level) content control - extract its runs inline
                    var sdtRunContent = sdtRun.SdtContentRun;
                    if (sdtRunContent != null)
                    {
                        // Parse each run inside the content control and add inline
                        foreach (var sdtChildRun in sdtRunContent.Descendants<OoxmlRun>())
                        {
                            // Check for breaks within the run
                            var breakElement = sdtChildRun.GetFirstChild<Break>();
                            if (breakElement != null)
                            {
                                var breakType = breakElement.Type?.Value;
                                if (breakType == BreakValues.Page)
                                {
                                    if (runs.Count > 0)
                                    {
                                        result.Add(new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                        runs.Clear();
                                    }

                                    result.Add(new PageBreakElement());
                                    continue;
                                }

                                if (breakType == BreakValues.Column)
                                {
                                    if (runs.Count > 0)
                                    {
                                        result.Add(new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                        runs.Clear();
                                    }

                                    result.Add(new ColumnBreakElement());
                                    continue;
                                }

                                // Line break - add newline character
                                var runProps = ParseRunProperties(sdtChildRun.RunProperties, mainPart);
                                runs.Add(new()
                                {
                                    Text = "\n",
                                    Properties = runProps
                                });
                                continue;
                            }

                            // Check for drawings (images/icons) within the SdtRun child
                            foreach (var drawing in sdtChildRun.Descendants<Drawing>())
                            {
                                var imageElements = ParseDrawingElements(drawing, mainPart);
                                if (imageElements.Count > 0)
                                {
                                    // Emit current paragraph content before the images
                                    if (runs.Count > 0)
                                    {
                                        result.Add(new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                        runs.Clear();
                                    }

                                    result.AddRange(imageElements);
                                }
                            }

                            // Parse the run for text content (skip if it only contains a drawing)
                            if (!sdtChildRun.Descendants<Drawing>().Any())
                            {
                                var parsedRun = ParseRun(sdtChildRun, mainPart, paragraphStyleId);
                                if (parsedRun != null)
                                {
                                    runs.Add(parsedRun);
                                }
                            }
                        }
                    }

                    break;

                case SdtCell sdtCell:
                    // Cell-level content control - extract runs from its content
                    foreach (var sdtCellRun in sdtCell.Descendants<OoxmlRun>())
                    {
                        // Check for breaks within the run
                        var cellBreakElement = sdtCellRun.GetFirstChild<Break>();
                        if (cellBreakElement != null)
                        {
                            var breakType = cellBreakElement.Type?.Value;
                            if (breakType == BreakValues.Page)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement());
                                continue;
                            }

                            if (breakType == BreakValues.Column)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(new ColumnBreakElement());
                                continue;
                            }

                            var runProps = ParseRunProperties(sdtCellRun.RunProperties, mainPart);
                            runs.Add(new()
                            {
                                Text = "\n",
                                Properties = runProps
                            });
                            continue;
                        }

                        var parsedRun = ParseRun(sdtCellRun, mainPart, paragraphStyleId);
                        if (parsedRun != null)
                        {
                            runs.Add(parsedRun);
                        }
                    }

                    break;

                case SdtBlock sdtBlock:
                    // Block-level content control - extract runs from its content
                    foreach (var sdtBlockRun in sdtBlock.Descendants<OoxmlRun>())
                    {
                        // Check for breaks within the run
                        var blockBreakElement = sdtBlockRun.GetFirstChild<Break>();
                        if (blockBreakElement != null)
                        {
                            var breakType = blockBreakElement.Type?.Value;
                            if (breakType == BreakValues.Page)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement());
                                continue;
                            }

                            if (breakType == BreakValues.Column)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(new ColumnBreakElement());
                                continue;
                            }

                            var runProps = ParseRunProperties(sdtBlockRun.RunProperties, mainPart);
                            runs.Add(new()
                            {
                                Text = "\n",
                                Properties = runProps
                            });
                            continue;
                        }

                        var parsedRun = ParseRun(sdtBlockRun, mainPart, paragraphStyleId);
                        if (parsedRun != null)
                        {
                            runs.Add(parsedRun);
                        }
                    }

                    break;

                case OoxmlRun run:
                    // Check for legacy form fields (FieldChar with FormFieldData)
                    var formField = ParseFormField(run);
                    if (formField != null)
                    {
                        // Emit current paragraph content before the form field
                        if (runs.Count > 0)
                        {
                            result.Add(new ParagraphElement
                            {
                                Runs = new List<Run>(runs),
                                Properties = props
                            });
                            runs.Clear();
                        }

                        result.Add(formField);
                        break;
                    }

                    // Check for drawings (images/icons/WordArt/ink/shapes) within run
                    foreach (var drawing in run.Descendants<Drawing>())
                    {
                        // Try background shapes first (solid fill behind text)
                        // May return multiple shapes when a WordprocessingGroup contains multiple non-decorative shapes
                        var shapeElements = ShapeParser.ParseBackgroundShapes(drawing, currentThemeColors, mainPart, props.SpacingBeforePoints);

                        // Check if there's a group - groups may contain text boxes and images even without shapes
                        var hasGroup = drawing.Descendants<WPG.WordprocessingGroup>().Any();

                        if (shapeElements.Count > 0 || hasGroup)
                        {
                            // Emit current paragraph content before the shapes/group content
                            if (runs.Count > 0)
                            {
                                result.Add(new ParagraphElement
                                {
                                    Runs = new List<Run>(runs),
                                    Properties = props
                                });
                                runs.Clear();
                            }

                            result.AddRange(shapeElements);

                            // Also check for images in the same drawing/group (e.g., decorative overlays, SVG backgrounds)
                            var overlayImages = ParseDrawingElements(drawing, mainPart);
                            result.AddRange(overlayImages);

                            // Parse text boxes and solid fill shapes inside the shapes/group (they contain the actual content)
                            var shapesFromDrawing = ParseAllShapesFromDrawing(drawing, mainPart);
                            result.AddRange(shapesFromDrawing);

                            continue;
                        }

                        // Try ink first, then WordArt, then fall back to image
                        var inkElement = InkParser.ParseInk(drawing, mainPart);
                        if (inkElement != null)
                        {
                            // Emit current paragraph content before the ink
                            if (runs.Count > 0)
                            {
                                result.Add(new ParagraphElement
                                {
                                    Runs = new List<Run>(runs),
                                    Properties = props
                                });
                                runs.Clear();
                            }

                            result.Add(inkElement);
                        }
                        else
                        {
                            var wordArtElement = ParseWordArt(drawing);
                            if (wordArtElement != null)
                            {
                                // Emit current paragraph content before the WordArt
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(wordArtElement);
                            }
                            else
                            {
                                // Try text box (positioned text without WordArt transform)
                                var textBoxElement = ParseTextBox(drawing, mainPart);
                                if (textBoxElement != null)
                                {
                                    // Emit current paragraph content before the text box
                                    if (runs.Count > 0)
                                    {
                                        result.Add(new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                        runs.Clear();
                                    }

                                    result.Add(textBoxElement);
                                }
                                else
                                {
                                    // Check if this is an inline image (wp:inline) - should flow with text
                                    var isInline = drawing.Descendants().Any(e => e.LocalName == "inline");

                                    if (isInline)
                                    {
                                        // Try to create an inline image run
                                        var inlineRun = TryParseInlineImageRun(drawing, mainPart, new());
                                        if (inlineRun != null)
                                        {
                                            runs.Add(inlineRun);
                                        }
                                    }
                                    else
                                    {
                                        // Anchored/floating images are block elements
                                        var imageElements = ParseDrawingElements(drawing, mainPart);
                                        if (imageElements.Count > 0)
                                        {
                                            // Emit current paragraph content before the images
                                            if (runs.Count > 0)
                                            {
                                                result.Add(new ParagraphElement
                                                {
                                                    Runs = new List<Run>(runs),
                                                    Properties = props
                                                });
                                                runs.Clear();
                                            }

                                            result.AddRange(imageElements);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Check for breaks within run
                    foreach (var runChild in run.ChildElements)
                    {
                        if (runChild is Break breakElement)
                        {
                            var breakType = breakElement.Type?.Value;

                            if (breakType == BreakValues.Page)
                            {
                                // Emit current paragraph content before the break
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement());
                            }
                            else if (breakType == BreakValues.Column)
                            {
                                // Emit current paragraph content before the break
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(new ColumnBreakElement());
                            }
                            else
                            {
                                // Line break (no type or TextWrapping) - add newline to text
                                // Get current run properties for the line break
                                var runProps = run.RunProperties;
                                var parsedProps = ParseRunProperties(runProps, mainPart);

                                // Add any text before the break
                                var textBefore = string.Concat(run.Descendants<Text>()
                                    .TakeWhile(t => t != runChild.NextSibling())
                                    .Select(t => t.Text));
                                if (!string.IsNullOrEmpty(textBefore))
                                {
                                    runs.Add(new()
                                    {
                                        Text = textBefore,
                                        Properties = parsedProps
                                    });
                                }

                                // Add the line break as a newline character
                                runs.Add(new()
                                {
                                    Text = "\n",
                                    Properties = parsedProps
                                });
                            }
                        }
                        else if (lastRenderedPageBreakCount >= 20 && runChild is LastRenderedPageBreak && !run.Descendants<Text>().Any() && !run.Descendants<Break>().Any())
                        {
                            // Word caches pagination using lastRenderedPageBreak. Only treat it as a page boundary hint
                            // when the document has lots of these markers (i.e., likely reflects full-document pagination).
                            if (result.LastOrDefault() is not PageBreakElement)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(new ParagraphElement
                                    {
                                        Runs = new List<Run>(runs),
                                        Properties = props
                                    });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement());
                            }
                        }
                        else if (runChild is Text textElement)
                        {
                            // Regular text - will be handled by ParseRun
                        }
                    }

                    // Parse the run normally (this handles text content)
                    // Skip if run only contains a drawing (no text)
                    if (!run.Descendants<Drawing>().Any())
                    {
                        var parsedRun = ParseRun(run, mainPart, paragraphStyleId);
                        if (parsedRun != null && !run.Descendants<Break>().Any())
                        {
                            runs.Add(parsedRun);
                        }
                        else if (parsedRun != null && run.Descendants<Break>().All(b =>
                                     b.Type?.Value != BreakValues.Page && b.Type?.Value != BreakValues.Column))
                        {
                            // Has only line breaks, text already handled above
                        }
                    }

                    break;
            }
        }

        // Add remaining content
        if (runs.Count == 0 && result.Count == 0)
        {
            // Empty paragraph - still counts for spacing
            // Keep runs empty so the renderer can avoid creating spurious extra pages at document end.
            result.Add(new ParagraphElement
            {
                Runs = [],
                Properties = props
            });
        }
        else if (runs.Count > 0)
        {
            result.Add(new ParagraphElement
            {
                Runs = runs,
                Properties = props
            });
        }

        // Add section break after paragraph content
        if (sectionBreak != null)
        {
            result.Add(sectionBreak);
        }

        return result;
    }

    /// <summary>
    /// Represents an accumulated transform from nested groups.
    /// </summary>
    struct AccumulatedTransform
    {
        public double OffsetX; // Accumulated offset in EMUs
        public double OffsetY;
        public double ScaleX; // Accumulated scale
        public double ScaleY;
    }

    /// <summary>
    /// Calculates the accumulated transform for an element by walking up through ancestor grpSp groups.
    /// </summary>
    static AccumulatedTransform GetAccumulatedTransform(OpenXmlElement element, long rootChOffX, long rootChOffY, double rootScaleX, double rootScaleY)
    {
        // Collect ancestor group transforms (from innermost to outermost, excluding root wgp)
        var groupTransforms = new List<(long offX, long offY, long extCx, long extCy, long chOffX, long chOffY, long chExtCx, long chExtCy)>();

        var current = element.Parent;
        while (current != null)
        {
            // Check if this is a grpSp element (DrawingML group, not wgp which is already handled)
            if (current.LocalName == "grpSp")
            {
                var grpSpPr = current.Elements().FirstOrDefault(e => e.LocalName == "grpSpPr");
                var xfrm = grpSpPr?.Elements().FirstOrDefault(e => e.LocalName == "xfrm");

                if (xfrm != null)
                {
                    long offX = 0, offY = 0, extCx = 1, extCy = 1, chOffX = 0, chOffY = 0, chExtCx = 1, chExtCy = 1;

                    var off = xfrm.Elements().FirstOrDefault(e => e.LocalName == "off");
                    var ext = xfrm.Elements().FirstOrDefault(e => e.LocalName == "ext");
                    var chOff = xfrm.Elements().FirstOrDefault(e => e.LocalName == "chOff");
                    var chExt = xfrm.Elements().FirstOrDefault(e => e.LocalName == "chExt");

                    if (off != null)
                    {
                        var xAttr = off.GetAttributes().FirstOrDefault(a => a.LocalName == "x");
                        var yAttr = off.GetAttributes().FirstOrDefault(a => a.LocalName == "y");
                        if (xAttr.Value != null)
                        {
                            long.TryParse(xAttr.Value, out offX);
                        }

                        if (yAttr.Value != null)
                        {
                            long.TryParse(yAttr.Value, out offY);
                        }
                    }

                    if (ext != null)
                    {
                        var cxAttr = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cx");
                        var cyAttr = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cy");
                        if (cxAttr.Value != null)
                        {
                            long.TryParse(cxAttr.Value, out extCx);
                        }

                        if (cyAttr.Value != null)
                        {
                            long.TryParse(cyAttr.Value, out extCy);
                        }
                    }

                    if (chOff != null)
                    {
                        var xAttr = chOff.GetAttributes().FirstOrDefault(a => a.LocalName == "x");
                        var yAttr = chOff.GetAttributes().FirstOrDefault(a => a.LocalName == "y");
                        if (xAttr.Value != null)
                        {
                            long.TryParse(xAttr.Value, out chOffX);
                        }

                        if (yAttr.Value != null)
                        {
                            long.TryParse(yAttr.Value, out chOffY);
                        }
                    }

                    if (chExt != null)
                    {
                        var cxAttr = chExt.GetAttributes().FirstOrDefault(a => a.LocalName == "cx");
                        var cyAttr = chExt.GetAttributes().FirstOrDefault(a => a.LocalName == "cy");
                        if (cxAttr.Value != null)
                        {
                            long.TryParse(cxAttr.Value, out chExtCx);
                        }

                        if (cyAttr.Value != null)
                        {
                            long.TryParse(cyAttr.Value, out chExtCy);
                        }
                    }

                    if (extCx <= 0)
                    {
                        extCx = 1;
                    }

                    if (extCy <= 0)
                    {
                        extCy = 1;
                    }

                    if (chExtCx <= 0)
                    {
                        chExtCx = 1;
                    }

                    if (chExtCy <= 0)
                    {
                        chExtCy = 1;
                    }

                    groupTransforms.Add((offX, offY, extCx, extCy, chOffX, chOffY, chExtCx, chExtCy));
                }
            }
            else if (current.LocalName == "wgp")
            {
                // Stop at the root WordprocessingGroup - its transform is applied separately via rootScaleX/Y
                break;
            }

            current = current.Parent;
        }

        // Apply transforms from outermost to innermost (reverse the list since we collected from innermost)
        // Start with no offset, unit scale - the element's own position will be added later
        double accumX = 0;
        double accumY = 0;
        var accumScaleX = 1.0;
        var accumScaleY = 1.0;

        // Process from outermost to innermost
        for (var i = groupTransforms.Count - 1; i >= 0; i--)
        {
            var (offX, offY, extCx, extCy, chOffX, chOffY, chExtCx, chExtCy) = groupTransforms[i];

            // Scale factors for this group
            var scaleX = (double) extCx / chExtCx;
            var scaleY = (double) extCy / chExtCy;

            // Transform accumulated position into this group's coordinate system
            // First apply the child offset (origin of child coordinates)
            // Then apply the group's own offset
            accumX = offX + (accumX - chOffX) * scaleX;
            accumY = offY + (accumY - chOffY) * scaleY;

            // Accumulate scales
            accumScaleX *= scaleX;
            accumScaleY *= scaleY;
        }

        // Apply root wgp transform
        accumX = (accumX - rootChOffX) * rootScaleX;
        accumY = (accumY - rootChOffY) * rootScaleY;
        accumScaleX *= rootScaleX;
        accumScaleY *= rootScaleY;

        return new()
        {
            OffsetX = accumX,
            OffsetY = accumY,
            ScaleX = accumScaleX,
            ScaleY = accumScaleY
        };
    }

    /// <summary>
    /// Tries to parse an inline image from a drawing element and returns it as a Run.
    /// Returns null if the drawing is not a simple inline image (e.g., if it's anchored or a group).
    /// </summary>
    static Run? TryParseInlineImageRun(Drawing drawing, MainDocumentPart mainPart, RunProperties runProps)
    {
        // Use XML-based approach for better namespace handling
        var hasAnchor = drawing.Descendants().Any(e => e.LocalName == "anchor");
        var hasInline = drawing.Descendants().Any(e => e.LocalName == "inline");

        // Only handle simple inline images, not anchored images
        if (hasAnchor || !hasInline)
        {
            return null;
        }

        // Check if this is a group (more complex structure)
        var hasGroup = drawing.Descendants<WPG.WordprocessingGroup>().Any();
        if (hasGroup)
        {
            return null;
        }

        // Find the pic element
        var pic = drawing.Descendants().FirstOrDefault(e => e.LocalName == "pic");
        if (pic == null)
        {
            return null;
        }

        // Get the picture's shape properties for size (same approach as ParseDrawingElements)
        var spPr = pic.Elements().FirstOrDefault(e => e.LocalName == "spPr");

        var xfrm = spPr?.Elements().FirstOrDefault(e => e.LocalName == "xfrm");
        if (xfrm == null)
        {
            return null;
        }

        // Get image extent from pic's spPr (more reliable than inline.Extent for some documents)
        long picWidth = 0, picHeight = 0;
        var ext = xfrm.Elements().FirstOrDefault(e => e.LocalName == "ext");
        if (ext != null)
        {
            var cxAttr = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cx");
            var cyAttr = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cy");
            if (cxAttr.Value != null)
            {
                long.TryParse(cxAttr.Value, out picWidth);
            }

            if (cyAttr.Value != null)
            {
                long.TryParse(cyAttr.Value, out picHeight);
            }
        }

        if (picWidth == 0 || picHeight == 0)
        {
            return null;
        }

        var widthPoints = picWidth / emusPerPoint;
        var heightPoints = picHeight / emusPerPoint;

        // Find the blip (image reference)
        var blipFill = pic.Elements().FirstOrDefault(e => e.LocalName == "blipFill");
        if (blipFill == null)
        {
            return null;
        }

        var blip = blipFill.Descendants().FirstOrDefault(e => e.LocalName == "blip");
        if (blip == null)
        {
            return null;
        }

        var embedAttr = blip.GetAttributes().FirstOrDefault(a => a.LocalName == "embed");
        if (embedAttr.Value == null)
        {
            return null;
        }

        // Try to get SVG first, then fall back to regular image
        byte[]? imageData = null;
        string? contentType = null;

        // Check for SVG extension
        var extLst = blip.Elements().FirstOrDefault(e => e.LocalName == "extLst");
        if (extLst != null)
        {
            foreach (var extEl in extLst.Elements().Where(e => e.LocalName == "ext"))
            {
                var uriAttr = extEl.GetAttributes().FirstOrDefault(a => a.LocalName == "uri");
                if (uriAttr.Value != "{96DAC541-7B7A-43D3-8B79-37D633B846F1}")
                {
                    continue;
                }

                var svgBlip = extEl.Descendants().FirstOrDefault(e => e.LocalName == "svgBlip");
                if (svgBlip == null)
                {
                    continue;
                }

                var svgEmbedAttr = svgBlip.GetAttributes().FirstOrDefault(a => a.LocalName == "embed");
                if (svgEmbedAttr.Value == null)
                {
                    continue;
                }

                var svgPart = mainPart.GetPartById(svgEmbedAttr.Value);
                using var stream = svgPart.GetStream();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                imageData = ms.ToArray();
                contentType = "image/svg+xml";
            }
        }

        // Fall back to regular image
        if (imageData == null)
        {
            if (mainPart.GetPartById(embedAttr.Value) is ImagePart imagePart)
            {
                using var stream = imagePart.GetStream();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                imageData = ms.ToArray();
                contentType = imagePart.ContentType;
            }
        }

        if (imageData == null || imageData.Length == 0)
        {
            return null;
        }

        // Create a Run with inline image data
        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageData = imageData,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            InlineImageContentType = contentType
        };
    }

    /// <summary>
    /// Parses all images from a drawing element, including multiple images in groups.
    /// </summary>
    static List<DocumentElement> ParseDrawingElements(Drawing drawing, MainDocumentPart mainPart)
    {
        var result = new List<DocumentElement>();

        var anchor = drawing.GetFirstChild<DW.Anchor>();
        var inline = drawing.GetFirstChild<DW.Inline>();

        // Get the group transform if present (for coordinate system)
        long groupOffsetX = 0, groupOffsetY = 0;
        double groupScaleX = 1.0, groupScaleY = 1.0;

        var grpSpPr = drawing.Descendants().FirstOrDefault(e => e.LocalName == "grpSpPr");
        if (grpSpPr != null)
        {
            var xfrm = grpSpPr.Elements().FirstOrDefault(e => e.LocalName == "xfrm");
            if (xfrm != null)
            {
                // Get child extents for scaling
                var chOff = xfrm.Elements().FirstOrDefault(e => e.LocalName == "chOff");
                var chExt = xfrm.Elements().FirstOrDefault(e => e.LocalName == "chExt");
                var ext = xfrm.Elements().FirstOrDefault(e => e.LocalName == "ext");

                if (chOff != null)
                {
                    var xAttr = chOff.GetAttributes().FirstOrDefault(a => a.LocalName == "x");
                    var yAttr = chOff.GetAttributes().FirstOrDefault(a => a.LocalName == "y");
                    if (xAttr.Value != null)
                    {
                        long.TryParse(xAttr.Value, out groupOffsetX);
                    }

                    if (yAttr.Value != null)
                    {
                        long.TryParse(yAttr.Value, out groupOffsetY);
                    }
                }

                if (chExt != null && ext != null)
                {
                    var chCx = chExt.GetAttributes().FirstOrDefault(a => a.LocalName == "cx");
                    var chCy = chExt.GetAttributes().FirstOrDefault(a => a.LocalName == "cy");
                    var extCx = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cx");
                    var extCy = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cy");

                    if (chCx.Value != null && extCx.Value != null &&
                        long.TryParse(chCx.Value, out var childWidth) && long.TryParse(extCx.Value, out var actualWidth) &&
                        childWidth > 0)
                    {
                        groupScaleX = (double) actualWidth / childWidth;
                    }

                    if (chCy.Value != null && extCy.Value != null &&
                        long.TryParse(chCy.Value, out var childHeight) && long.TryParse(extCy.Value, out var actualHeight) &&
                        childHeight > 0)
                    {
                        groupScaleY = (double) actualHeight / childHeight;
                    }
                }
            }
        }

        // Find ALL pic elements (including in groups)
        var pics = drawing.Descendants().Where(e => e.LocalName == "pic").ToList();

        foreach (var pic in pics)
        {
            // Get the picture's shape properties for position/size
            var spPr = pic.Elements().FirstOrDefault(e => e.LocalName == "spPr");
            if (spPr == null)
            {
                continue;
            }

            var xfrm = spPr.Elements().FirstOrDefault(e => e.LocalName == "xfrm");
            if (xfrm == null)
            {
                continue;
            }

            // Get offset within group
            long picOffsetX = 0, picOffsetY = 0;
            var off = xfrm.Elements().FirstOrDefault(e => e.LocalName == "off");
            if (off != null)
            {
                var xAttr = off.GetAttributes().FirstOrDefault(a => a.LocalName == "x");
                var yAttr = off.GetAttributes().FirstOrDefault(a => a.LocalName == "y");
                if (xAttr.Value != null)
                {
                    long.TryParse(xAttr.Value, out picOffsetX);
                }

                if (yAttr.Value != null)
                {
                    long.TryParse(yAttr.Value, out picOffsetY);
                }
            }

            // Get image extent
            long picWidth = 0, picHeight = 0;
            var ext = xfrm.Elements().FirstOrDefault(e => e.LocalName == "ext");
            if (ext != null)
            {
                var cxAttr = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cx");
                var cyAttr = ext.GetAttributes().FirstOrDefault(a => a.LocalName == "cy");
                if (cxAttr.Value != null)
                {
                    long.TryParse(cxAttr.Value, out picWidth);
                }

                if (cyAttr.Value != null)
                {
                    long.TryParse(cyAttr.Value, out picHeight);
                }
            }

            if (picWidth == 0 || picHeight == 0)
            {
                continue;
            }

            // Get accumulated transform from all ancestor grpSp groups
            var accumTransform = GetAccumulatedTransform(pic, groupOffsetX, groupOffsetY, groupScaleX, groupScaleY);

            // Apply accumulated transform to the pic's position and size
            var finalX = accumTransform.OffsetX + picOffsetX * accumTransform.ScaleX;
            var finalY = accumTransform.OffsetY + picOffsetY * accumTransform.ScaleY;
            var finalWidth = picWidth * accumTransform.ScaleX;
            var finalHeight = picHeight * accumTransform.ScaleY;

            // Convert to points
            var widthPoints = finalWidth / emusPerPoint;
            var heightPoints = finalHeight / emusPerPoint;
            var offsetXPoints = finalX / emusPerPoint;
            var offsetYPoints = finalY / emusPerPoint;

            // Find the blip (image reference)
            var blipFill = pic.Elements().FirstOrDefault(e => e.LocalName == "blipFill");
            if (blipFill == null)
            {
                continue;
            }

            var blip = blipFill.Descendants().FirstOrDefault(e => e.LocalName == "blip");
            if (blip == null)
            {
                continue;
            }

            var embedAttr = blip.GetAttributes().FirstOrDefault(a => a.LocalName == "embed");
            if (embedAttr.Value == null)
            {
                continue;
            }

            // Try to get SVG first, then fall back to regular image
            byte[]? imageData = null;
            string? contentType = null;

            // Check for SVG extension
            var extLst = blip.Elements().FirstOrDefault(e => e.LocalName == "extLst");
            if (extLst != null)
            {
                foreach (var extEl in extLst.Elements().Where(e => e.LocalName == "ext"))
                {
                    var uriAttr = extEl.GetAttributes().FirstOrDefault(a => a.LocalName == "uri");
                    if (uriAttr.Value == "{96DAC541-7B7A-43D3-8B79-37D633B846F1}")
                    {
                        var svgBlip = extEl.Descendants().FirstOrDefault(e => e.LocalName == "svgBlip");
                        if (svgBlip != null)
                        {
                            var svgEmbedAttr = svgBlip.GetAttributes().FirstOrDefault(a => a.LocalName == "embed");
                            if (svgEmbedAttr.Value != null)
                            {
                                var svgPart = mainPart.GetPartById(svgEmbedAttr.Value);
                                using var stream = svgPart.GetStream();
                                using var ms = new MemoryStream();
                                stream.CopyTo(ms);
                                imageData = ms.ToArray();
                                contentType = "image/svg+xml";
                            }
                        }
                    }
                }
            }

            // Fall back to regular image
            if (imageData == null)
            {
                if (mainPart.GetPartById(embedAttr.Value) is ImagePart imagePart)
                {
                    using var stream = imagePart.GetStream();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    imageData = ms.ToArray();
                    contentType = imagePart.ContentType;
                }
            }

            if (imageData == null || imageData.Length == 0)
            {
                continue;
            }

            // Create the image element
            if (anchor == null)
            {
                result.Add(new ImageElement
                {
                    ImageData = imageData,
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    ContentType = contentType
                });
            }
            else
            {
                var floatingImage = ParseAnchoredImageWithOffset(anchor, imageData, widthPoints, heightPoints, contentType, offsetXPoints, offsetYPoints);
                result.Add(floatingImage);
            }
        }

        return result;
    }

    /// <summary>
    /// Parses an anchored image with additional X/Y offset within a group.
    /// </summary>
    static FloatingImageElement ParseAnchoredImageWithOffset(DW.Anchor anchor, byte[] imageData, double widthPoints, double heightPoints, string? contentType, double offsetXPoints, double offsetYPoints)
    {
        // Parse horizontal position
        var hPosPoints = offsetXPoints;
        var hAnchor = HorizontalAnchor.Column;

        var posH = anchor.GetFirstChild<DW.HorizontalPosition>();
        if (posH != null)
        {
            if (posH.RelativeFrom?.HasValue == true)
            {
                var relFrom = posH.RelativeFrom.Value;
                if (relFrom == DW.HorizontalRelativePositionValues.Page)
                {
                    hAnchor = HorizontalAnchor.Page;
                }
                else if (relFrom == DW.HorizontalRelativePositionValues.Margin)
                {
                    hAnchor = HorizontalAnchor.Margin;
                }
                else if (relFrom == DW.HorizontalRelativePositionValues.Column)
                {
                    hAnchor = HorizontalAnchor.Column;
                }
            }

            var posOffset = posH.GetFirstChild<DW.PositionOffset>();
            if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var hOffsetEmu))
            {
                hPosPoints += hOffsetEmu / emusPerPoint;
            }
        }

        // Parse vertical position
        var vPosPoints = offsetYPoints;
        var vAnchor = VerticalAnchor.Paragraph;

        var posV = anchor.GetFirstChild<DW.VerticalPosition>();
        if (posV != null)
        {
            if (posV.RelativeFrom?.HasValue == true)
            {
                var relFrom = posV.RelativeFrom.Value;
                if (relFrom == DW.VerticalRelativePositionValues.Page)
                {
                    vAnchor = VerticalAnchor.Page;
                }
                else if (relFrom == DW.VerticalRelativePositionValues.Margin)
                {
                    vAnchor = VerticalAnchor.Margin;
                }
                else if (relFrom == DW.VerticalRelativePositionValues.Paragraph)
                {
                    vAnchor = VerticalAnchor.Paragraph;
                }
            }

            var posOffset = posV.GetFirstChild<DW.PositionOffset>();
            if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var vOffsetEmu))
            {
                vPosPoints += vOffsetEmu / emusPerPoint;
            }
        }

        // Check if behind text
        var isBehindText = anchor.BehindDoc?.Value == true;

        return new()
        {
            ImageData = imageData,
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            ContentType = contentType,
            HorizontalPositionPoints = hPosPoints,
            VerticalPositionPoints = vPosPoints,
            HorizontalAnchor = hAnchor,
            VerticalAnchor = vAnchor,
            BehindText = isBehindText
        };
    }

    /// <summary>
    /// Parses an anchored (floating) image with positioning information.
    /// </summary>
    static FloatingImageElement ParseAnchoredImage(DW.Anchor anchor, byte[] imageData, double widthPoints, double heightPoints, string? contentType, double imageOffsetYPoints = 0)
    {
        // Parse horizontal position
        double hPosPoints = 0;
        var hAnchor = HorizontalAnchor.Column;

        var posH = anchor.GetFirstChild<DW.HorizontalPosition>();
        if (posH != null)
        {
            // Parse relative from
            if (posH.RelativeFrom?.HasValue == true)
            {
                var relFrom = posH.RelativeFrom.Value;
                if (relFrom == DW.HorizontalRelativePositionValues.Page)
                {
                    hAnchor = HorizontalAnchor.Page;
                }
                else if (relFrom == DW.HorizontalRelativePositionValues.Margin)
                {
                    hAnchor = HorizontalAnchor.Margin;
                }
                else if (relFrom == DW.HorizontalRelativePositionValues.Column)
                {
                    hAnchor = HorizontalAnchor.Column;
                }
                else if (relFrom == DW.HorizontalRelativePositionValues.Character)
                {
                    hAnchor = HorizontalAnchor.Character;
                }
            }

            // Parse position offset
            var posOffset = posH.GetFirstChild<DW.PositionOffset>();
            if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var hOffsetEmu))
            {
                hPosPoints = hOffsetEmu / emusPerPoint;
            }

            // Handle alignment (center, left, right, etc.)
            var align = posH.GetFirstChild<DW.HorizontalAlignment>();
            if (align?.Text != null)
            {
                // For alignment, we'll calculate the position later during rendering
                // Store a flag or calculate approximate position
            }
        }

        // Parse vertical position
        double vPosPoints = 0;
        var vAnchor = VerticalAnchor.Paragraph;

        var posV = anchor.GetFirstChild<DW.VerticalPosition>();
        if (posV != null)
        {
            // Parse relative from
            if (posV.RelativeFrom?.HasValue == true)
            {
                var relFrom = posV.RelativeFrom.Value;
                if (relFrom == DW.VerticalRelativePositionValues.Page)
                {
                    vAnchor = VerticalAnchor.Page;
                }
                else if (relFrom == DW.VerticalRelativePositionValues.Margin)
                {
                    vAnchor = VerticalAnchor.Margin;
                }
                else if (relFrom == DW.VerticalRelativePositionValues.Paragraph)
                {
                    vAnchor = VerticalAnchor.Paragraph;
                }
                else if (relFrom == DW.VerticalRelativePositionValues.Line)
                {
                    vAnchor = VerticalAnchor.Line;
                }
            }

            // Parse position offset
            var posOffset = posV.GetFirstChild<DW.PositionOffset>();
            if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var vOffsetEmu))
            {
                vPosPoints = vOffsetEmu / emusPerPoint;
            }
        }

        // Add the image's offset within its group (for images positioned within a group)
        vPosPoints += imageOffsetYPoints;

        // Parse wrap type
        var wrapType = WrapType.None;
        if (anchor.GetFirstChild<DW.WrapNone>() != null)
        {
            wrapType = WrapType.None;
        }
        else if (anchor.GetFirstChild<DW.WrapSquare>() != null)
        {
            wrapType = WrapType.Square;
        }
        else if (anchor.GetFirstChild<DW.WrapTight>() != null)
        {
            wrapType = WrapType.Tight;
        }
        else if (anchor.GetFirstChild<DW.WrapThrough>() != null)
        {
            wrapType = WrapType.Through;
        }
        else if (anchor.GetFirstChild<DW.WrapTopBottom>() != null)
        {
            wrapType = WrapType.TopAndBottom;
        }

        // Parse behind text flag
        var behindText = anchor.BehindDoc?.Value ?? false;

        // Parse z-order (relative z-ordering)
        var zOrder = 0;
        if (anchor.RelativeHeight?.HasValue == true)
        {
            zOrder = (int) anchor.RelativeHeight.Value;
        }

        return new()
        {
            ImageData = imageData,
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            ContentType = contentType,
            HorizontalPositionPoints = hPosPoints,
            VerticalPositionPoints = vPosPoints,
            HorizontalAnchor = hAnchor,
            VerticalAnchor = vAnchor,
            WrapType = wrapType,
            BehindText = behindText,
            ZOrder = zOrder
        };
    }

    /// <summary>
    /// Parses a Drawing element to extract a text box (shape with text content, without WordArt transform).
    /// </summary>
    FloatingTextBoxElement? ParseTextBox(Drawing drawing, MainDocumentPart mainPart)
    {
        // Get dimensions and anchor info
        long widthEmu = 0;
        long heightEmu = 0;

        var inline = drawing.GetFirstChild<DW.Inline>();
        var anchor = drawing.GetFirstChild<DW.Anchor>();

        if (inline != null)
        {
            var extent = inline.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }
        else if (anchor != null)
        {
            var extent = anchor.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }

        if (widthEmu == 0 || heightEmu == 0)
        {
            return null;
        }

        var widthPoints = widthEmu / emusPerPoint;
        var heightPoints = heightEmu / emusPerPoint;

        // Find WordprocessingShape element
        var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
        if (wsp == null)
        {
            return null;
        }

        // Get text content from text box
        var txbx = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (txbx == null)
        {
            return null;
        }

        var txbxContent = txbx.GetFirstChild<TextBoxContent>();
        if (txbxContent == null)
        {
            return null;
        }

        // Check if this is WordArt (has text transform) - if so, skip it for this parser
        var bodyPr = wsp.GetFirstChild<WPS.TextBodyProperties>();
        if (bodyPr != null)
        {
            var prstTxWarp = bodyPr.GetFirstChild<A.PresetTextWarp>();
            if (prstTxWarp?.Preset?.HasValue == true && prstTxWarp.Preset.Value != A.TextShapeValues.TextNoShape)
            {
                // This is WordArt, let ParseWordArt handle it
                return null;
            }
        }

        // Parse the content as document elements
        var content = new List<DocumentElement>();
        foreach (var element in txbxContent.ChildElements)
        {
            if (element is Paragraph para)
            {
                var parsedElements = ParseParagraph(para, mainPart);
                content.AddRange(parsedElements);
            }
            else if (element is Table table)
            {
                var parsedTable = ParseTable(table, mainPart);
                if (parsedTable != null)
                {
                    content.Add(parsedTable);
                }
            }
        }

        if (content.Count == 0)
        {
            return null;
        }

        // Get position and wrap info from anchor
        double hPosPoints = 0;
        double vPosPoints = 0;
        var hAnchor = HorizontalAnchor.Column;
        var vAnchor = VerticalAnchor.Paragraph;
        var wrapType = WrapType.None;
        var behindText = false;
        string? bgColor = null;

        if (anchor != null)
        {
            // Parse horizontal position
            var posH = anchor.GetFirstChild<DW.HorizontalPosition>();
            if (posH != null)
            {
                if (posH.RelativeFrom?.HasValue == true)
                {
                    var relFrom = posH.RelativeFrom.Value;
                    if (relFrom == DW.HorizontalRelativePositionValues.Page)
                    {
                        hAnchor = HorizontalAnchor.Page;
                    }
                    else if (relFrom == DW.HorizontalRelativePositionValues.Margin)
                    {
                        hAnchor = HorizontalAnchor.Margin;
                    }
                    else if (relFrom == DW.HorizontalRelativePositionValues.Column)
                    {
                        hAnchor = HorizontalAnchor.Column;
                    }
                }

                var posOffset = posH.GetFirstChild<DW.PositionOffset>();
                if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var hOffsetEmu))
                {
                    hPosPoints = hOffsetEmu / emusPerPoint;
                }
            }

            // Parse vertical position
            var posV = anchor.GetFirstChild<DW.VerticalPosition>();
            if (posV != null)
            {
                if (posV.RelativeFrom?.HasValue == true)
                {
                    var relFrom = posV.RelativeFrom.Value;
                    if (relFrom == DW.VerticalRelativePositionValues.Page)
                    {
                        vAnchor = VerticalAnchor.Page;
                    }
                    else if (relFrom == DW.VerticalRelativePositionValues.Margin)
                    {
                        vAnchor = VerticalAnchor.Margin;
                    }
                    else if (relFrom == DW.VerticalRelativePositionValues.Paragraph)
                    {
                        vAnchor = VerticalAnchor.Paragraph;
                    }
                }

                var posOffset = posV.GetFirstChild<DW.PositionOffset>();
                if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var vOffsetEmu))
                {
                    vPosPoints = vOffsetEmu / emusPerPoint;
                }
            }

            // Parse wrap type
            if (anchor.GetFirstChild<DW.WrapNone>() != null)
            {
                wrapType = WrapType.None;
            }
            else if (anchor.GetFirstChild<DW.WrapSquare>() != null)
            {
                wrapType = WrapType.Square;
            }
            else if (anchor.GetFirstChild<DW.WrapTight>() != null)
            {
                wrapType = WrapType.Tight;
            }
            else if (anchor.GetFirstChild<DW.WrapTopBottom>() != null)
            {
                wrapType = WrapType.TopAndBottom;
            }

            behindText = anchor.BehindDoc?.Value ?? false;
        }

        // Parse background color from shape properties
        var spPr = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (spPr != null)
        {
            var solidFill = spPr.GetFirstChild<A.SolidFill>();
            if (solidFill != null)
            {
                var rgbColor = solidFill.GetFirstChild<A.RgbColorModelHex>();
                if (rgbColor?.Val?.HasValue == true)
                {
                    bgColor = rgbColor.Val.Value;
                }
            }
        }

        return new()
        {
            Content = content,
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            HorizontalPositionPoints = hPosPoints,
            VerticalPositionPoints = vPosPoints,
            HorizontalAnchor = hAnchor,
            VerticalAnchor = vAnchor,
            WrapType = wrapType,
            BehindText = behindText,
            BackgroundColorHex = bgColor
        };
    }

    /// <summary>
    /// Parses ALL shapes from a drawing (handles groups with multiple shapes).
    /// Returns both text boxes and solid fill shapes.
    /// </summary>
    List<DocumentElement> ParseAllShapesFromDrawing(Drawing drawing, MainDocumentPart mainPart)
    {
        var result = new List<DocumentElement>();

        var anchor = drawing.GetFirstChild<DW.Anchor>();
        if (anchor == null)
        {
            return result;
        }

        // Get base positioning from anchor
        var positioning = anchor.ParsePositioning();
        var behindText = anchor.BehindDoc?.Value ?? false;

        // Check for a WordprocessingGroup
        var wgp = drawing.Descendants<WPG.WordprocessingGroup>().FirstOrDefault();
        if (wgp != null)
        {
            // Get root group transform info
            var grpSpPr = wgp.GetFirstChild<WPG.GroupShapeProperties>();
            var grpXfrm = grpSpPr?.GetFirstChild<A.TransformGroup>();

            long chOffX = 0, chOffY = 0;
            long chExtCx = 1, chExtCy = 1;

            var chOff = grpXfrm?.ChildOffset;
            var chExt = grpXfrm?.ChildExtents;

            if (chOff != null)
            {
                chOffX = chOff.X ?? 0;
                chOffY = chOff.Y ?? 0;
            }

            if (chExt != null)
            {
                chExtCx = chExt.Cx ?? 1;
                chExtCy = chExt.Cy ?? 1;
            }

            var extent = anchor.Extent;
            var rootScaleX = (extent?.Cx ?? 1) / (double) chExtCx;
            var rootScaleY = (extent?.Cy ?? 1) / (double) chExtCy;

            // Process all shapes in the group (including nested grpSp groups)
            foreach (var wsp in wgp.Descendants<WPS.WordprocessingShape>())
            {
                // Get accumulated transform from all ancestor grpSp groups
                var accumTransform = GetAccumulatedTransform(wsp, chOffX, chOffY, rootScaleX, rootScaleY);

                var textBox = ParseTextBoxFromShapeWithTransform(wsp, positioning, accumTransform, behindText, mainPart);
                if (textBox != null)
                {
                    result.Add(textBox);
                }
                else
                {
                    // Try to parse as a solid fill shape (no text box)
                    var solidShape = ParseSolidFillShape(wsp, positioning, accumTransform, behindText, mainPart);
                    if (solidShape != null)
                    {
                        result.Add(solidShape);
                    }
                }
            }
        }
        else
        {
            // Single shape
            var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
            if (wsp != null)
            {
                var extent = anchor.Extent;
                var widthPoints = (extent?.Cx ?? 0) / emusPerPoint;
                var heightPoints = (extent?.Cy ?? 0) / emusPerPoint;

                var textBox = ParseTextBoxFromShape(wsp, positioning, 0, 0, 1, 1, behindText, mainPart, widthPoints, heightPoints);
                if (textBox != null)
                {
                    result.Add(textBox);
                }
                else
                {
                    // Try to parse as a solid fill shape (no text box)
                    var accumTransform = new AccumulatedTransform
                    {
                        OffsetX = 0,
                        OffsetY = 0,
                        ScaleX = 1,
                        ScaleY = 1
                    };
                    var solidShape = ParseSolidFillShape(wsp, positioning, accumTransform, behindText, mainPart, widthPoints, heightPoints);
                    if (solidShape != null)
                    {
                        result.Add(solidShape);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a text box from a single WordprocessingShape.
    /// </summary>
    FloatingTextBoxElement? ParseTextBoxFromShape(
        WPS.WordprocessingShape wsp,
        AnchorPositioning positioning,
        long chOffX, long chOffY,
        double scaleX, double scaleY,
        bool behindText,
        MainDocumentPart mainPart,
        double? overrideWidth = null,
        double? overrideHeight = null)
    {
        var txbx = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (txbx == null)
        {
            return null;
        }

        var txbxContent = txbx.GetFirstChild<TextBoxContent>();
        if (txbxContent == null)
        {
            return null;
        }

        // Skip WordArt (has text transform)
        var bodyPr = wsp.GetFirstChild<WPS.TextBodyProperties>();
        if (bodyPr != null)
        {
            var prstTxWarp = bodyPr.GetFirstChild<A.PresetTextWarp>();
            if (prstTxWarp?.Preset?.HasValue == true && prstTxWarp.Preset.Value != A.TextShapeValues.TextNoShape)
            {
                return null;
            }
        }

        // Get shape transform for positioning
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        var xfrm = shapeProps?.GetFirstChild<A.Transform2D>();

        var xPt = positioning.HorizontalPositionPoints;
        var yPt = positioning.VerticalPositionPoints;
        var widthPt = overrideWidth ?? 0;
        var heightPt = overrideHeight ?? 0;
        double rotationDegrees = 0;

        if (xfrm != null)
        {
            var off = xfrm.Offset;
            var ext = xfrm.Extents;

            if (off != null)
            {
                long shapeX = off.X ?? 0;
                long shapeY = off.Y ?? 0;
                xPt = positioning.HorizontalPositionPoints + ((shapeX - chOffX) * scaleX).EmuToPoints();
                yPt = positioning.VerticalPositionPoints + ((shapeY - chOffY) * scaleY).EmuToPoints();
            }

            if (ext != null)
            {
                widthPt = ((ext.Cx ?? 0) * scaleX).EmuToPoints();
                heightPt = ((ext.Cy ?? 0) * scaleY).EmuToPoints();
            }

            // Extract rotation (in 60,000ths of a degree)
            if (xfrm.Rotation?.HasValue == true)
            {
                rotationDegrees = xfrm.Rotation.Value / 60000.0;
            }
        }

        if (widthPt <= 0 || heightPt <= 0)
        {
            return null;
        }

        // Parse content
        var content = new List<DocumentElement>();
        foreach (var element in txbxContent.ChildElements)
        {
            if (element is Paragraph para)
            {
                var paragraphElements = ParseParagraph(para, mainPart);
                content.AddRange(paragraphElements);
            }
            else if (element is Table table)
            {
                var tableElement = ParseTable(table, mainPart);
                if (tableElement != null)
                {
                    content.Add(tableElement);
                }
            }
        }

        if (content.Count == 0)
        {
            return null;
        }

        // Get background color if present
        string? bgColor = null;
        var solidFill = shapeProps?.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            bgColor = ShapeParser.ExtractSolidFillColor(solidFill, currentThemeColors);
        }

        return new()
        {
            Content = content,
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            HorizontalPositionPoints = xPt,
            VerticalPositionPoints = yPt,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            WrapType = WrapType.None,
            BehindText = behindText,
            BackgroundColorHex = bgColor,
            RotationDegrees = rotationDegrees
        };
    }

    /// <summary>
    /// Parses a solid fill shape (no text box) as a FloatingShapeElement.
    /// </summary>
    FloatingShapeElement? ParseSolidFillShape(
        WPS.WordprocessingShape wsp,
        AnchorPositioning positioning,
        AccumulatedTransform accumTransform,
        bool behindText,
        MainDocumentPart mainPart,
        double? overrideWidth = null,
        double? overrideHeight = null)
    {
        // Get shape properties
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (shapeProps == null)
        {
            return null;
        }

        // Skip shapes with blip fill (image fill) - these are already handled by ShapeParser.ParseBackgroundShapes
        var blipFill = shapeProps.GetFirstChild<A.BlipFill>();
        if (blipFill != null)
        {
            return null;
        }

        // Check for solid fill in shape properties
        var solidFill = shapeProps.GetFirstChild<A.SolidFill>();
        string? fillColorHex = null;

        if (solidFill != null)
        {
            // Check for direct RGB color
            var srgbClr = solidFill.GetFirstChild<A.RgbColorModelHex>();
            if (srgbClr?.Val?.HasValue == true)
            {
                fillColorHex = srgbClr.Val.Value;
            }
            else
            {
                // Check for scheme color (theme color)
                var schemeClr = solidFill.GetFirstChild<A.SchemeColor>();
                if (schemeClr?.Val?.HasValue == true)
                {
                    // Check if the scheme color has any color transforms (lumMod, lumOff, etc.)
                    var hasLumMod = schemeClr.GetFirstChild<A.LuminanceModulation>() != null;
                    var hasLumOff = schemeClr.GetFirstChild<A.LuminanceOffset>() != null;
                    var hasTint = schemeClr.GetFirstChild<A.Tint>() != null;
                    var hasShade = schemeClr.GetFirstChild<A.Shade>() != null;

                    if ((hasLumMod || hasLumOff || hasTint || hasShade) && currentThemeColors != null)
                    {
                        // Use ThemeColors.ResolveColor which properly handles color transforms
                        var schemeValue = ((IEnumValue) schemeClr.Val.Value).Value;
                        var transforms = new ColorTransforms
                        {
                            LumMod = hasLumMod ? schemeClr.GetFirstChild<A.LuminanceModulation>()!.Val!.Value / 1000.0 : null,
                            LumOff = hasLumOff ? schemeClr.GetFirstChild<A.LuminanceOffset>()!.Val!.Value / 1000.0 : null,
                            Tint = hasTint ? (byte) (schemeClr.GetFirstChild<A.Tint>()!.Val!.Value / 392.157) : null,
                            Shade = hasShade ? (byte) (schemeClr.GetFirstChild<A.Shade>()!.Val!.Value / 392.157) : null
                        };
                        fillColorHex = currentThemeColors.ResolveColor(schemeValue ?? "", transforms);
                    }

                    // Fallback to original method (no transforms or ResolveColor failed)
                    if (fillColorHex == null)
                    {
                        fillColorHex = ResolveSchemeColor(schemeClr.Val.Value, mainPart);
                    }
                }
            }
        }

        // If no direct fill, check for style reference (fillRef in wps:style)
        if (fillColorHex == null)
        {
            var shapeStyle = wsp.GetFirstChild<WPS.ShapeStyle>();
            var fillRef = shapeStyle?.FillReference;
            if (fillRef != null)
            {
                // fillRef idx="1" with a scheme color means solid fill with that color
                var schemeClr = fillRef.GetFirstChild<A.SchemeColor>();
                if (schemeClr?.Val?.HasValue == true)
                {
                    fillColorHex = ResolveSchemeColor(schemeClr.Val.Value, mainPart);
                }
            }
        }

        // If no fill, skip this shape
        if (fillColorHex == null)
        {
            return null;
        }

        // Get transform for positioning and size
        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();

        var xPt = positioning.HorizontalPositionPoints;
        var yPt = positioning.VerticalPositionPoints;
        var widthPt = overrideWidth ?? 0;
        var heightPt = overrideHeight ?? 0;

        if (xfrm != null)
        {
            var off = xfrm.Offset;
            var ext = xfrm.Extents;

            if (off != null)
            {
                long shapeX = off.X ?? 0;
                long shapeY = off.Y ?? 0;
                var finalX = accumTransform.OffsetX + shapeX * accumTransform.ScaleX;
                var finalY = accumTransform.OffsetY + shapeY * accumTransform.ScaleY;
                xPt = positioning.HorizontalPositionPoints + finalX.EmuToPoints();
                yPt = positioning.VerticalPositionPoints + finalY.EmuToPoints();
            }

            if (ext != null)
            {
                widthPt = ((ext.Cx ?? 0) * accumTransform.ScaleX).EmuToPoints();
                heightPt = ((ext.Cy ?? 0) * accumTransform.ScaleY).EmuToPoints();
            }
        }

        if (widthPt <= 0 || heightPt <= 0)
        {
            return null;
        }

        return new()
        {
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            HorizontalPositionPoints = xPt,
            VerticalPositionPoints = yPt,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            BehindText = behindText,
            FillColorHex = fillColorHex
        };
    }

    /// <summary>
    /// Resolves a scheme color to an RGB hex value using the document theme.
    /// </summary>
    static string? ResolveSchemeColor(A.SchemeColorValues schemeColor, MainDocumentPart mainPart)
    {
        var themePart = mainPart.ThemePart;
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

    /// <summary>
    /// Parses a text box from a WordprocessingShape using accumulated transform from nested groups.
    /// </summary>
    FloatingTextBoxElement? ParseTextBoxFromShapeWithTransform(
        WPS.WordprocessingShape wsp,
        AnchorPositioning positioning,
        AccumulatedTransform accumTransform,
        bool behindText,
        MainDocumentPart mainPart)
    {
        var txbx = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (txbx == null)
        {
            return null;
        }

        var txbxContent = txbx.GetFirstChild<TextBoxContent>();
        if (txbxContent == null)
        {
            return null;
        }

        // Skip WordArt (has text transform)
        var bodyPr = wsp.GetFirstChild<WPS.TextBodyProperties>();
        if (bodyPr != null)
        {
            var prstTxWarp = bodyPr.GetFirstChild<A.PresetTextWarp>();
            if (prstTxWarp?.Preset?.HasValue == true && prstTxWarp.Preset.Value != A.TextShapeValues.TextNoShape)
            {
                return null;
            }
        }

        // Get shape transform for positioning
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        var xfrm = shapeProps?.GetFirstChild<A.Transform2D>();

        var xPt = positioning.HorizontalPositionPoints;
        var yPt = positioning.VerticalPositionPoints;
        double widthPt = 0;
        double heightPt = 0;
        double rotationDegrees = 0;

        if (xfrm != null)
        {
            var off = xfrm.Offset;
            var ext = xfrm.Extents;

            if (off != null)
            {
                long shapeX = off.X ?? 0;
                long shapeY = off.Y ?? 0;
                // Apply accumulated transform: offset + shape position * scale
                var finalX = accumTransform.OffsetX + shapeX * accumTransform.ScaleX;
                var finalY = accumTransform.OffsetY + shapeY * accumTransform.ScaleY;
                xPt = positioning.HorizontalPositionPoints + finalX.EmuToPoints();
                yPt = positioning.VerticalPositionPoints + finalY.EmuToPoints();
            }

            if (ext != null)
            {
                widthPt = ((ext.Cx ?? 0) * accumTransform.ScaleX).EmuToPoints();
                heightPt = ((ext.Cy ?? 0) * accumTransform.ScaleY).EmuToPoints();
            }

            // Extract rotation (in 60,000ths of a degree)
            if (xfrm.Rotation?.HasValue == true)
            {
                rotationDegrees = xfrm.Rotation.Value / 60000.0;
            }
        }

        if (widthPt <= 0 || heightPt <= 0)
        {
            return null;
        }

        // Parse content
        var content = new List<DocumentElement>();
        foreach (var element in txbxContent.ChildElements)
        {
            if (element is Paragraph para)
            {
                var paragraphElements = ParseParagraph(para, mainPart);
                content.AddRange(paragraphElements);
            }
            else if (element is Table table)
            {
                var tableElement = ParseTable(table, mainPart);
                if (tableElement != null)
                {
                    content.Add(tableElement);
                }
            }
        }

        if (content.Count == 0)
        {
            return null;
        }

        // Get background color if present
        string? bgColor = null;
        var solidFill = shapeProps?.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            bgColor = ShapeParser.ExtractSolidFillColor(solidFill, currentThemeColors);
        }

        return new()
        {
            Content = content,
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            HorizontalPositionPoints = xPt,
            VerticalPositionPoints = yPt,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            WrapType = WrapType.None,
            BehindText = behindText,
            BackgroundColorHex = bgColor,
            RotationDegrees = rotationDegrees
        };
    }

    /// <summary>
    /// Parses a Drawing element to extract a WordArt shape.
    /// Returns WordArtElement for inline WordArt, FloatingWordArtElement for anchored WordArt.
    /// </summary>
    DocumentElement? ParseWordArt(Drawing drawing)
    {
        // Get dimensions from Inline or Anchor
        long widthEmu = 0;
        long heightEmu = 0;
        var isAnchored = false;

        var inline = drawing.GetFirstChild<DW.Inline>();
        var anchor = drawing.GetFirstChild<DW.Anchor>();

        if (inline != null)
        {
            var extent = inline.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }
        else if (anchor != null)
        {
            isAnchored = true;
            var extent = anchor.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }

        if (widthEmu == 0 || heightEmu == 0)
        {
            return null;
        }

        // Convert EMUs to points
        var widthPoints = widthEmu / emusPerPoint;
        var heightPoints = heightEmu / emusPerPoint;

        // Find WordprocessingShape element (wps:wsp)
        var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
        if (wsp == null)
        {
            return null;
        }

        // Get text content from text box (wps:txbx/w:txbxContent)
        var txbx = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (txbx == null)
        {
            return null;
        }

        var txbxContent = txbx.GetFirstChild<TextBoxContent>();
        if (txbxContent == null)
        {
            return null;
        }

        // Extract text from paragraphs in text box
        var textBuilder = new StringBuilder();
        foreach (var para in txbxContent.Descendants<Paragraph>())
        {
            foreach (var run in para.Descendants<OoxmlRun>())
            {
                foreach (var text in run.Descendants<Text>())
                {
                    textBuilder.Append(text.Text);
                }
            }
        }

        var wordArtText = textBuilder.ToString().Trim();
        if (string.IsNullOrEmpty(wordArtText))
        {
            return null;
        }

        // Parse font properties from the first run
        var fontFamily = "Aptos";
        double fontSize = 36;
        var bold = false;
        var italic = false;
        string? fillColor = null;

        var firstRun = txbxContent.Descendants<OoxmlRun>().FirstOrDefault();
        if (firstRun?.RunProperties != null)
        {
            var runProps = firstRun.RunProperties;

            var runFonts = runProps.GetFirstChild<RunFonts>();
            if (runFonts != null)
            {
                // First try theme font reference
                if (runFonts.AsciiTheme?.HasValue == true && currentThemeFonts != null)
                {
                    var themeValue = ((IEnumValue) runFonts.AsciiTheme.Value).Value;
                    var resolvedFont = currentThemeFonts.ResolveFont(themeValue);
                    if (resolvedFont != null)
                    {
                        fontFamily = resolvedFont;
                    }
                }
                // Fall back to direct font name
                else if (runFonts.Ascii?.HasValue == true)
                {
                    fontFamily = runFonts.Ascii.Value!;
                }
            }

            var fontSizeElement = runProps.GetFirstChild<FontSize>();
            if (fontSizeElement?.Val?.HasValue == true)
            {
                fontSize = double.Parse(fontSizeElement.Val.Value!) / 2.0;
            }

            var boldElement = runProps.GetFirstChild<Bold>();
            if (boldElement != null)
            {
                bold = boldElement.Val?.Value != false;
            }

            var italicElement = runProps.GetFirstChild<Italic>();
            if (italicElement != null)
            {
                italic = italicElement.Val?.Value != false;
            }

            var colorElement = runProps.GetFirstChild<Color>();
            if (colorElement?.Val?.HasValue == true)
            {
                fillColor = colorElement.Val.Value;
            }
        }

        // Parse text transform preset from body properties
        var transform = WordArtTransform.None;
        string? outlineColor = null;
        double outlineWidth = 0;
        var hasShadow = false;
        var hasReflection = false;
        var hasGlow = false;

        var bodyPr = wsp.GetFirstChild<WPS.TextBodyProperties>();
        if (bodyPr != null)
        {
            // Parse preset text warp (prstTxWarp)
            var prstTxWarp = bodyPr.GetFirstChild<A.PresetTextWarp>();
            if (prstTxWarp?.Preset?.HasValue == true)
            {
                transform = ParseTextWarpPreset(prstTxWarp.Preset.Value);
            }
        }

        // Check for effects in the shape style
        var spPr = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (spPr != null)
        {
            // Parse outline
            var outline = spPr.GetFirstChild<A.Outline>();
            if (outline != null)
            {
                var solidFill = outline.GetFirstChild<A.SolidFill>();
                if (solidFill != null)
                {
                    var rgbColor = solidFill.GetFirstChild<A.RgbColorModelHex>();
                    if (rgbColor?.Val?.HasValue == true)
                    {
                        outlineColor = rgbColor.Val.Value;
                    }

                    var schemeColor = solidFill.GetFirstChild<A.SchemeColor>();
                    if (schemeColor != null)
                    {
                        // Map common scheme colors
                        outlineColor = MapSchemeColor(schemeColor.Val?.Value);
                    }
                }

                if (outline.Width?.HasValue == true)
                {
                    outlineWidth = outline.Width.Value / emusPerPoint;
                }
            }

            // Parse fill color from shape properties
            var shapeSolidFill = spPr.GetFirstChild<A.SolidFill>();
            if (shapeSolidFill != null && fillColor == null)
            {
                var rgbColor = shapeSolidFill.GetFirstChild<A.RgbColorModelHex>();
                if (rgbColor?.Val?.HasValue == true)
                {
                    fillColor = rgbColor.Val.Value;
                }
            }

            // Check for effects
            var effectList = spPr.GetFirstChild<A.EffectList>();
            if (effectList != null)
            {
                hasShadow = effectList.GetFirstChild<A.OuterShadow>() != null ||
                            effectList.GetFirstChild<A.InnerShadow>() != null;
                hasReflection = effectList.GetFirstChild<A.Reflection>() != null;
                hasGlow = effectList.GetFirstChild<A.Glow>() != null;
            }
        }

        // For anchored WordArt, return a floating element with position info
        if (isAnchored && anchor != null)
        {
            // Parse horizontal position
            double hPosPoints = 0;
            var hAnchor = HorizontalAnchor.Column;

            var posH = anchor.GetFirstChild<DW.HorizontalPosition>();
            if (posH != null)
            {
                if (posH.RelativeFrom?.HasValue == true)
                {
                    var relFrom = posH.RelativeFrom.Value;
                    if (relFrom == DW.HorizontalRelativePositionValues.Page)
                    {
                        hAnchor = HorizontalAnchor.Page;
                    }
                    else if (relFrom == DW.HorizontalRelativePositionValues.Margin)
                    {
                        hAnchor = HorizontalAnchor.Margin;
                    }
                    else if (relFrom == DW.HorizontalRelativePositionValues.Character)
                    {
                        hAnchor = HorizontalAnchor.Character;
                    }
                }

                var posOffset = posH.GetFirstChild<DW.PositionOffset>();
                if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var hOffsetEmu))
                {
                    hPosPoints = hOffsetEmu / emusPerPoint;
                }
            }

            // Parse vertical position
            double vPosPoints = 0;
            var vAnchor = VerticalAnchor.Paragraph;

            var posV = anchor.GetFirstChild<DW.VerticalPosition>();
            if (posV != null)
            {
                if (posV.RelativeFrom?.HasValue == true)
                {
                    var relFrom = posV.RelativeFrom.Value;
                    if (relFrom == DW.VerticalRelativePositionValues.Page)
                    {
                        vAnchor = VerticalAnchor.Page;
                    }
                    else if (relFrom == DW.VerticalRelativePositionValues.Margin)
                    {
                        vAnchor = VerticalAnchor.Margin;
                    }
                    else if (relFrom == DW.VerticalRelativePositionValues.Line)
                    {
                        vAnchor = VerticalAnchor.Line;
                    }
                }

                var posOffset = posV.GetFirstChild<DW.PositionOffset>();
                if (posOffset?.Text != null && long.TryParse(posOffset.Text, out var vOffsetEmu))
                {
                    vPosPoints = vOffsetEmu / emusPerPoint;
                }
            }

            var isBehindText = anchor.BehindDoc?.Value == true;

            return new FloatingWordArtElement
            {
                Text = wordArtText,
                WidthPoints = widthPoints,
                HeightPoints = heightPoints,
                HorizontalPositionPoints = hPosPoints,
                VerticalPositionPoints = vPosPoints,
                HorizontalAnchor = hAnchor,
                VerticalAnchor = vAnchor,
                BehindText = isBehindText,
                FontFamily = fontFamily,
                FontSizePoints = fontSize,
                Bold = bold,
                Italic = italic,
                FillColorHex = fillColor,
                OutlineColorHex = outlineColor,
                OutlineWidthPoints = outlineWidth,
                HasShadow = hasShadow,
                HasReflection = hasReflection,
                HasGlow = hasGlow,
                Transform = transform
            };
        }

        // For inline WordArt, return a regular element
        return new WordArtElement
        {
            Text = wordArtText,
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            FontFamily = fontFamily,
            FontSizePoints = fontSize,
            Bold = bold,
            Italic = italic,
            FillColorHex = fillColor,
            OutlineColorHex = outlineColor,
            OutlineWidthPoints = outlineWidth,
            HasShadow = hasShadow,
            HasReflection = hasReflection,
            HasGlow = hasGlow,
            Transform = transform
        };
    }

    static WordArtTransform ParseTextWarpPreset(A.TextShapeValues preset)
    {
        if (preset == A.TextShapeValues.TextArchUp || preset == A.TextShapeValues.TextArchUpPour)
        {
            return WordArtTransform.ArchUp;
        }

        if (preset == A.TextShapeValues.TextArchDown || preset == A.TextShapeValues.TextArchDownPour)
        {
            return WordArtTransform.ArchDown;
        }

        if (preset == A.TextShapeValues.TextCircle || preset == A.TextShapeValues.TextCirclePour)
        {
            return WordArtTransform.Circle;
        }

        if (preset == A.TextShapeValues.TextWave1 || preset == A.TextShapeValues.TextWave2 || preset == A.TextShapeValues.TextWave4)
        {
            return WordArtTransform.Wave;
        }

        if (preset == A.TextShapeValues.TextChevron)
        {
            return WordArtTransform.ChevronUp;
        }

        if (preset == A.TextShapeValues.TextChevronInverted)
        {
            return WordArtTransform.ChevronDown;
        }

        if (preset == A.TextShapeValues.TextSlantUp)
        {
            return WordArtTransform.SlantUp;
        }

        if (preset == A.TextShapeValues.TextSlantDown)
        {
            return WordArtTransform.SlantDown;
        }

        if (preset == A.TextShapeValues.TextTriangle || preset == A.TextShapeValues.TextTriangleInverted)
        {
            return WordArtTransform.Triangle;
        }

        if (preset == A.TextShapeValues.TextFadeRight || preset == A.TextShapeValues.TextFadeUp)
        {
            return WordArtTransform.FadeRight;
        }

        if (preset == A.TextShapeValues.TextFadeLeft || preset == A.TextShapeValues.TextFadeDown)
        {
            return WordArtTransform.FadeLeft;
        }

        return WordArtTransform.None;
    }

    static string? MapSchemeColor(A.SchemeColorValues? schemeColor)
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

    /// <summary>
    /// Checks if an SdtRun is a specific content control type that should be rendered as a ContentControlElement.
    /// Returns true for checkboxes, combo boxes, dropdowns, date pickers, and plain text controls.
    /// Returns false for generic rich text containers that should just have their runs extracted.
    /// </summary>
    static bool IsContentControlType(SdtRun sdtRun)
    {
        var props = sdtRun.SdtProperties;
        if (props == null)
        {
            return false;
        }

        // Check for Office 2010 checkbox (w14:checkbox)
        if (props.Descendants().Any(e => e.LocalName == "checkbox"))
        {
            return true;
        }

        // Check for combo box, dropdown, date, text, or picture controls
        return props.GetFirstChild<SdtContentComboBox>() != null ||
               props.GetFirstChild<SdtContentDropDownList>() != null ||
               props.GetFirstChild<SdtContentDate>() != null ||
               props.GetFirstChild<SdtContentText>() != null ||
               props.GetFirstChild<SdtContentPicture>() != null;
    }

    /// <summary>
    /// Parses a content control (SdtRun) to extract form control information.
    /// </summary>
    ContentControlElement? ParseSdtRun(SdtRun sdtRun, MainDocumentPart mainPart, string? paragraphStyleId = null)
    {
        var props = sdtRun.SdtProperties;
        if (props == null)
        {
            return null;
        }

        // Determine control type
        var controlType = ContentControlType.RichText;
        string? tag = null;
        string? title = null;
        string? placeholder = null;
        bool? isChecked = null;
        List<string>? listItems = null;
        DateTime? dateValue = null;

        // Get tag and title
        var tagElement = props.GetFirstChild<Tag>();
        if (tagElement?.Val?.HasValue == true)
        {
            tag = tagElement.Val.Value;
        }

        var aliasElement = props.GetFirstChild<SdtAlias>();
        if (aliasElement?.Val?.HasValue == true)
        {
            title = aliasElement.Val.Value;
        }

        // Check for specific control types using Office 2010 Word namespace
        var checkbox14 = props.Descendants()
            .FirstOrDefault(e => e.LocalName == "checkbox");
        if (checkbox14 != null)
        {
            controlType = ContentControlType.CheckBox;
            var checkedElement = checkbox14.Descendants()
                .FirstOrDefault(e => e.LocalName == "checked");
            var checkedVal = checkedElement?.GetAttributes()
                .FirstOrDefault(a => a.LocalName == "val").Value;
            isChecked = checkedVal is "1" or "true";
        }
        else if (props.GetFirstChild<SdtContentComboBox>() != null)
        {
            controlType = ContentControlType.ComboBox;
            var combo = props.GetFirstChild<SdtContentComboBox>();
            listItems = combo?.Elements<ListItem>()
                .Select(li => li.DisplayText?.Value ?? li.Value?.Value ?? "")
                .ToList();
        }
        else if (props.GetFirstChild<SdtContentDropDownList>() != null)
        {
            controlType = ContentControlType.DropDownList;
            var dropdown = props.GetFirstChild<SdtContentDropDownList>();
            listItems = dropdown?.Elements<ListItem>()
                .Select(li => li.DisplayText?.Value ?? li.Value?.Value ?? "")
                .ToList();
        }
        else if (props.GetFirstChild<SdtContentDate>() != null)
        {
            controlType = ContentControlType.Date;
            var dateControl = props.GetFirstChild<SdtContentDate>();
            var fullDateVal = dateControl?.FullDate?.Value;
            if (fullDateVal.HasValue)
            {
                dateValue = fullDateVal.Value;
            }
        }
        else if (props.GetFirstChild<SdtContentText>() != null)
        {
            controlType = ContentControlType.PlainText;
        }
        else if (props.GetFirstChild<SdtContentPicture>() != null)
        {
            controlType = ContentControlType.Picture;
        }

        // Get placeholder text
        var placeholderElement = props.GetFirstChild<SdtPlaceholder>();
        if (placeholderElement != null)
        {
            var docPartElement = placeholderElement.GetFirstChild<DocPartGallery>();
            placeholder = docPartElement?.Val?.Value;
        }

        // Get content - extract styled runs to preserve formatting
        var content = "";
        var styledRuns = new List<Run>();
        var sdtContent = sdtRun.SdtContentRun;
        if (sdtContent != null)
        {
            // Parse each run with full styling, inheriting from paragraph style
            foreach (var run in sdtContent.Descendants<OoxmlRun>())
            {
                // Check for line breaks within the run
                var breakElement = run.GetFirstChild<Break>();
                if (breakElement != null && breakElement.Type?.Value != BreakValues.Page && breakElement.Type?.Value != BreakValues.Column)
                {
                    // Line break - add newline character
                    var runProps = ParseRunProperties(run.RunProperties, mainPart);
                    styledRuns.Add(new()
                    {
                        Text = "\n",
                        Properties = runProps
                    });
                    continue;
                }

                var parsedRun = ParseRun(run, mainPart, paragraphStyleId);
                if (parsedRun != null)
                {
                    styledRuns.Add(parsedRun);
                }
            }

            // Also build plain text content for backward compatibility
            content = string.Join("", styledRuns.Select(r => r.Text));
        }

        return new()
        {
            ControlType = controlType,
            Tag = tag,
            Title = title,
            PlaceholderText = placeholder,
            Content = content,
            Runs = styledRuns.Count > 0 ? styledRuns : null,
            Checked = isChecked,
            ListItems = listItems,
            DateValue = dateValue,
            WidthPoints = 100 // Default width
        };
    }

    /// <summary>
    /// Parses a legacy form field from a run containing FormFieldData.
    /// </summary>
    static FormFieldElement? ParseFormField(OoxmlRun run)
    {
        // Look for FieldChar with fldCharType="begin" followed by FormFieldData
        var fieldChar = run.GetFirstChild<FieldChar>();
        if (fieldChar?.FieldCharType?.Value != FieldCharValues.Begin)
        {
            return null;
        }

        var ffData = run.GetFirstChild<FormFieldData>();
        if (ffData == null)
        {
            return null;
        }

        // Get common properties
        var nameElement = ffData.GetFirstChild<FormFieldName>();
        var name = nameElement?.Val?.Value;

        var enabledElement = ffData.GetFirstChild<Enabled>();
        var enabled = enabledElement?.Val?.Value != false;

        // Check for checkbox
        var checkbox = ffData.GetFirstChild<CheckBox>();
        if (checkbox != null)
        {
            var checkedElement = checkbox.GetFirstChild<Checked>();
            // Default element may not have a strongly-typed class, search by local name
            var defaultElement = checkbox.ChildElements.FirstOrDefault(e => e.LocalName == "default");
            var sizeElement = checkbox.GetFirstChild<FormFieldSize>();

            var isChecked = checkedElement != null &&
                            (checkedElement.Val == null || checkedElement.Val.Value != false);
            var defaultChecked = false;
            if (defaultElement != null)
            {
                // Check if it has a val attribute with false value
                var valAttr = defaultElement.GetAttributes().FirstOrDefault(a => a.LocalName == "val");
                defaultChecked = valAttr.Value == null || (valAttr.Value != "0" && !valAttr.Value.Equals("false", StringComparison.CurrentCultureIgnoreCase));
            }

            double size = 0;
            if (sizeElement?.Val?.HasValue == true && double.TryParse(sizeElement.Val.Value, out var sizeValue))
            {
                size = sizeValue / 2.0; // Half-points to points
            }

            return new CheckBoxFormFieldElement
            {
                Name = name,
                Enabled = enabled,
                Checked = isChecked,
                DefaultChecked = defaultChecked,
                SizePoints = size
            };
        }

        // Check for text input
        var textInput = ffData.GetFirstChild<TextInput>();
        if (textInput != null)
        {
            var typeElement = textInput.GetFirstChild<TextBoxFormFieldType>();
            var defaultElement = textInput.GetFirstChild<DefaultTextBoxFormFieldString>();
            var maxLengthElement = textInput.GetFirstChild<MaxLength>();

            var textType = TextFormFieldType.Regular;
            if (typeElement?.Val?.HasValue == true)
            {
                var val = typeElement.Val.Value;
                if (val == TextBoxFormFieldValues.Number)
                {
                    textType = TextFormFieldType.Number;
                }
                else if (val == TextBoxFormFieldValues.Date)
                {
                    textType = TextFormFieldType.Date;
                }
                else if (val == TextBoxFormFieldValues.CurrentDate)
                {
                    textType = TextFormFieldType.CurrentDate;
                }
                else if (val == TextBoxFormFieldValues.CurrentTime)
                {
                    textType = TextFormFieldType.CurrentTime;
                }
                else if (val == TextBoxFormFieldValues.Calculated)
                {
                    textType = TextFormFieldType.Calculated;
                }
            }

            return new TextFormFieldElement
            {
                Name = name,
                Enabled = enabled,
                DefaultText = defaultElement?.Val?.Value,
                Value = defaultElement?.Val?.Value ?? "",
                MaxLength = maxLengthElement?.Val?.Value ?? 0,
                TextType = textType,
                WidthPoints = 100 // Default width
            };
        }

        // Check for drop-down list
        var dropDown = ffData.GetFirstChild<DropDownListFormField>();
        if (dropDown != null)
        {
            var items = dropDown.Elements<ListEntryFormField>()
                .Select(li => li.Val?.Value ?? "")
                .ToList();

            var resultElement = dropDown.GetFirstChild<DropDownListSelection>();
            var selectedIndex = resultElement?.Val?.Value ?? 0;

            return new DropDownFormFieldElement
            {
                Name = name,
                Enabled = enabled,
                Items = items,
                SelectedIndex = selectedIndex,
                WidthPoints = 100 // Default width
            };
        }

        return null;
    }

    SectionBreakElement? ParseSectionBreak(SectionProperties sectionProps)
    {
        var typeElement = sectionProps.GetFirstChild<SectionType>();
        var breakType = SectionBreakType.NextPage; // Default

        if (typeElement?.Val?.HasValue == true)
        {
            var val = typeElement.Val.Value;
            if (val == SectionMarkValues.Continuous)
            {
                breakType = SectionBreakType.Continuous;
            }
            else if (val == SectionMarkValues.EvenPage)
            {
                breakType = SectionBreakType.EvenPage;
            }
            else if (val == SectionMarkValues.OddPage)
            {
                breakType = SectionBreakType.OddPage;
            }
            else if (val == SectionMarkValues.NextColumn)
            {
                breakType = SectionBreakType.NextColumn;
            }
            // else NextPage (default)
        }

        // SectionProperties (sectPr) describes the section it belongs to.
        // For a section break, the following section's properties are stored in the next sectPr in the document.
        PageSettings? newSettings = null;
        if (nextSectionSettings != null && nextSectionSettings.TryGetValue(sectionProps, out var nextSettings))
        {
            newSettings = nextSettings;
        }

        // Fallback: if we couldn't resolve the next section settings, parse from this sectPr.
        if (newSettings == null)
        {
            newSettings = ExtractPageSettings(sectionProps);
        }

        return new()
        {
            BreakType = breakType,
            NewSectionSettings = newSettings
        };
    }

    ParagraphProperties ParseParagraphProperties(OoxmlParagraphProperties? props, string? styleId = null)
    {
        // Get style defaults if available
        ParagraphProperties? styleDefaults = null;
        if (styleParagraphProperties != null && styleId != null)
        {
            styleParagraphProperties.TryGetValue(styleId, out styleDefaults);
        }

        // Start with style defaults or system defaults
        var alignment = styleDefaults?.Alignment ?? TextAlignment.Left;
        var spacingBefore = styleDefaults?.SpacingBeforePoints ?? 0;
        var spacingAfter = styleDefaults?.SpacingAfterPoints ?? defaultSpacingAfterPoints;
        var lineSpacingMultiplier = styleDefaults?.LineSpacingMultiplier ?? 1.08; // Slight leading for readability
        var lineSpacingPoints = styleDefaults?.LineSpacingPoints ?? 0;
        var lineSpacingRule = styleDefaults?.LineSpacingRule ?? LineSpacingRule.Auto;
        var firstLineIndent = styleDefaults?.FirstLineIndentPoints ?? 0;
        var leftIndent = styleDefaults?.LeftIndentPoints ?? 0;
        var rightIndent = styleDefaults?.RightIndentPoints ?? 0;
        var hangingIndent = styleDefaults?.HangingIndentPoints ?? 0;
        var contextualSpacing = styleDefaults?.ContextualSpacing ?? false;
        var suppressLineNumbers = false;
        var suppressAutoHyphens = false;

        // Pagination properties - get from style defaults
        var keepLines = styleDefaults?.KeepLines ?? false;
        var keepNext = styleDefaults?.KeepNext ?? false;
        var widowControl = styleDefaults?.WidowControl ?? true; // Default is true per OpenXML spec
        var pageBreakBefore = styleDefaults?.PageBreakBefore ?? false;
        var backgroundColor = styleDefaults?.BackgroundColorHex;

        // If no inline properties, return style defaults
        if (props == null)
        {
            return new()
            {
                Alignment = alignment,
                SpacingBeforePoints = spacingBefore,
                SpacingAfterPoints = spacingAfter,
                LineSpacingMultiplier = lineSpacingMultiplier,
                LineSpacingPoints = lineSpacingPoints,
                LineSpacingRule = lineSpacingRule,
                FirstLineIndentPoints = firstLineIndent,
                LeftIndentPoints = leftIndent,
                RightIndentPoints = rightIndent,
                HangingIndentPoints = hangingIndent,
                ContextualSpacing = contextualSpacing,
                SuppressLineNumbers = suppressLineNumbers,
                SuppressAutoHyphens = suppressAutoHyphens,
                KeepLines = keepLines,
                KeepNext = keepNext,
                WidowControl = widowControl,
                PageBreakBefore = pageBreakBefore,
                BackgroundColorHex = backgroundColor,
                StyleId = styleId
            };
        }

        // Override with inline properties
        var justification = props.GetFirstChild<Justification>();
        if (justification?.Val?.HasValue == true)
        {
            var justVal = justification.Val.Value;
            if (justVal == JustificationValues.Center)
            {
                alignment = TextAlignment.Center;
            }
            else if (justVal == JustificationValues.Right)
            {
                alignment = TextAlignment.Right;
            }
            else if (justVal == JustificationValues.Both || justVal == JustificationValues.Distribute)
            {
                alignment = TextAlignment.Justify;
            }
            else
            {
                alignment = TextAlignment.Left;
            }
        }

        var spacing = props.GetFirstChild<SpacingBetweenLines>();
        if (spacing != null)
        {
            if (spacing.Before?.HasValue == true)
            {
                spacingBefore = double.Parse(spacing.Before.Value!) / twipsPerPoint;
            }

            if (spacing.After?.HasValue == true)
            {
                spacingAfter = double.Parse(spacing.After.Value!) / twipsPerPoint;
            }

            if (spacing.Line?.HasValue == true)
            {
                var ruleValue = spacing.LineRule?.Value ?? LineSpacingRuleValues.Auto;

                if (ruleValue == LineSpacingRuleValues.Auto)
                {
                    // Line spacing in 240ths of a line
                    lineSpacingMultiplier = double.Parse(spacing.Line.Value!) / 240.0;
                    lineSpacingRule = LineSpacingRule.Auto;
                }
                else if (ruleValue == LineSpacingRuleValues.Exact)
                {
                    // Line spacing in twips (1/20 of a point)
                    lineSpacingPoints = double.Parse(spacing.Line.Value!) / twipsPerPoint;
                    lineSpacingRule = LineSpacingRule.Exactly;
                }
                else if (ruleValue == LineSpacingRuleValues.AtLeast)
                {
                    // Line spacing in twips (1/20 of a point)
                    lineSpacingPoints = double.Parse(spacing.Line.Value!) / twipsPerPoint;
                    lineSpacingRule = LineSpacingRule.AtLeast;
                }
            }
        }

        var indentation = props.GetFirstChild<Indentation>();
        if (indentation != null)
        {
            if (indentation.FirstLine?.HasValue == true)
            {
                firstLineIndent = double.Parse(indentation.FirstLine.Value!) / twipsPerPoint;
            }

            if (indentation.Left?.HasValue == true)
            {
                leftIndent = double.Parse(indentation.Left.Value!) / twipsPerPoint;
            }

            if (indentation.Right?.HasValue == true)
            {
                rightIndent = double.Parse(indentation.Right.Value!) / twipsPerPoint;
            }

            if (indentation.Hanging?.HasValue == true)
            {
                hangingIndent = double.Parse(indentation.Hanging.Value!) / twipsPerPoint;
            }
        }

        // Check if line numbers are suppressed for this paragraph
        suppressLineNumbers = props.GetFirstChild<SuppressLineNumbers>() != null;
        suppressAutoHyphens = props.GetFirstChild<SuppressAutoHyphens>() != null;

        // Contextual spacing collapses space between paragraphs with matching styles
        if (props.GetFirstChild<ContextualSpacing>() != null)
        {
            contextualSpacing = true;
        }

        // Parse pagination properties
        if (props.GetFirstChild<KeepLines>() != null)
        {
            keepLines = true;
        }

        if (props.GetFirstChild<KeepNext>() != null)
        {
            keepNext = true;
        }

        // WidowControl element toggles the control - presence means off if val is false/0, on if val is true/1 or absent
        var widowControlEl = props.GetFirstChild<WidowControl>();
        if (widowControlEl != null)
        {
            // If the element exists with val="0" or val="false", widow control is disabled
            // If val is missing or true, it's enabled (but we default to true anyway)
            var valAttr = widowControlEl.Val;
            if (valAttr != null && valAttr.HasValue)
            {
                widowControl = valAttr.Value;
            }
            else
            {
                widowControl = true; // Presence without val means enabled
            }
        }

        if (props.GetFirstChild<PageBreakBefore>() != null)
        {
            pageBreakBefore = true;
        }

        // Parse paragraph shading/background color (w:shd element)
        var shadingElement = props.GetFirstChild<Shading>();
        if (shadingElement != null)
        {
            string? inlineBgColor = null;
            // Check for theme fill color first, then direct fill value
            var themeFill = shadingElement.ThemeFill?.Value;
            if (themeFill != null && currentThemeColors != null)
            {
                var themeFillValue = ((IEnumValue) themeFill).Value;
                inlineBgColor = currentThemeColors.ResolveColor(themeFillValue ?? "", null, null);
            }

            // Fall back to direct fill value
            if (inlineBgColor == null && shadingElement.Fill?.HasValue == true &&
                shadingElement.Fill.Value != "auto" && shadingElement.Fill.Value != "none")
            {
                inlineBgColor = shadingElement.Fill.Value;
            }

            if (inlineBgColor != null)
            {
                backgroundColor = inlineBgColor;
            }
        }

        // Parse paragraph mark font size (used for empty paragraphs)
        double? paragraphMarkFontSize = null;
        var paragraphMarkRunProps = props.ParagraphMarkRunProperties;
        if (paragraphMarkRunProps != null)
        {
            var fontSize = paragraphMarkRunProps.GetFirstChild<FontSize>();
            if (fontSize?.Val?.HasValue == true && double.TryParse(fontSize.Val.Value, out var halfPoints))
            {
                paragraphMarkFontSize = halfPoints / 2.0; // Convert half-points to points
            }
        }

        return new()
        {
            Alignment = alignment,
            SpacingBeforePoints = spacingBefore,
            SpacingAfterPoints = spacingAfter,
            LineSpacingMultiplier = lineSpacingMultiplier,
            LineSpacingPoints = lineSpacingPoints,
            LineSpacingRule = lineSpacingRule,
            FirstLineIndentPoints = firstLineIndent,
            LeftIndentPoints = leftIndent,
            RightIndentPoints = rightIndent,
            HangingIndentPoints = hangingIndent,
            SuppressLineNumbers = suppressLineNumbers,
            SuppressAutoHyphens = suppressAutoHyphens,
            ContextualSpacing = contextualSpacing,
            KeepLines = keepLines,
            KeepNext = keepNext,
            WidowControl = widowControl,
            PageBreakBefore = pageBreakBefore,
            ParagraphMarkFontSizePoints = paragraphMarkFontSize,
            BackgroundColorHex = backgroundColor,
            StyleId = styleId
        };
    }

    // Unicode characters for hyphenation
    const char softHyphenChar = '\u00AD'; // Soft hyphen (optional break point)
    const char nonBreakingHyphenChar = '\u2011'; // Non-breaking hyphen

    Run? ParseRun(OoxmlRun run, MainDocumentPart mainPart, string? paragraphStyleId = null)
    {
        // Build text content including special hyphen characters
        var textBuilder = new StringBuilder();
        foreach (var child in run.ChildElements)
        {
            if (child is Text textElement)
            {
                textBuilder.Append(textElement.Text);
            }
            else if (child is SoftHyphen)
            {
                textBuilder.Append(softHyphenChar);
            }
            else if (child is NoBreakHyphen)
            {
                textBuilder.Append(nonBreakingHyphenChar);
            }
        }

        var text = textBuilder.ToString();

        // Skip empty runs that don't have any content
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var runProps = run.RunProperties;
        var properties = ParseRunProperties(runProps, mainPart, paragraphStyleId);

        return new()
        {
            Text = text,
            Properties = properties
        };
    }

    RunProperties ParseRunProperties(OoxmlRunProperties? props, MainDocumentPart mainPart, string? paragraphStyleId = null)
    {
        // Start with defaults from paragraph style if available
        // If no explicit style, default to "Normal" which is the implicit default style in Word
        RunProperties? styleDefaults = null;
        if (styleRunProperties != null)
        {
            var styleId = paragraphStyleId ?? "Normal";
            styleRunProperties.TryGetValue(styleId, out styleDefaults);
        }

        // If no inline properties, return style defaults or empty properties
        if (props == null)
        {
            return styleDefaults ?? new RunProperties();
        }

        // Start with style defaults or built-in defaults
        var fontFamily = styleDefaults?.FontFamily ?? "Aptos";
        var fontSize = styleDefaults?.FontSizePoints ?? 11;
        var bold = styleDefaults?.Bold ?? false;
        var italic = styleDefaults?.Italic ?? false;
        var underline = styleDefaults?.Underline ?? false;
        var strikethrough = styleDefaults?.Strikethrough ?? false;
        var allCaps = styleDefaults?.AllCaps ?? false;
        var color = styleDefaults?.ColorHex;
        var backgroundColor = styleDefaults?.BackgroundColorHex;
        var verticalAlignment = styleDefaults?.VerticalAlignment ?? VerticalRunAlignment.Baseline;

        // Override with inline properties if specified
        var runFonts = props.GetFirstChild<RunFonts>();
        if (runFonts != null)
        {
            // First try theme font reference
            if (runFonts.AsciiTheme?.HasValue == true && currentThemeFonts != null)
            {
                var themeValue = ((IEnumValue) runFonts.AsciiTheme.Value).Value;
                var resolvedFont = currentThemeFonts.ResolveFont(themeValue);
                if (resolvedFont != null)
                {
                    fontFamily = resolvedFont;
                }
            }
            // Fall back to direct font name
            else if (runFonts.Ascii?.HasValue == true)
            {
                fontFamily = runFonts.Ascii.Value!;
            }
        }

        var fontSizeElement = props.GetFirstChild<FontSize>();
        if (fontSizeElement?.Val?.HasValue == true)
        {
            fontSize = double.Parse(fontSizeElement.Val.Value!) / 2.0;
        }

        var boldElement = props.GetFirstChild<Bold>();
        if (boldElement != null)
        {
            bold = boldElement.Val?.Value != false;
        }

        var italicElement = props.GetFirstChild<Italic>();
        if (italicElement != null)
        {
            italic = italicElement.Val?.Value != false;
        }

        var underlineElement = props.GetFirstChild<Underline>();
        if (underlineElement != null && underlineElement.Val?.Value != UnderlineValues.None)
        {
            underline = true;
        }

        var strikeElement = props.GetFirstChild<Strike>();
        if (strikeElement != null)
        {
            strikethrough = strikeElement.Val?.Value != false;
        }

        var capsElement = props.GetFirstChild<Caps>();
        if (capsElement != null)
        {
            allCaps = capsElement.Val?.Value != false;
        }

        // Vertical alignment (subscript/superscript)
        var vertAlignElement = props.GetFirstChild<VerticalTextAlignment>();
        if (vertAlignElement?.Val?.HasValue == true)
        {
            var vertAlignVal = vertAlignElement.Val.Value;
            if (vertAlignVal == VerticalPositionValues.Superscript)
            {
                verticalAlignment = VerticalRunAlignment.Superscript;
            }
            else if (vertAlignVal == VerticalPositionValues.Subscript)
            {
                verticalAlignment = VerticalRunAlignment.Subscript;
            }
            else
            {
                verticalAlignment = VerticalRunAlignment.Baseline;
            }
        }

        // Color - check for theme color first, then direct color as fallback
        var colorElement = props.GetFirstChild<Color>();
        if (colorElement != null)
        {
            string? inlineColor = null;
            var themeColor = colorElement.ThemeColor?.Value;
            if (themeColor != null && currentThemeColors != null)
            {
                byte? shade = null;
                byte? tint = null;

                if (colorElement.ThemeShade?.HasValue == true)
                {
                    if (byte.TryParse(colorElement.ThemeShade.Value, NumberStyles.HexNumber, null, out var shadeVal))
                    {
                        shade = shadeVal;
                    }
                }

                if (colorElement.ThemeTint?.HasValue == true)
                {
                    if (byte.TryParse(colorElement.ThemeTint.Value, NumberStyles.HexNumber, null, out var tintVal))
                    {
                        tint = tintVal;
                    }
                }

                // Use IEnumValue.Value instead of ToString() to get actual enum value string
                var themeColorValue = ((IEnumValue) themeColor).Value;
                inlineColor = currentThemeColors.ResolveColor(themeColorValue ?? "", shade, tint);
            }

            // Fall back to direct value if theme resolution failed or no theme color
            if (inlineColor == null && colorElement.Val?.HasValue == true && colorElement.Val.Value != "auto")
            {
                inlineColor = colorElement.Val.Value;
            }

            if (inlineColor != null)
            {
                color = inlineColor;
            }
        }

        // Background/shading color (w:shd element)
        var shadingElement = props.GetFirstChild<Shading>();
        if (shadingElement != null)
        {
            string? inlineBgColor = null;
            // Check for theme fill color first, then direct fill value
            var themeFill = shadingElement.ThemeFill?.Value;
            if (themeFill != null && currentThemeColors != null)
            {
                var themeFillValue = ((IEnumValue) themeFill).Value;
                inlineBgColor = currentThemeColors.ResolveColor(themeFillValue ?? "", null, null);
            }

            // Fall back to direct fill value
            if (inlineBgColor == null && shadingElement.Fill?.HasValue == true &&
                shadingElement.Fill.Value != "auto" && shadingElement.Fill.Value != "none")
            {
                inlineBgColor = shadingElement.Fill.Value;
            }

            if (inlineBgColor != null)
            {
                backgroundColor = inlineBgColor;
            }
        }

        // Also check for run-specific style reference that overrides paragraph style
        // IMPORTANT: Only apply properties that are EXPLICITLY defined in the character style,
        // not inherited defaults. This ensures character styles like "Shade" (which only defines
        // background color) don't override font size from paragraph styles like Heading1.
        var runStyleId = props.GetFirstChild<RunStyle>()?.Val?.Value;
        if (runStyleId != null && styleRunProperties != null && styleRunProperties.TryGetValue(runStyleId, out var runStyleProps))
        {
            // Look up the original style definition to check which properties are explicitly defined
            var stylesPart = mainPart.StyleDefinitionsPart;
            var originalStyle = stylesPart?.Styles?.Elements<Style>()
                .FirstOrDefault(s => s.StyleId?.Value == runStyleId);
            var originalRPr = originalStyle?.StyleRunProperties;

            // Only override with run style properties that are EXPLICITLY defined in the style
            if (props.GetFirstChild<RunFonts>() == null && originalRPr?.GetFirstChild<RunFonts>() != null)
            {
                fontFamily = runStyleProps.FontFamily;
            }

            if (props.GetFirstChild<FontSize>() == null && originalRPr?.GetFirstChild<FontSize>() != null)
            {
                fontSize = runStyleProps.FontSizePoints;
            }

            if (props.GetFirstChild<Bold>() == null && originalRPr?.GetFirstChild<Bold>() != null)
            {
                bold = runStyleProps.Bold;
            }

            if (props.GetFirstChild<Italic>() == null && originalRPr?.GetFirstChild<Italic>() != null)
            {
                italic = runStyleProps.Italic;
            }

            if (props.GetFirstChild<Underline>() == null && originalRPr?.GetFirstChild<Underline>() != null)
            {
                underline = runStyleProps.Underline;
            }

            if (props.GetFirstChild<Strike>() == null && originalRPr?.GetFirstChild<Strike>() != null)
            {
                strikethrough = runStyleProps.Strikethrough;
            }

            if (props.GetFirstChild<Caps>() == null && originalRPr?.GetFirstChild<Caps>() != null)
            {
                allCaps = runStyleProps.AllCaps;
            }

            if (props.GetFirstChild<Color>() == null && originalRPr?.GetFirstChild<Color>() != null)
            {
                color = runStyleProps.ColorHex;
            }

            if (props.GetFirstChild<Shading>() == null && originalRPr?.GetFirstChild<Shading>() != null)
            {
                backgroundColor = runStyleProps.BackgroundColorHex;
            }

            if (props.GetFirstChild<VerticalTextAlignment>() == null && originalRPr?.GetFirstChild<VerticalTextAlignment>() != null)
            {
                verticalAlignment = runStyleProps.VerticalAlignment;
            }
        }

        return new()
        {
            FontFamily = fontFamily,
            FontSizePoints = fontSize,
            Bold = bold,
            Italic = italic,
            Underline = underline,
            Strikethrough = strikethrough,
            AllCaps = allCaps,
            ColorHex = color,
            BackgroundColorHex = backgroundColor,
            VerticalAlignment = verticalAlignment
        };
    }
}
