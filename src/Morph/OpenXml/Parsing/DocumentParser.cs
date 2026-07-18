using System.Diagnostics.CodeAnalysis;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableProperties = DocumentFormat.OpenXml.Wordprocessing.TableProperties;
using OoxmlFieldCode = DocumentFormat.OpenXml.Wordprocessing.FieldCode;
using OoxmlPageBorders = DocumentFormat.OpenXml.Wordprocessing.PageBorders;
using OoxmlTabStop = DocumentFormat.OpenXml.Wordprocessing.TabStop;
using OoxmlTabs = DocumentFormat.OpenXml.Wordprocessing.Tabs;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

/// <summary>
/// Parses DOCX files using OpenXML.
/// </summary>
[SuppressMessage("Style", "IDE0028:Simplify collection initialization")]
[SuppressMessage("Style", "IDE0306:Simplify collection initialization")]
sealed class DocumentParser(string defaultFont)
{
    // Conversion constants
    const double twipsPerPoint = 20.0;

    // EMUs per point
    const double emusPerPoint = 914400.0 / 72.0;

    // Word's built-in Normal style (normal.dotm), supplied when a document declares no
    // styles.xml or no docDefaults at all. Modern Word (Aptos era, 2023+) uses
    // w:sz w:val="24" (12pt) with w:spacing w:line="278" w:lineRule="auto" w:after="160";
    // the Calibri-era values these replaced were 11pt / w:line="259" (1.08) / w:after="160".
    // Verified against long_paragraph (document.xml only, no styles.xml): Word's glyph
    // advances solve to 12pt Aptos, and its line pitch is 35.35px at 150 DPI, matching
    // 12pt * 1.2207em (the Aptos hhea line box) * 278/240.
    // The default font family is configurable — see constructor.
    const double builtInDefaultFontSizePoints = 12.0;
    const double builtInLineSpacingMultiplier = 278 / 240.0;
    const double builtInSpacingAfterPoints = 8;

    // Fallback font family to use when the document does not specify one in docDefaults.
    // Passed in from the export options' DefaultFont or DefaultFontSettings.DefaultFont.

    public DocumentParser()
        : this(DefaultFontSettings.DefaultFont)
    {
    }

    // Theme colors for the current document being parsed
    ThemeColors? currentThemeColors;

    // Floating tables (w:tblpPr) discovered while parsing nested cells.
    // Lifted to body-level after the body is parsed so they participate in normal page-flow,
    // pagination, and rendering logic instead of being rendered inside a cell that may
    // paginate or get clipped.
    List<TableElement>? pendingLiftedFloatingTables;

    // Theme fonts for the current document being parsed
    ThemeFonts? currentThemeFonts;

    // Section transitions: for a given sectPr (end of a section), what settings apply to the next section?
    Dictionary<SectionProperties, PageSettings?>? nextSectionSettings;

    int lastRenderedPageBreakCount;

    // Style definitions cached during parsing (styleId -> full run properties)
    Dictionary<string, RunProperties>? styleRunProperties;

    // Raw style elements by styleId, first-wins to match the Elements<Style>().FirstOrDefault
    // scan it replaces. ParseRunProperties consults the original style XML for every run that
    // carries w:rStyle (hyperlinks, TOC entries, note refs) to know which properties the
    // character style explicitly defines — that was a linear styles.xml scan per run.
    Dictionary<string, Style>? stylesById;

    // Hyperlink relationship id → resolved URL. mainPart.HyperlinkRelationships is an OfType<>
    // filter over the part's full relationship list, re-enumerated per w:hyperlink — quadratic
    // on link-heavy documents (TOCs, bibliographies).
    Dictionary<string, string>? hyperlinkUrlsByRelId;

    // One decompressed buffer per image part: every drawing referencing a part used to copy its
    // own byte[], so repeated logos/icons retained N identical arrays in the model. Sharing one
    // array also lets the render-side byte[]-identity caches dedupe repeats across elements.
    readonly Dictionary<OpenXmlPart, byte[]> imagePartBytes = [];

    // Document default text colour from w:docDefaults/w:rPrDefault/w:color (theme-resolved). The
    // base of the colour cascade: every run that doesn't get a colour from a style or inline rPr
    // inherits this — including a white default (dark-board templates like menus/07 set FFFFFF
    // background1 and Word paints fall-through runs white). Null when docDefaults sets no colour
    // (the common case — runs stay automatic).
    string? defaultRunColorHex;

    // Sentinel stored in a style's ColorHex when the style declares w:color w:val="auto":
    // "automatic" must RESET an inherited colour (a card template's white docDefaults must not
    // leak through Normal's explicit auto), so it can't be modelled as null — null means
    // "nothing declared, keep inheriting". Never escapes into the model: the run-resolution
    // sites convert it to automaticRunColorHex. Safe because real values are hex strings.
    const string automaticColorSentinel = "auto";

    // What "automatic" resolves to: Word's automatic text colour is contrast-aware against the
    // page background — black normally, white when w:background is dark (brochures/03 paints
    // its titles white on navy). Null = leave unset so renderers use their default black.
    string? automaticRunColorHex;

    // Style paragraph properties cached during parsing (styleId -> paragraph properties)
    Dictionary<string, ParagraphProperties>? styleParagraphProperties;

    // Numbering definitions: numId -> ilvl -> NumberingLevelDefinition
    Dictionary<int, Dictionary<int, NumberingLevelDefinition>>? numberingDefinitions;

    // Style numbering: styleId -> (numId, ilvl) for styles that define numbering
    Dictionary<string, (int numId, int ilvl)>? styleNumbering;

    // Numbering counters keyed by (abstractNumId, ilvl): OOXML's counter belongs to the
    // ABSTRACT definition, shared by every numId that references it — that's how a template's
    // per-table restart works (each table's first row carries its own numId + startOverride,
    // while the style-attached rows share the base numId; both must walk one sequence).
    Dictionary<(int abstractId, int ilvl), int> numberingCounters = [];

    // numId -> abstractNumId (counter key resolution).
    Dictionary<int, int> numberingAbstractIds = [];

    // (numId, ilvl) pairs carrying a w:startOverride, and the ones already applied — the
    // override resets the shared abstract counter at the numId's FIRST use only.
    HashSet<(int numId, int ilvl)> numberingStartOverrides = [];
    HashSet<(int numId, int ilvl)> appliedStartOverrides = [];

    // Table style borders cached during parsing (styleId -> borders + conditional formatting)
    Dictionary<string, TableStyleBorderInfo>? tableStyleBorders;

    // StyleId of the table style flagged as w:default — used as the implicit style for tables
    // that don't carry an explicit w:tblStyle reference (ECMA-376 §17.7.4.4).
    string? defaultTableStyleId;

    // StyleId of the paragraph style flagged as w:default (typically "Normal") — Word
    // implicitly applies it to paragraphs without an explicit w:pStyle, layered on top of
    // pPrDefault. Without this, our docDefaults-only fallback misses Normal's overrides
    // (e.g. an explicit <w:ind w:left="0"/> that resets the pPrDefault indent).
    string? defaultParagraphStyleId;

    // Document-level background color (applies to all pages)
    string? documentBackgroundColor;

    // Document default paragraph properties (from docDefaults/pPrDefault). When a document
    // specifies no paragraph-spacing default, Word reads that as zero — not as its normal.dotm
    // 8pt-after built-in — so the fallback is 0 (verified against resumes/13, which has styles
    // but no docDefaults and renders tight in Word).
    double defaultSpacingAfterPoints;
    double defaultSpacingBeforePoints;
    double defaultLeftIndentPoints;
    double defaultRightIndentPoints;

    // Document default paragraph alignment from docDefaults/pPrDefault/w:jc. Card/label/menu
    // templates centre every paragraph this way rather than styling each one — a style or
    // paragraph that declares its own w:jc (including an explicit "left") still overrides.
    TextAlignment defaultAlignment = TextAlignment.Left;

    // Document default line spacing, used when no style supplies one. Word's built-in Normal
    // applies only when the document declares no styles.xml or no docDefaults (see
    // ExtractDefaultParagraphProperties); otherwise this keeps its long-standing 1.08.
    // This deliberately does NOT read docDefaults/pPrDefault's own w:line: Word's cascade for
    // that value is conditional in undecoded ways around table cells. Two measured
    // counterexamples: postcards/04 declares w:line="360" yet Word renders its (atLeast-height)
    // card cells at single spacing (applying 1.5 grows them 3->6 pages); agendas-minutes/07
    // declares w:line="264" and applying it to the Normal-derived cell paragraphs shifts the
    // whole content block ~45px below Word's render even though the inter-heading gaps then
    // match Word exactly. Body-only evidence (letters/09: docDefaults without w:line renders
    // single, not 1.08) says the docDefault should cascade for BODY paragraphs — decode the
    // table-cell rule before wiring this up.
    double defaultLineSpacingMultiplier = 1.08;

    // Document-level w:defaultTabStop in points (720 twips = 36 pt = 0.5 inch, OOXML default).
    double defaultTabStopPoints = 36;

    // Document-level w:gutterAtTop flag; when true, w:pgMar/@w:gutter is added to the top margin instead of left.
    bool gutterAtTopSetting;

    // Set true while parsing when a NUMPAGES/SECTIONPAGES field is seen anywhere (body, header,
    // footer). Surfaced on ParsedDocument.RequiresTotalPageCount so the converters run a counting
    // pass before rendering. A fresh DocumentParser is created per document, so this never leaks.
    bool requiresTotalPageCount;

    // Document-default font family resolved from docDefaults; falls back to the constructor's
    // defaultFont when the document doesn't specify one. Used by every run that doesn't carry an explicit font.
    string effectiveDefaultFont = "";

    // Document-default run font size resolved from docDefaults (w:rPrDefault/w:sz); falls back to
    // Word's built-in Normal size when the document doesn't specify one. Used by every run that
    // doesn't carry an explicit size — including runs with no w:rPr at all, which must not fall
    // through to the RunProperties record's own static default.
    double effectiveDefaultFontSizePoints = builtInDefaultFontSizePoints;

    public ParsedDocument Parse(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Parse(stream);
    }

    public ParsedDocument Parse(Stream stream)
    {
        // Translate ISO 29500 Strict documents to Transitional first; the SDK's typed DOM only binds
        // Transitional namespaces. For the common Transitional case Normalize returns the input stream
        // unchanged (no copy), so only dispose the result when it produced a fresh buffer — never the
        // caller's stream.
        var normalized = StrictToTransitional.Normalize(stream);
        try
        {
            using var doc = WordprocessingDocument.Open(normalized, false);
            return ParseDocument(doc);
        }
        finally
        {
            if (!ReferenceEquals(normalized, stream))
            {
                normalized.Dispose();
            }
        }
    }

    ParsedDocument ParseDocument(WordprocessingDocument doc)
    {
        var mainPart = doc.MainDocumentPart
                       ?? throw new InvalidOperationException("Document has no main part");

        var body = mainPart.Document?.Body
                   ?? throw new InvalidOperationException("Document has no body");

        lastRenderedPageBreakCount = body.Descendants<LastRenderedPageBreak>().Count();

        // Extract and store theme colors early (needed for background color and other theme-resolved values)
        currentThemeColors = ThemeParser.ExtractThemeColors(mainPart);

        // Extract and store theme fonts for use during parsing
        currentThemeFonts = ThemeParser.ExtractThemeFonts(mainPart);

        // Resolve the document's default font from docDefaults (w:rPrDefault/w:rFonts). Every run
        // that doesn't carry an explicit font inherits this. Falls back to the constructor's
        // defaultFont when the document doesn't specify one.
        effectiveDefaultFont = ResolveDocDefaultFont(mainPart) ?? defaultFont;
        effectiveDefaultFontSizePoints = ResolveDocDefaultFontSizePoints(mainPart) ?? builtInDefaultFontSizePoints;

        // Extract document-level background color (w:background element)
        documentBackgroundColor = ExtractDocumentBackgroundColor(mainPart.Document);
        automaticRunColorHex = ComputeAutomaticRunColor(documentBackgroundColor);

        // Extract document default spacing from pPrDefault
        ExtractDefaultParagraphProperties(mainPart);

        // Extract document-level default tab stop width (w:defaultTabStop in settings.xml)
        ExtractDefaultTabStop(mainPart);

        // Extract gutterAtTop setting (so ExtractPageSettings can apply gutter to the right margin).
        gutterAtTopSetting = mainPart.DocumentSettingsPart?.Settings?
            .GetFirstChild<GutterAtTop>().IsOn() ?? false;

        // SectionProperties (sectPr) describes the section it belongs to, and the section break is stored
        // on the last paragraph of the section. The next section's properties are stored in the next sectPr.
        var sectionPropsList = body.Descendants<SectionProperties>().ToList();
        stylesById = null;
        hyperlinkUrlsByRelId = null;
        imagePartBytes.Clear();
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
        numberingDefinitions = ExtractNumberingDefinitions(mainPart, numberingAbstractIds, numberingStartOverrides);

        // Extract style numbering (styles that have numPr)
        styleNumbering = ExtractStyleNumbering(mainPart);

        // Extract table style borders
        tableStyleBorders = ExtractTableStyleBorders(mainPart);

        var elements = ParseElements(body, mainPart);

        // Append floating tables that were nested inside cells. Lifting them here lets the
        // top-level rendering loop handle pagination and absolute positioning, rather than
        // attempting to render them within a parent cell whose page-flow may differ.
        if (pendingLiftedFloatingTables is { Count: > 0 } lifted)
        {
            elements.AddRange(lifted);
            pendingLiftedFloatingTables = null;
        }

        // Pull consecutive w:framePr-positioned paragraphs out of normal flow into floating
        // text-frame elements (Word's text-frame feature).
        elements = FrameGrouper.Group(elements);

        var header = ExtractHeaderFooter(sectionPropsList, mainPart, HeaderFooterValues.Default, isHeader: true);
        var footer = ExtractHeaderFooter(sectionPropsList, mainPart, HeaderFooterValues.Default, isHeader: false);
        var firstPageHeader = pageSettings.DifferentFirstPage
            ? ExtractHeaderFooter(sectionPropsList, mainPart, HeaderFooterValues.First, isHeader: true)
            : null;
        var firstPageFooter = pageSettings.DifferentFirstPage
            ? ExtractHeaderFooter(sectionPropsList, mainPart, HeaderFooterValues.First, isHeader: false)
            : null;

        // w:settings/w:evenAndOddHeaders opts the document into separate even-page parts.
        var evenAndOddHeaders = mainPart.DocumentSettingsPart?.Settings?
            .GetFirstChild<EvenAndOddHeaders>().IsOn() ?? false;
        var evenPageHeader = evenAndOddHeaders
            ? ExtractHeaderFooter(sectionPropsList, mainPart, HeaderFooterValues.Even, isHeader: true)
            : null;
        var evenPageFooter = evenAndOddHeaders
            ? ExtractHeaderFooter(sectionPropsList, mainPart, HeaderFooterValues.Even, isHeader: false)
            : null;
        var hyphenation = ExtractHyphenationSettings(mainPart);
        var compatibility = ExtractCompatibilitySettings(mainPart);

        // Both bookmark and comment extraction anchor to paragraph ordinals; the map costs a
        // full body walk, so it's built at most once and only when either feature is present.
        Dictionary<Paragraph, int>? paragraphOrdinals = null;
        Dictionary<Paragraph, int> ParagraphOrdinals()
        {
            if (paragraphOrdinals == null)
            {
                paragraphOrdinals = new();
                var ordinal = 0;
                foreach (var paragraph in body.Descendants<Paragraph>())
                {
                    paragraphOrdinals[paragraph] = ordinal++;
                }
            }

            return paragraphOrdinals;
        }

        var bookmarks = ExtractBookmarks(body, ParagraphOrdinals);
        var comments = ExtractComments(mainPart, body, ParagraphOrdinals);
        var trackedChanges = ExtractTrackedChanges(body);
        var protection = ExtractDocumentProtection(mainPart);
        var fieldCodes = ExtractFieldCodes(body);
        var footnotes = ExtractFootnotes(mainPart);
        var endnotes = ExtractEndnotes(mainPart);
        var embeddedObjects = ExtractEmbeddedObjects(body);
        var watermarks = ExtractWatermarks(mainPart);
        var features = DetectAdvancedFeatures(body, watermarks.Count > 0);

        return new()
        {
            PageSettings = pageSettings,
            Elements = elements,
            Header = header,
            Footer = footer,
            FirstPageHeader = firstPageHeader,
            FirstPageFooter = firstPageFooter,
            EvenPageHeader = evenPageHeader,
            EvenPageFooter = evenPageFooter,
            Hyphenation = hyphenation,
            ThemeColors = currentThemeColors,
            ThemeFonts = currentThemeFonts,
            Compatibility = compatibility,
            Bookmarks = bookmarks,
            Comments = comments,
            TrackedChanges = trackedChanges,
            Protection = protection,
            FieldCodes = fieldCodes,
            RequiresTotalPageCount = requiresTotalPageCount,
            Footnotes = footnotes,
            Endnotes = endnotes,
            EmbeddedObjects = embeddedObjects,
            Watermarks = watermarks,
            Features = features
        };
    }

    static DocumentFeatures DetectAdvancedFeatures(Body body, bool hasWatermark)
    {
        var hasCharts = false;
        var hasSmartArt = false;
        var hasMath = false;
        var hasGradients = false;
        var hasBezier = false;
        var has3d = false;
        var hasConnectors = false;
        var hasDuotone = false;

        // Single descendant pass: every feature flag is OR'd from a switch on LocalName,
        // so we don't need separate walks (the prior version did one for math + one for
        // everything else, doubling DOM traversal cost on large documents).
        foreach (var element in body.Descendants())
        {
            switch (element.LocalName)
            {
                case "oMath":
                case "oMathPara":
                    hasMath = true;
                    break;
                case "graphicData":
                    var uri = element.AttributeValue("uri");
                    if (uri == "http://schemas.openxmlformats.org/drawingml/2006/chart")
                    {
                        hasCharts = true;
                    }
                    else if (uri == "http://schemas.openxmlformats.org/drawingml/2006/diagram")
                    {
                        hasSmartArt = true;
                    }

                    break;
                case "gradFill":
                    hasGradients = true;
                    break;
                case "custGeom":
                    hasBezier = true;
                    break;
                case "sp3d":
                case "scene3d":
                    has3d = true;
                    break;
                case "cxnSp":
                    hasConnectors = true;
                    break;
                case "duotone":
                case "clrChange":
                    hasDuotone = true;
                    break;
            }
        }

        // hasWatermark is determined externally and passed in (extracted alongside the watermark list).

        return new()
        {
            HasCharts = hasCharts,
            HasSmartArt = hasSmartArt,
            HasMath = hasMath,
            HasWatermarks = hasWatermark,
            HasGradientFills = hasGradients,
            HasBezierShapes = hasBezier,
            Has3dEffects = has3d,
            HasConnectors = hasConnectors,
            HasDuotoneEffects = hasDuotone
        };
    }

    static List<EmbeddedObject> ExtractEmbeddedObjects(Body body)
    {
        var result = new List<EmbeddedObject>();
        foreach (var obj in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.EmbeddedObject>())
        {
            string? progId = null;
            string? relId = null;
            foreach (var child in obj.Descendants())
            {
                if (child.LocalName != "OLEObject")
                {
                    continue;
                }

                foreach (var attribute in child.GetAttributes())
                {
                    if (attribute.LocalName == "ProgID")
                    {
                        progId = attribute.Value;
                    }
                    else if (attribute.LocalName == "id" &&
                             attribute.NamespaceUri.EndsWith("/relationships"))
                    {
                        relId = attribute.Value;
                    }
                }
            }

            result.Add(
                new()
                {
                    ProgId = progId,
                    RelationshipId = relId
                });
        }

        return result;
    }

    List<Watermark> ExtractWatermarks(MainDocumentPart mainPart)
    {
        var result = new List<Watermark>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var headerPart in mainPart.HeaderParts)
        {
            if (headerPart.Header == null)
            {
                continue;
            }

            foreach (var shape in headerPart.Header.Descendants<DocumentFormat.OpenXml.Vml.Shape>())
            {
                // Word emits the watermark shape with id="WordPictureWatermark..." or
                // id="WordTextWatermark...". Some templates also tag it via o:cls or class — match
                // any attribute carrying the marker so all variants are detected.
                var isWatermark = shape.GetAttributes()
                    .Any(_ =>
                        _.Value is { } v &&
                        (v.Contains("WordPictureWatermark", StringComparison.OrdinalIgnoreCase) ||
                         v.Contains("WordTextWatermark", StringComparison.OrdinalIgnoreCase)));
                if (!isWatermark)
                {
                    continue;
                }

                // De-dup across header parts: identical shapes can repeat in even/first-page headers.
                var dedupKey = shape.OuterXml;
                if (!seen.Add(dedupKey))
                {
                    continue;
                }

                var imageData = shape.GetFirstChild<DocumentFormat.OpenXml.Vml.ImageData>();
                if (imageData != null)
                {
                    var pictureWatermark = ParsePictureWatermark(imageData, headerPart);
                    if (pictureWatermark != null)
                    {
                        result.Add(pictureWatermark);
                    }

                    continue;
                }

                var textPath = shape.GetFirstChild<DocumentFormat.OpenXml.Vml.TextPath>();
                if (textPath?.String?.Value is {Length: > 0} textValue)
                {
                    result.Add(ParseTextWatermark(textPath, textValue, shape));
                }
            }
        }

        return result;
    }

    Watermark? ParsePictureWatermark(DocumentFormat.OpenXml.Vml.ImageData imageData, HeaderPart headerPart)
    {
        var relId = imageData.RelationshipId?.Value
                    ?? imageData.GetAttributes().FirstOrDefault(_ => _.LocalName == "id" && _.NamespaceUri.EndsWith("/relationships")).Value;
        if (relId == null)
        {
            return null;
        }

        if (headerPart.GetPartById(relId) is not ImagePart imagePart)
        {
            return null;
        }

        // Word stores gain/blacklevel as fixed-point /65536 (the trailing "f" is a hint for that).
        // 65536 → 1.0; default Gain = 1.0 (no contrast change), default BlackLevel = 0.
        var gain = ParseFixedPoint(imageData.Gain?.Value, defaultValue: 1.0);
        var blackLevel = ParseFixedPoint(imageData.BlackLevel?.Value, defaultValue: 0.0);

        return new()
        {
            ImageData = GetPartBytes(imagePart),
            ContentType = imagePart.ContentType,
            Gain = gain,
            BlackLevel = blackLevel
        };
    }

    static Watermark ParseTextWatermark(DocumentFormat.OpenXml.Vml.TextPath textPath, string text, DocumentFormat.OpenXml.Vml.Shape shape)
    {
        // textpath @style is a CSS-like string: "font:bold 36pt Calibri"
        var fontFamily = "Calibri";
        var fontSize = 36.0;
        var bold = false;

        if (textPath.Style?.Value is { } styleString)
        {
            var styleSpan = styleString.AsSpan();
            foreach (var propRange in styleSpan.Split(';'))
            {
                var prop = styleSpan[propRange];
                var colonIndex = prop.IndexOf(':');
                if (colonIndex < 0)
                {
                    continue;
                }

                var name = prop[..colonIndex].Trim();
                if (!name.Equals("font", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // CSS shorthand: "[style] [weight] size family" — last token is the family,
                // any "bold" token sets weight, any "Npt" token sets size.
                var fontValue = prop[(colonIndex + 1)..].Trim();
                ReadOnlySpan<char> lastToken = default;
                foreach (var tokenRange in fontValue.Split(' '))
                {
                    var token = fontValue[tokenRange];
                    if (token.IsEmpty)
                    {
                        continue;
                    }

                    lastToken = token;

                    if (token.Equals("bold", StringComparison.OrdinalIgnoreCase))
                    {
                        bold = true;
                    }
                    else if (token.EndsWith("pt", StringComparison.OrdinalIgnoreCase) &&
                             double.TryParse(token[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt))
                    {
                        fontSize = pt;
                    }
                }

                if (!lastToken.IsEmpty)
                {
                    fontFamily = lastToken.ToString();
                }
            }
        }

        // shape.FillColor is a VML colour string (named or "#hex"); strip the "#".
        var color = "BFBFBF";
        if (shape.FillColor?.Value is {Length: > 0} fillColor)
        {
            color = fillColor.TrimStart('#').ToUpperInvariant();
        }

        return new()
        {
            Text = text,
            FontFamily = fontFamily,
            FontSizePoints = fontSize,
            Bold = bold,
            ColorHex = color
        };
    }

    static double ParseFixedPoint(string? raw, double defaultValue)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }

        // Strip the trailing "f" Word sometimes emits (denotes fixed-point /65536).
        var trimmed = raw.TrimEnd('f', 'F');
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value / 65536.0;
        }

        return defaultValue;
    }

    string? ResolveDocDefaultFont(MainDocumentPart mainPart)
    {
        var rPrDefault = mainPart.StyleDefinitionsPart?.Styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
        var fonts = rPrDefault?.GetFirstChild<RunFonts>();
        if (fonts == null)
        {
            return null;
        }

        if (fonts.AsciiTheme?.HasValue == true &&
            currentThemeFonts != null)
        {
            var themeValue = ((IEnumValue) fonts.AsciiTheme.Value).Value;
            var resolved = currentThemeFonts.ResolveFont(themeValue);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return fonts.Ascii?.HasValue == true ? fonts.Ascii.Value : null;
    }

    static double? ResolveDocDefaultFontSizePoints(MainDocumentPart mainPart)
    {
        var rPrDefault = mainPart.StyleDefinitionsPart?.Styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
        var size = rPrDefault?.GetFirstChild<FontSize>();
        if (size?.Val?.HasValue != true)
        {
            return null;
        }

        return double.Parse(size.Val.Value!).HalfPointsToPoints();
    }

    static IReadOnlyList<Footnote> ExtractFootnotes(MainDocumentPart mainPart)
    {
        var part = mainPart.FootnotesPart;
        if (part?.Footnotes == null)
        {
            return [];
        }

        var result = new List<Footnote>();
        foreach (var fn in part.Footnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Footnote>())
        {
            // Skip Word's built-in separator / continuation-separator entries.
            if (fn.Type?.HasValue == true &&
                fn.Type.Value != FootnoteEndnoteValues.Normal)
            {
                continue;
            }

            if (fn.Id?.Value is not { } idLong)
            {
                continue;
            }

            var text = string.Concat(fn.Descendants<Text>().Select(_ => _.Text));
            result.Add(new()
            {
                Id = idLong.ToString(),
                Text = text
            });
        }

        return result;
    }

    static List<Endnote> ExtractEndnotes(MainDocumentPart mainPart)
    {
        var part = mainPart.EndnotesPart;
        if (part?.Endnotes == null)
        {
            return [];
        }

        var result = new List<Endnote>();
        foreach (var en in part.Endnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Endnote>())
        {
            if (en.Type?.HasValue == true &&
                en.Type.Value != FootnoteEndnoteValues.Normal)
            {
                continue;
            }

            if (en.Id?.Value is not { } idLong)
            {
                continue;
            }

            var text = string.Concat(en.Descendants<Text>().Select(_ => _.Text));
            result.Add(new()
            {
                Id = idLong.ToString(),
                Text = text
            });
        }

        return result;
    }

    static IReadOnlyList<FieldCode> ExtractFieldCodes(Body body)
    {
        var result = new List<FieldCode>();
        var instructionStack = new Stack<StringBuilder>();
        var resultStack = new Stack<StringBuilder>();
        var inResult = new Stack<bool>();

        foreach (var run in body.Descendants<OoxmlRun>())
        {
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Begin:
                        instructionStack.Push(new());
                        resultStack.Push(new());
                        inResult.Push(false);
                        break;

                    case OoxmlFieldCode instr when instructionStack.Count > 0:
                        if (!inResult.Peek())
                        {
                            instructionStack.Peek().Append(instr.Text);
                        }

                        break;

                    case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Separate &&
                                           inResult.Count > 0:
                        var flag = inResult.Pop();
                        inResult.Push(true);
                        _ = flag;
                        break;

                    case Text t when instructionStack.Count > 0 && inResult.Peek():
                        resultStack.Peek().Append(t.Text);
                        break;

                    case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.End && instructionStack.Count > 0:
                        var instruction = instructionStack.Pop().TrimmedToString();
                        var resultText = resultStack.Pop().ToString();
                        inResult.Pop();
                        if (instruction.Length > 0)
                        {
                            result.Add(new()
                            {
                                Instruction = instruction,
                                Result = resultText
                            });
                        }

                        break;
                }
            }
        }

        // Legacy w:fldSimple (single-element field). The instruction lives in @w:instr and the
        // cached result is the descendant text.
        foreach (var simple in body.Descendants<SimpleField>())
        {
            var rawInstruction = simple.Instruction?.Value;
            if (!rawInstruction.TryTrim(out var instruction))
            {
                continue;
            }

            var resultText = string.Concat(simple.Descendants<Text>().Select(_ => _.Text));
            result.Add(new()
            {
                Instruction = instruction,
                Result = resultText
            });
        }

        return result;
    }

    static DocumentProtectionSettings ExtractDocumentProtection(MainDocumentPart mainPart)
    {
        var settings = mainPart.DocumentSettingsPart?.Settings;
        var protection = settings?.GetFirstChild<DocumentProtection>();
        if (protection?.Edit?.Value is not { } editValue)
        {
            return new();
        }

        var mode = DocumentEditingMode.None;
        if (editValue == DocumentProtectionValues.ReadOnly)
        {
            mode = DocumentEditingMode.ReadOnly;
        }
        else if (editValue == DocumentProtectionValues.Comments)
        {
            mode = DocumentEditingMode.Comments;
        }
        else if (editValue == DocumentProtectionValues.TrackedChanges)
        {
            mode = DocumentEditingMode.TrackedChanges;
        }
        else if (editValue == DocumentProtectionValues.Forms)
        {
            mode = DocumentEditingMode.Forms;
        }

        return new()
        {
            EditingMode = mode
        };
    }

    static List<TrackedChange> ExtractTrackedChanges(Body body)
    {
        var result = new List<TrackedChange>();

        foreach (var ins in body.Descendants<InsertedRun>())
        {
            if (ins.Id?.Value is not { } id)
            {
                continue;
            }

            result.Add(BuildChange(id, ins.Author?.Value, ins.Date?.Value, TrackedChangeType.Insertion, ins));
        }

        foreach (var del in body.Descendants<DeletedRun>())
        {
            if (del.Id?.Value is not { } id)
            {
                continue;
            }

            result.Add(BuildChange(id, del.Author?.Value, del.Date?.Value, TrackedChangeType.Deletion, del));
        }

        return result;

        static TrackedChange BuildChange(string id, string? author, DateTime? date, TrackedChangeType type, OpenXmlElement element)
        {
            // DeletedText (w:delText) is the deleted content; Text (w:t) is the regular variant under InsertedRun.
            var text = string.Concat(element.Descendants<Text>().Select(_ => _.Text)
                .Concat(element.Descendants<DeletedText>().Select(_ => _.Text)));

            return new()
            {
                Id = id,
                Author = author,
                Date = date is { } dt ? new DateTimeOffset(dt) : null,
                Type = type,
                Text = text
            };
        }
    }

    static List<Comment> ExtractComments(MainDocumentPart mainPart, Body body, Func<Dictionary<Paragraph, int>> getParagraphOrdinals)
    {
        var commentsPart = mainPart.WordprocessingCommentsPart;
        if (commentsPart?.Comments == null)
        {
            return [];
        }

        // Map each w:commentRangeStart's id → enclosing paragraph ordinal. The ordinal map is
        // shared with bookmark extraction and only materialized when a range start exists.
        Dictionary<Paragraph, int>? paragraphOrdinals = null;
        var anchorByCommentId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rangeStart in body.Descendants<CommentRangeStart>())
        {
            if (rangeStart.Id?.Value is not { } rangeId)
            {
                continue;
            }

            paragraphOrdinals ??= getParagraphOrdinals();
            for (var current = rangeStart.Parent; current != null; current = current.Parent)
            {
                if (current is Paragraph paragraph && paragraphOrdinals.TryGetValue(paragraph, out var idx))
                {
                    anchorByCommentId[rangeId] = idx;
                    break;
                }
            }
        }

        var result = new List<Comment>();
        foreach (var ooxmlComment in commentsPart.Comments.Elements<DocumentFormat.OpenXml.Wordprocessing.Comment>())
        {
            if (ooxmlComment.Id?.Value is not { } id)
            {
                continue;
            }

            var text = string.Concat(ooxmlComment.Descendants<Text>().Select(_ => _.Text));
            DateTimeOffset? date = null;
            if (ooxmlComment.Date?.Value is { } dateValue)
            {
                date = dateValue;
            }

            int? anchor = anchorByCommentId.TryGetValue(id, out var idx) ? idx : null;

            result.Add(
                new()
                {
                    Id = id,
                    Author = ooxmlComment.Author?.Value,
                    Text = text,
                    Date = date,
                    AnchorParagraphIndex = anchor
                });
        }

        return result;
    }

    static List<Bookmark> ExtractBookmarks(Body body, Func<Dictionary<Paragraph, int>> getParagraphOrdinals)
    {
        // The paragraph-ordinal map costs a full body walk; most documents have no bookmarks,
        // so it's only materialized (shared with comment extraction) once a bookmark is found.
        Dictionary<Paragraph, int>? paragraphOrdinals = null;

        var result = new List<Bookmark>();
        foreach (var start in body.Descendants<BookmarkStart>())
        {
            if (start.Id?.Value is not { } id ||
                start.Name?.Value is not { } name)
            {
                continue;
            }

            paragraphOrdinals ??= getParagraphOrdinals();

            int? paragraphIndex = null;
            for (var current = start.Parent; current != null; current = current.Parent)
            {
                if (current is Paragraph paragraph && paragraphOrdinals.TryGetValue(paragraph, out var idx))
                {
                    paragraphIndex = idx;
                    break;
                }
            }

            result.Add(
                new()
                {
                    Id = id,
                    Name = name,
                    ParagraphIndex = paragraphIndex
                });
        }

        return result;
    }

    // Resolves a w:color element to a 6-hex string: a theme colour (w:themeColor with optional
    // w:themeShade/w:themeTint) via the document's colour scheme, else the direct w:val. Returns
    // null for "auto" or an unresolved/absent value.
    string? ResolveRunColor(Color colorElement)
    {
        var themeColor = colorElement.ThemeColor?.Value;
        if (themeColor != null && currentThemeColors != null)
        {
            byte? shade = null;
            byte? tint = null;
            if (colorElement.ThemeShade?.HasValue == true &&
                byte.TryParse(colorElement.ThemeShade.Value, NumberStyles.HexNumber, null, out var shadeValue))
            {
                shade = shadeValue;
            }

            if (colorElement.ThemeTint?.HasValue == true &&
                byte.TryParse(colorElement.ThemeTint.Value, NumberStyles.HexNumber, null, out var tintValue))
            {
                tint = tintValue;
            }

            var resolved = currentThemeColors.ResolveColor(((IEnumValue) themeColor).Value, shade, tint);
            if (resolved != null)
            {
                return resolved;
            }
        }

        if (colorElement.Val?.HasValue == true && colorElement.Val.Value != "auto")
        {
            return colorElement.Val.Value;
        }

        return null;
    }

    Dictionary<string, RunProperties> ExtractStyleRunProperties(MainDocumentPart mainPart)
    {
        var styleProps = new Dictionary<string, RunProperties>(StringComparer.OrdinalIgnoreCase);

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return styleProps;
        }

        // Extract docDefaults run properties as the base defaults. The size is already resolved
        // from docDefaults (see effectiveDefaultFontSizePoints), so it needs no re-parse below.
        var defaultFontFamily = defaultFont;
        var defaultFontSize = effectiveDefaultFontSizePoints;

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

            // Text colour from docDefaults — the base of the run-colour cascade. Many templates set
            // their body colour here as a theme colour (e.g. text2 #44546a) rather than on every run.
            // A WHITE default applies too: dark-board templates (menus/07/08, inline_group_crop)
            // declare FFFFFF background1 and Word paints fall-through runs white. What keeps the
            // matching card/invitation templates readable is not a white filter here but the style
            // chain: their Normal declares w:color w:val="auto", which RESETS the cascade to
            // automatic (see automaticColorSentinel) — cards/01/05/15 and wedding/03 pair the same
            // white default with an auto Normal, and their body text stays black.
            var defaultColorElement = rPrDefault.GetFirstChild<Color>();
            if (defaultColorElement != null)
            {
                defaultRunColorHex = ResolveRunColor(defaultColorElement);
            }
        }

        // First pass: collect all styles and their basedOn references
        var styles = stylesPart.Styles.Elements<Style>().ToList();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build a set of all style IDs that exist in the document
        var existingStyleIds = new HashSet<string>(
            styles
                .Select(_ => _.StyleId?.Value)
                .Where(_ => _ != null)!,
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
                if (styleId == null ||
                    processed.Contains(styleId))
                {
                    continue;
                }

                // Check if base style needs to be processed first
                var basedOnId = style.BasedOn?.Val?.Value;
                // Only wait for the base style if it actually exists in this document
                // If basedOn references a non-existent style, process without waiting
                if (basedOnId != null &&
                    existingStyleIds.Contains(basedOnId) &&
                    !processed.Contains(basedOnId))
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
                var smallCaps = baseProps?.SmallCaps ?? false;
                var color = baseProps?.ColorHex ?? defaultRunColorHex;
                var backgroundColor = baseProps?.BackgroundColorHex;
                var characterSpacing = baseProps?.CharacterSpacingPoints ?? 0.0;

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
                        SmallCaps = smallCaps,
                        ColorHex = color,
                        BackgroundColorHex = backgroundColor,
                        CharacterSpacingPoints = characterSpacing
                    };
                    processed.Add(styleId);
                    continue;
                }

                // Font
                var runFonts = runProps.GetFirstChild<RunFonts>();
                if (runFonts != null)
                {
                    // First try theme font reference
                    if (runFonts.AsciiTheme?.HasValue == true &&
                        currentThemeFonts != null)
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
                    fontSize = double.Parse(fontSizeElement.Val.Value!).HalfPointsToPoints();
                }

                // Bold
                var boldElement = runProps.GetFirstChild<Bold>();
                if (boldElement != null)
                {
                    bold = boldElement.IsOn();
                }

                // Italic
                var italicElement = runProps.GetFirstChild<Italic>();
                if (italicElement != null)
                {
                    italic = italicElement.IsOn();
                }

                // Underline
                var underlineElement = runProps.GetFirstChild<Underline>();
                if (underlineElement != null &&
                    underlineElement.Val?.Value != UnderlineValues.None)
                {
                    underline = true;
                }

                // Strikethrough
                var strikeElement = runProps.GetFirstChild<Strike>();
                if (strikeElement != null)
                {
                    strikethrough = strikeElement.IsOn();
                }

                // All caps
                var capsElement = runProps.GetFirstChild<Caps>();
                if (capsElement != null)
                {
                    allCaps = capsElement.IsOn();
                }

                // Small caps
                var smallCapsElement = runProps.GetFirstChild<SmallCaps>();
                if (smallCapsElement != null)
                {
                    smallCaps = smallCapsElement.IsOn();
                }

                // Character spacing (w:spacing in rPr)
                var spacingElement = runProps.GetFirstChild<Spacing>();
                if (spacingElement?.Val?.HasValue == true)
                {
                    characterSpacing = spacingElement.Val.Value / twipsPerPoint;
                }

                // Colour — mirror the run-level rule: an explicit w:color on the style overrides
                // whatever was inherited (basedOn chain / docDefaults), and w:val="auto" RESETS the
                // inherited colour to automatic rather than falling through to it (a card template's
                // white docDefaults must not leak through Normal's explicit auto). The sentinel
                // survives basedOn inheritance and is converted to automaticRunColorHex at the run
                // resolution sites.
                var colorElement = runProps.GetFirstChild<Color>();
                if (colorElement != null)
                {
                    color = ResolveRunColor(colorElement) ?? automaticColorSentinel;
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
                        backgroundColor = currentThemeColors.ResolveColor(themeFillValue);
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
                    SmallCaps = smallCaps,
                    ColorHex = color,
                    BackgroundColorHex = backgroundColor,
                    CharacterSpacingPoints = characterSpacing
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
            styles.Select(_ => _.StyleId?.Value).Where(id => id != null)!,
            StringComparer.OrdinalIgnoreCase);

        // Capture the default paragraph style id (typically "Normal") so unstyled paragraphs
        // can layer its properties on top of pPrDefault — Word applies it implicitly.
        foreach (var style in styles)
        {
            if (style.Type?.Value == StyleValues.Paragraph &&
                style.Default?.Value == true &&
                style.StyleId?.Value is { } defaultPStyleId)
            {
                defaultParagraphStyleId = defaultPStyleId;
                break;
            }
        }

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

                // Start with base style properties or document defaults (pPrDefault)
                var alignment = baseProps?.Alignment ?? defaultAlignment;
                var spacingBefore = baseProps?.SpacingBeforePoints ?? defaultSpacingBeforePoints;
                var spacingAfter = baseProps?.SpacingAfterPoints ?? defaultSpacingAfterPoints;
                var lineSpacingMultiplier = baseProps?.LineSpacingMultiplier ?? 1.04;
                var lineSpacingPoints = baseProps?.LineSpacingPoints ?? 0;
                var lineSpacingRule = baseProps?.LineSpacingRule ?? LineSpacingRule.Auto;
                var firstLineIndent = baseProps?.FirstLineIndentPoints ?? 0;
                var leftIndent = baseProps?.LeftIndentPoints ?? defaultLeftIndentPoints;
                var rightIndent = baseProps?.RightIndentPoints ?? defaultRightIndentPoints;
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
                        BackgroundColorHex = backgroundColor,
                        TabStops = baseProps?.TabStops ?? [],
                        DefaultTabStopPoints = defaultTabStopPoints,
                        Frame = baseProps?.Frame
                        // PageBreakBefore intentionally not inherited from styles
                    };
                    processed.Add(styleId);
                    continue;
                }

                // Text-frame positioning (w:framePr). A style's own framePr replaces any inherited
                // one; otherwise the base style's frame carries through.
                var frame = baseProps?.Frame;
                var styleFramePr = paraProps.GetFirstChild<FrameProperties>();
                if (styleFramePr != null)
                {
                    frame = ParseParagraphFrame(styleFramePr);
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

                // Parse indentation — but skip when the style has numPr without a numId.
                // In Word, indentation in a style that has numPr is numbering-level indentation
                // and only applies when the numbering is active. Without a numId the numbering
                // is dormant, so the indent should fall back to the base style's value.
                var styleNumPr = paraProps.GetFirstChild<NumberingProperties>();
                var hasOrphanedNumPr = styleNumPr != null && (styleNumPr.NumberingId?.Val?.Value ?? 0) == 0;

                var indentation = paraProps.GetFirstChild<Indentation>();
                if (indentation != null && !hasOrphanedNumPr)
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
                    var valAttribute = widowControlEl.Val;
                    if (valAttribute != null && valAttribute.HasValue)
                    {
                        widowControl = valAttribute.Value;
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
                        backgroundColor = currentThemeColors.ResolveColor(themeFillValue);
                    }

                    // Fall back to direct fill value
                    if (backgroundColor == null && shadingElement.Fill?.HasValue == true &&
                        shadingElement.Fill.Value != "auto" && shadingElement.Fill.Value != "none")
                    {
                        backgroundColor = shadingElement.Fill.Value;
                    }
                }

                // Parse paragraph borders (w:pBdr)
                var borders = baseProps?.Borders;
                var borderTopSpace = baseProps?.BorderTopSpacePoints ?? 0;
                var borderBottomSpace = baseProps?.BorderBottomSpacePoints ?? 0;
                var borderLeftSpace = baseProps?.BorderLeftSpacePoints ?? 0;
                var borderRightSpace = baseProps?.BorderRightSpacePoints ?? 0;
                var borderBetween = baseProps?.BorderBetween ?? BorderEdge.None;
                var borderBetweenSpace = baseProps?.BorderBetweenSpacePoints ?? 0;
                var pBdr = paraProps.GetFirstChild<ParagraphBorders>();
                if (pBdr != null)
                {
                    var topBorder = pBdr.GetFirstChild<TopBorder>();
                    var rightBorder = pBdr.GetFirstChild<RightBorder>();
                    var bottomBorder = pBdr.GetFirstChild<BottomBorder>();
                    var leftBorder = pBdr.GetFirstChild<LeftBorder>();
                    var betweenBorder = pBdr.GetFirstChild<BetweenBorder>();
                    borders = new()
                    {
                        Top = ParseBorderEdge(topBorder),
                        Right = ParseBorderEdge(rightBorder),
                        Bottom = ParseBorderEdge(bottomBorder),
                        Left = ParseBorderEdge(leftBorder)
                    };
                    borderTopSpace = ParseBorderSpace(topBorder);
                    borderRightSpace = ParseBorderSpace(rightBorder);
                    borderBottomSpace = ParseBorderSpace(bottomBorder);
                    borderLeftSpace = ParseBorderSpace(leftBorder);
                    borderBetween = ParseBorderEdge(betweenBorder);
                    borderBetweenSpace = ParseBorderSpace(betweenBorder);
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
                    BackgroundColorHex = backgroundColor,
                    Borders = borders,
                    BorderTopSpacePoints = borderTopSpace,
                    BorderBottomSpacePoints = borderBottomSpace,
                    BorderLeftSpacePoints = borderLeftSpace,
                    BorderRightSpacePoints = borderRightSpace,
                    BorderBetween = borderBetween,
                    BorderBetweenSpacePoints = borderBetweenSpace,
                    TabStops = ParseTabs(paraProps, baseProps?.TabStops ?? []),
                    DefaultTabStopPoints = defaultTabStopPoints,
                    Frame = frame
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
        public NumberFormatValues NumberFormat { get; init; } = NumberFormatValues.Decimal;

        /// <summary>
        /// OOXML <c>w:lvlRestart</c>. <c>null</c> = default behaviour (restart whenever any
        /// shallower level increments). <c>0</c> = never restart. A positive value <c>K</c>
        /// means restart only when an item at level <c>K-1</c> or shallower increments.
        /// </summary>
        public int? LevelRestart { get; init; }
    }

    static Dictionary<int, Dictionary<int, NumberingLevelDefinition>> ExtractNumberingDefinitions(MainDocumentPart mainPart, Dictionary<int, int> numIdToAbstract, HashSet<(int numId, int ilvl)> startOverrides)
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
                var lvlRestart = level.LevelRestart?.Val?.Value;

                levels[ilvl] = new()
                {
                    LevelText = levelText,
                    FontFamily = fontFamily,
                    LeftIndentPoints = leftIndent,
                    HangingIndentPoints = hangingIndent,
                    IsBullet = isBullet,
                    StartNumber = startNumber,
                    NumberFormat = numFmt ?? NumberFormatValues.Decimal,
                    LevelRestart = lvlRestart
                };
            }

            abstractNums[abstractNumId] = levels;
        }

        // Now map numId to abstractNumId, applying any level overrides
        foreach (var numInstance in numbering.Elements<NumberingInstance>())
        {
            var numId = numInstance.NumberID?.Value ?? 0;
            var abstractNumIdRef = numInstance.AbstractNumId?.Val?.Value ?? 0;

            if (!abstractNums.TryGetValue(abstractNumIdRef, out var abstractLevels))
            {
                continue;
            }

            numIdToAbstract[numId] = abstractNumIdRef;

            // Check for level overrides (w:lvlOverride with w:startOverride)
            var overrides = numInstance.Elements<LevelOverride>().ToList();
            if (overrides.Count == 0)
            {
                result[numId] = abstractLevels;
                continue;
            }

            // Clone levels and apply overrides
            var levels = new Dictionary<int, NumberingLevelDefinition>(abstractLevels);
            foreach (var lvlOverride in overrides)
            {
                var overrideIlvl = lvlOverride.LevelIndex?.Value ?? 0;
                var startOverride = lvlOverride.StartOverrideNumberingValue?.Val?.Value;

                if (startOverride != null && levels.TryGetValue(overrideIlvl, out var baseDef))
                {
                    startOverrides.Add((numId, overrideIlvl));
                    levels[overrideIlvl] = new()
                    {
                        LevelText = baseDef.LevelText,
                        FontFamily = baseDef.FontFamily,
                        LeftIndentPoints = baseDef.LeftIndentPoints,
                        HangingIndentPoints = baseDef.HangingIndentPoints,
                        IsBullet = baseDef.IsBullet,
                        StartNumber = startOverride.Value,
                        NumberFormat = baseDef.NumberFormat
                    };
                }
            }

            result[numId] = levels;
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
                        result.TryAdd(styleId, (numId, ilvl));
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

    Dictionary<string, TableStyleBorderInfo> ExtractTableStyleBorders(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, TableStyleBorderInfo>(StringComparer.OrdinalIgnoreCase);

        var stylesPart = mainPart.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            return result;
        }

        // Index table styles by ID so we can resolve inherited (w:basedOn) cell margins.
        var tableStylesById = new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            if (style.Type?.Value == StyleValues.Table && style.StyleId?.Value is { } id)
            {
                tableStylesById[id] = style;
            }
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

            if (style.Default?.Value == true)
            {
                defaultTableStyleId = styleId;
            }

            // Look for table properties in the style
            var tblPr = style.StyleTableProperties;

            CellBorders cellBorders = new();
            var insideH = BorderEdge.None;
            var insideV = BorderEdge.None;
            var colBandSize = 1;
            var rowBandSize = 1;

            if (tblPr != null)
            {
                // Look for tblBorders in the table properties
                var borders = tblPr.GetFirstChild<TableBorders>();
                if (borders != null)
                {
                    cellBorders = new()
                    {
                        Top = ParseBorderEdge(borders.GetFirstChild<TopBorder>()),
                        Right = ParseBorderEdge(borders.GetFirstChild<RightBorder>()),
                        Bottom = ParseBorderEdge(borders.GetFirstChild<BottomBorder>()),
                        Left = ParseBorderEdge(borders.GetFirstChild<LeftBorder>())
                    };

                    insideH = ParseBorderEdge(borders.GetFirstChild<InsideHorizontalBorder>());
                    insideV = ParseBorderEdge(borders.GetFirstChild<InsideVerticalBorder>());
                }

                var colBand = tblPr.GetFirstChild<TableStyleColumnBandSize>();
                if (colBand?.Val?.HasValue == true)
                {
                    colBandSize = colBand.Val.Value;
                }

                var rowBand = tblPr.GetFirstChild<TableStyleRowBandSize>();
                if (rowBand?.Val?.HasValue == true)
                {
                    rowBandSize = rowBand.Val.Value;
                }
            }

            // w:tblCellSpacing on the style-level tblPr propagates to every table that
            // references the style (TableWeb1/2/3 etc. all use this for HTML-style detached borders).
            var styleCellSpacing = ReadTableCellSpacing(tblPr);

            // Whole-table cell shading lives on the style's StyleTableCellProperties (the
            // <w:tcPr> that's a direct child of <w:style>, distinct from
            // StyleTableProperties which holds <w:tblPr>). All cells inherit this fill
            // unless a more-specific conditional region or an explicit cell w:shd overrides.
            var wholeTableShading = ReadShadingFill(style.StyleTableCellProperties?.GetFirstChild<Shading>());

            // Whole-table cell vAlign also lives on StyleTableCellProperties — Word's
            // built-in form-style tables (FormTable / TableGrid) put vAlign="center" here
            // so every data row's content sits centred between the row's top and bottom borders.
            CellVerticalAlignment? styleVerticalAlignment = null;
            var styleVAlign = style.StyleTableCellProperties?.GetFirstChild<TableCellVerticalAlignment>();
            if (styleVAlign?.Val?.HasValue == true)
            {
                var vAlignVal = styleVAlign.Val.Value;
                if (vAlignVal == TableVerticalAlignmentValues.Center)
                {
                    styleVerticalAlignment = CellVerticalAlignment.Center;
                }
                else if (vAlignVal == TableVerticalAlignmentValues.Bottom)
                {
                    styleVerticalAlignment = CellVerticalAlignment.Bottom;
                }
                else
                {
                    styleVerticalAlignment = CellVerticalAlignment.Top;
                }
            }

            // Parse conditional formatting (tblStylePr) for border + shading overrides
            Dictionary<TableStyleOverrideValues, ConditionalFormat>? conditionals = null;
            foreach (var tblStylePr in style.Elements<TableStyleProperties>())
            {
                var type = tblStylePr.Type?.Value;
                if (type == null)
                {
                    continue;
                }

                var tcPr = tblStylePr.TableStyleConditionalFormattingTableCellProperties;
                var tcBorders = tcPr?.GetFirstChild<TableCellBorders>();

                CellBorders? condBorders = null;
                if (tcBorders != null)
                {
                    condBorders = new()
                    {
                        Top = ParseBorderEdge(tcBorders.GetFirstChild<TopBorder>()),
                        Right = ParseBorderEdge(tcBorders.GetFirstChild<RightBorder>()),
                        Bottom = ParseBorderEdge(tcBorders.GetFirstChild<BottomBorder>()),
                        Left = ParseBorderEdge(tcBorders.GetFirstChild<LeftBorder>())
                    };
                }

                var condShading = ReadShadingFill(tcPr?.GetFirstChild<Shading>());

                // Run-level rPr inside the tblStylePr — Morph cascades just the run colour
                // for now (Word's tblStylePr also defines bold/italic/font, but those are
                // currently expected to come from the run-level rPr in the document body).
                string? condRunColor = null;
                var rPr = tblStylePr.GetFirstChild<RunPropertiesBaseStyle>();
                if (rPr != null)
                {
                    var color = rPr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Color>();
                    if (color?.Val?.HasValue == true && color.Val.Value != "auto")
                    {
                        condRunColor = color.Val.Value;
                    }
                }

                if (condBorders == null && condShading == null && condRunColor == null)
                {
                    continue;
                }

                conditionals ??= new();
                conditionals[type.Value] = new(condBorders, condShading, condRunColor);
            }

            // Resolve default cell padding by walking the w:basedOn chain — Word's built-in
            // TableGrid inherits its tblCellMar from TableNormal, so a table that names
            // TableGrid would otherwise lose the 108-twip start/end padding.
            var styleCellPadding = ResolveStyleCellPadding(style, tableStylesById);

            if (cellBorders.HasAnyBorder || insideH.IsVisible || insideV.IsVisible || wholeTableShading != null || conditionals != null || styleCellSpacing > 0 || styleVerticalAlignment != null || styleCellPadding != null)
            {
                result[styleId] = new(cellBorders, insideH, insideV, wholeTableShading, rowBandSize, colBandSize, styleCellSpacing, conditionals, styleVerticalAlignment, styleCellPadding);
            }
        }

        return result;
    }

    static CellSpacing? ResolveStyleCellPadding(Style style, Dictionary<string, Style> tableStylesById)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = style;
        while (current != null)
        {
            if (current.StyleId?.Value is { } id && !visited.Add(id))
            {
                break;
            }

            var tblCellMar = current.StyleTableProperties?.GetFirstChild<TableCellMarginDefault>();
            if (tblCellMar != null)
            {
                var padding = ParseTableCellMargin(tblCellMar);
                if (padding != null)
                {
                    return padding;
                }
            }

            var basedOnId = current.BasedOn?.Val?.Value;
            if (basedOnId == null || !tableStylesById.TryGetValue(basedOnId, out var baseStyle))
            {
                break;
            }

            current = baseStyle;
        }

        return null;
    }

    static string? ReadShadingFill(Shading? shading)
    {
        if (shading?.Fill?.HasValue != true)
        {
            return null;
        }

        var fill = shading.Fill.Value;
        return fill is null or "auto" ? null : fill;
    }

    // Maps the named-colour palette of <w:highlight w:val="..."/> to RGB hex strings.
    // Word's highlight pen uses a fixed palette (not arbitrary RGB), so mapping is
    // deterministic and fixed by the spec — the colours match the highlighter swatches
    // in Word's Home → Text Highlight Color picker.
    static string? HighlightToHex(HighlightColorValues value)
    {
        if (value == HighlightColorValues.Yellow) return "FFFF00";
        if (value == HighlightColorValues.Green) return "00FF00";
        if (value == HighlightColorValues.Cyan) return "00FFFF";
        if (value == HighlightColorValues.Magenta) return "FF00FF";
        if (value == HighlightColorValues.Blue) return "0000FF";
        if (value == HighlightColorValues.Red) return "FF0000";
        if (value == HighlightColorValues.DarkBlue) return "000080";
        if (value == HighlightColorValues.DarkCyan) return "008080";
        if (value == HighlightColorValues.DarkGreen) return "008000";
        if (value == HighlightColorValues.DarkMagenta) return "800080";
        if (value == HighlightColorValues.DarkRed) return "800000";
        if (value == HighlightColorValues.DarkYellow) return "808000";
        if (value == HighlightColorValues.DarkGray) return "808080";
        if (value == HighlightColorValues.LightGray) return "C0C0C0";
        if (value == HighlightColorValues.Black) return "000000";
        if (value == HighlightColorValues.White) return "FFFFFF";
        return null;
    }

    /// <summary>
    /// Reads <c>w:tblLook</c> into the subset of conditional flags that the table allows
    /// to be derived from cell position. Defaults to all conditions when no <c>w:tblLook</c>
    /// is present (matches Word's behaviour for tables saved without an explicit look).
    /// </summary>
    internal static ConditionalFormatFlags ParseTableLookMask(TableLook? tableLook)
    {
        if (tableLook == null)
        {
            return ConditionalFormatFlagsExtensions.AllConditions;
        }

        var mask = ConditionalFormatFlagsExtensions.AllConditions;

        // First/last-row and first/last-column derivation only when explicitly enabled.
        if (tableLook.FirstRow?.Value != true) mask &= ~ConditionalFormatFlags.FirstRow;
        if (tableLook.LastRow?.Value != true) mask &= ~ConditionalFormatFlags.LastRow;
        if (tableLook.FirstColumn?.Value != true) mask &= ~ConditionalFormatFlags.FirstColumn;
        if (tableLook.LastColumn?.Value != true) mask &= ~ConditionalFormatFlags.LastColumn;

        // Banding is enabled by default; tblLook's noHBand/noVBand flags suppress it.
        if (tableLook.NoHorizontalBand?.Value == true)
        {
            mask &= ~(ConditionalFormatFlags.OddHBand | ConditionalFormatFlags.EvenHBand);
        }

        if (tableLook.NoVerticalBand?.Value == true)
        {
            mask &= ~(ConditionalFormatFlags.OddVBand | ConditionalFormatFlags.EvenVBand);
        }

        return mask;
    }

    internal static ConditionalFormatFlags ParseConditionalFormatFlags(ConditionalFormatStyle? cnfStyle)
    {
        if (cnfStyle == null)
        {
            return ConditionalFormatFlags.None;
        }

        var flags = ConditionalFormatFlags.None;
        if (cnfStyle.FirstRow?.Value == true) flags |= ConditionalFormatFlags.FirstRow;
        if (cnfStyle.LastRow?.Value == true) flags |= ConditionalFormatFlags.LastRow;
        if (cnfStyle.FirstColumn?.Value == true) flags |= ConditionalFormatFlags.FirstColumn;
        if (cnfStyle.LastColumn?.Value == true) flags |= ConditionalFormatFlags.LastColumn;
        if (cnfStyle.OddVerticalBand?.Value == true) flags |= ConditionalFormatFlags.OddVBand;
        if (cnfStyle.EvenVerticalBand?.Value == true) flags |= ConditionalFormatFlags.EvenVBand;
        if (cnfStyle.OddHorizontalBand?.Value == true) flags |= ConditionalFormatFlags.OddHBand;
        if (cnfStyle.EvenHorizontalBand?.Value == true) flags |= ConditionalFormatFlags.EvenHBand;
        if (cnfStyle.FirstRowFirstColumn?.Value == true) flags |= ConditionalFormatFlags.FirstRowFirstColumn;
        if (cnfStyle.FirstRowLastColumn?.Value == true) flags |= ConditionalFormatFlags.FirstRowLastColumn;
        if (cnfStyle.LastRowFirstColumn?.Value == true) flags |= ConditionalFormatFlags.LastRowFirstColumn;
        if (cnfStyle.LastRowLastColumn?.Value == true) flags |= ConditionalFormatFlags.LastRowLastColumn;

        return flags;
    }

    /// <summary>
    /// Returns the conditional regions that apply to a given cell, in cascade order
    /// (lowest priority first). Caller cascades borders / shading by overlaying each
    /// region's value onto the running result.
    /// <para>
    /// ECMA-376 priority (low → high), which this method emits in order:
    /// wholeTable (caller's base) → bandHoriz → bandVert → lastCol → firstCol →
    /// lastRow → firstRow → seCell → swCell → neCell → nwCell.
    /// </para>
    /// </summary>
    /// <param name="flags">Explicit <c>w:cnfStyle</c> flags from the row, cell, or paragraph. When
    /// <see cref="ConditionalFormatFlags.None"/>, flags are derived from the cell's grid position.</param>
    /// <param name="rowIndex">Zero-based row index of the cell within the table.</param>
    /// <param name="colIndex">Zero-based column index of the cell within the row.</param>
    /// <param name="totalRows">Total number of rows in the table.</param>
    /// <param name="totalCols">Total number of columns in the row.</param>
    /// <param name="rowBandSize">Row band size from <c>w:tblStylePr</c> (rows per horizontal band).</param>
    /// <param name="colBandSize">Column band size from <c>w:tblStylePr</c> (columns per vertical band).</param>
    /// <param name="tableLookMask">Subset of conditions that the table's <c>w:tblLook</c>
    /// allows to be auto-derived from cell position. Explicit <c>w:cnfStyle</c> is always
    /// honoured regardless of this mask. Word writes the <c>w:tblLook</c> bits to indicate
    /// "treat this row as the header" / "no banding" — without honouring it Morph applies
    /// banding shading to tables Word renders un-banded (see <c>cards/09</c>).</param>
    internal static IEnumerable<TableStyleOverrideValues> ResolveActiveConditions(
        ConditionalFormatFlags flags,
        int rowIndex,
        int colIndex,
        int totalRows,
        int totalCols,
        int rowBandSize,
        int colBandSize,
        ConditionalFormatFlags tableLookMask = ConditionalFormatFlagsExtensions.AllConditions)
    {
        // When the source provides an explicit cnfStyle (row, cell, or paragraph),
        // trust those flags. Otherwise derive them from the cell's grid position,
        // but only for conditions the table's tblLook permits.
        if (flags == ConditionalFormatFlags.None)
        {
            flags = DerivePositionalFlags(rowIndex, colIndex, totalRows, totalCols, rowBandSize, colBandSize) & tableLookMask;
        }

        // ECMA-376 priority: lowest precedence first; later overrides earlier.
        if ((flags & ConditionalFormatFlags.OddHBand) != 0)
        {
            yield return TableStyleOverrideValues.Band1Horizontal;
        }
        else if ((flags & ConditionalFormatFlags.EvenHBand) != 0)
        {
            yield return TableStyleOverrideValues.Band2Horizontal;
        }

        if ((flags & ConditionalFormatFlags.OddVBand) != 0)
        {
            yield return TableStyleOverrideValues.Band1Vertical;
        }
        else if ((flags & ConditionalFormatFlags.EvenVBand) != 0)
        {
            yield return TableStyleOverrideValues.Band2Vertical;
        }

        if ((flags & ConditionalFormatFlags.LastColumn) != 0)
        {
            yield return TableStyleOverrideValues.LastColumn;
        }

        if ((flags & ConditionalFormatFlags.FirstColumn) != 0)
        {
            yield return TableStyleOverrideValues.FirstColumn;
        }

        if ((flags & ConditionalFormatFlags.LastRow) != 0)
        {
            yield return TableStyleOverrideValues.LastRow;
        }

        if ((flags & ConditionalFormatFlags.FirstRow) != 0)
        {
            yield return TableStyleOverrideValues.FirstRow;
        }

        if ((flags & ConditionalFormatFlags.LastRowLastColumn) != 0)
        {
            yield return TableStyleOverrideValues.SouthEastCell;
        }

        if ((flags & ConditionalFormatFlags.LastRowFirstColumn) != 0)
        {
            yield return TableStyleOverrideValues.SouthWestCell;
        }

        if ((flags & ConditionalFormatFlags.FirstRowLastColumn) != 0)
        {
            yield return TableStyleOverrideValues.NorthEastCell;
        }

        if ((flags & ConditionalFormatFlags.FirstRowFirstColumn) != 0)
        {
            yield return TableStyleOverrideValues.NorthWestCell;
        }
    }

    static ConditionalFormatFlags DerivePositionalFlags(
        int rowIndex,
        int colIndex,
        int totalRows,
        int totalCols,
        int rowBandSize,
        int colBandSize)
    {
        var flags = ConditionalFormatFlags.None;

        if (rowIndex == 0)
        {
            flags |= ConditionalFormatFlags.FirstRow;
        }
        else if (rowIndex == totalRows - 1)
        {
            flags |= ConditionalFormatFlags.LastRow;
        }
        else if (rowBandSize > 0)
        {
            // Body rows alternate after the header row.
            flags |= (rowIndex - 1) / rowBandSize % 2 == 0
                ? ConditionalFormatFlags.OddHBand
                : ConditionalFormatFlags.EvenHBand;
        }

        if (colIndex == 0)
        {
            flags |= ConditionalFormatFlags.FirstColumn;
        }
        else if (colIndex == totalCols - 1)
        {
            flags |= ConditionalFormatFlags.LastColumn;
        }
        else if (colBandSize > 0)
        {
            flags |= (colIndex - 1) / colBandSize % 2 == 0
                ? ConditionalFormatFlags.OddVBand
                : ConditionalFormatFlags.EvenVBand;
        }

        return flags;
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
            // For bullets, the level text IS the bullet character. Word stores it in
            // the level's font's Private Use Area encoding - the same PUA codepoint
            // means a *different glyph* in Symbol vs Wingdings vs Wingdings 2 (e.g.
            // U+F0A7 is a club suit in Symbol but a small filled square in Wingdings).
            // Map to Unicode based on the level's declared font, not just the codepoint.
            text = MapBulletPuaToUnicode(levelDef.LevelText, levelDef.FontFamily);

            if (string.IsNullOrEmpty(text))
            {
                text = "•";
            }
        }
        else
        {
            // The counter is shared across every numId referencing the same abstract; a numId's
            // w:startOverride resets it once, at that numId's first use (Word's per-table restart).
            var abstractId = numberingAbstractIds.GetValueOrDefault(numId, numId);
            var counterKey = (abstractId, ilvl);
            int counter;
            if (numberingStartOverrides.Contains((numId, ilvl)) && appliedStartOverrides.Add((numId, ilvl)))
            {
                counter = levelDef.StartNumber;
            }
            else if (!numberingCounters.TryGetValue(counterKey, out counter))
            {
                counter = levelDef.StartNumber;
            }
            else
            {
                counter++;
            }

            numberingCounters[counterKey] = counter;

            // OOXML default: incrementing level N restarts every deeper level of the
            // same numId, so e.g. a fresh "II." re-starts its child a/b counter.
            // <w:lvlRestart w:val="0"/> on a deeper level opts out; <w:lvlRestart w:val="K"/>
            // means that level only restarts when a level <= K-1 increments, so any
            // deeper level whose lvlRestart-1 is less than ilvl stays put.
            ResetDeeperCounters(abstractId, ilvl, levels);

            // Replace %N placeholders with formatted counter values
            text = levelDef.LevelText;
            for (var lvl = 0; lvl <= ilvl; lvl++)
            {
                var placeholder = $"%{lvl + 1}";
                if (!text.Contains(placeholder))
                {
                    continue;
                }

                int lvlCounter;
                if (lvl == ilvl)
                {
                    lvlCounter = counter;
                }
                else
                {
                    // For parent levels, use the current counter (or start if not yet seen)
                    var parentKey = (abstractId, lvl);
                    lvlCounter = numberingCounters.GetValueOrDefault(parentKey, levelDef.StartNumber);
                }

                var numFmt = levelDef.NumberFormat;
                // Try to get the actual format for parent levels
                if (lvl != ilvl && levels.TryGetValue(lvl, out var parentLevelDef))
                {
                    numFmt = parentLevelDef.NumberFormat;
                }

                text = text.Replace(placeholder, FormatNumber(lvlCounter, numFmt));
            }
        }

        return new()
        {
            Text = text,
            Level = ilvl,
            FontFamily = levelDef.FontFamily,
            IndentPoints = levelDef.LeftIndentPoints,
            HangingIndentPoints = levelDef.HangingIndentPoints
        };
    }

    void ResetDeeperCounters(int abstractId, int incrementedIlvl, Dictionary<int, NumberingLevelDefinition> levels)
    {
        foreach (var (deeperIlvl, deeperLevel) in levels)
        {
            if (deeperIlvl <= incrementedIlvl)
            {
                continue;
            }

            // lvlRestart=0 means "never restart" - the counter persists across parent changes.
            if (deeperLevel.LevelRestart == 0)
            {
                continue;
            }

            // lvlRestart=K means "restart when an item at level <= K-1 increments".
            // If our incrementedIlvl is deeper than K-1, this level keeps its value.
            if (deeperLevel.LevelRestart is { } restart && restart > 0 && incrementedIlvl > restart - 1)
            {
                continue;
            }

            numberingCounters.Remove((abstractId, deeperIlvl));
        }
    }

    /// <summary>
    /// Maps a single Private Use Area bullet character to a Unicode glyph that
    /// <c>Bullets.ttf</c> (and most system fonts) can actually render.
    /// </summary>
    /// <remarks>
    /// Word stores bullet glyphs as PUA codepoints in the level's declared font - the
    /// renderer must therefore use that font, but in our pipeline bullets are drawn from
    /// the bundled <c>Morph Bullets</c> subset which only carries Unicode codepoints. The
    /// same PUA codepoint maps to a different glyph per font, so the lookup branches on
    /// <paramref name="fontFamily"/> first. Multi-character or non-PUA level texts are
    /// returned unchanged - those are e.g. the literal lowercase "o" Word emits at the
    /// second bullet level (font = Courier New).
    /// </remarks>
    static string MapBulletPuaToUnicode(string levelText, string? fontFamily)
    {
        if (string.IsNullOrEmpty(levelText) || levelText.Length != 1)
        {
            return levelText;
        }

        var c = levelText[0];
        if (c is < '' or > '')
        {
            return levelText;
        }

        // Wingdings and its siblings - codepoints reflect the standard Wingdings encoding
        // shifted into the F000 PUA block (so 0xA7 -> U+F0A7, etc.).
        if (fontFamily != null &&
            fontFamily.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase))
        {
            return c switch
            {
                '' => "▪", // Wingdings 0x76 - small filled square
                '' => "▪", // Wingdings 0xA7 - black small square (Word's default bullet at ilvl 2)
                '' => "▢", // Wingdings 0xA8 - white square
                '' => "✓", // Wingdings 0xFC - check mark
                '' => "✗", // Wingdings 0xFB - ballot X
                '' => "►", // Wingdings 0xD8 - black right-pointing pointer
                _ => "▪" // Default Wingdings bullet to a small square
            };
        }

        // Symbol font (and unknown/unspecified fonts - Symbol is by far the most common
        // bullet font in Word templates, so it's the safest default).
        return c switch
        {
            '' => "•", // Symbol 0xB7 - bullet operator
            '' => "●", // Symbol 0x6C - filled circle
            '' => "○", // Symbol 0xA8 - hollow circle
            '' => "◆", // Symbol 0xD8 - diamond
            _ => "•"
        };
    }

    static string FormatNumber(int number, NumberFormatValues format)
    {
        if (format == NumberFormatValues.UpperRoman)
        {
            return ToRoman(number);
        }

        if (format == NumberFormatValues.LowerRoman)
        {
            return ToRoman(number).ToLowerInvariant();
        }

        if (format == NumberFormatValues.UpperLetter)
        {
            return ToLetter(number);
        }

        if (format == NumberFormatValues.LowerLetter)
        {
            return ToLetter(number).ToLowerInvariant();
        }

        return number.ToString();
    }

    static string ToRoman(int number)
    {
        if (number is <= 0 or > 3999)
        {
            return number.ToString();
        }

        ReadOnlySpan<(int value, string numeral)> romanNumerals =
        [
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I")
        ];

        var result = new StringBuilder();
        foreach (var (value, numeral) in romanNumerals)
        {
            while (number >= value)
            {
                result.Append(numeral);
                number -= value;
            }
        }

        return result.ToString();
    }

    static string ToLetter(int number)
    {
        if (number <= 0)
        {
            return number.ToString();
        }

        // 1=A, 2=B, ..., 26=Z, 27=AA, 28=AB, ...
        var result = new StringBuilder();
        while (number > 0)
        {
            number--;
            result.Insert(0, (char) ('A' + number % 26));
            number /= 26;
        }

        return result.ToString();
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
        // Default 0.25 inch
        double hyphenationZonePoints = 18;
        var consecutiveHyphenLimit = 0;
        var doNotHyphenateCaps = false;

        // Parse autoHyphenation
        var autoHyphen = settings.GetFirstChild<AutoHyphenation>();
        if (autoHyphen != null)
        {
            autoHyphenation = autoHyphen.IsOn();
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
            doNotHyphenateCaps = doNotHyphenCaps.IsOn();
        }

        return new()
        {
            AutoHyphenation = autoHyphenation,
            HyphenationZonePoints = hyphenationZonePoints,
            ConsecutiveHyphenLimit = consecutiveHyphenLimit,
            DoNotHyphenateCaps = doNotHyphenateCaps
        };
    }

    void ExtractDefaultTabStop(MainDocumentPart mainPart)
    {
        var settings = mainPart.DocumentSettingsPart?.Settings;
        var defaultTabStop = settings?.GetFirstChild<DefaultTabStop>();
        if (defaultTabStop?.Val?.HasValue == true)
        {
            defaultTabStopPoints = defaultTabStop.Val.Value / twipsPerPoint;
        }
    }

    static CompatibilitySettings ExtractCompatibilitySettings(MainDocumentPart mainPart)
    {
        // Word treats a document that declares no compatibilityMode as mode 12 (ECMA-376 /
        // Word 2007 rules) — not as a modern document.
        var settingsPart = mainPart.DocumentSettingsPart;
        var compat = settingsPart?.Settings?.GetFirstChild<Compatibility>();
        if (compat == null)
        {
            return new()
            {
                CompatibilityMode = 12
            };
        }

        // Look for compatibilityMode in CompatSetting elements
        var compatMode = 12;

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

        double gutterPoints = 0;
        if (pageMargin != null)
        {
            if (pageMargin.Top?.HasValue == true)
            {
                // ECMA-376 §17.6.11: a negative w:top means |val| is the distance from the top of
                // the page to the top of the body, regardless of header height.
                marginTop = Math.Abs(pageMargin.Top.Value) / twipsPerPoint;
            }

            if (pageMargin.Bottom?.HasValue == true)
            {
                marginBottom = Math.Abs(pageMargin.Bottom.Value) / twipsPerPoint;
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

            if (pageMargin.Gutter?.HasValue == true)
            {
                gutterPoints = pageMargin.Gutter.Value / twipsPerPoint;
            }
        }

        // Gutter is added to the appropriate margin at parse time so the rest of the pipeline
        // doesn't need to know about it. Preserved separately on PageSettings for consumers.
        var gutterAtTop = gutterAtTopSetting;
        if (gutterPoints > 0)
        {
            if (gutterAtTop)
            {
                marginTop += gutterPoints;
            }
            else
            {
                marginLeft += gutterPoints;
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

        // Parse different first page setting (w:titlePg)
        var differentFirstPage = sectionProps.GetFirstChild<TitlePage>() != null;

        // Parse line numbering settings
        var lineNumbers = ParseLineNumberSettings(sectionProps);

        // Parse document grid settings (used by Word to align text to a baseline grid).
        // w:docGrid applies a grid only when @type asks for one — Word writes a bare
        // <w:docGrid w:linePitch="360"/> with no @type into most documents as a stored
        // default and applies no grid to layout. Treating that pitch as a per-line floor
        // inflated business-plans/13's 11pt body lines to 18pt, overflowing pages Word fits.
        double documentGridLinePitchPoints = 0;
        var docGrid = sectionProps.GetFirstChild<DocGrid>();
        if (docGrid?.LinePitch?.HasValue == true &&
            docGrid.Type?.Value is { } gridType &&
            gridType != DocGridValues.Default)
        {
            documentGridLinePitchPoints = docGrid.LinePitch.Value / twipsPerPoint;
        }

        // Parse page borders (w:pgBorders)
        var pageBorders = ParsePageBorders(sectionProps.GetFirstChild<OoxmlPageBorders>());

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
            BackgroundColorHex = documentBackgroundColor,
            DifferentFirstPage = differentFirstPage,
            PageBorders = pageBorders,
            GutterPoints = gutterPoints,
            GutterAtTop = gutterAtTop
        };
    }

    PageBorders? ParsePageBorders(OoxmlPageBorders? element)
    {
        if (element == null)
        {
            return null;
        }

        var top = ParseBorderEdge(element.GetFirstChild<TopBorder>());
        var right = ParseBorderEdge(element.GetFirstChild<RightBorder>());
        var bottom = ParseBorderEdge(element.GetFirstChild<BottomBorder>());
        var left = ParseBorderEdge(element.GetFirstChild<LeftBorder>());

        if (!top.IsVisible && !right.IsVisible && !bottom.IsVisible && !left.IsVisible)
        {
            return null;
        }

        return new()
        {
            Top = top,
            Right = right,
            Bottom = bottom,
            Left = left,
            TopSpacePoints = ReadSpacePoints(element.GetFirstChild<TopBorder>()),
            RightSpacePoints = ReadSpacePoints(element.GetFirstChild<RightBorder>()),
            BottomSpacePoints = ReadSpacePoints(element.GetFirstChild<BottomBorder>()),
            LeftSpacePoints = ReadSpacePoints(element.GetFirstChild<LeftBorder>())
        };

        static double ReadSpacePoints(BorderType? edge) =>
            edge?.Space?.HasValue == true ? edge.Space.Value : 24;
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
        // Default 0.25 inch
        double distancePoints = 18;
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

    // Word's "automatic" text colour is contrast-aware: black on an ordinary page, white when the
    // page background (w:background) is dark — brochures/03 paints its automatic titles white on
    // navy. Null means "leave unset" so renderers fall back to their default black, keeping models
    // for ordinary documents free of a stamped 000000.
    static string? ComputeAutomaticRunColor(string? pageBackgroundHex)
    {
        if (pageBackgroundHex is not {Length: 6} ||
            !uint.TryParse(pageBackgroundHex, NumberStyles.HexNumber, null, out var rgb))
        {
            return null;
        }

        var red = (rgb >> 16) & 0xFF;
        var green = (rgb >> 8) & 0xFF;
        var blue = rgb & 0xFF;

        // ITU-R BT.601 perceived brightness; below mid-grey counts as dark.
        var brightness = (299 * red + 587 * green + 114 * blue) / 1000;
        return brightness < 128 ? "FFFFFF" : null;
    }

    string? ExtractDocumentBackgroundColor(DocumentFormat.OpenXml.Wordprocessing.Document document)
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

    void ExtractDefaultParagraphProperties(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.StyleDefinitionsPart;

        // No styles.xml, or styles with no docDefaults element at all: the document declares
        // no document-wide defaults, so Word supplies its normal.dotm built-in 8pt-after.
        // Minimal hand-built docx (multiple_pages, table_multipage, header_footer) have no
        // styles.xml and rely on this.
        if (stylesPart?.Styles == null)
        {
            defaultSpacingAfterPoints = builtInSpacingAfterPoints;
            defaultLineSpacingMultiplier = builtInLineSpacingMultiplier;
            return;
        }

        var docDefaults = stylesPart.Styles.DocDefaults;
        if (docDefaults == null)
        {
            defaultSpacingAfterPoints = builtInSpacingAfterPoints;
            defaultLineSpacingMultiplier = builtInLineSpacingMultiplier;
            return;
        }

        // docDefaults IS present but carries no paragraph defaults: the document explicitly
        // configured its document-wide defaults and omitted paragraph spacing, which Word
        // reads as zero — not the 8pt built-in. Verified against resumes/13, cover-letters/03,
        // letters/09, wedding/05 (all have <w:docDefaults> without a pPrDefault and render
        // tight in Word). The field is already 0.
        var pPrDefault = docDefaults.ParagraphPropertiesDefault;
        if (pPrDefault?.ParagraphPropertiesBaseStyle == null)
        {
            return;
        }

        var pPr = pPrDefault.ParagraphPropertiesBaseStyle;

        // Spacing defaults
        var spacing = pPr.SpacingBetweenLines;
        if (spacing != null)
        {
            if (spacing.After?.HasValue == true)
            {
                defaultSpacingAfterPoints = double.Parse(spacing.After.Value!) / twipsPerPoint;
            }

            if (spacing.Before?.HasValue == true)
            {
                defaultSpacingBeforePoints = double.Parse(spacing.Before.Value!) / twipsPerPoint;
            }

            // pPrDefault's w:line is deliberately not read — see defaultLineSpacingMultiplier.
        }

        // Indentation defaults
        var indentation = pPr.Indentation;
        if (indentation != null)
        {
            if (indentation.Left?.HasValue == true)
            {
                defaultLeftIndentPoints = double.Parse(indentation.Left.Value!) / twipsPerPoint;
            }

            if (indentation.Right?.HasValue == true)
            {
                defaultRightIndentPoints = double.Parse(indentation.Right.Value!) / twipsPerPoint;
            }
        }

        // Alignment default (w:jc) — the base of the alignment cascade.
        var justification = pPr.Justification;
        if (justification?.Val?.HasValue == true)
        {
            var justVal = justification.Val.Value;
            if (justVal == JustificationValues.Center)
            {
                defaultAlignment = TextAlignment.Center;
            }
            else if (justVal == JustificationValues.Right)
            {
                defaultAlignment = TextAlignment.Right;
            }
            else if (justVal == JustificationValues.Both || justVal == JustificationValues.Distribute)
            {
                defaultAlignment = TextAlignment.Justify;
            }
        }
    }

    HeaderFooterContent? ExtractHeaderFooter(IReadOnlyList<SectionProperties> sectionPropsList, MainDocumentPart mainPart, HeaderFooterValues type, bool isHeader)
    {
        // Search every section's properties for a matching reference, not just the last sectPr.
        // The last sectPr is the trailing/document-level one and often has no header refs; earlier
        // sectPrs (e.g. section 0/1) carry the actual references for cover-page and body sections.
        // Picking up MainPart.HeaderParts.FirstOrDefault as a blind fallback risks landing on the
        // wrong header (e.g. an unreferenced cover-page header that paints a coloured background).
        // The caller passes the body's already-materialized sectPr list — this runs 2–6× per parse.
        OpenXmlPart? part = null;
        foreach (var sectionProps in sectionPropsList)
        {
            if (isHeader)
            {
                var headerRef = sectionProps.Descendants<HeaderReference>()
                    .FirstOrDefault(_ => _.Type?.Value == type);
                if (headerRef?.Id?.Value != null)
                {
                    part = mainPart.GetPartById(headerRef.Id.Value);
                    break;
                }
            }
            else
            {
                var footerRef = sectionProps.Descendants<FooterReference>()
                    .FirstOrDefault(_ => _.Type?.Value == type);
                if (footerRef?.Id?.Value != null)
                {
                    part = mainPart.GetPartById(footerRef.Id.Value);
                    break;
                }
            }
        }

        // Get the root element from the part
        OpenXmlCompositeElement? rootElement = part switch
        {
            HeaderPart hp => hp.Header,
            FooterPart fp => fp.Footer,
            _ => null
        };

        if (rootElement == null)
        {
            return null;
        }

        var elements = new List<DocumentElement>();
        AppendHeaderFooterElements(rootElement, mainPart, elements);

        return elements.Count > 0
            ? new HeaderFooterContent
            {
                Elements = elements
            }
            : null;
    }

    void AppendHeaderFooterElements(OpenXmlElement container, MainDocumentPart mainPart, List<DocumentElement> elements)
    {
        foreach (var element in container.ChildElements)
        {
            switch (element)
            {
                case Paragraph para:
                    elements.AddRange(ParseParagraph(para, mainPart));
                    break;
                case Table table:
                    var parsedTable = ParseTable(table, mainPart);
                    if (parsedTable != null)
                    {
                        elements.Add(parsedTable);
                    }

                    break;
                case SdtBlock sdt:
                    // Word's built-in galleries (e.g. "Page Numbers (Bottom of Page)") wrap the
                    // whole footer in a content control — unwrap it, or the footer parses empty.
                    var content = sdt.SdtContentBlock;
                    if (content != null)
                    {
                        AppendHeaderFooterElements(content, mainPart, elements);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Drops page breaks synthesized from <c>w:lastRenderedPageBreak</c> hints when they land on
    /// a boundary the flow already breaks at — right after an explicit page break or a section
    /// break that starts a new page. Word writes the hint into the first run of each rendered
    /// page, so the marker that follows such a boundary records the SAME page start; honouring
    /// it manufactured a blank page Word never produced (business-plans/13: the cover's nextPage
    /// sectPr is immediately followed by the TOC page's hint). Floating/behind-text elements
    /// between them don't advance the flow, so they are looked past.
    /// </summary>
    static void RemoveRedundantPaginationHintBreaks(List<DocumentElement> elements)
    {
        for (var i = elements.Count - 1; i >= 0; i--)
        {
            if (elements[i] is not PageBreakElement {FromPaginationHint: true})
            {
                continue;
            }

            for (var j = i - 1; j >= 0; j--)
            {
                var previous = elements[j];
                if (previous is FloatingImageElement or FloatingShapeElement or FloatingTextBoxElement or FloatingWordArtElement)
                {
                    continue;
                }

                if (previous is PageBreakElement
                    or SectionBreakElement {BreakType: SectionBreakType.NextPage or SectionBreakType.EvenPage or SectionBreakType.OddPage})
                {
                    elements.RemoveAt(i);
                }

                break;
            }
        }
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

        RemoveRedundantPaginationHintBreaks(elements);

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
        var rowList = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().ToList();
        var totalRows = rowList.Count;

        // Look up table style border info (including conditional formatting) early,
        // so we can apply per-cell conditional borders during cell parsing.
        // When the table has no explicit w:tblStyle reference, fall back to the styles part's
        // default table style (the one with w:default="true") — Word implicitly applies it.
        TableStyleBorderInfo? styleInfo = null;
        if (tableStyleBorders != null)
        {
            var styleId = tableProps?.GetFirstChild<TableStyle>()?.Val?.Value
                          ?? defaultTableStyleId;
            if (styleId != null)
            {
                tableStyleBorders.TryGetValue(styleId, out styleInfo);
            }
        }

        // Parse table grid (column widths)
        List<double>? gridColumnWidths = null;
        var tableGrid = table.GetFirstChild<TableGrid>();
        if (tableGrid != null)
        {
            gridColumnWidths = [];
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

        // w:tblW — explicit table preferred width. dxa is a fixed point value; pct says
        // "fill <pct>% of container"; auto/missing means "fit to content".
        double? preferredWidthPoints = null;
        var fillContainer = false;
        var tblWidthEl = tableProps?.GetFirstChild<TableWidth>();
        if (tblWidthEl?.Type?.Value == TableWidthUnitValues.Dxa &&
            tblWidthEl.Width?.HasValue == true &&
            double.TryParse(tblWidthEl.Width.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tblWTwips) &&
            tblWTwips > 0)
        {
            preferredWidthPoints = tblWTwips / twipsPerPoint;
        }
        else if (tblWidthEl?.Type?.Value == TableWidthUnitValues.Pct &&
                 tblWidthEl.Width?.HasValue == true &&
                 double.TryParse(tblWidthEl.Width.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tblWPct) &&
                 tblWPct > 0)
        {
            fillContainer = true;
        }

        // Parse table-level default cell margins and floating table positioning
        CellSpacing? defaultCellMargin = null;
        CellSpacing? defaultCellPadding = null;
        var isFloating = false;
        double floatingYOffsetPoints = 0;
        double floatingXOffsetPoints = 0;
        var floatingVAnchor = FloatingTableVerticalAnchor.Text;
        var floatingHAnchor = FloatingTableHorizontalAnchor.Text;
        if (tableProps != null)
        {
            var tblCellMar = tableProps.GetFirstChild<TableCellMarginDefault>();
            if (tblCellMar != null)
            {
                defaultCellPadding = ParseTableCellMargin(tblCellMar);
            }

            // Check for floating table positioning (tblpPr).
            var tblpPr = tableProps.GetFirstChild<TablePositionProperties>();
            isFloating = tblpPr != null;
            if (tblpPr?.TablePositionY?.HasValue == true)
            {
                floatingYOffsetPoints = tblpPr.TablePositionY.Value / twipsPerPoint;
            }
            if (tblpPr?.TablePositionX?.HasValue == true)
            {
                floatingXOffsetPoints = tblpPr.TablePositionX.Value / twipsPerPoint;
            }
            if (tblpPr?.VerticalAnchor?.Value is { } vAnch)
            {
                floatingVAnchor = vAnch == VerticalAnchorValues.Page
                    ? FloatingTableVerticalAnchor.Page
                    : vAnch == VerticalAnchorValues.Margin
                        ? FloatingTableVerticalAnchor.Margin
                        : FloatingTableVerticalAnchor.Text;
            }
            if (tblpPr?.HorizontalAnchor?.Value is { } hAnch)
            {
                floatingHAnchor = hAnch == HorizontalAnchorValues.Page
                    ? FloatingTableHorizontalAnchor.Page
                    : hAnch == HorizontalAnchorValues.Margin
                        ? FloatingTableHorizontalAnchor.Margin
                        : FloatingTableHorizontalAnchor.Text;
            }
        }

        // tblLook gates which conditional flags can be derived from cell position when the
        // document doesn't carry explicit w:cnfStyle.
        var tableLookMask = ParseTableLookMask(tableProps?.GetFirstChild<TableLook>());

        // Total grid columns — used for resolving lastColumn cnfStyle. Falls back to the
        // first row's gridSpan-aware cell count when no explicit grid is supplied.
        var totalCols = gridColumnWidths?.Count ?? 0;
        if (totalCols == 0 && rowList.Count > 0)
        {
            foreach (var cell in rowList[0].Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
            {
                var span = cell.GetFirstChild<OoxmlTableCellProperties>()?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1;
                totalCols += span;
            }
        }

        for (var rowIndex = 0; rowIndex < rowList.Count; rowIndex++)
        {
            var row = rowList[rowIndex];
            var cells = new List<TableCell>();
            var gridColIndex = 0;
            var rowFlags = ParseConditionalFormatFlags(row.GetFirstChild<TableRowProperties>()?.GetFirstChild<ConditionalFormatStyle>());

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
                                if (nested.Properties.IsFloating)
                                {
                                    // Lift floating tables to body level so they participate in
                                    // page-flow / pagination at the document level rather than
                                    // being rendered inside a cell that may clip or paginate.
                                    (pendingLiftedFloatingTables ??= []).Add(nested);
                                }
                                else
                                {
                                    cellContent.Add(nested);
                                }
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
                                        if (nestedSdt.Properties.IsFloating)
                                        {
                                            (pendingLiftedFloatingTables ??= []).Add(nestedSdt);
                                        }
                                        else
                                        {
                                            cellContent.Add(nestedSdt);
                                        }
                                    }
                                }
                            }

                            break;
                    }
                }

                // Get cell properties
                double? width = null;
                double? widthFraction = null;
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
                    else if (cellWidth?.Width?.HasValue == true &&
                             cellWidth.Type?.Value == TableWidthUnitValues.Pct &&
                             double.TryParse(cellWidth.Width.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tcPct) &&
                             tcPct > 0)
                    {
                        // w:tcW pct is in fiftieths of a percent (5000 = 100% of table width).
                        widthFraction = tcPct / 5000.0;
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

                // Parse cell-level borders. Side-borders only get materialised when at
                // least one of the four side children appears, so a cell that specifies
                // only diagonals doesn't lose its table-level side cascade.
                CellBorders? cellBorders = null;
                CellDiagonals? cellDiagonals = null;
                var tcBorders = cellProps?.GetFirstChild<TableCellBorders>();
                if (tcBorders != null)
                {
                    var topChild = tcBorders.GetFirstChild<TopBorder>();
                    var rightChild = tcBorders.GetFirstChild<RightBorder>();
                    var bottomChild = tcBorders.GetFirstChild<BottomBorder>();
                    var leftChild = tcBorders.GetFirstChild<LeftBorder>();
                    if (topChild != null || rightChild != null || bottomChild != null || leftChild != null)
                    {
                        cellBorders = new()
                        {
                            Top = ParseBorderEdge(topChild),
                            Right = ParseBorderEdge(rightChild),
                            Bottom = ParseBorderEdge(bottomChild),
                            Left = ParseBorderEdge(leftChild)
                        };
                    }

                    var tl2br = tcBorders.GetFirstChild<TopLeftToBottomRightCellBorder>();
                    var tr2bl = tcBorders.GetFirstChild<TopRightToBottomLeftCellBorder>();
                    if (tl2br != null || tr2bl != null)
                    {
                        cellDiagonals = new()
                        {
                            Down = ParseBorderEdge(tl2br),
                            Up = ParseBorderEdge(tr2bl)
                        };
                    }
                }

                // Parse grid span (number of columns this cell spans)
                var gridSpan = 1;
                var gridSpanElement = cellProps?.GetFirstChild<GridSpan>();
                if (gridSpanElement?.Val?.HasValue == true)
                {
                    gridSpan = gridSpanElement.Val.Value;
                }

                // Parse vertical alignment (w:vAlign). Direct cell w:vAlign wins; otherwise
                // fall back to the table style's whole-table tcPr (e.g. FormTable's vAlign="center").
                var verticalAlign = styleInfo?.VerticalAlignment ?? CellVerticalAlignment.Top;
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
                    else
                    {
                        verticalAlign = CellVerticalAlignment.Top;
                    }
                }

                // Parse text direction (w:textDirection)
                var textDirection = CellTextDirection.LeftToRight;
                var textDirElement = cellProps?.GetFirstChild<TextDirection>();
                if (textDirElement?.Val?.HasValue == true)
                {
                    var dirVal = textDirElement.Val.Value;
                    if (dirVal == TextDirectionValues.BottomToTopLeftToRight)
                    {
                        textDirection = CellTextDirection.BottomToTop;
                    }
                    else if (dirVal == TextDirectionValues.TopToBottomRightToLeft)
                    {
                        textDirection = CellTextDirection.TopToBottom;
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

                // Apply conditional table-style formatting (w:cnfStyle / w:tblStylePr cascade).
                // Cell- and row-level explicit overrides (w:tcBorders / w:shd on the cell) still
                // win — they're applied above by leaving cellBorders / bgColor non-null.
                //
                // Word distributes cnfStyle across both row and cell: the row carries row-spanning
                // flags (firstRow / lastRow / oddHBand / evenHBand) and the cell carries
                // cell-spanning flags (firstColumn / lastColumn / corner cells). ORing the two is
                // exactly what Word's renderer does — the cell at (0,0) of a styled table ends up
                // with firstRow ∪ firstColumn ∪ nwCell, all from a single OR.
                if (styleInfo != null)
                {
                    var cellFlags = ParseConditionalFormatFlags(cellProps?.GetFirstChild<ConditionalFormatStyle>());
                    var effectiveFlags = cellFlags | rowFlags;

                    var conditionalBorders = (CellBorders?) null;
                    var conditionalShading = styleInfo.BackgroundColorHex;
                    string? conditionalRunColor = null;

                    if (styleInfo.Conditionals != null)
                    {
                        foreach (var condType in ResolveActiveConditions(
                                     effectiveFlags, rowIndex, gridColIndex, totalRows, totalCols,
                                     styleInfo.RowBandSize, styleInfo.ColBandSize, tableLookMask))
                        {
                            if (!styleInfo.Conditionals.TryGetValue(condType, out var cond))
                            {
                                continue;
                            }

                            if (cond.Borders != null)
                            {
                                conditionalBorders = cond.Borders;
                            }

                            if (cond.BackgroundColorHex != null)
                            {
                                conditionalShading = cond.BackgroundColorHex;
                            }

                            if (cond.RunColorHex != null)
                            {
                                conditionalRunColor = cond.RunColorHex;
                            }
                        }
                    }

                    cellBorders ??= conditionalBorders;
                    bgColor ??= conditionalShading;

                    // Cascade tblStylePr's run colour onto runs in this cell that don't carry
                    // an explicit w:color. Explicit run colour wins (RunProperties.ColorHex
                    // is non-null only when the run carried a w:color element).
                    if (conditionalRunColor != null)
                    {
                        ApplyDefaultRunColor(cellContent, conditionalRunColor);
                    }
                }

                // Word collapses the unavoidable empty end-of-cell paragraph mark that directly
                // follows a nested table to zero height.
                if (cellContent.Count >= 2 &&
                    cellContent[^1] is ParagraphElement {Runs.Count: 0, IsAnchorOnlyMark: false} endMark &&
                    cellContent[^2] is TableElement)
                {
                    cellContent[^1] = new ParagraphElement
                    {
                        Runs = endMark.Runs,
                        Properties = endMark.Properties,
                        IsCollapsedCellMark = true
                    };
                }

                cells.Add(
                    new()
                    {
                        Content = cellContent,
                        Properties = new()
                        {
                            WidthPoints = width,
                            WidthFraction = widthFraction,
                            BackgroundColorHex = bgColor,
                            Padding = cellPadding,
                            Margin = cellMargin,
                            Borders = cellBorders,
                            Diagonals = cellDiagonals,
                            HideMark = cellProps?.GetFirstChild<HideMark>() != null,
                            NoWrap = cellProps?.GetFirstChild<NoWrap>() != null,
                            GridSpan = gridSpan,
                            VerticalAlignment = verticalAlign,
                            VerticalMerge = verticalMerge,
                            TextDirection = textDirection
                        }
                    });

                gridColIndex += gridSpan;
            }

            // Parse row properties for height
            double? rowHeight = null;
            var isExactHeight = false;
            var isHeader = false;
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

                var headerElement = rowProps.GetFirstChild<TableHeader>();
                if (headerElement != null)
                {
                    isHeader = headerElement.Val?.Value != OnOffOnlyValues.Off;
                }
            }

            // Parse w:tblPrEx — row-level overrides for table-wide properties (borders, cell margins).
            CellBorders? rowOverrideBorders = null;
            BorderEdge? rowOverrideInsideH = null;
            BorderEdge? rowOverrideInsideV = null;
            CellSpacing? rowOverrideCellPadding = null;

            var tblPrEx = row.GetFirstChild<TablePropertyExceptions>();
            if (tblPrEx != null)
            {
                var exBorders = tblPrEx.GetFirstChild<TableBorders>();
                if (exBorders != null)
                {
                    rowOverrideBorders = new()
                    {
                        Top = ParseBorderEdge(exBorders.GetFirstChild<TopBorder>()),
                        Right = ParseBorderEdge(exBorders.GetFirstChild<RightBorder>()),
                        Bottom = ParseBorderEdge(exBorders.GetFirstChild<BottomBorder>()),
                        Left = ParseBorderEdge(exBorders.GetFirstChild<LeftBorder>())
                    };

                    var insideH = ParseBorderEdge(exBorders.GetFirstChild<InsideHorizontalBorder>());
                    if (insideH.IsVisible)
                    {
                        rowOverrideInsideH = insideH;
                    }

                    var insideV = ParseBorderEdge(exBorders.GetFirstChild<InsideVerticalBorder>());
                    if (insideV.IsVisible)
                    {
                        rowOverrideInsideV = insideV;
                    }
                }

                var exCellMar = tblPrEx.GetFirstChild<TableCellMarginDefault>();
                if (exCellMar != null)
                {
                    rowOverrideCellPadding = ParseTableCellMargin(exCellMar);
                }
            }

            rows.Add(
                new()
                {
                    Cells = cells,
                    HeightPoints = rowHeight,
                    IsExactHeight = isExactHeight,
                    IsHeader = isHeader,
                    OverrideBorders = rowOverrideBorders,
                    OverrideInsideHBorder = rowOverrideInsideH,
                    OverrideInsideVBorder = rowOverrideInsideV,
                    OverrideCellPadding = rowOverrideCellPadding
                });
        }

        if (rows.Count == 0)
        {
            return null;
        }

        // Parse table properties
        CellBorders? defaultBorders = null;
        BorderEdge? insideHBorder = null;
        BorderEdge? insideVBorder = null;
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

                var insideH = ParseBorderEdge(borders.GetFirstChild<InsideHorizontalBorder>());
                var insideV = ParseBorderEdge(borders.GetFirstChild<InsideVerticalBorder>());

                if (insideH.IsVisible)
                {
                    insideHBorder = insideH;
                }

                if (insideV.IsVisible)
                {
                    insideVBorder = insideV;
                }
            }

            // If no inline borders, try to get borders from the table style
            if (defaultBorders == null && insideHBorder == null && insideVBorder == null && styleInfo != null)
            {
                defaultBorders = styleInfo.Outer;
                if (styleInfo.InsideH.IsVisible)
                {
                    insideHBorder = styleInfo.InsideH;
                }

                if (styleInfo.InsideV.IsVisible)
                {
                    insideVBorder = styleInfo.InsideV;
                }
            }

            // Parse table indent
            var tblInd = tableProps.GetFirstChild<TableIndentation>();
            if (tblInd?.Width?.HasValue == true)
            {
                indentPoints = tblInd.Width.Value / twipsPerPoint;
            }
        }

        // Parse w:tblCellSpacing — non-zero switches the table to the detached-border model.
        // Falls back to the table style's tblCellSpacing when the document doesn't override.
        var cellSpacingPoints = ReadTableCellSpacing(tableProps);
        if (cellSpacingPoints == 0)
        {
            cellSpacingPoints = styleInfo?.CellSpacingPoints ?? 0;
        }

        // Parse table layout type (w:tblPr/w:tblLayout/@type)
        var isAutoFit = true;
        var tableLayoutEl = tableProps?.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableLayout>();
        if (tableLayoutEl?.Type?.Value is { } layoutType)
        {
            isAutoFit = layoutType != TableLayoutValues.Fixed;
        }

        // Parse table-level horizontal alignment (w:tblPr/w:jc)
        var alignment = TextAlignment.Left;
        var tableJustification = tableProps?.GetFirstChild<TableJustification>();
        if (tableJustification?.Val?.HasValue == true)
        {
            var jcVal = tableJustification.Val.Value;
            if (jcVal == TableRowAlignmentValues.Center)
            {
                alignment = TextAlignment.Center;
            }
            else if (jcVal == TableRowAlignmentValues.Right)
            {
                alignment = TextAlignment.Right;
            }
        }
        // Floating tables can carry their horizontal alignment in tblpXSpec instead of w:jc.
        // We don't honour the absolute X coordinate (tblpX) — the inline fallback re-uses
        // the alignment so the table at least lands in the right column.
        else if (tableProps?.GetFirstChild<TablePositionProperties>()?.TablePositionXAlignment?.Value is { } xAlign)
        {
            if (xAlign == HorizontalAlignmentValues.Center) alignment = TextAlignment.Center;
            else if (xAlign == HorizontalAlignmentValues.Right) alignment = TextAlignment.Right;
        }

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                IsFloating = isFloating,
                FloatingYOffsetPoints = floatingYOffsetPoints,
                FloatingXOffsetPoints = floatingXOffsetPoints,
                FloatingVerticalAnchor = floatingVAnchor,
                FloatingHorizontalAnchor = floatingHAnchor,
                DefaultBorders = defaultBorders,
                InsideHorizontalBorder = insideHBorder,
                InsideVerticalBorder = insideVBorder,
                // For tables without an explicit w:tblCellMar and no inherited style padding,
                // vertical padding is 0, per the OOXML spec AND per Word's real render: an
                // unstyled row measures exactly font-line + spacing-after (simple_table row
                // pitch 52.02px at 150 DPI = 12pt Aptos line 16.97pt + 8pt after — no
                // breathing room). An earlier 2pt top/bottom fudge here compensated for the
                // built-in Normal line height being too small (11pt x 1.08 instead of Word's
                // 12pt x 278/240); with that fixed, the fudge overshot every unstyled row.
                DefaultCellPadding = defaultCellPadding ?? styleInfo?.DefaultCellPadding ??
                    new CellSpacing(0),
                DefaultCellMargin = defaultCellMargin ?? new CellSpacing(0),
                IndentPoints = indentPoints,
                GridColumnWidths = gridColumnWidths,
                PreferredWidthPoints = preferredWidthPoints,
                FillContainer = fillContainer,
                Alignment = alignment,
                IsAutoFit = isAutoFit,
                CellSpacingPoints = cellSpacingPoints
            }
        };
    }

    /// <summary>
    /// Reads <c>w:tblCellSpacing</c> from the given properties container (either
    /// <c>w:tblPr</c> or its style equivalent). Returns 0 when absent.
    /// </summary>
    static double ReadTableCellSpacing(OpenXmlElement? container)
    {
        var spacing = container?.GetFirstChild<TableCellSpacing>();
        if (spacing?.Width?.Value is not { } widthStr)
        {
            return 0;
        }

        if (!double.TryParse(widthStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips))
        {
            return 0;
        }

        // Only honour explicit dxa values. pct/auto/nil aren't modelled — treating them as 0
        // matches Word's behaviour for non-positive cell spacing (no detached-border switch).
        var type = spacing.Type?.Value;
        if (type != null && type != TableWidthUnitValues.Dxa)
        {
            return 0;
        }

        return twips / twipsPerPoint;
    }

    internal static CellSpacing? ParseTableCellMargin(TableCellMarginDefault margin)
    {
        double top = 0, right = 0, bottom = 0, left = 0;
        var hasAny = false;

        var topMargin = margin.TopMargin;
        if (topMargin?.Width?.HasValue == true)
        {
            top = TableWidthToPoints(topMargin.Width.Value!);
            hasAny = true;
        }

        if (margin.EndMargin?.Width?.HasValue == true)
        {
            right = TableWidthToPoints(margin.EndMargin.Width.Value!);
            hasAny = true;
        }
        else if (margin.TableCellRightMargin?.Width?.HasValue == true)
        {
            right = margin.TableCellRightMargin.Width.Value / twipsPerPoint;
            hasAny = true;
        }

        var bottomMargin = margin.BottomMargin;
        if (bottomMargin?.Width?.HasValue == true)
        {
            bottom = TableWidthToPoints(bottomMargin.Width.Value!);
            hasAny = true;
        }

        if (margin.StartMargin?.Width?.HasValue == true)
        {
            left = TableWidthToPoints(margin.StartMargin.Width.Value!);
            hasAny = true;
        }
        else if (margin.TableCellLeftMargin?.Width?.HasValue == true)
        {
            left = margin.TableCellLeftMargin.Width.Value / twipsPerPoint;
            hasAny = true;
        }

        return hasAny ? new CellSpacing(top, right, bottom, left) : null;
    }

    // Parses an OOXML table-width measure (CT_TblWidth/@w:w, type ST_MeasurementOrPercent) to points.
    // Accepts a bare number in twips (the dxa form Word normally writes) or an ST_UniversalMeasure
    // value carrying an explicit unit (e.g. "0pt", "1.5cm") — Aspose emits the latter for cell
    // margins. Percent and unparseable values yield 0.
    static double TableWidthToPoints(string value)
    {
        var span = value.AsSpan().Trim();
        if (span.IsEmpty || span[^1] == '%')
        {
            return 0;
        }

        if (span.Length > 2 && char.IsAsciiLetter(span[^1]) && char.IsAsciiLetter(span[^2]))
        {
            if (!double.TryParse(span[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var measure))
            {
                return 0;
            }

            return span[^2..].ToString().ToLowerInvariant() switch
            {
                "pt" => measure,
                "pc" or "pi" => measure * 12,
                "in" => measure * 72,
                "cm" => measure * 72 / 2.54,
                "mm" => measure * 72 / 25.4,
                _ => 0
            };
        }

        if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips))
        {
            return twips / twipsPerPoint;
        }

        return 0;
    }

    internal static CellSpacing? ParseCellMargin(TableCellMargin margin)
    {
        double top = 0, right = 0, bottom = 0, left = 0;
        var hasAny = false;

        var topMargin = margin.TopMargin;
        if (topMargin?.Width?.HasValue == true)
        {
            top = TableWidthToPoints(topMargin.Width.Value!);
            hasAny = true;
        }

        if (margin.EndMargin?.Width?.HasValue == true)
        {
            right = TableWidthToPoints(margin.EndMargin.Width.Value!);
            hasAny = true;
        }
        else if (margin.RightMargin?.Width?.HasValue == true)
        {
            right = TableWidthToPoints(margin.RightMargin.Width.Value!);
            hasAny = true;
        }

        var bottomMargin = margin.BottomMargin;
        if (bottomMargin?.Width?.HasValue == true)
        {
            bottom = TableWidthToPoints(bottomMargin.Width.Value!);
            hasAny = true;
        }

        if (margin.StartMargin?.Width?.HasValue == true)
        {
            left = TableWidthToPoints(margin.StartMargin.Width.Value!);
            hasAny = true;
        }
        else if (margin.LeftMargin?.Width?.HasValue == true)
        {
            left = TableWidthToPoints(margin.LeftMargin.Width.Value!);
            hasAny = true;
        }

        return hasAny ? new CellSpacing(top, right, bottom, left) : null;
    }

    static double ParseBorderSpace(BorderType? border) =>
        border?.Space?.HasValue == true ? border.Space.Value : 0;

    /// <summary>
    /// Cascades a tblStylePr-derived run colour onto the runs in a freshly-parsed cell.
    /// Only fills in runs that don't carry an explicit <c>w:color</c> — explicit run colour
    /// always wins. Nested tables are skipped because they run their own conditional cascade.
    /// </summary>
    static void ApplyDefaultRunColor(List<DocumentElement> cellContent, string color)
    {
        for (var i = 0; i < cellContent.Count; i++)
        {
            if (cellContent[i] is ParagraphElement paragraph)
            {
                cellContent[i] = ApplyDefaultRunColorToParagraph(paragraph, color);
            }
        }
    }

    static ParagraphElement ApplyDefaultRunColorToParagraph(ParagraphElement paragraph, string color)
    {
        Run[]? rebuilt = null;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            if (run.Properties.ColorHex != null)
            {
                continue;
            }

            rebuilt ??= [.. paragraph.Runs];
            // WithProperties preserves every member — the previous manual copy silently dropped
            // hyperlink targets, footnote/endnote ids and page-field markers from recoloured runs.
            rebuilt[i] = run.WithProperties(run.Properties with {ColorHex = color});
        }

        if (rebuilt == null)
        {
            return paragraph;
        }

        return new()
        {
            Runs = rebuilt,
            Properties = paragraph.Properties
        };
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
            color = currentThemeColors.ResolveColor(themeColorValue);
        }

        return new()
        {
            IsVisible = true,
            WidthPoints = width,
            ColorHex = color,
            Style = MapBorderStyle(border.Val?.Value)
        };
    }

    static BorderLineStyle MapBorderStyle(BorderValues? val)
    {
        if (val == null) return BorderLineStyle.Single;
        if (val == BorderValues.Double) return BorderLineStyle.Double;
        if (val == BorderValues.Dotted) return BorderLineStyle.Dotted;
        if (val == BorderValues.Dashed || val == BorderValues.DashSmallGap ||
            val == BorderValues.DashDotStroked || val == BorderValues.DotDash ||
            val == BorderValues.DotDotDash) return BorderLineStyle.Dashed;
        return BorderLineStyle.Single;
    }

    // Per-field parse state for the PAGE/NUMPAGES/SECTIONPAGES tracker (see HandleFieldControlRun
    // in ParseParagraph). A stack of these follows the fldChar begin…separate…end nesting so a
    // page field wrapped in another field still resolves.
    sealed class FieldParseState
    {
        public readonly StringBuilder Instruction = new();
        public bool CollectingResult;
        public PageFieldKind Kind;
        public string? NumberFormat;
        public readonly StringBuilder ResultText = new();
        public RunProperties? ResultProperties;
        public RunProperties? SeparateProperties;
    }

    /// <summary>
    /// Classifies a field instruction as a page-numbering field and extracts its <c>\*</c> numeric
    /// format switch. Returns <see cref="PageFieldKind.None"/> for everything else.
    /// </summary>
    static (PageFieldKind Kind, string? NumberFormat) ClassifyPageField(string? instruction)
    {
        if (instruction == null)
        {
            return (PageFieldKind.None, null);
        }

        var span = instruction.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return (PageFieldKind.None, null);
        }

        var space = span.IndexOfAny(' ', '\t');
        var keyword = (space < 0 ? span : span[..space]).ToString().ToUpperInvariant();
        var kind = keyword switch
        {
            "PAGE" => PageFieldKind.Page,
            "NUMPAGES" => PageFieldKind.NumberOfPages,
            "SECTIONPAGES" => PageFieldKind.SectionPages,
            _ => PageFieldKind.None
        };

        if (kind == PageFieldKind.None)
        {
            return (PageFieldKind.None, null);
        }

        // A "\* <format>" switch overrides the default decimal rendering. MERGEFORMAT / CHARFORMAT
        // are formatting-preservation switches, not numeric formats, so they leave the default.
        string? numberFormat = null;
        var star = instruction.IndexOf("\\*", StringComparison.Ordinal);
        if (star >= 0)
        {
            var rest = instruction.AsSpan(star + 2).TrimStart();
            var end = rest.IndexOfAny(' ', '\t');
            var token = (end < 0 ? rest : rest[..end]).ToString();
            if (token is "roman" or "Roman" or "ROMAN"
                or "alphabetic" or "Alphabetic" or "ALPHABETIC"
                or "arabic" or "Arabic" or "ARABIC")
            {
                numberFormat = token;
            }
        }

        return (kind, numberFormat);
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
        var inScopedPart = para.Ancestors<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Any()
            || para.Ancestors<DocumentFormat.OpenXml.Wordprocessing.Header>().Any()
            || para.Ancestors<DocumentFormat.OpenXml.Wordprocessing.Footer>().Any();
        var props = ParseParagraphProperties(paraProps, mainPart, paragraphStyleId, omitParagraphMark: inScopedPart);

        // --- PAGE / NUMPAGES / SECTIONPAGES field tracking ---
        // Word emits these as complex fields: fldChar begin → instrText → fldChar separate →
        // cached-result run(s) → fldChar end. The cached result is a fixed string (wrong on every
        // page but the one Word computed it for), so we collapse each page field's result into a
        // single Run tagged with its PageFieldKind; the renderers substitute the live value. The
        // tracker is shared by the run loops below (plain runs and inline SDT content).
        var fieldStack = new Stack<FieldParseState>();

        // Handles a field-control run (fldChar begin/separate/end or instrText). Returns true when
        // the run was consumed as field plumbing (it carries no visible text). On a page field's
        // fldChar end it appends the single tagged result run to <paramref name="runs"/>.
        bool HandleFieldControlRun(OoxmlRun controlRun)
        {
            var fieldChar = controlRun.GetFirstChild<FieldChar>();
            if (fieldChar != null)
            {
                var type = fieldChar.FieldCharType?.Value;
                if (type == FieldCharValues.Begin)
                {
                    fieldStack.Push(new());
                }
                else if (type == FieldCharValues.Separate && fieldStack.Count > 0)
                {
                    var state = fieldStack.Peek();
                    (state.Kind, state.NumberFormat) = ClassifyPageField(state.Instruction.ToString());
                    if (state.Kind != PageFieldKind.None)
                    {
                        state.CollectingResult = true;
                        state.SeparateProperties = ParseRunProperties(controlRun.RunProperties, mainPart, paragraphStyleId);
                    }
                }
                else if (type == FieldCharValues.End && fieldStack.Count > 0)
                {
                    var state = fieldStack.Pop();
                    if (state.Kind != PageFieldKind.None)
                    {
                        runs.Add(
                            new()
                            {
                                Text = state.ResultText.ToString(),
                                Properties = state.ResultProperties
                                    ?? state.SeparateProperties
                                    ?? ParseRunProperties(null, mainPart, paragraphStyleId),
                                PageField = state.Kind,
                                PageFieldNumberFormat = state.NumberFormat
                            });

                        if (state.Kind is PageFieldKind.NumberOfPages or PageFieldKind.SectionPages)
                        {
                            requiresTotalPageCount = true;
                        }
                    }
                }

                return true;
            }

            var instruction = controlRun.GetFirstChild<OoxmlFieldCode>();
            if (instruction != null && fieldStack.Count > 0)
            {
                fieldStack.Peek().Instruction.Append(instruction.Text);
                return true;
            }

            return false;
        }

        // While a page field is between its separate and end, capture the cached result text (kept
        // for the exporters) and its formatting, and report that the run must not be emitted here —
        // HandleFieldControlRun emits one tagged run at the field's end instead.
        bool CaptureSuppressedFieldResult(OoxmlRun resultRun)
        {
            if (fieldStack.Count == 0 || !fieldStack.Peek().CollectingResult)
            {
                return false;
            }

            var state = fieldStack.Peek();
            foreach (var text in resultRun.Descendants<Text>())
            {
                state.ResultText.Append(text.Text);
            }

            state.ResultProperties ??= ParseRunProperties(resultRun.RunProperties, mainPart, paragraphStyleId);
            return true;
        }

        // Check for numbering (direct on paragraph or from style)
        var numberingInfo = GetNumberingInfo(paraProps, paragraphStyleId);
        if (numberingInfo != null)
        {
            // OOXML indent cascade for a numbered paragraph. Each component (left / hanging)
            // resolves independently — a layer may set only one of them and inherit the rest —
            // and the numbering level's <w:ind> sits at a different layer depending on how the
            // numbering was attached:
            //   - numPr directly on the paragraph: the level's indent behaves like direct
            //     formatting and overrides the style's (dot_points: ListParagraph's flat 720
            //     loses to the level's 720..4320 progression, matching Word).
            //   - numPr inherited from the style: the style definition is self-consistent —
            //     its own <w:ind> deliberately overrides the level it references
            //     (agendas-minutes/17: the Bullets style tightens the level's 1800 to 360).
            // Direct paragraph <w:ind> wins over both.
            ParagraphProperties? styleDefaults = null;
            if (paragraphStyleId != null && styleParagraphProperties != null)
            {
                styleParagraphProperties.TryGetValue(paragraphStyleId, out styleDefaults);
            }

            var directInd = paraProps?.GetFirstChild<Indentation>();
            var directHasLeft = directInd?.Left?.HasValue == true;
            var directHasHanging = directInd?.Hanging?.HasValue == true;

            // Within this block the numbering resolved, so a present direct numPr is what
            // supplied it (a direct numPr with numId 0 removes numbering entirely and never
            // reaches here).
            var numberingIsDirect = paraProps?.GetFirstChild<NumberingProperties>() != null;

            // A layer "has" the value when it's non-zero - we can't distinguish "explicitly 0"
            // from "default 0" without re-parsing, but in practice any zero indent means the
            // layer didn't set one and the next should fill in.
            var styleHasLeft = (styleDefaults?.LeftIndentPoints ?? 0) > 0;
            var styleHasHanging = (styleDefaults?.HangingIndentPoints ?? 0) > 0;
            var numberingHasLeft = numberingInfo.IndentPoints > 0;
            var numberingHasHanging = numberingInfo.HangingIndentPoints > 0;

            double leftIndent;
            double hangingIndent;
            if (numberingIsDirect)
            {
                leftIndent = directHasLeft ? props.LeftIndentPoints
                    : numberingHasLeft ? numberingInfo.IndentPoints
                    : styleDefaults?.LeftIndentPoints ?? 0;
                hangingIndent = directHasHanging ? props.HangingIndentPoints
                    : numberingHasHanging ? numberingInfo.HangingIndentPoints
                    : styleDefaults?.HangingIndentPoints ?? 0;
            }
            else
            {
                leftIndent = directHasLeft ? props.LeftIndentPoints
                    : styleHasLeft ? styleDefaults!.LeftIndentPoints
                    : numberingInfo.IndentPoints;
                hangingIndent = directHasHanging ? props.HangingIndentPoints
                    : styleHasHanging ? styleDefaults!.HangingIndentPoints
                    : numberingInfo.HangingIndentPoints;
            }

            props = props with
            {
                Numbering = numberingInfo,
                LeftIndentPoints = leftIndent,
                HangingIndentPoints = hangingIndent
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
                            // Page-number fields (Word wraps them in a "Page Numbers" content
                            // control) resolve the same way as in the plain run loop.
                            if (HandleFieldControlRun(sdtChildRun) || CaptureSuppressedFieldResult(sdtChildRun))
                            {
                                continue;
                            }

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

                                // Line break - add newline character. Don't `continue` — the
                                // run may also have <w:t> text after the break (e.g.
                                // <w:r><w:br/><w:t>Sharma</w:t></w:r>) and we still need to
                                // parse it so neither half of the run is dropped.
                                var runProps = ParseRunProperties(sdtChildRun.RunProperties, mainPart);
                                runs.Add(
                                    new()
                                    {
                                        Text = "\n",
                                        Properties = runProps
                                    });
                            }

                            // Check for drawings (images/icons) within the SdtRun child.
                            // Collected once — the drawing loop and the text-content guard
                            // below used to walk the same subtree twice.
                            List<Drawing>? sdtRunDrawings = null;
                            foreach (var drawing in sdtChildRun.Descendants<Drawing>())
                            {
                                (sdtRunDrawings ??= []).Add(drawing);
                            }

                            foreach (var drawing in sdtRunDrawings ?? (IReadOnlyList<Drawing>) [])
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
                            if (sdtRunDrawings == null)
                            {
                                runs.AddRange(ParseRun(sdtChildRun, mainPart, paragraphStyleId));
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
                            runs.Add(
                                new()
                                {
                                    Text = "\n",
                                    Properties = runProps
                                });
                            continue;
                        }

                        runs.AddRange(ParseRun(sdtCellRun, mainPart, paragraphStyleId));
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
                                    result.Add(
                                        new ParagraphElement
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
                            runs.Add(
                                new()
                                {
                                    Text = "\n",
                                    Properties = runProps
                                });
                            continue;
                        }

                        runs.AddRange(ParseRun(sdtBlockRun, mainPart, paragraphStyleId));
                    }

                    break;

                case SimpleField simpleField:
                    // w:fldSimple wraps the cached result runs of a legacy field. A PAGE/NUMPAGES/
                    // SECTIONPAGES field collapses to one tagged run (cached text kept for the
                    // exporters; the renderers substitute the live value); anything else renders
                    // its cached result inline. The instruction is also captured on FieldCodes.
                    var (simpleKind, simpleFormat) = ClassifyPageField(simpleField.Instruction?.Value);
                    if (simpleKind != PageFieldKind.None)
                    {
                        var firstResultRun = simpleField.Descendants<OoxmlRun>().FirstOrDefault();
                        var cachedText = string.Concat(simpleField.Descendants<Text>().Select(_ => _.Text));
                        runs.Add(
                            new()
                            {
                                Text = cachedText,
                                Properties = ParseRunProperties(firstResultRun?.RunProperties, mainPart, paragraphStyleId),
                                PageField = simpleKind,
                                PageFieldNumberFormat = simpleFormat
                            });

                        if (simpleKind is PageFieldKind.NumberOfPages or PageFieldKind.SectionPages)
                        {
                            requiresTotalPageCount = true;
                        }
                    }
                    else
                    {
                        foreach (var fieldRun in simpleField.Descendants<OoxmlRun>())
                        {
                            runs.AddRange(ParseRun(fieldRun, mainPart, paragraphStyleId));
                        }
                    }

                    break;

                case Hyperlink hyperlink:
                    // w:hyperlink wraps runs that point at an external URL or internal anchor. The
                    // visible content is the inner runs; the link target is captured on each run so
                    // the HTML/Markdown exporters can emit links (raster rendering ignores it).
                    var hyperlinkUrl = ResolveHyperlinkUrl(hyperlink, mainPart);
                    foreach (var hlRun in hyperlink.Elements<OoxmlRun>())
                    {
                        runs.AddRange(ParseRun(hlRun, mainPart, paragraphStyleId, hyperlinkUrl));
                    }

                    break;

                case InsertedRun insertedRun:
                    // Tracked-change insertion: render the inserted runs inline ("as accepted").
                    // The revision metadata is captured separately on ParsedDocument.TrackedChanges.
                    foreach (var insRun in insertedRun.Elements<OoxmlRun>())
                    {
                        runs.AddRange(ParseRun(insRun, mainPart, paragraphStyleId));
                    }

                    break;

                case DeletedRun:
                    // Tracked-change deletion: drop the runs ("as accepted" = remove deleted text).
                    break;

                case DocumentFormat.OpenXml.Math.OfficeMath inlineMath:
                    // Office Math (OMML) — render the textual content of m:t descendants as a fallback.
                    // Proper math layout (radicals, fractions, sub/superscripts, big operators) is
                    // not modelled, so symbols and structure are lost; equations like "a²+b²=c²"
                    // come through as "a2+b2=c2" but at least don't disappear from the page.
                    AppendMathText(runs, inlineMath);
                    break;

                case DocumentFormat.OpenXml.Math.Paragraph mathPara:
                    AppendMathText(runs, mathPara);

                    // m:oMathPara is display math: Word centres the equation block — OMML's
                    // m:oMathParaPr/m:jc defaults to centerGroup. A w:jc on the paragraph itself
                    // still wins; an explicit m:jc left/right is honoured.
                    if (paraProps?.GetFirstChild<Justification>() == null)
                    {
                        var mathJustification = mathPara.ParagraphProperties?
                            .GetFirstChild<DocumentFormat.OpenXml.Math.Justification>()?.Val?.Value;
                        var mathAlignment = TextAlignment.Center;
                        if (mathJustification != null)
                        {
                            if (mathJustification == DocumentFormat.OpenXml.Math.JustificationValues.Left)
                            {
                                mathAlignment = TextAlignment.Left;
                            }
                            else if (mathJustification == DocumentFormat.OpenXml.Math.JustificationValues.Right)
                            {
                                mathAlignment = TextAlignment.Right;
                            }
                        }

                        props = props with {Alignment = mathAlignment};
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

                    // PAGE/NUMPAGES/SECTIONPAGES field plumbing (fldChar/instrText) carries no text
                    // and is consumed here; the field's cached result is captured and re-emitted as
                    // a single tagged run at the field's end (see the tracker above).
                    if (HandleFieldControlRun(run) || CaptureSuppressedFieldResult(run))
                    {
                        break;
                    }

                    // One walk over the run's subtree collects everything the branches below
                    // need — this block used to re-traverse it three times (drawings loop,
                    // drawing-presence check, break scan) for every run in the document.
                    List<Drawing>? runDrawings = null;
                    var hasPageOrColumnBreak = false;
                    foreach (var descendant in run.Descendants())
                    {
                        if (descendant is Drawing descendantDrawing)
                        {
                            (runDrawings ??= []).Add(descendantDrawing);
                        }
                        else if (descendant is Break descendantBreak &&
                                 (descendantBreak.Type?.Value == BreakValues.Page || descendantBreak.Type?.Value == BreakValues.Column))
                        {
                            hasPageOrColumnBreak = true;
                        }
                    }

                    // Check for drawings (images/icons/WordArt/ink/shapes) within run
                    foreach (var drawing in runDrawings ?? (IReadOnlyList<Drawing>) [])
                    {
                        // Try background shapes first (solid fill behind text)
                        // May return multiple shapes when a WordprocessingGroup contains multiple non-decorative shapes
                        var shapeElements = ShapeParser.ParseBackgroundShapes(drawing, currentThemeColors, mainPart, props.SpacingBeforePoints, GetPartBytes);

                        // Check if there's a group - groups may contain text boxes and images even without shapes
                        var hasGroup = drawing.Descendants<WPG.WordprocessingGroup>().Any();

                        // Inline shape groups (no behindDoc anchor, just <wp:inline> with a wpg:wgp inside)
                        // need to flow with the surrounding text rather than render as floating block content.
                        // Skip the group-as-block branch for inline drawings so the inline-image path catches them.
                        var isInlineGroup = hasGroup && drawing.Descendants().Any(_ => _.LocalName == "inline");

                        if ((shapeElements.Count > 0 || hasGroup) && !isInlineGroup)
                        {
                            // Emit current paragraph content before the shapes/group content
                            if (runs.Count > 0)
                            {
                                result.Add(
                                    new ParagraphElement
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

                            // Parse text boxes and solid fill shapes inside the shapes/group (they contain the actual content).
                            // Skip any solid-fill FloatingShapeElement whose position/size matches one
                            // already emitted by ShapeParser above — both parsers traverse the same wgp
                            // group and would otherwise produce duplicates.
                            var shapesFromDrawing = ParseAllShapesFromDrawing(drawing, mainPart);
                            foreach (var shape in shapesFromDrawing)
                            {
                                if (shape is FloatingShapeElement fse &&
                                    shapeElements.Any(existing =>
                                        existing is FloatingShapeElement e &&
                                        Math.Abs(e.HorizontalPositionPoints - fse.HorizontalPositionPoints) < 0.5 &&
                                        Math.Abs(e.VerticalPositionPoints - fse.VerticalPositionPoints) < 0.5 &&
                                        Math.Abs(e.WidthPoints - fse.WidthPoints) < 0.5 &&
                                        Math.Abs(e.HeightPoints - fse.HeightPoints) < 0.5))
                                {
                                    continue;
                                }

                                result.Add(shape);
                            }

                            continue;
                        }

                        // Try ink first, then WordArt, then fall back to image
                        var inkElement = InkParser.ParseInk(drawing, mainPart);
                        if (inkElement != null)
                        {
                            // Emit current paragraph content before the ink
                            if (runs.Count > 0)
                            {
                                result.Add(
                                    new ParagraphElement
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
                                    result.Add(
                                        new ParagraphElement
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
                                    var isInline = drawing.Descendants().Any(_ => _.LocalName == "inline");

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
                            // Line breaks (no type or TextWrapping) are emitted in document order
                            // by ParseRun below (emitLineBreaks), so the text on both sides of the
                            // break is preserved — nothing to do here.
                        }
                        else if (lastRenderedPageBreakCount >= 20 &&
                                 runChild is LastRenderedPageBreak &&
                                 !run.Descendants<Text>().Any() &&
                                 !run.Descendants<Break>().Any())
                        {
                            // Word caches pagination using lastRenderedPageBreak. Only treat it as a page boundary hint
                            // when the document has lots of these markers (i.e., likely reflects full-document pagination).
                            if (result.LastOrDefault() is not PageBreakElement)
                            {
                                if (runs.Count > 0)
                                {
                                    result.Add(
                                        new ParagraphElement
                                        {
                                            Runs = new List<Run>(runs),
                                            Properties = props
                                        });
                                    runs.Clear();
                                }

                                result.Add(new PageBreakElement {FromPaginationHint = true});
                            }
                        }
                        else if (runChild is Text)
                        {
                            // Regular text - will be handled by ParseRun
                        }
                    }

                    // Parse the run normally (this handles text content, with line breaks emitted as
                    // "\n" runs in document order). Skip if the run only contains a drawing (no text).
                    if (runDrawings == null)
                    {
                        var parsedRuns = ParseRun(run, mainPart, paragraphStyleId, emitLineBreaks: true);
                        // A page/column break splits the paragraph above, so its run's content is
                        // intentionally not appended here; line-break-only (or break-free) runs are.
                        if (parsedRuns.Count > 0 && !hasPageOrColumnBreak)
                        {
                            runs.AddRange(parsedRuns);
                        }
                    }

                    break;
            }
        }

        // Behind-text decorative shapes are lifted out of the flow, so a paragraph that produced
        // only those is still an empty paragraph as far as line height goes — its mark takes a
        // line exactly as it would have if the drawing had produced nothing at all. Testing
        // `result.Count == 0` alone made that line appear or vanish depending on whether the
        // shape parser happened to understand the drawing.
        var onlyBehindTextShapes = result.Count > 0 &&
                                   result.All(_ => _ is FloatingShapeElement {BehindText: true});

        // Add remaining content
        if (runs.Count == 0 &&
            (result.Count == 0 || (onlyBehindTextShapes && props.SpacingAfterPoints <= 0)))
        {
            // Empty paragraph - still counts for spacing
            // Keep runs empty so the renderer can avoid creating spurious extra pages at document end.
            // A paragraph whose only purpose is carrying the sectPr is not a numbered line in Word —
            // it keeps its height but must neither draw nor consume a line number.
            result.Add(new ParagraphElement
            {
                Runs = [],
                Properties = sectionBreak != null
                    ? props with {SuppressLineNumbers = true}
                    : props
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
        else if (props.SpacingAfterPoints > 0 &&
                 result.All(_ => _ is FloatingShapeElement {BehindText: true}))
        {
            // Paragraph contained only behind-text decorative shapes (a "background
            // placeholder" pattern) but carries explicit spacing-after — Word honours
            // that spacing so the following block sits the correct distance below.
            // Emit a marker paragraph with zero line height (IsAnchorOnlyMark) and
            // spacing-before collapsed: Word does not allocate a line for the paragraph
            // mark in this case, only the trailing spacing. Without this, the table or
            // heading immediately after a background placeholder paragraph snaps too
            // high (see agendas-minutes/11).
            result.Add(new ParagraphElement
            {
                Runs = [],
                Properties = props with {SpacingBeforePoints = 0},
                IsAnchorOnlyMark = true
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
                var grpSpPr = current.Elements().FirstOrDefault(_ => _.LocalName == "grpSpPr");
                var xfrm = grpSpPr?.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");

                if (xfrm != null)
                {
                    long offX = 0, offY = 0, extCx = 1, extCy = 1, chOffX = 0, chOffY = 0, chExtCx = 1, chExtCy = 1;

                    var off = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "off");
                    var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");
                    var chOff = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chOff");
                    var chExt = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chExt");

                    if (off != null)
                    {
                        var attributes = off.GetAttributes();
                        long.TryParse(attributes.AttributeValue("x"), out offX);
                        long.TryParse(attributes.AttributeValue("y"), out offY);
                    }

                    if (ext != null)
                    {
                        var attributes = ext.GetAttributes();
                        long.TryParse(attributes.AttributeValue("cx"), out extCx);
                        long.TryParse(attributes.AttributeValue("cy"), out extCy);
                    }

                    if (chOff != null)
                    {
                        var attributes = chOff.GetAttributes();
                        long.TryParse(attributes.AttributeValue("x"), out chOffX);
                        long.TryParse(attributes.AttributeValue("y"), out chOffY);
                    }

                    if (chExt != null)
                    {
                        var attributes = chExt.GetAttributes();
                        long.TryParse(attributes.AttributeValue("cx"), out chExtCx);
                        long.TryParse(attributes.AttributeValue("cy"), out chExtCy);
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
    Run? TryParseInlineImageRun(Drawing drawing, MainDocumentPart mainPart, RunProperties runProps)
    {
        var hostPart = ResolveHostPart(drawing, mainPart);
        // Use XML-based approach for better namespace handling
        var hasAnchor = drawing.Descendants().Any(_ => _.LocalName == "anchor");
        var hasInline = drawing.Descendants().Any(_ => _.LocalName == "inline");

        // Only handle simple inline images, not anchored images
        if (hasAnchor || !hasInline)
        {
            return null;
        }

        // Inline WordprocessingGroup — parse the contained shapes into an inline shape-group
        // run so the renderer can draw the icon inline (down-arrows on heading rows, accent
        // arrow accents on cover pages, etc.).
        var wpgGroup = drawing.Descendants<WPG.WordprocessingGroup>().FirstOrDefault();
        if (wpgGroup != null)
        {
            return ParseInlineShapeGroupRun(drawing, wpgGroup, hostPart, runProps);
        }

        // Standalone inline connector — a single <wps:wsp> with prstGeom prst="line" sitting
        // directly under <a:graphicData> (no wpg:wgp wrapper). Used by some templates for
        // section divider rules between sub-headings; without this branch the line is silently
        // dropped because it has no <pic> child.
        var standaloneWsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
        if (standaloneWsp != null &&
            ShapeParser.IsLineShape(standaloneWsp.GetFirstChild<WPS.ShapeProperties>()))
        {
            return ParseInlineSingleLineRun(drawing, standaloneWsp, runProps);
        }

        // Find the pic element
        var pic = drawing.Descendants().FirstOrDefault(_ => _.LocalName == "pic");
        if (pic == null)
        {
            return null;
        }

        // Get the picture's shape properties for size (same approach as ParseDrawingElements)
        var spPr = pic.Elements().FirstOrDefault(_ => _.LocalName == "spPr");

        var xfrm = spPr?.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");
        if (xfrm == null)
        {
            return null;
        }

        // Get image extent from pic's spPr (more reliable than inline.Extent for some documents)
        long picWidth = 0, picHeight = 0;
        var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");
        if (ext != null)
        {
            var attributes = ext.GetAttributes();
            long.TryParse(attributes.AttributeValue("cx"), out picWidth);
            long.TryParse(attributes.AttributeValue("cy"), out picHeight);
        }

        if (picWidth == 0 || picHeight == 0)
        {
            return null;
        }

        var widthPoints = picWidth / emusPerPoint;
        var heightPoints = picHeight / emusPerPoint;

        // Rotation (a:xfrm/@rot, in 60,000ths of a degree, clockwise)
        var rotationDegrees = ReadRotationDegrees(xfrm);
        var (flipHorizontal, flipVertical) = ReadFlips(xfrm);

        // Find the blip (image reference)
        var blipFill = pic.Elements().FirstOrDefault(_ => _.LocalName == "blipFill");
        if (blipFill == null)
        {
            return null;
        }

        var crop = ReadCrop(blipFill);

        if (ReadBlipImage(blipFill, hostPart) is not { } image)
        {
            return null;
        }

        // Create a Run with inline image data
        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageData = image.Data,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            InlineImageContentType = image.ContentType,
            InlineImageDescription = ReadImageDescription(pic, drawing),
            InlineImageRasterFallbackData = image.RasterFallbackData,
            InlineImageRasterFallbackContentType = image.RasterFallbackContentType,
            InlineImageRotationDegrees = rotationDegrees,
            InlineImageFlipHorizontal = flipHorizontal,
            InlineImageFlipVertical = flipVertical,
            InlineImageCrop = crop
        };
    }

    // The a:blip extension that carries the vector original of an Office icon. When present the
    // SVG is the primary image and the a:blip raster becomes a fallback for backends that can't
    // rasterize SVG; without it the raster is the primary image.
    const string svgBlipExtensionUri = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}";

    /// <summary>
    /// Resolves the image bytes a <c>blipFill</c> points at. Null when the blip is missing or
    /// resolves to an empty part.
    /// </summary>
    (byte[] Data, string? ContentType, byte[]? RasterFallbackData, string? RasterFallbackContentType)?
        ReadBlipImage(OpenXmlElement blipFill, OpenXmlPart hostPart)
    {
        var blip = blipFill.Descendants().FirstOrDefault(_ => _.LocalName == "blip");
        if (blip?.AttributeValue("embed") is not { } embed)
        {
            return null;
        }

        byte[]? imageData = null;
        string? contentType = null;
        byte[]? rasterFallbackData = null;
        string? rasterFallbackContentType = null;

        var extLst = blip.Elements().FirstOrDefault(_ => _.LocalName == "extLst");
        if (extLst != null)
        {
            foreach (var extEl in extLst.Elements().Where(_ => _.LocalName == "ext"))
            {
                if (extEl.AttributeValue("uri") != svgBlipExtensionUri)
                {
                    continue;
                }

                var svgBlip = extEl.Descendants().FirstOrDefault(_ => _.LocalName == "svgBlip");
                if (svgBlip?.AttributeValue("embed") is not { } svgEmbed)
                {
                    continue;
                }

                imageData = GetPartBytes(hostPart.GetPartById(svgEmbed));
                contentType = "image/svg+xml";
            }
        }

        if (hostPart.GetPartById(embed) is ImagePart imagePart)
        {
            var rasterBytes = GetPartBytes(imagePart);
            if (rasterBytes.Length > 0)
            {
                if (imageData == null)
                {
                    imageData = rasterBytes;
                    contentType = imagePart.ContentType;
                }
                else
                {
                    rasterFallbackData = rasterBytes;
                    rasterFallbackContentType = imagePart.ContentType;
                }
            }
        }

        if (imageData is not {Length: > 0})
        {
            return null;
        }

        return (imageData, contentType, rasterFallbackData, rasterFallbackContentType);
    }

    Run? ParseInlineShapeGroupRun(Drawing drawing, WPG.WordprocessingGroup wpgGroup, OpenXmlPart hostPart, RunProperties runProps)
    {
        // Inline drawings carry their displayed size on <wp:extent>; the wpg's own xfrm gives
        // the child coordinate space we treat as 0..ChildExtX × 0..ChildExtY.
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        if (extent?.Cx == null || extent.Cy == null)
        {
            return null;
        }

        // wp:effectExtent reserves layout space for shape effects (shadows, outlines that
        // visually extend past the bounding box). Word lays inline drawings out at
        // (extent + effectExtent) so adjacent icons get a visible gap; honouring it here
        // keeps the connector-line groups (e.g. arrow icons) from packing tight.
        var effectExtent = drawing.Descendants<DW.EffectExtent>().FirstOrDefault();
        var effectL = effectExtent?.LeftEdge?.Value ?? 0;
        var effectT = effectExtent?.TopEdge?.Value ?? 0;
        var effectR = effectExtent?.RightEdge?.Value ?? 0;
        var effectB = effectExtent?.BottomEdge?.Value ?? 0;

        var outerCx = extent.Cx.Value + effectL + effectR;
        var outerCy = extent.Cy.Value + effectT + effectB;
        var widthPoints = outerCx / emusPerPoint;
        var heightPoints = outerCy / emusPerPoint;
        if (widthPoints <= 0 || heightPoints <= 0)
        {
            return null;
        }

        var grpSpPr = wpgGroup.GetFirstChild<WPG.GroupShapeProperties>();
        var grpXfrm = grpSpPr?.GetFirstChild<A.TransformGroup>();
        var chExt = grpXfrm?.ChildExtents;
        var childExtentX = (double) (chExt?.Cx ?? extent.Cx.Value);
        var childExtentY = (double) (chExt?.Cy ?? extent.Cy.Value);
        var chOffX = (double) (grpXfrm?.ChildOffset?.X ?? 0);
        var chOffY = (double) (grpXfrm?.ChildOffset?.Y ?? 0);
        if (childExtentX <= 0 || childExtentY <= 0)
        {
            return null;
        }

        var rotationDegrees = grpXfrm?.Rotation?.Value is { } rot ? rot / 60000.0 : 0;

        // Shift each child's position by the effect-padding offset so it lines up with the inner
        // (extent) box inside the outer (extent + effectExtent) frame. The renderer scales the
        // entire outer rectangle into the inline fragment, so without the offset every shape
        // would land in the top-left padding region.
        var paddingX = effectL * (childExtentX / extent.Cx.Value);
        var paddingY = effectT * (childExtentY / extent.Cy.Value);

        var shapes = new List<GroupShape>();
        foreach (var child in GroupDrawables(wpgGroup))
        {
            if (child is WPS.WordprocessingShape wsp)
            {
                var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
                var shapeXfrm = shapeProps?.GetFirstChild<A.Transform2D>();
                if (shapeProps == null || shapeXfrm?.Offset == null || shapeXfrm.Extents == null)
                {
                    continue;
                }

                var fill = shapeProps.GetFirstChild<A.SolidFill>();
                var stroke = ReadGroupStroke(shapeProps, wsp.GetFirstChild<WPS.ShapeStyle>()?.LineReference);

                shapes.Add(new()
                {
                    X = (shapeXfrm.Offset.X ?? 0) - chOffX + paddingX,
                    Y = (shapeXfrm.Offset.Y ?? 0) - chOffY + paddingY,
                    Width = shapeXfrm.Extents.Cx ?? 0,
                    Height = shapeXfrm.Extents.Cy ?? 0,
                    ColorHex = stroke.ColorHex,
                    LineWidthEmu = stroke.WidthEmu,
                    LineAlpha = stroke.Alpha,
                    FlipVertical = shapeXfrm.VerticalFlip?.Value == true,
                    FlipHorizontal = shapeXfrm.HorizontalFlip?.Value == true,
                    Geometry = MapGroupGeometry(shapeProps),
                    Subpaths = ShapeParser.ExtractSubpaths(shapeProps)
                               ?? PresetShapeGeometry.TryBuild(
                                   shapeProps.GetFirstChild<A.PresetGeometry>(),
                                   shapeXfrm.Extents.Cx ?? 0,
                                   shapeXfrm.Extents.Cy ?? 0),
                    FillColorHex = fill != null ? ExtractFirstFillColor(fill) : null,
                    FillAlpha = fill != null ? ShapeParser.ExtractSolidFillAlpha(fill) : 1,
                    Shadow = ReadOuterShadow(shapeProps)
                });
            }
            else if (child is PIC.Picture picture)
            {
                var picProps = picture.ShapeProperties;
                var picXfrm = picProps?.Transform2D;
                if (picProps == null ||
                    picXfrm?.Offset == null ||
                    picXfrm.Extents == null ||
                    picture.BlipFill == null ||
                    ReadBlipImage(picture.BlipFill, hostPart) is not { } image)
                {
                    continue;
                }

                // pic:spPr carries the shape the picture is cropped to (Word's circular "picture
                // style") and the ring drawn around it, the same way a wps:wsp carries its own.
                // A picture has no wps:style, so its outline width has to be explicit.
                var picStroke = ReadGroupStroke(picProps, lineReference: null);

                shapes.Add(new()
                {
                    X = (picXfrm.Offset.X ?? 0) - chOffX + paddingX,
                    Y = (picXfrm.Offset.Y ?? 0) - chOffY + paddingY,
                    Width = picXfrm.Extents.Cx ?? 0,
                    Height = picXfrm.Extents.Cy ?? 0,
                    ColorHex = picStroke.ColorHex,
                    LineWidthEmu = picStroke.WidthEmu,
                    LineAlpha = picStroke.Alpha,
                    ImageDescription = ReadImageDescription(picture, drawing),
                    Geometry = MapGroupGeometry(picProps),
                    Shadow = ReadOuterShadow(picProps),
                    ImageData = image.Data,
                    ImageContentType = image.ContentType,
                    ImageRasterFallbackData = image.RasterFallbackData,
                    ImageCrop = ReadCrop(picture.BlipFill)
                });
            }
        }

        if (shapes.Count == 0)
        {
            return null;
        }

        // Stretch the child coord space by the same outer/inner ratio so shape coordinates
        // (now padded above) map correctly into the rendered outer rectangle.
        var outerChildExtentX = childExtentX * outerCx / extent.Cx.Value;
        var outerChildExtentY = childExtentY * outerCy / extent.Cy.Value;

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            InlineShapeGroup = new()
            {
                ChildExtentX = outerChildExtentX,
                ChildExtentY = outerChildExtentY,
                RotationDegrees = rotationDegrees,
                Shapes = shapes
            }
        };
    }

    /// <summary>
    /// Resolves a group member's stroke — colour, width (EMU) and opacity. DrawingML layers the
    /// shape's own <c>a:ln</c> over the theme line style that <c>wps:style/a:lnRef/@idx</c> names,
    /// so an <c>a:ln</c> carrying only a colour still strokes, at the theme's width. An explicit
    /// <c>a:noFill</c> on the outline means no stroke; so does a shape with neither a width nor a
    /// resolvable <c>a:lnRef</c>.
    /// </summary>
    (string ColorHex, double WidthEmu, double Alpha) ReadGroupStroke(OpenXmlElement shapeProperties, A.LineReference? lineReference)
    {
        var outline = shapeProperties.GetFirstChild<A.Outline>();
        if (outline?.GetFirstChild<A.NoFill>() != null)
        {
            return ("000000", 0, 1);
        }

        var solidFill = outline?.GetFirstChild<A.SolidFill>();

        // The lnRef's own colour child overrides the theme line style's phClr placeholder, so it
        // is the colour to fall back on when the shape doesn't name one itself.
        var colorSource = (OpenXmlElement?) solidFill ?? lineReference;

        return (
            ExtractFirstFillColor(colorSource) ?? "000000",
            outline?.Width?.Value ?? ResolveLineReferenceWidthEmu(lineReference),
            solidFill != null ? ShapeParser.ExtractSolidFillAlpha(solidFill) : 1);
    }

    /// <summary>
    /// Reads a group member's <c>a:effectLst/a:outerShdw</c>. <c>@dist</c> is the offset length in
    /// EMU and <c>@dir</c> its angle in 60,000ths of a degree, measured clockwise from the +x axis
    /// in screen space (y grows downward), so the two resolve straight into an x/y offset. A shadow
    /// with no distance is a blur halo rather than a drop shadow, and we have nothing to offset.
    /// </summary>
    GroupShadow? ReadOuterShadow(OpenXmlElement shapeProperties)
    {
        var shadow = shapeProperties.GetFirstChild<A.EffectList>()?.GetFirstChild<A.OuterShadow>();
        if (shadow?.Distance?.Value is not { } distance || distance <= 0)
        {
            return null;
        }

        var radians = (shadow.Direction?.Value ?? 0) / 60000.0 * Math.PI / 180.0;

        // Word writes shadow colours as <a:prstClr val="black">. Only the monochrome presets show
        // up in practice, so the rest fall back to black rather than carrying a 140-name table.
        var colorHex = ExtractFirstFillColor(shadow);
        if (colorHex == null && shadow.GetFirstChild<A.PresetColor>()?.Val?.Value is { } preset)
        {
            colorHex = preset == A.PresetColorValues.White ? "FFFFFF" : "000000";
        }

        var alpha = shadow.Descendants<A.Alpha>().FirstOrDefault()?.Val?.Value is { } opacity
            ? opacity / 100000.0
            : 1.0;

        return new()
        {
            OffsetX = distance * Math.Cos(radians),
            OffsetY = distance * Math.Sin(radians),
            ColorHex = colorHex ?? "000000",
            Alpha = alpha
        };
    }

    /// <summary>Stroke width an <c>a:lnRef/@idx</c> selects from the theme's 1-based <c>lnStyleLst</c>.</summary>
    long ResolveLineReferenceWidthEmu(A.LineReference? lineReference)
    {
        if (lineReference?.Index?.Value is not { } index || index == 0)
        {
            return 0;
        }

        var widths = (currentThemeColors ?? new()).LineStyleWidthsEmu;
        return index < widths.Count ? widths[(int) index] : 0;
    }

    /// <summary>
    /// Reads the <c>a:prstGeom</c> of a group member's shape properties. Line, rect and ellipse
    /// cover icon-style groups; anything fancier (curves, arcs, custGeom) collapses to a rectangle
    /// so we render at least the bounding box.
    /// </summary>
    static GroupShapeGeometry MapGroupGeometry(OpenXmlElement shapeProperties)
    {
        var preset = shapeProperties.GetFirstChild<A.PresetGeometry>()?.Preset?.Value;
        if (preset == A.ShapeTypeValues.Line)
        {
            return GroupShapeGeometry.Line;
        }

        return preset == A.ShapeTypeValues.Ellipse
            ? GroupShapeGeometry.Ellipse
            : GroupShapeGeometry.Rectangle;
    }

    /// <summary>
    /// True when <paramref name="shape"/> shares a <c>wpg:wgp</c> with other shapes or pictures.
    /// Parsers that read the drawing-level <c>wp:extent</c> and treat the first <c>wps:wsp</c> as
    /// the whole drawing (WordArt, text boxes) must decline those: the extent describes the group,
    /// and claiming the drawing would drop the shape's siblings. Word's icon groups are exactly
    /// this — an oval carrying a hidden descriptive text box, with the icon picture on top.
    /// </summary>
    static bool HasGroupSiblings(OpenXmlElement shape) =>
        shape.Ancestors<WPG.WordprocessingGroup>().FirstOrDefault() is { } group &&
        GroupDrawables(group).Skip(1).Any();

    /// <summary>
    /// The shapes and pictures a <c>wpg:wgp</c> paints, in document (back-to-front) order,
    /// flattening nested <c>wpg:grpSp</c> groups. Deliberately does not use <c>Descendants</c>:
    /// a shape's <c>wps:txbx</c> can hold its own drawings, which belong to the text box rather
    /// than to this group's coordinate space.
    /// </summary>
    static IEnumerable<OpenXmlElement> GroupDrawables(OpenXmlElement group)
    {
        foreach (var child in group.ChildElements)
        {
            switch (child)
            {
                case WPS.WordprocessingShape:
                case PIC.Picture:
                    yield return child;
                    break;
                case WPG.GroupShape nested:
                    foreach (var descendant in GroupDrawables(nested))
                    {
                        yield return descendant;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Wraps a single <c>prstGeom prst="line"</c> shape sitting directly under
    /// <c>a:graphicData</c> (no <c>wpg:wgp</c>) into a one-element <see cref="InlineShapeGroup"/>.
    /// The shape's own <c>a:xfrm</c> defines the line in its local 0..cx × 0..cy box, so the
    /// group's child coord space is just the wp:extent itself.
    /// </summary>
    Run? ParseInlineSingleLineRun(Drawing drawing, WPS.WordprocessingShape wsp, RunProperties runProps)
    {
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        if (extent?.Cx == null || extent.Cy == null)
        {
            return null;
        }

        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        var shapeXfrm = shapeProps?.GetFirstChild<A.Transform2D>();
        if (shapeProps == null || shapeXfrm?.Extents == null)
        {
            return null;
        }

        var ln = shapeProps.GetFirstChild<A.Outline>();
        var lineWidth = ln?.Width?.Value ?? 0;
        var stroke = ExtractFirstFillColor(ln) ?? "000000";

        // wp:extent of an inline connector with cy=0 collapses to a zero-height layout box —
        // the rendered line falls on the baseline. Give the group a one-EMU child height so
        // GroupShape coordinates stay in-range when the renderer scales them.
        var childExtentX = (double) (shapeXfrm.Extents.Cx ?? extent.Cx.Value);
        var childExtentY = Math.Max(1.0, (double) (shapeXfrm.Extents.Cy ?? extent.Cy.Value));

        var widthPoints = extent.Cx.Value / emusPerPoint;
        var heightPoints = Math.Max(lineWidth / emusPerPoint, extent.Cy.Value / emusPerPoint);
        if (widthPoints <= 0)
        {
            return null;
        }

        var shapes = new List<GroupShape>
        {
            new()
            {
                X = 0,
                Y = 0,
                Width = shapeXfrm.Extents.Cx ?? 0,
                Height = shapeXfrm.Extents.Cy ?? 0,
                ColorHex = stroke,
                LineWidthEmu = lineWidth,
                Geometry = GroupShapeGeometry.Line
            }
        };

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            InlineShapeGroup = new()
            {
                ChildExtentX = childExtentX,
                ChildExtentY = childExtentY,
                Shapes = shapes
            }
        };
    }

    /// <summary>
    /// Walks an <c>a:ln</c> / <c>a:solidFill</c> for the first solid colour we can resolve, in
    /// document order — an outline carries exactly one. Each colour flavour applies its own
    /// <c>lumMod</c>/<c>lumOff</c>/<c>tint</c>/<c>shade</c> children: ignoring them turns Word's
    /// "Lighter 80%" tints back into the saturated base colour.
    /// </summary>
    string? ExtractFirstFillColor(OpenXmlElement? element)
    {
        if (element == null)
        {
            return null;
        }

        foreach (var color in element.Descendants())
        {
            switch (color)
            {
                case A.RgbColorModelHex {Val.HasValue: true} rgb:
                    return ShapeParser.ApplyLiteralColorTransforms(rgb.Val.Value!, rgb);

                // Word caches the resolved value of a system colour in @lastClr; @val ("window",
                // "windowText") names a host UI colour a document renderer must not look up.
                case A.SystemColor {LastColor.HasValue: true} system:
                    return ShapeParser.ApplyLiteralColorTransforms(system.LastColor.Value!, system);

                case A.SchemeColor {Val.HasValue: true} scheme when currentThemeColors != null:
                    var schemeValue = ((IEnumValue) scheme.Val.Value).Value;
                    return currentThemeColors.ResolveColor(schemeValue, ShapeParser.ExtractColorTransforms(scheme));
            }
        }

        return null;
    }

    static double ReadRotationDegrees(OpenXmlElement xfrm)
    {
        if (xfrm.AttributeValue("rot") is { } rotValue && long.TryParse(rotValue, out var rot60000ths))
        {
            return rot60000ths / 60000.0;
        }

        return 0;
    }

    /// <summary>Reads the mirror flags from an <c>a:xfrm</c> (<c>@flipH</c>/<c>@flipV</c>).</summary>
    static (bool FlipHorizontal, bool FlipVertical) ReadFlips(OpenXmlElement xfrm) =>
        (xfrm.AttributeValue("flipH") is "1" or "true",
            xfrm.AttributeValue("flipV") is "1" or "true");

    static LigatureMode ParseLigatureMode(DocumentFormat.OpenXml.Office2010.Word.Ligatures? element)
    {
        if (element?.Val?.Value is not { } val)
        {
            return LigatureMode.Standard;
        }

        var v = DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.None;
        if (val == v) return LigatureMode.None;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Standard) return LigatureMode.Standard;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Contextual) return LigatureMode.Contextual;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Historical) return LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.Discretional) return LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardContextual) return LigatureMode.Standard | LigatureMode.Contextual;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardHistorical) return LigatureMode.Standard | LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardDiscretional) return LigatureMode.Standard | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.ContextualHistorical) return LigatureMode.Contextual | LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.ContextualDiscretional) return LigatureMode.Contextual | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.HistoricalDiscretional) return LigatureMode.Historical | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardContextualHistorical) return LigatureMode.Standard | LigatureMode.Contextual | LigatureMode.Historical;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardContextualDiscretional) return LigatureMode.Standard | LigatureMode.Contextual | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.StandardHistoricalDiscretional) return LigatureMode.Standard | LigatureMode.Historical | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.ContextualHistoricalDiscretional) return LigatureMode.Contextual | LigatureMode.Historical | LigatureMode.Discretional;
        if (val == DocumentFormat.OpenXml.Office2010.Word.LigaturesValues.All) return LigatureMode.All;
        return LigatureMode.Standard;
    }

    internal static (BlipColorEffect Effect, string? DuotoneColorHex) ReadBlipColorEffect(OpenXmlElement? blip, ThemeColors? themeColors)
    {
        if (blip == null)
        {
            return (BlipColorEffect.None, null);
        }

        // a:blip can carry one or more colour-transform children. Pick the most visible one.
        foreach (var child in blip.Elements())
        {
            switch (child.LocalName)
            {
                case "grayscl":
                    return (BlipColorEffect.Grayscale, null);
                case "duotone":
                    return (BlipColorEffect.Duotone, ResolveDuotoneDarkColor(child, themeColors));
                case "lum":
                    // a:lum bright="N" — N>0 means washout (lighten), N<0 means darken.
                    var brightAttribute = child.AttributeValue("bright");
                    if (int.TryParse(brightAttribute, out var bright) && bright > 0)
                    {
                        return (BlipColorEffect.Washout, null);
                    }

                    break;
            }
        }

        return (BlipColorEffect.None, null);
    }

    // Word's Recolor gallery emits a:duotone as (darkColor, white): the image's luminance maps
    // onto a dark→white ramp. The dark end is the first resolvable colour child (the trailing
    // prstClr white is the ramp's light end). Null → renderers fall back to greyscale.
    static string? ResolveDuotoneDarkColor(OpenXmlElement duotone, ThemeColors? themeColors)
    {
        foreach (var colorChild in duotone.Elements())
        {
            switch (colorChild)
            {
                case A.RgbColorModelHex rgb when rgb.Val?.HasValue == true:
                    return ShapeParser.ApplyLiteralColorTransforms(rgb.Val.Value!, rgb);
                case A.SchemeColor scheme when scheme.Val?.HasValue == true && themeColors != null:
                    var schemeValue = ((IEnumValue) scheme.Val.Value).Value;
                    var resolved = themeColors.ResolveColor(schemeValue, ShapeParser.ExtractColorTransforms(scheme));
                    if (resolved != null)
                    {
                        return resolved;
                    }

                    break;
            }
        }

        return null;
    }

    static ImageCrop? ReadCrop(OpenXmlElement blipFill)
    {
        // a:srcRect attributes l/t/r/b are in 1000ths of a percent (100000 = 100%).
        var srcRect = blipFill.Elements().FirstOrDefault(_ => _.LocalName == "srcRect");
        if (srcRect == null)
        {
            return null;
        }

        var crop = new ImageCrop
        {
            Left = ReadFraction(srcRect, "l"),
            Top = ReadFraction(srcRect, "t"),
            Right = ReadFraction(srcRect, "r"),
            Bottom = ReadFraction(srcRect, "b")
        };

        return crop.IsCropped ? crop : null;

        static double ReadFraction(OpenXmlElement element, string attributeName)
        {
            if (element.AttributeValue(attributeName) is { } value && long.TryParse(value, out var thousandthsOfPercent))
            {
                // Negative values are padding (the image shrinks inside its frame) and are as
                // common as positive crops in Word's icon templates; the -10 floor only guards
                // against degenerate markup.
                return Math.Clamp(thousandthsOfPercent / 100000.0, -10, 1);
            }

            return 0;
        }
    }

    /// <summary>
    /// Parses all images from a drawing element, including multiple images in groups.
    /// </summary>
    static OpenXmlPart ResolveHostPart(OpenXmlElement element, MainDocumentPart mainPart)
    {
        var current = element;
        while (current != null)
        {
            if (current is OpenXmlPartRootElement {OpenXmlPart: { } part})
            {
                return part;
            }

            current = current.Parent;
        }

        return mainPart;
    }

    // Alt text for a picture: the picture's own pic:cNvPr wins (most specific — e.g. per-image
    // inside a group), falling back to the drawing-level wp:docPr. @descr is Word's "Description"
    // alt-text field; @title ("Title") is the secondary fallback. The auto-generated @name
    // ("Picture 1") is never used — it is a shape name, not alt text.
    static string? ReadImageDescription(OpenXmlElement pic, Drawing drawing)
    {
        return Describe(pic.Descendants().FirstOrDefault(_ => _.LocalName == "cNvPr"))
            ?? Describe(drawing.Descendants().FirstOrDefault(_ => _.LocalName == "docPr"));

        static string? Describe(OpenXmlElement? properties)
        {
            if (properties == null)
            {
                return null;
            }

            if (properties.AttributeValue("descr") is {Length: > 0} descr && !string.IsNullOrWhiteSpace(descr))
            {
                return descr.Trim();
            }

            return properties.AttributeValue("title") is {Length: > 0} title && !string.IsNullOrWhiteSpace(title)
                ? title.Trim()
                : null;
        }
    }

    List<DocumentElement> ParseDrawingElements(Drawing drawing, MainDocumentPart mainPart)
    {
        var hostPart = ResolveHostPart(drawing, mainPart);
        var result = new List<DocumentElement>();

        var anchor = drawing.GetFirstChild<DW.Anchor>();

        // Get the group transform if present (for coordinate system)
        long groupOffsetX = 0, groupOffsetY = 0;
        double groupScaleX = 1.0, groupScaleY = 1.0;

        var grpSpPr = drawing.Descendants().FirstOrDefault(_ => _.LocalName == "grpSpPr");
        if (grpSpPr != null)
        {
            var xfrm = grpSpPr.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");
            if (xfrm != null)
            {
                // Get child extents for scaling
                var chOff = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chOff");
                var chExt = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chExt");
                var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");

                if (chOff != null)
                {
                    var attributes = chOff.GetAttributes();
                    long.TryParse(attributes.AttributeValue("x"), out groupOffsetX);
                    long.TryParse(attributes.AttributeValue("y"), out groupOffsetY);
                }

                if (chExt != null && ext != null)
                {
                    var chExtAttributes = chExt.GetAttributes();
                    var extAttributes = ext.GetAttributes();
                    var chCx = chExtAttributes.AttributeValue("cx");
                    var chCy = chExtAttributes.AttributeValue("cy");
                    var extCx = extAttributes.AttributeValue("cx");
                    var extCy = extAttributes.AttributeValue("cy");

                    if (chCx != null && extCx != null &&
                        long.TryParse(chCx, out var childWidth) && long.TryParse(extCx, out var actualWidth) &&
                        childWidth > 0)
                    {
                        groupScaleX = (double) actualWidth / childWidth;
                    }

                    if (chCy != null && extCy != null &&
                        long.TryParse(chCy, out var childHeight) && long.TryParse(extCy, out var actualHeight) &&
                        childHeight > 0)
                    {
                        groupScaleY = (double) actualHeight / childHeight;
                    }
                }
            }
        }

        // Find ALL pic elements (including in groups)
        var pics = drawing.Descendants().Where(_ => _.LocalName == "pic").ToList();

        foreach (var pic in pics)
        {
            // Get the picture's shape properties for position/size
            var spPr = pic.Elements().FirstOrDefault(_ => _.LocalName == "spPr");
            if (spPr == null)
            {
                continue;
            }

            var xfrm = spPr.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");
            if (xfrm == null)
            {
                continue;
            }

            // Get offset within group
            long picOffsetX = 0, picOffsetY = 0;
            var off = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "off");
            if (off != null)
            {
                var attributes = off.GetAttributes();
                long.TryParse(attributes.AttributeValue("x"), out picOffsetX);
                long.TryParse(attributes.AttributeValue("y"), out picOffsetY);
            }

            // Get image extent
            long picWidth = 0, picHeight = 0;
            var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");
            if (ext != null)
            {
                var attributes = ext.GetAttributes();
                long.TryParse(attributes.AttributeValue("cx"), out picWidth);
                long.TryParse(attributes.AttributeValue("cy"), out picHeight);
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

            // Rotation (a:xfrm/@rot, in 60,000ths of a degree, clockwise)
            var rotationDegrees = ReadRotationDegrees(xfrm);
            var (flipHorizontal, flipVertical) = ReadFlips(xfrm);

            // Find the blip (image reference)
            var blipFill = pic.Elements().FirstOrDefault(_ => _.LocalName == "blipFill");
            if (blipFill == null)
            {
                continue;
            }

            var crop = ReadCrop(blipFill);

            var blip = blipFill.Descendants().FirstOrDefault(_ => _.LocalName == "blip");
            if (blip == null)
            {
                continue;
            }

            if (blip.AttributeValue("embed") is not { } embed)
            {
                continue;
            }

            // Try to get SVG first, then fall back to regular image
            byte[]? imageData = null;
            string? contentType = null;
            byte[]? rasterFallbackData = null;
            string? rasterFallbackContentType = null;

            // Check for SVG extension
            var extLst = blip.Elements().FirstOrDefault(_ => _.LocalName == "extLst");
            if (extLst != null)
            {
                foreach (var extEl in extLst.Elements().Where(_ => _.LocalName == "ext"))
                {
                    if (extEl.AttributeValue("uri") == "{96DAC541-7B7A-43D3-8B79-37D633B846F1}")
                    {
                        var svgBlip = extEl.Descendants().FirstOrDefault(_ => _.LocalName == "svgBlip");
                        if (svgBlip != null)
                        {
                            if (svgBlip.AttributeValue("embed") is { } svgEmbed)
                            {
                                var svgPart = hostPart.GetPartById(svgEmbed);
                                imageData = GetPartBytes(svgPart);
                                contentType = "image/svg+xml";
                            }
                        }
                    }
                }
            }

            // Read the raster blob the blip points to. When SVG is set, this becomes the
            // raster fallback for backends that can't render SVG; otherwise it's the
            // primary imageData.
            if (hostPart.GetPartById(embed) is ImagePart rasterPart)
            {
                var rasterBytes = GetPartBytes(rasterPart);
                if (rasterBytes.Length > 0)
                {
                    if (imageData == null)
                    {
                        imageData = rasterBytes;
                        contentType = rasterPart.ContentType;
                    }
                    else
                    {
                        rasterFallbackData = rasterBytes;
                        rasterFallbackContentType = rasterPart.ContentType;
                    }
                }
            }

            if (imageData == null || imageData.Length == 0)
            {
                continue;
            }

            var (colorEffect, duotoneColorHex) = ReadBlipColorEffect(blip, currentThemeColors);
            var description = ReadImageDescription(pic, drawing);

            // Create the image element
            if (anchor == null)
            {
                result.Add(new ImageElement
                {
                    ImageData = imageData,
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    ContentType = contentType,
                    Description = description,
                    RotationDegrees = rotationDegrees,
                    FlipHorizontal = flipHorizontal,
                    FlipVertical = flipVertical,
                    Crop = crop,
                    ColorEffect = colorEffect,
                    DuotoneColorHex = duotoneColorHex,
                    RasterFallbackData = rasterFallbackData,
                    RasterFallbackContentType = rasterFallbackContentType
                });
            }
            else
            {
                var floatingImage = ParseAnchoredImageWithOffset(anchor, imageData, widthPoints, heightPoints, contentType, offsetXPoints, offsetYPoints, rotationDegrees, flipHorizontal, flipVertical, crop, rasterFallbackData, rasterFallbackContentType, description, colorEffect, duotoneColorHex);
                result.Add(floatingImage);
            }
        }

        return result;
    }

    /// <summary>
    /// Parses an anchored image with additional X/Y offset within a group.
    /// </summary>
    static FloatingImageElement ParseAnchoredImageWithOffset(DW.Anchor anchor, byte[] imageData, double widthPoints, double heightPoints, string? contentType, double offsetXPoints, double offsetYPoints, double rotationDegrees = 0, bool flipHorizontal = false, bool flipVertical = false, ImageCrop? crop = null, byte[]? rasterFallbackData = null, string? rasterFallbackContentType = null, string? description = null, BlipColorEffect colorEffect = BlipColorEffect.None, string? duotoneColorHex = null)
    {
        var positioning = anchor.ParsePositioning(offsetXPoints, offsetYPoints);
        var wrap = ParseWrap(anchor);

        return new()
        {
            ImageData = imageData,
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            ContentType = contentType,
            Description = description,
            ColorEffect = colorEffect,
            DuotoneColorHex = duotoneColorHex,
            HorizontalPositionPoints = positioning.HorizontalPositionPoints,
            VerticalPositionPoints = positioning.VerticalPositionPoints,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            WrapType = wrap.Type,
            WrapTextSide = wrap.Side,
            WrapDistanceLeftPoints = wrap.DistLeft,
            WrapDistanceTopPoints = wrap.DistTop,
            WrapDistanceRightPoints = wrap.DistRight,
            WrapDistanceBottomPoints = wrap.DistBottom,
            BehindText = positioning.BehindText,
            RotationDegrees = rotationDegrees,
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical,
            Crop = crop,
            WidthPercent = positioning.WidthPercent,
            WidthRelativeFrom = positioning.WidthRelativeFrom,
            HeightPercent = positioning.HeightPercent,
            HeightRelativeFrom = positioning.HeightRelativeFrom,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            RasterFallbackData = rasterFallbackData,
            RasterFallbackContentType = rasterFallbackContentType
        };
    }

    /// <summary>
    /// Reads the anchor's wrap element (wp:wrapSquare / wrapTight / wrapThrough /
    /// wrapTopAndBottom), its side preference (@wrapText) and clearance distances. Distances
    /// are EMU in OOXML; absent attributes mean zero clearance / both sides.
    /// </summary>
    static (WrapType Type, WrapTextSide Side, double DistLeft, double DistTop, double DistRight, double DistBottom) ParseWrap(DW.Anchor anchor)
    {
        const double emusPerPoint = 12700.0;

        if (anchor.GetFirstChild<DW.WrapSquare>() is { } square)
        {
            return (WrapType.Square,
                ParseWrapTextSide(square.WrapText),
                (square.DistanceFromLeft?.Value ?? 0) / emusPerPoint,
                (square.DistanceFromTop?.Value ?? 0) / emusPerPoint,
                (square.DistanceFromRight?.Value ?? 0) / emusPerPoint,
                (square.DistanceFromBottom?.Value ?? 0) / emusPerPoint);
        }

        if (anchor.GetFirstChild<DW.WrapTight>() is { } tight)
        {
            return (WrapType.Tight,
                ParseWrapTextSide(tight.WrapText),
                (tight.DistanceFromLeft?.Value ?? 0) / emusPerPoint,
                0,
                (tight.DistanceFromRight?.Value ?? 0) / emusPerPoint,
                0);
        }

        if (anchor.GetFirstChild<DW.WrapThrough>() is { } through)
        {
            return (WrapType.Through,
                ParseWrapTextSide(through.WrapText),
                (through.DistanceFromLeft?.Value ?? 0) / emusPerPoint,
                0,
                (through.DistanceFromRight?.Value ?? 0) / emusPerPoint,
                0);
        }

        if (anchor.GetFirstChild<DW.WrapTopBottom>() is { } topBottom)
        {
            return (WrapType.TopAndBottom,
                WrapTextSide.BothSides,
                0,
                (topBottom.DistanceFromTop?.Value ?? 0) / emusPerPoint,
                0,
                (topBottom.DistanceFromBottom?.Value ?? 0) / emusPerPoint);
        }

        return (WrapType.None, WrapTextSide.BothSides, 0, 0, 0, 0);
    }

    static WrapTextSide ParseWrapTextSide(EnumValue<DW.WrapTextValues>? wrapText)
    {
        if (wrapText?.Value is not { } value)
        {
            return WrapTextSide.BothSides;
        }

        if (value == DW.WrapTextValues.Left)
        {
            return WrapTextSide.Left;
        }

        if (value == DW.WrapTextValues.Right)
        {
            return WrapTextSide.Right;
        }

        if (value == DW.WrapTextValues.Largest)
        {
            return WrapTextSide.Largest;
        }

        return WrapTextSide.BothSides;
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

        if (HasGroupSiblings(wsp))
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
        var positioning = anchor?.ParsePositioning() ?? default;
        var wrapType = WrapType.None;
        string? bgColor = null;

        if (anchor != null)
        {
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
            HorizontalPositionPoints = positioning.HorizontalPositionPoints,
            VerticalPositionPoints = positioning.VerticalPositionPoints,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            WrapType = wrapType,
            BehindText = positioning.BehindText,
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
                else if (wsp.GetFirstChild<WPS.TextBoxInfo2>() == null)
                {
                    // Only fall back to solid-fill parsing for shapes without a txbx. A wsp with an
                    // empty txbx is a decorative-shape carrier (Word stores a placeholder text box
                    // in templated artwork like full-page coloured rectangles or line-art geometry);
                    // turning it into a FloatingShapeElement here would overdraw same-anchor image
                    // overlays or render the bounding box of a custom-geometry outline as a filled
                    // rect.
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
                else if (wsp.GetFirstChild<WPS.TextBoxInfo2>() == null)
                {
                    // See note above — only shapes with no txbx feed the solid-fill fallback.
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

        if (!HasRenderableContent(content))
        {
            // Word documents commonly use a wsp with an empty txbx as the carrier for a behind-text
            // decorative shape (e.g. a full-page coloured rectangle plus an anchored picture). The
            // FloatingShapeElement emitted by ShapeParser already covers the fill; emitting a
            // FloatingTextBoxElement here too would draw the bg again on top of any same-anchor
            // image overlay, masking it. Skip when there's no actual text.
            return null;
        }

        // Get background color if present
        string? bgColor = null;
        var solidFill = shapeProps?.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            bgColor = ShapeParser.ExtractSolidFillColor(solidFill, currentThemeColors);
        }

        // The shape's own chrome: a:ln outline plus any non-rect geometry (roundRect ticket
        // outlines, plaque frames, custGeom) — drawn behind the text content.
        var (lineColor, lineWidth, lineAlpha) = shapeProps == null
            ? (null, null, 1)
            : ShapeParser.ExtractLineStyle(wsp, shapeProps, currentThemeColors);
        var subpaths = shapeProps == null
            ? null
            : ShapeParser.ExtractSubpaths(shapeProps)
              ?? PresetShapeGeometry.TryBuild(shapeProps.GetFirstChild<A.PresetGeometry>(), widthPt, heightPt);

        return new()
        {
            Content = content,
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            HorizontalPositionPoints = xPt,
            VerticalPositionPoints = yPt,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            WrapType = WrapType.None,
            BehindText = behindText,
            BackgroundColorHex = bgColor,
            LineColorHex = lineColor,
            LineWidthPoints = lineWidth ?? 0,
            LineAlpha = lineAlpha,
            Subpaths = subpaths,
            RotationDegrees = rotationDegrees
        };
    }

    static bool HasRenderableContent(List<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is ParagraphElement para)
            {
                if (para.Runs.Any(_ => !string.IsNullOrEmpty(_.Text)))
                {
                    return true;
                }
            }
            else
            {
                // Tables, images, etc. always count as renderable.
                return true;
            }
        }
        return false;
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

        // prstGeom prst="line" — stroke-only connector. Has no fill (and typically cy=0
        // for a horizontal line, cx=0 for a vertical), so the rest of the fill-driven
        // pipeline can't render it. Emit a FloatingShapeElement with the line color/width
        // and let the renderer's stroke branch draw it. Must run before the noFill bail-out
        // because line connectors carry an explicit <a:noFill/> alongside the stroke.
        if (ShapeParser.IsLineShape(shapeProps))
        {
            return ParseLineShape(wsp, shapeProps, positioning, accumTransform, behindText);
        }

        // An explicit <a:noFill/> in spPr means the shape has no fill, even if the
        // wps:style contains a fillRef — the direct property always wins.
        if (shapeProps.GetFirstChild<A.NoFill>() != null)
        {
            return null;
        }

        // No bezier-count fallback here — the path flattener (ExtractSubpaths)
        // now turns cubic / quadratic curves into a polyline approximation, so high-bezier
        // custGeoms render as fillable shapes. The earlier "skip when >50 beziers" guard
        // existed because we would otherwise have drawn the bounding rect as a solid colour
        // overlay.

        // Check for solid fill in shape properties
        var solidFill = shapeProps.GetFirstChild<A.SolidFill>();
        string? fillColorHex = null;
        var fillAlpha = 1.0;

        if (solidFill != null)
        {
            // Parse alpha transform (a:alpha val="X" where X is in thousandths of a percent)
            var alphaElement = solidFill.Descendants<A.Alpha>().FirstOrDefault();
            if (alphaElement?.Val?.HasValue == true)
            {
                fillAlpha = alphaElement.Val.Value / 100000.0;
            }

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
                        fillColorHex = currentThemeColors.ResolveColor(schemeValue, transforms);
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

        // Resolve the outline once. A shape can be stroke-only (no fill) — e.g. the thin accent
        // rules down the left and across the top of the Agenda template, which are stroked
        // custom-geometry line segments (moveTo + lnTo) with no fill. Those must still render, so
        // don't bail purely because there's no fill; only bail when there's neither fill nor stroke.
        var (lineColorHex, lineWidthPoints, _) = ShapeParser.ExtractLineStyle(wsp, shapeProps, currentThemeColors);
        // A dashed outline would render as a solid line here (this path draws no dash pattern),
        // which looks worse than not drawing it — so a stroke-only shape only counts as strokeable
        // when its dash is solid. Matches the prstGeom-line policy in ParseLineShape; the Agenda
        // accent rules are prstDash="solid" and still render, while dashed decorations (e.g. the
        // letterhead rules in letters/05) stay unrendered rather than becoming solid bars.
        var strokeDash = shapeProps.GetFirstChild<A.Outline>()?.GetFirstChild<A.PresetDash>()?.Val;
        var isSolidStroke = strokeDash is null || strokeDash.Value == A.PresetLineDashValues.Solid;
        var hasStroke = lineColorHex != null && lineWidthPoints is > 0 && isSolidStroke;
        if (fillColorHex == null && !hasStroke)
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

        // Without this, custGeom shapes (e.g. half-circle decorations with cubic-bezier arcs)
        // render as their bounding rect instead of the actual curved silhouette. Stroke-only
        // shapes allow an open two-point line segment through (minContourPoints: 2) so the
        // renderer strokes the actual rule rather than the bounding box; filled shapes keep the
        // 3-point minimum, so their geometry is unchanged.
        var subpaths = ShapeParser.ExtractSubpaths(
            shapeProps,
            minContourPoints: fillColorHex == null ? 2 : 3);

        // A stroke-only shape with no custom-geometry path to stroke would fall back to a
        // bounding-box outline — a border Word doesn't draw. Skip it.
        if (fillColorHex == null && subpaths == null)
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
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            BehindText = behindText,
            FillColorHex = fillColorHex,
            FillAlpha = fillAlpha,
            // Only stroke-only shapes render their outline here — a filled decorative shape's
            // <a:ln> was never drawn by this path, so leave that behavior untouched.
            LineColorHex = fillColorHex == null ? lineColorHex : null,
            LineWidthPoints = fillColorHex == null ? lineWidthPoints : null,
            Preset = ShapeParser.ExtractPresetShape(shapeProps),
            Subpaths = subpaths
        };
    }

    FloatingShapeElement? ParseLineShape(
        WPS.WordprocessingShape wsp,
        WPS.ShapeProperties shapeProps,
        AnchorPositioning positioning,
        AccumulatedTransform accumTransform,
        bool behindText)
    {
        var (lineColor, lineWidth, _) = ShapeParser.ExtractLineStyle(wsp, shapeProps, currentThemeColors);
        if (lineColor == null || lineWidth is not > 0)
        {
            return null;
        }

        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();
        if (xfrm?.Offset is not { } off ||
            xfrm.Extents is not { } ext)
        {
            return null;
        }

        // Rotation and dash patterns aren't applied by the rect-stroke render path used for
        // line shapes — drawing them anyway produces worse output than leaving them out
        // (e.g. a 90° dashed connector becomes a solid vertical line bisecting the page).
        if (xfrm.Rotation?.Value is { } rot && rot != 0)
        {
            return null;
        }

        var directLn = shapeProps.GetFirstChild<A.Outline>();
        if (directLn?.GetFirstChild<A.PresetDash>() != null)
        {
            return null;
        }

        long shapeX = off.X ?? 0;
        long shapeY = off.Y ?? 0;
        var finalX = accumTransform.OffsetX + shapeX * accumTransform.ScaleX;
        var finalY = accumTransform.OffsetY + shapeY * accumTransform.ScaleY;
        var widthPt = ((ext.Cx ?? 0) * accumTransform.ScaleX).EmuToPoints();
        var heightPt = ((ext.Cy ?? 0) * accumTransform.ScaleY).EmuToPoints();

        // Lines have a zero extent on the perpendicular axis. Bail if both are zero
        // (degenerate point) — there's nothing to draw.
        if (widthPt <= 0 && heightPt <= 0)
        {
            return null;
        }

        return new()
        {
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            HorizontalPositionPoints = positioning.HorizontalPositionPoints + finalX.EmuToPoints(),
            VerticalPositionPoints = positioning.VerticalPositionPoints + finalY.EmuToPoints(),
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            BehindText = behindText,
            LineColorHex = lineColor,
            LineWidthPoints = lineWidth
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

        if (!HasRenderableContent(content))
        {
            // See note in ParseTextBoxFromShape — empty txbx shapes are decorative-shape carriers,
            // not real text boxes; emitting one would mask any same-anchor image overlay.
            return null;
        }

        // Get background color if present
        string? bgColor = null;
        var solidFill = shapeProps?.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            bgColor = ShapeParser.ExtractSolidFillColor(solidFill, currentThemeColors);
        }

        // The shape's own chrome: a:ln outline plus any non-rect geometry (roundRect ticket
        // outlines, plaque frames, custGeom) — drawn behind the text content.
        var (lineColor, lineWidth, lineAlpha) = shapeProps == null
            ? (null, null, 1)
            : ShapeParser.ExtractLineStyle(wsp, shapeProps, currentThemeColors);
        var subpaths = shapeProps == null
            ? null
            : ShapeParser.ExtractSubpaths(shapeProps)
              ?? PresetShapeGeometry.TryBuild(shapeProps.GetFirstChild<A.PresetGeometry>(), widthPt, heightPt);

        return new()
        {
            Content = content,
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            HorizontalPositionPoints = xPt,
            VerticalPositionPoints = yPt,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            WrapType = WrapType.None,
            BehindText = behindText,
            BackgroundColorHex = bgColor,
            LineColorHex = lineColor,
            LineWidthPoints = lineWidth ?? 0,
            LineAlpha = lineAlpha,
            Subpaths = subpaths,
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

        if (HasGroupSiblings(wsp))
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

        var wordArtText = textBuilder.TrimmedToString();
        if (wordArtText.Length == 0)
        {
            return null;
        }

        // Parse font properties from the first run
        var fontFamily = effectiveDefaultFont;
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
                fontSize = double.Parse(fontSizeElement.Val.Value!).HalfPointsToPoints();
            }

            var boldElement = runProps.GetFirstChild<Bold>();
            if (boldElement != null)
            {
                bold = boldElement.IsOn();
            }

            var italicElement = runProps.GetFirstChild<Italic>();
            if (italicElement != null)
            {
                italic = italicElement.IsOn();
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
            var positioning = anchor.ParsePositioning();

            return new FloatingWordArtElement
            {
                Text = wordArtText,
                WidthPoints = widthPoints,
                HeightPoints = heightPoints,
                HorizontalPositionPoints = positioning.HorizontalPositionPoints,
                VerticalPositionPoints = positioning.VerticalPositionPoints,
                HorizontalAnchor = positioning.HorizontalAnchor,
                VerticalAnchor = positioning.VerticalAnchor,
                HorizontalPositionPercent = positioning.HorizontalPositionPercent,
                VerticalPositionPercent = positioning.VerticalPositionPercent,
                BehindText = positioning.BehindText,
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

        // Top+bottom envelope warps: both edges curve away from the centre line (Inflate)
        // or toward it (Deflate). InflateTop/Bottom and DeflateTop/Bottom only curve one
        // edge — closest match is the symmetric variant rather than a separate enum value.
        if (preset == A.TextShapeValues.TextInflate ||
            preset == A.TextShapeValues.TextInflateTop ||
            preset == A.TextShapeValues.TextInflateBottom)
        {
            return WordArtTransform.Inflate;
        }

        if (preset == A.TextShapeValues.TextDeflate ||
            preset == A.TextShapeValues.TextDeflateTop ||
            preset == A.TextShapeValues.TextDeflateBottom)
        {
            return WordArtTransform.Deflate;
        }

        if (preset == A.TextShapeValues.TextCanUp)
        {
            return WordArtTransform.CanUp;
        }

        if (preset == A.TextShapeValues.TextCanDown)
        {
            return WordArtTransform.CanDown;
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
        if (props.Descendants().Any(_ => _.LocalName == "checkbox"))
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
        string? dateFormat = null;

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
            .FirstOrDefault(_ => _.LocalName == "checkbox");
        if (checkbox14 != null)
        {
            controlType = ContentControlType.CheckBox;
            var checkedElement = checkbox14.Descendants()
                .FirstOrDefault(_ => _.LocalName == "checked");
            var checkedVal = checkedElement?.AttributeValue("val");
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

            // Capture w:dateFormat for the empty-run-text fallback. Word displays the run text
            // verbatim, so this is only used when the control carries a fullDate but no content.
            dateFormat = dateControl?.GetFirstChild<DateFormat>()?.Val?.Value;
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
                // Check for line breaks within the run. The run may also contain text after
                // the break (e.g. <w:r><w:br/><w:t>Sharma</w:t></w:r>) — emit a newline run
                // for the break, then fall through to also parse the text content so neither
                // half of the run is dropped.
                var breakElement = run.GetFirstChild<Break>();
                if (breakElement != null &&
                    breakElement.Type?.Value != BreakValues.Page &&
                    breakElement.Type?.Value != BreakValues.Column)
                {
                    var runProps = ParseRunProperties(run.RunProperties, mainPart);
                    styledRuns.Add(
                        new()
                        {
                            Text = "\n",
                            Properties = runProps
                        });
                }

                styledRuns.AddRange(ParseRun(run, mainPart, paragraphStyleId));
            }

            // Also build plain text content for backward compatibility
            content = string.Concat(styledRuns.Select(_ => _.Text));
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
            DateFormat = dateFormat,
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
            var defaultElement = checkbox.ChildElements.FirstOrDefault(_ => _.LocalName == "default");
            var sizeElement = checkbox.GetFirstChild<FormFieldSize>();

            var isChecked = checkedElement != null &&
                            (checkedElement.Val == null || checkedElement.Val.Value);
            var defaultChecked = false;
            if (defaultElement != null)
            {
                // Check if it has a val attribute with false value
                var val = defaultElement.AttributeValue("val");
                defaultChecked = val == null || (val != "0" && !val.Equals("false", StringComparison.CurrentCultureIgnoreCase));
            }

            double size = 0;
            if (sizeElement?.Val?.HasValue == true && double.TryParse(sizeElement.Val.Value, out var sizeValue))
            {
                size = sizeValue.HalfPointsToPoints();
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

    SectionBreakElement ParseSectionBreak(SectionProperties sectionProps)
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

    static IReadOnlyList<TabStop> ParseTabs(OpenXmlCompositeElement? props, IReadOnlyList<TabStop> inheritedTabs)
    {
        var tabsEl = props?.GetFirstChild<OoxmlTabs>();
        if (tabsEl == null)
        {
            return inheritedTabs;
        }

        // Merge inherited + inline. Inline w:val="clear" removes inherited stops at that position.
        var map = new Dictionary<double, TabStop>();
        foreach (var inherited in inheritedTabs)
        {
            map[inherited.PositionPoints] = inherited;
        }

        foreach (var tabStop in tabsEl.Elements<OoxmlTabStop>())
        {
            if (tabStop.Position?.HasValue != true)
            {
                continue;
            }

            var positionPoints = tabStop.Position.Value / twipsPerPoint;
            var alignment = MapTabAlignment(tabStop.Val?.InnerText);
            if (alignment == TabAlignment.Clear)
            {
                map.Remove(positionPoints);
                continue;
            }

            map[positionPoints] = new()
            {
                PositionPoints = positionPoints,
                Alignment = alignment,
                Leader = MapTabLeader(tabStop.Leader?.InnerText)
            };
        }

        return [.. map.Values.OrderBy(_ => _.PositionPoints)];
    }

    static TabAlignment MapTabAlignment(string? val) =>
        val switch
        {
            "clear" => TabAlignment.Clear,
            "center" => TabAlignment.Center,
            "right" or "end" => TabAlignment.Right,
            "decimal" => TabAlignment.Decimal,
            "bar" => TabAlignment.Bar,
            _ => TabAlignment.Left
        };

    static TabLeader MapTabLeader(string? val) =>
        val switch
        {
            "dot" => TabLeader.Dot,
            "hyphen" => TabLeader.Hyphen,
            "underscore" => TabLeader.Underscore,
            "middleDot" => TabLeader.MiddleDot,
            "heavy" => TabLeader.Heavy,
            _ => TabLeader.None
        };

    ParagraphProperties ParseParagraphProperties(OoxmlParagraphProperties? props, MainDocumentPart mainPart, string? styleId = null, bool omitParagraphMark = false)
    {
        // Get style defaults if available. Paragraphs without an explicit w:pStyle still
        // inherit the document's default paragraph style (the one with w:default="1",
        // typically Normal) — without this, properties like ind=0 from Normal don't
        // override the indent inherited from pPrDefault.
        ParagraphProperties? styleDefaults = null;
        var effectiveStyleId = styleId ?? defaultParagraphStyleId;
        if (styleParagraphProperties != null && effectiveStyleId != null)
        {
            styleParagraphProperties.TryGetValue(effectiveStyleId, out styleDefaults);
        }

        // Resolve the paragraph mark's run formatting (w:pPr/w:rPr over the paragraph style
        // chain) the same way a run without direct formatting resolves. Word derives an empty
        // paragraph's line height from the mark, not from the first run or a fixed default.
        // Table cells and headers/footers are excluded for now: cell empty-mark heights are
        // entangled with exact-row clipping and Word's end-of-cell mark collapse, and a footer
        // empty changes the content box of every page — both need their own rules first
        // (see page_counts.md, pass 4).
        var paragraphMark = omitParagraphMark
            ? null
            : ParseRunProperties(props?.ParagraphMarkRunProperties, mainPart, styleId);

        // Start with style defaults or document defaults (pPrDefault)
        var alignment = styleDefaults?.Alignment ?? defaultAlignment;
        var spacingBefore = styleDefaults?.SpacingBeforePoints ?? defaultSpacingBeforePoints;
        var spacingAfter = styleDefaults?.SpacingAfterPoints ?? defaultSpacingAfterPoints;
        // When no style applies (bare docx without styles.xml), Word falls back to its built-in
        // Normal style — see defaultLineSpacingMultiplier, which carries that value only when the
        // document declares no styles.xml or no docDefaults, and 1.08 otherwise.
        var lineSpacingMultiplier = styleDefaults?.LineSpacingMultiplier ?? defaultLineSpacingMultiplier;
        var lineSpacingPoints = styleDefaults?.LineSpacingPoints ?? 0;
        var lineSpacingRule = styleDefaults?.LineSpacingRule ?? LineSpacingRule.Auto;
        var firstLineIndent = styleDefaults?.FirstLineIndentPoints ?? 0;
        var leftIndent = styleDefaults?.LeftIndentPoints ?? defaultLeftIndentPoints;
        var rightIndent = styleDefaults?.RightIndentPoints ?? defaultRightIndentPoints;
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
                ParagraphMarkRunProperties = paragraphMark,
                BackgroundColorHex = backgroundColor,
                StyleId = styleId,
                TabStops = styleDefaults?.TabStops ?? [],
                DefaultTabStopPoints = defaultTabStopPoints
            };
        }

        // Override with inline properties. One pass over the children replaces ~20
        // GetFirstChild probes (each restarts at FirstChild and rescans the siblings);
        // ??= keeps GetFirstChild's first-in-document-order semantics on duplicates.
        Justification? justification = null;
        SpacingBetweenLines? spacing = null;
        Indentation? indentation = null;
        SuppressLineNumbers? suppressLineNumbersElement = null;
        SuppressAutoHyphens? suppressAutoHyphensElement = null;
        ContextualSpacing? contextualSpacingElement = null;
        KeepLines? keepLinesElement = null;
        KeepNext? keepNextElement = null;
        WidowControl? widowControlEl = null;
        PageBreakBefore? pageBreakBeforeElement = null;
        MirrorIndents? mirrorIndentsElement = null;
        Shading? shadingElement = null;
        ParagraphBorders? pBdr = null;
        BiDi? bidi = null;
        FrameProperties? framePr = null;

        foreach (var child in props.ChildElements)
        {
            switch (child)
            {
                case Justification element:
                    justification ??= element;
                    break;
                case SpacingBetweenLines element:
                    spacing ??= element;
                    break;
                case Indentation element:
                    indentation ??= element;
                    break;
                case SuppressLineNumbers element:
                    suppressLineNumbersElement ??= element;
                    break;
                case SuppressAutoHyphens element:
                    suppressAutoHyphensElement ??= element;
                    break;
                case ContextualSpacing element:
                    contextualSpacingElement ??= element;
                    break;
                case KeepLines element:
                    keepLinesElement ??= element;
                    break;
                case KeepNext element:
                    keepNextElement ??= element;
                    break;
                case WidowControl element:
                    widowControlEl ??= element;
                    break;
                case PageBreakBefore element:
                    pageBreakBeforeElement ??= element;
                    break;
                case MirrorIndents element:
                    mirrorIndentsElement ??= element;
                    break;
                case Shading element:
                    shadingElement ??= element;
                    break;
                case ParagraphBorders element:
                    pBdr ??= element;
                    break;
                case BiDi element:
                    bidi ??= element;
                    break;
                case FrameProperties element:
                    framePr ??= element;
                    break;
            }
        }

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
        suppressLineNumbers = suppressLineNumbersElement != null;
        suppressAutoHyphens = suppressAutoHyphensElement != null;

        // Contextual spacing collapses space between paragraphs with matching styles
        if (contextualSpacingElement != null)
        {
            contextualSpacing = true;
        }

        // Parse pagination properties
        if (keepLinesElement != null)
        {
            keepLines = true;
        }

        if (keepNextElement != null)
        {
            keepNext = true;
        }

        // WidowControl element toggles the control - presence means off if val is false/0, on if val is true/1 or absent
        if (widowControlEl != null)
        {
            // If the element exists with val="0" or val="false", widow control is disabled
            // If val is missing or true, it's enabled (but we default to true anyway)
            var valAttribute = widowControlEl.Val;
            if (valAttribute != null && valAttribute.HasValue)
            {
                widowControl = valAttribute.Value;
            }
            else
            {
                widowControl = true; // Presence without val means enabled
            }
        }

        if (pageBreakBeforeElement != null)
        {
            pageBreakBefore = true;
        }

        // w:mirrorIndents — left/right indents swap on even pages (mirror printing).
        var mirrorIndents = (styleDefaults?.MirrorIndents ?? false) || mirrorIndentsElement != null;

        // Parse paragraph shading/background color (w:shd element)
        if (shadingElement != null)
        {
            string? inlineBgColor = null;
            // Check for theme fill color first, then direct fill value
            var themeFill = shadingElement.ThemeFill?.Value;
            if (themeFill != null && currentThemeColors != null)
            {
                var themeFillValue = ((IEnumValue) themeFill).Value;
                inlineBgColor = currentThemeColors.ResolveColor(themeFillValue);
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

        // Parse paragraph borders (w:pBdr)
        var borders = styleDefaults?.Borders;
        var borderTopSpace = styleDefaults?.BorderTopSpacePoints ?? 0;
        var borderBottomSpace = styleDefaults?.BorderBottomSpacePoints ?? 0;
        var borderLeftSpace = styleDefaults?.BorderLeftSpacePoints ?? 0;
        var borderRightSpace = styleDefaults?.BorderRightSpacePoints ?? 0;
        var borderBetween = styleDefaults?.BorderBetween ?? BorderEdge.None;
        var borderBetweenSpace = styleDefaults?.BorderBetweenSpacePoints ?? 0;
        if (pBdr != null)
        {
            var topBorder = pBdr.GetFirstChild<TopBorder>();
            var rightBorder = pBdr.GetFirstChild<RightBorder>();
            var bottomBorder = pBdr.GetFirstChild<BottomBorder>();
            var leftBorder = pBdr.GetFirstChild<LeftBorder>();
            var betweenBorder = pBdr.GetFirstChild<BetweenBorder>();
            borders = new()
            {
                Top = ParseBorderEdge(topBorder),
                Right = ParseBorderEdge(rightBorder),
                Bottom = ParseBorderEdge(bottomBorder),
                Left = ParseBorderEdge(leftBorder)
            };
            borderTopSpace = ParseBorderSpace(topBorder);
            borderRightSpace = ParseBorderSpace(rightBorder);
            borderBottomSpace = ParseBorderSpace(bottomBorder);
            borderLeftSpace = ParseBorderSpace(leftBorder);
            borderBetween = ParseBorderEdge(betweenBorder);
            borderBetweenSpace = ParseBorderSpace(betweenBorder);
        }

        // Parse paragraph mark font size (used for empty paragraphs)
        double? paragraphMarkFontSize = null;
        var paragraphMarkRunProps = props.ParagraphMarkRunProperties;
        if (paragraphMarkRunProps != null)
        {
            var fontSize = paragraphMarkRunProps.GetFirstChild<FontSize>();
            if (fontSize?.Val?.HasValue == true && double.TryParse(fontSize.Val.Value, out var halfPoints))
            {
                paragraphMarkFontSize = halfPoints.HalfPointsToPoints();
            }
        }

        // RTL paragraph (w:bidi)
        var paraRtl = false;
        if (bidi != null)
        {
            paraRtl = bidi.IsOn();
        }

        // Parse drop cap (w:framePr/w:dropCap, w:framePr/w:lines).
        // The OOXML SDK exposes DropCap as a typed EnumValue, but reading via the raw attribute
        // is more reliable across SDK versions.
        var dropCap = DropCapPosition.None;
        var dropCapLines = 0;
        var frame = styleDefaults?.Frame;
        if (framePr != null)
        {
            var attributes = framePr.GetAttributes();
            var dropCapAttribute = attributes.AttributeValue("dropCap");
            if (string.Equals(dropCapAttribute, "drop", StringComparison.OrdinalIgnoreCase))
            {
                dropCap = DropCapPosition.Drop;
            }
            else if (string.Equals(dropCapAttribute, "margin", StringComparison.OrdinalIgnoreCase))
            {
                dropCap = DropCapPosition.Margin;
            }

            if (int.TryParse(attributes.AttributeValue("lines"), out var parsedLines))
            {
                dropCapLines = parsedLines;
            }

            // Frame positioning. When the paragraph's style carries a frame, its positioning is
            // authoritative: Word's editor re-emits a direct framePr full of neutral defaults
            // (xAlign="left", yAlign="inline", vAnchor="margin") whenever the framed paragraph is
            // touched, and those defaults must not clobber the style's real placement (observed
            // across the agendas / letters / labels templates, where the styled frame's xAlign and
            // anchors are what Word actually renders). So prefer the style frame; only parse the
            // direct framePr as the frame when the style has none.
            frame = styleDefaults?.Frame ?? ParseParagraphFrame(framePr);
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
            ParagraphMarkRunProperties = paragraphMark,
            BackgroundColorHex = backgroundColor,
            StyleId = styleId,
            Borders = borders,
            BorderTopSpacePoints = borderTopSpace,
            BorderBottomSpacePoints = borderBottomSpace,
            BorderLeftSpacePoints = borderLeftSpace,
            BorderRightSpacePoints = borderRightSpace,
            BorderBetween = borderBetween,
            BorderBetweenSpacePoints = borderBetweenSpace,
            TabStops = ParseTabs(props, styleDefaults?.TabStops ?? []),
            DefaultTabStopPoints = defaultTabStopPoints,
            DropCap = dropCap,
            DropCapLines = dropCapLines,
            Frame = frame,
            IsRightToLeft = paraRtl,
            MirrorIndents = mirrorIndents
        };
    }

    /// <summary>
    /// Parses the positioning subset of a <c>w:framePr</c> (anchors, alignment, explicit offset,
    /// and size) into a <see cref="ParagraphFrame"/>, layering present attributes over
    /// <paramref name="baseFrame"/> (the style's frame). Returns null when neither the element nor
    /// the base carries any positioning signal — i.e. a drop-cap-only frame — so drop-cap handling
    /// is left untouched.
    /// </summary>
    static ParagraphFrame? ParseParagraphFrame(FrameProperties framePr, ParagraphFrame? baseFrame = null)
    {
        var attributes = framePr.GetAttributes();

        var hAnchorRaw = attributes.AttributeValue("hAnchor");
        var vAnchorRaw = attributes.AttributeValue("vAnchor");
        var xAlignRaw = attributes.AttributeValue("xAlign");
        var yAlignRaw = attributes.AttributeValue("yAlign");
        var xRaw = attributes.AttributeValue("x");
        var yRaw = attributes.AttributeValue("y");
        var widthRaw = attributes.AttributeValue("w");
        var heightRaw = attributes.AttributeValue("h");

        // Only treat this as a positioning frame when this element or the inherited style frame
        // carries a positioning attribute. A bare drop-cap frame (just dropCap/lines) with no
        // inherited frame must NOT become a ParagraphFrame.
        var hasPositioning = hAnchorRaw != null || vAnchorRaw != null ||
                             xAlignRaw != null || yAlignRaw != null ||
                             xRaw != null || yRaw != null ||
                             widthRaw != null || heightRaw != null;
        if (!hasPositioning && baseFrame == null)
        {
            return null;
        }

        return new()
        {
            HorizontalAnchor = hAnchorRaw switch
            {
                "page" => HorizontalAnchor.Page,
                "margin" => HorizontalAnchor.Margin,
                // "text" anchors to the text column's leading edge.
                "text" or "column" => HorizontalAnchor.Column,
                _ => baseFrame?.HorizontalAnchor ?? HorizontalAnchor.Column
            },
            VerticalAnchor = vAnchorRaw switch
            {
                "page" => VerticalAnchor.Page,
                "text" => VerticalAnchor.Paragraph,
                "margin" => VerticalAnchor.Margin,
                _ => baseFrame?.VerticalAnchor ?? VerticalAnchor.Margin
            },
            HorizontalAlignment = xAlignRaw switch
            {
                "left" or "inside" => FrameHorizontalAlignment.Left,
                "center" => FrameHorizontalAlignment.Center,
                "right" or "outside" => FrameHorizontalAlignment.Right,
                _ => baseFrame?.HorizontalAlignment ?? FrameHorizontalAlignment.None
            },
            VerticalAlignment = yAlignRaw switch
            {
                "top" or "inside" => FrameVerticalAlignment.Top,
                "center" => FrameVerticalAlignment.Center,
                "bottom" or "outside" => FrameVerticalAlignment.Bottom,
                "inline" => FrameVerticalAlignment.Inline,
                _ => baseFrame?.VerticalAlignment ?? FrameVerticalAlignment.None
            },
            XPoints = int.TryParse(xRaw, out var xTwips) ? xTwips / twipsPerPoint : baseFrame?.XPoints ?? 0,
            YPoints = int.TryParse(yRaw, out var yTwips) ? yTwips / twipsPerPoint : baseFrame?.YPoints ?? 0,
            WidthPoints = int.TryParse(widthRaw, out var widthTwips) && widthTwips > 0 ? widthTwips / twipsPerPoint : baseFrame?.WidthPoints,
            HeightPoints = int.TryParse(heightRaw, out var heightTwips) && heightTwips > 0 ? heightTwips / twipsPerPoint : baseFrame?.HeightPoints
        };
    }

    // Unicode characters for hyphenation
    const char softHyphenChar = '\u00AD'; // Soft hyphen (optional break point)
    const char nonBreakingHyphenChar = '\u2011'; // Non-breaking hyphen

    string? ResolveHyperlinkUrl(Hyperlink hyperlink, MainDocumentPart mainPart)
    {
        // External links carry an r:id pointing at a HyperlinkRelationship; internal links to a
        // bookmark carry a w:anchor. Combine both: an external target may also include a sub-anchor.
        string? target = null;
        if (hyperlink.Id?.Value is { } relationshipId)
        {
            if (hyperlinkUrlsByRelId == null)
            {
                hyperlinkUrlsByRelId = new(StringComparer.Ordinal);
                foreach (var relationship in mainPart.HyperlinkRelationships)
                {
                    hyperlinkUrlsByRelId.TryAdd(
                        relationship.Id,
                        relationship.Uri.IsAbsoluteUri ? relationship.Uri.AbsoluteUri : relationship.Uri.OriginalString);
                }
            }

            hyperlinkUrlsByRelId.TryGetValue(relationshipId, out target);
        }

        if (hyperlink.Anchor?.Value is { Length: > 0 } anchor)
        {
            return target == null ? $"#{anchor}" : $"{target}#{anchor}";
        }

        return target;
    }

    // Decompressed part bytes, one buffer per part per parse (see imagePartBytes).
    byte[] GetPartBytes(OpenXmlPart part)
    {
        if (!imagePartBytes.TryGetValue(part, out var bytes))
        {
            using var stream = part.GetStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            imagePartBytes[part] = bytes;
        }

        return bytes;
    }

    // First-wins on duplicate style ids, matching the FirstOrDefault scan this replaces.
    Style? LookupStyle(MainDocumentPart mainPart, string styleId)
    {
        if (stylesById == null)
        {
            stylesById = new(StringComparer.Ordinal);
            var styles = mainPart.StyleDefinitionsPart?.Styles;
            if (styles != null)
            {
                foreach (var style in styles.Elements<Style>())
                {
                    if (style.StyleId?.Value is { } id)
                    {
                        stylesById.TryAdd(id, style);
                    }
                }
            }
        }

        return stylesById.TryGetValue(styleId, out var match) ? match : null;
    }

    List<Run> ParseRun(OoxmlRun run, MainDocumentPart mainPart, string? paragraphStyleId = null, string? hyperlinkUrl = null, bool emitLineBreaks = false)
    {
        var result = new List<Run>();
        RunProperties? properties = null;

        RunProperties GetProperties() =>
            properties ??= ParseRunProperties(run.RunProperties, mainPart, paragraphStyleId);

        // w:vanish / w:specVanish — drop hidden runs at parse time so they don't enter
        // measurement or rendering. Cheaper than filtering at every render call site.
        if (GetProperties().Hidden)
        {
            return result;
        }

        // Walk children in order so w:tab splits the run into separate model Runs (text, tab, text, ...)
        var textBuilder = new StringBuilder();

        void FlushText()
        {
            if (textBuilder.Length == 0)
            {
                return;
            }

            result.Add(
                new()
                {
                    Text = textBuilder.ToString(),
                    Properties = GetProperties(),
                    HyperlinkUrl = hyperlinkUrl
                });
            textBuilder.Clear();
        }

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text textElement:
                    textBuilder.Append(textElement.Text);
                    break;
                case SoftHyphen:
                    textBuilder.Append(softHyphenChar);
                    break;
                case NoBreakHyphen:
                    textBuilder.Append(nonBreakingHyphenChar);
                    break;
                case TabChar:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = "\t",
                            Properties = GetProperties(),
                            IsTab = true,
                            HyperlinkUrl = hyperlinkUrl
                        });
                    break;
                // A w:footnoteReference / w:endnoteReference marks where a note is cited. The note
                // body lives in footnotes.xml / endnotes.xml (ExtractFootnotes / ExtractEndnotes);
                // here we only record the citation position as an empty marker run so the text
                // exporters can emit a reference and a trailing notes section.
                case FootnoteReference footnoteReference when footnoteReference.Id?.Value is { } footnoteId:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = "",
                            Properties = GetProperties(),
                            HyperlinkUrl = hyperlinkUrl,
                            FootnoteReferenceId = footnoteId.ToString()
                        });
                    break;
                case EndnoteReference endnoteReference when endnoteReference.Id?.Value is { } endnoteId:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = "",
                            Properties = GetProperties(),
                            HyperlinkUrl = hyperlinkUrl,
                            EndnoteReferenceId = endnoteId.ToString()
                        });
                    break;
                // A <w:br/> inside a run becomes a newline at its document position, so text on
                // either side of it (e.g. <w:r><w:br/><w:t>An</w:t></w:r>) survives in order.
                // Opt-in: callers that emit their own break runs (the SDT paths) leave this off.
                // Page/column breaks are structural and stay with the caller's paragraph splitting.
                case Break breakChild when emitLineBreaks &&
                                           breakChild.Type?.Value != BreakValues.Page &&
                                           breakChild.Type?.Value != BreakValues.Column:
                    FlushText();
                    result.Add(
                        new()
                        {
                            Text = "\n",
                            Properties = GetProperties(),
                            HyperlinkUrl = hyperlinkUrl
                        });
                    break;
            }
        }

        FlushText();
        return result;
    }

    // props is any rPr-shaped element: a run's w:rPr (OoxmlRunProperties) or a paragraph
    // mark's w:pPr/w:rPr (ParagraphMarkRunProperties) — both carry the same children.
    RunProperties ParseRunProperties(OpenXmlElement? props, MainDocumentPart mainPart, string? paragraphStyleId = null)
    {
        // Start with defaults from paragraph style if available
        // If no explicit style, default to "Normal" which is the implicit default style in Word
        RunProperties? styleDefaults = null;
        if (styleRunProperties != null)
        {
            var styleId = paragraphStyleId ?? "Normal";
            styleRunProperties.TryGetValue(styleId, out styleDefaults);
        }

        // If no inline properties, return style defaults or empty properties.
        // When neither is present, anchor the font to the document default rather than the
        // RunProperties record's static default (which is Georgia, the cross-platform fallback).
        if (props == null)
        {
            if (styleDefaults != null)
            {
                // The style-chain sentinel for w:color w:val="auto" must not escape into the model,
                // and a colourless chain still resolves to the contextual automatic colour (white on
                // a dark w:background). Styles absorb defaultRunColorHex when they are built, so a
                // null here means the whole chain is colourless.
                var styleColor = styleDefaults.ColorHex == automaticColorSentinel
                    ? automaticRunColorHex
                    : styleDefaults.ColorHex ?? automaticRunColorHex;
                return styleColor == styleDefaults.ColorHex
                    ? styleDefaults
                    : styleDefaults with {ColorHex = styleColor};
            }

            return new()
            {
                FontFamily = effectiveDefaultFont,
                FontSizePoints = effectiveDefaultFontSizePoints,
                ColorHex = defaultRunColorHex ?? automaticRunColorHex
            };
        }

        // Start with style defaults or built-in defaults
        var fontFamily = styleDefaults?.FontFamily ?? effectiveDefaultFont;
        var fontSize = styleDefaults?.FontSizePoints ?? effectiveDefaultFontSizePoints;
        var bold = styleDefaults?.Bold ?? false;
        var italic = styleDefaults?.Italic ?? false;
        var underline = styleDefaults?.Underline ?? false;
        var strikethrough = styleDefaults?.Strikethrough ?? false;
        var allCaps = styleDefaults?.AllCaps ?? false;
        var smallCaps = styleDefaults?.SmallCaps ?? false;
        var color = styleDefaults?.ColorHex ?? defaultRunColorHex ?? automaticRunColorHex;
        if (color == automaticColorSentinel)
        {
            color = automaticRunColorHex;
        }

        var backgroundColor = styleDefaults?.BackgroundColorHex;
        var verticalAlignment = styleDefaults?.VerticalAlignment ?? VerticalRunAlignment.Baseline;
        var characterSpacing = styleDefaults?.CharacterSpacingPoints ?? 0.0;

        // Override with inline properties if specified. One pass over the children replaces
        // ~35 GetFirstChild probes (each restarts at FirstChild and rescans the siblings);
        // ??= keeps GetFirstChild's first-in-document-order semantics on duplicate children.
        RunFonts? runFonts = null;
        FontSize? fontSizeElement = null;
        Bold? boldElement = null;
        Italic? italicElement = null;
        Underline? underlineElement = null;
        Strike? strikeElement = null;
        Caps? capsElement = null;
        SmallCaps? smallCapsElement = null;
        Spacing? spacingElement = null;
        Kern? kernElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Ligatures? ligaturesElement = null;
        RightToLeftText? rtlElement = null;
        DocumentFormat.OpenXml.Office2010.Word.TextOutlineEffect? outlineEffectElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Shadow? shadowElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Glow? glowElement = null;
        DocumentFormat.OpenXml.Office2010.Word.Reflection? reflectionElement = null;
        VerticalTextAlignment? vertAlignElement = null;
        Color? colorElement = null;
        Shading? shadingElement = null;
        Highlight? highlightElement = null;
        RunStyle? runStyleElement = null;
        Vanish? vanishElement = null;
        SpecVanish? specVanishElement = null;
        Position? positionElement = null;
        Border? bdrElement = null;
        Emboss? embossElement = null;
        Imprint? imprintElement = null;
        Outline? outlineElement = null;

        foreach (var child in props.ChildElements)
        {
            switch (child)
            {
                case RunFonts element:
                    runFonts ??= element;
                    break;
                case FontSize element:
                    fontSizeElement ??= element;
                    break;
                case Bold element:
                    boldElement ??= element;
                    break;
                case Italic element:
                    italicElement ??= element;
                    break;
                case Underline element:
                    underlineElement ??= element;
                    break;
                case Strike element:
                    strikeElement ??= element;
                    break;
                case Caps element:
                    capsElement ??= element;
                    break;
                case SmallCaps element:
                    smallCapsElement ??= element;
                    break;
                case Spacing element:
                    spacingElement ??= element;
                    break;
                case Kern element:
                    kernElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Ligatures element:
                    ligaturesElement ??= element;
                    break;
                case RightToLeftText element:
                    rtlElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.TextOutlineEffect element:
                    outlineEffectElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Shadow element:
                    shadowElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Glow element:
                    glowElement ??= element;
                    break;
                case DocumentFormat.OpenXml.Office2010.Word.Reflection element:
                    reflectionElement ??= element;
                    break;
                case VerticalTextAlignment element:
                    vertAlignElement ??= element;
                    break;
                case Color element:
                    colorElement ??= element;
                    break;
                case Shading element:
                    shadingElement ??= element;
                    break;
                case Highlight element:
                    highlightElement ??= element;
                    break;
                case RunStyle element:
                    runStyleElement ??= element;
                    break;
                case Vanish element:
                    vanishElement ??= element;
                    break;
                case SpecVanish element:
                    specVanishElement ??= element;
                    break;
                case Position element:
                    positionElement ??= element;
                    break;
                case Border element:
                    bdrElement ??= element;
                    break;
                case Emboss element:
                    embossElement ??= element;
                    break;
                case Imprint element:
                    imprintElement ??= element;
                    break;
                case Outline element:
                    outlineElement ??= element;
                    break;
            }
        }

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

        if (fontSizeElement?.Val?.HasValue == true)
        {
            fontSize = double.Parse(fontSizeElement.Val.Value!).HalfPointsToPoints();
        }

        if (boldElement != null)
        {
            bold = boldElement.IsOn();
        }

        if (italicElement != null)
        {
            italic = italicElement.IsOn();
        }

        if (underlineElement != null && underlineElement.Val?.Value != UnderlineValues.None)
        {
            underline = true;
        }

        if (strikeElement != null)
        {
            strikethrough = strikeElement.IsOn();
        }

        if (capsElement != null)
        {
            allCaps = capsElement.IsOn();
        }

        if (smallCapsElement != null)
        {
            smallCaps = smallCapsElement.IsOn();
        }

        // Character spacing (w:spacing in rPr — extra space between characters, in twips)
        if (spacingElement?.Val?.HasValue == true)
        {
            characterSpacing = spacingElement.Val.Value / twipsPerPoint;
        }

        // Kerning threshold (w:kern in rPr — half-points; 0 means kerning is off)
        double kerningMinFontSize = 0;
        if (kernElement?.Val?.HasValue == true)
        {
            kerningMinFontSize = kernElement.Val.Value.HalfPointsToPoints();
        }

        // Ligature mode (w14:ligatures in rPr — Word 2010+ extension)
        var ligatures = ParseLigatureMode(ligaturesElement);

        // RTL run (w:rtl)
        var runRtl = false;
        if (rtlElement != null)
        {
            runRtl = rtlElement.IsOn();
        }

        // w14 text effects (parameters captured for outline/shadow/glow; reflection is presence-only)
        var outline = ParseTextOutline(outlineEffectElement, color);
        var shadow = ParseTextShadow(shadowElement);
        var glow = ParseTextGlow(glowElement);
        var hasReflection = reflectionElement != null;

        // Vertical alignment (subscript/superscript)
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

        // An explicit w:color on the run overrides any inherited (style / docDefaults) colour.
        // Crucially this includes w:val="auto": "automatic" means the run opts back out to Word's
        // contrast colour (black on a light page, white on a dark w:background), so it must RESET
        // the inherited colour rather than fall through to it — otherwise a coloured docDefaults
        // (e.g. a card template's white text default) would leak onto runs that explicitly asked
        // for automatic. ResolveRunColor returns null for auto / unresolved.
        if (colorElement != null)
        {
            color = ResolveRunColor(colorElement) ?? automaticRunColorHex;
        }

        // Background/shading color (w:shd element)
        if (shadingElement != null)
        {
            string? inlineBgColor = null;
            // Check for theme fill color first, then direct fill value
            var themeFill = shadingElement.ThemeFill?.Value;
            if (themeFill != null && currentThemeColors != null)
            {
                var themeFillValue = ((IEnumValue) themeFill).Value;
                inlineBgColor = currentThemeColors.ResolveColor(themeFillValue);
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

        // w:highlight — Word's highlighter pen, distinct from w:shd. Values are a fixed
        // palette of named colors (yellow, green, cyan, ...). Mapped to BackgroundColorHex
        // so the same renderer path that handles shading paints the highlight.
        if (highlightElement?.Val?.HasValue == true)
        {
            var highlightHex = HighlightToHex(highlightElement.Val.Value);
            if (highlightHex != null)
            {
                backgroundColor = highlightHex;
            }
        }

        // Also check for run-specific style reference that overrides paragraph style
        // IMPORTANT: Only apply properties that are EXPLICITLY defined in the character style,
        // not inherited defaults. This ensures character styles like "Shade" (which only defines
        // background color) don't override font size from paragraph styles like Heading1.
        var runStyleId = runStyleElement?.Val?.Value;
        if (runStyleId != null && styleRunProperties != null && styleRunProperties.TryGetValue(runStyleId, out var runStyleProps))
        {
            // Look up the original style definition to check which properties are explicitly defined
            var originalStyle = LookupStyle(mainPart, runStyleId);
            var originalRPr = originalStyle?.StyleRunProperties;

            // Only override with run style properties that are EXPLICITLY defined in the style
            if (runFonts == null && originalRPr?.GetFirstChild<RunFonts>() != null)
            {
                fontFamily = runStyleProps.FontFamily;
            }

            if (fontSizeElement == null && originalRPr?.GetFirstChild<FontSize>() != null)
            {
                fontSize = runStyleProps.FontSizePoints;
            }

            if (boldElement == null && originalRPr?.GetFirstChild<Bold>() != null)
            {
                bold = runStyleProps.Bold;
            }

            if (italicElement == null && originalRPr?.GetFirstChild<Italic>() != null)
            {
                italic = runStyleProps.Italic;
            }

            if (underlineElement == null && originalRPr?.GetFirstChild<Underline>() != null)
            {
                underline = runStyleProps.Underline;
            }

            if (strikeElement == null && originalRPr?.GetFirstChild<Strike>() != null)
            {
                strikethrough = runStyleProps.Strikethrough;
            }

            if (capsElement == null && originalRPr?.GetFirstChild<Caps>() != null)
            {
                allCaps = runStyleProps.AllCaps;
            }

            if (smallCapsElement == null && originalRPr?.GetFirstChild<SmallCaps>() != null)
            {
                smallCaps = runStyleProps.SmallCaps;
            }

            if (spacingElement == null && originalRPr?.GetFirstChild<Spacing>() != null)
            {
                characterSpacing = runStyleProps.CharacterSpacingPoints;
            }

            if (colorElement == null && originalRPr?.GetFirstChild<Color>() != null)
            {
                color = runStyleProps.ColorHex == automaticColorSentinel
                    ? automaticRunColorHex
                    : runStyleProps.ColorHex;
            }

            if (shadingElement == null && originalRPr?.GetFirstChild<Shading>() != null)
            {
                backgroundColor = runStyleProps.BackgroundColorHex;
            }

            if (vertAlignElement == null && originalRPr?.GetFirstChild<VerticalTextAlignment>() != null)
            {
                verticalAlignment = runStyleProps.VerticalAlignment;
            }
        }

        // w:vanish / w:specVanish — hidden text. Either form skips the run during layout.
        // w:webHidden is intentionally not consumed: it hides only in web view, and Morph
        // renders for print/image so the runs stay visible (parsed-and-discarded).
        var hidden = styleDefaults?.Hidden ?? false;
        if (vanishElement != null)
        {
            hidden = vanishElement.IsOn();
        }

        if (specVanishElement != null)
        {
            hidden = true;
        }

        // w:position — baseline shift in half-points (positive = up, negative = down).
        // Distinct from w:vertAlign which also resizes the glyph.
        var baselineShift = styleDefaults?.BaselineShiftPoints ?? 0.0;
        if (positionElement?.Val?.HasValue == true &&
            double.TryParse(positionElement.Val.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var posHalfPts))
        {
            baselineShift = posHalfPts.HalfPointsToPoints();
        }

        // w:bdr — per-run border, modelled as a single BorderEdge applied around the run's
        // measured box. Reuses ParseBorderEdge so dotted/dashed/single all flow through.
        var runBorder = styleDefaults?.Border;
        if (bdrElement != null)
        {
            var parsed = ParseBorderEdge(bdrElement);
            runBorder = parsed.IsVisible ? parsed : null;
        }

        // w:emboss / w:imprint / w:outline — three glyph-fill modifiers. Each is a presence
        // toggle (val="0" / val="false" turns it back off).
        var emboss = styleDefaults?.Emboss ?? false;
        if (embossElement != null)
        {
            emboss = embossElement.IsOn();
        }

        var imprint = styleDefaults?.Imprint ?? false;
        if (imprintElement != null)
        {
            imprint = imprintElement.IsOn();
        }

        var outlineOnly = styleDefaults?.OutlineOnly ?? false;
        if (outlineElement != null)
        {
            outlineOnly = outlineElement.IsOn();
        }

        // w:effect — animated text (blinkBackground, sparkle, etc.). Pure animation, so we
        // render the underlying text plain (parsed-and-discarded).

        return new()
        {
            FontFamily = fontFamily,
            FontSizePoints = fontSize,
            Bold = bold,
            Italic = italic,
            Underline = underline,
            Strikethrough = strikethrough,
            AllCaps = allCaps,
            SmallCaps = smallCaps,
            ColorHex = color,
            BackgroundColorHex = backgroundColor,
            CharacterSpacingPoints = characterSpacing,
            VerticalAlignment = verticalAlignment,
            KerningMinFontSizePoints = kerningMinFontSize,
            Ligatures = ligatures,
            IsRightToLeft = runRtl,
            Outline = outline,
            Shadow = shadow,
            Glow = glow,
            HasReflection = hasReflection,
            Hidden = hidden,
            BaselineShiftPoints = baselineShift,
            Border = runBorder,
            Emboss = emboss,
            Imprint = imprint,
            OutlineOnly = outlineOnly
        };
    }

    static void AppendMathText(List<Run> runs, OpenXmlElement mathElement)
    {
        // Math text inherits the surrounding run's font size when available so equations don't
        // collapse to the 11pt default. Variables render italic by default — Word's Cambria-Math
        // convention; numbers and operators stay upright (handled inside EmitMathRun).
        var context = runs.Count > 0 ? runs[^1].Properties : new();
        WalkMath(mathElement, runs, context with
        {
            Italic = true
        });
    }

    static void WalkMath(OpenXmlElement element, List<Run> runs, RunProperties props)
    {
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case DocumentFormat.OpenXml.Math.Run mathRun:
                    EmitMathRun(mathRun, runs, props);
                    break;

                case DocumentFormat.OpenXml.Math.Subscript sSub:
                {
                    var b = sSub.GetFirstChild<DocumentFormat.OpenXml.Math.Base>();
                    if (b != null) WalkMath(b, runs, props);
                    var sub = sSub.GetFirstChild<DocumentFormat.OpenXml.Math.SubArgument>();
                    if (sub != null)
                        WalkMath(sub, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Subscript
                        });
                    break;
                }

                case DocumentFormat.OpenXml.Math.Superscript sSup:
                {
                    var b = sSup.GetFirstChild<DocumentFormat.OpenXml.Math.Base>();
                    if (b != null) WalkMath(b, runs, props);
                    var sup = sSup.GetFirstChild<DocumentFormat.OpenXml.Math.SuperArgument>();
                    if (sup != null)
                        WalkMath(sup, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Superscript
                        });
                    break;
                }

                case DocumentFormat.OpenXml.Math.SubSuperscript sSubSup:
                {
                    var b = sSubSup.GetFirstChild<DocumentFormat.OpenXml.Math.Base>();
                    if (b != null) WalkMath(b, runs, props);
                    var sub = sSubSup.GetFirstChild<DocumentFormat.OpenXml.Math.SubArgument>();
                    if (sub != null)
                        WalkMath(sub, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Subscript
                        });
                    var sup = sSubSup.GetFirstChild<DocumentFormat.OpenXml.Math.SuperArgument>();
                    if (sup != null)
                        WalkMath(sup, runs, props with
                        {
                            VerticalAlignment = VerticalRunAlignment.Superscript
                        });
                    break;
                }

                case DocumentFormat.OpenXml.Math.Fraction frac:
                {
                    // Inline fallback: numerator "/" denominator. Stacked fractions need cutout
                    // layout the line engine doesn't model, so this is the closest we can get.
                    var num = frac.GetFirstChild<DocumentFormat.OpenXml.Math.Numerator>();
                    if (num != null) WalkMath(num, runs, props);
                    runs.Add(new()
                    {
                        Text = "/",
                        Properties = props with
                        {
                            Italic = false
                        }
                    });
                    var den = frac.GetFirstChild<DocumentFormat.OpenXml.Math.Denominator>();
                    if (den != null) WalkMath(den, runs, props);
                    break;
                }

                default:
                    // Recurse into other math composites (m:e, m:rad, m:nary, m:func, etc.)
                    // so their inner runs still surface as plain text.
                    if (child is OpenXmlCompositeElement composite)
                    {
                        WalkMath(composite, runs, props);
                    }

                    break;
            }
        }
    }

    static void EmitMathRun(DocumentFormat.OpenXml.Math.Run mathRun, List<Run> runs, RunProperties props)
    {
        foreach (var mathText in mathRun.Elements<DocumentFormat.OpenXml.Math.Text>())
        {
            var text = mathText.Text;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            // Math italic only applies to alphabetic variables; digits and operators stay upright.
            var isVariable = text.All(char.IsLetter);
            runs.Add(new()
            {
                Text = text,
                Properties = isVariable
                    ? props
                    : props with
                    {
                        Italic = false
                    }
            });
        }
    }

    static TextOutline? ParseTextOutline(
        DocumentFormat.OpenXml.Office2010.Word.TextOutlineEffect? element,
        string? fillColor)
    {
        if (element == null)
        {
            return null;
        }

        // LineWidth is in EMU; default to a thin stroke when absent.
        var widthEmu = element.LineWidth?.Value ?? (long) (0.75 * emusPerPoint);
        var widthPoints = widthEmu / emusPerPoint;

        var color = ReadW14SolidFillColor(element) ?? fillColor ?? "000000";
        return new()
        {
            ColorHex = color,
            WidthPoints = widthPoints
        };
    }

    static TextShadow? ParseTextShadow(DocumentFormat.OpenXml.Office2010.Word.Shadow? element)
    {
        if (element == null)
        {
            return null;
        }

        // Word's defaults when attributes absent: 4pt distance, 45deg, 4pt blur, semi-transparent black.
        var blurPoints = (element.BlurRadius?.Value ?? (long) (4 * emusPerPoint)) / emusPerPoint;
        var distancePoints = (element.DistanceFromText?.Value ?? (long) (4 * emusPerPoint)) / emusPerPoint;
        // DirectionAngle is in 60000ths of a degree; OOXML 0deg = right, 90deg = down.
        var directionDegrees = (element.DirectionAngle?.Value ?? 2700000) / 60000.0;

        var (color, alpha) = ReadW14ColorWithAlpha(element.RgbColorModelHex, element.SchemeColor);
        return new()
        {
            ColorHex = color ?? "000000",
            BlurPoints = blurPoints,
            DistancePoints = distancePoints,
            DirectionDegrees = directionDegrees,
            AlphaPercent = alpha ?? 50
        };
    }

    static TextGlow? ParseTextGlow(DocumentFormat.OpenXml.Office2010.Word.Glow? element)
    {
        if (element == null)
        {
            return null;
        }

        var radiusPoints = (element.GlowRadius?.Value ?? (long) (4 * emusPerPoint)) / emusPerPoint;
        var (color, alpha) = ReadW14ColorWithAlpha(element.RgbColorModelHex, element.SchemeColor);
        return new()
        {
            ColorHex = color ?? "FFFF00",
            RadiusPoints = radiusPoints,
            AlphaPercent = alpha ?? 60
        };
    }

    // Walks a w14 effect element looking for solidFill > srgbClr; returns null if not found.
    static string? ReadW14SolidFillColor(OpenXmlElement element)
    {
        foreach (var child in element.Descendants())
        {
            if (child is DocumentFormat.OpenXml.Office2010.Word.RgbColorModelHex {Val.HasValue: true} rgb)
            {
                return rgb.Val.Value;
            }
        }

        return null;
    }

    static (string? Color, int? AlphaPercent) ReadW14ColorWithAlpha(
        DocumentFormat.OpenXml.Office2010.Word.RgbColorModelHex? rgb,
        DocumentFormat.OpenXml.Office2010.Word.SchemeColor? scheme)
    {
        if (rgb?.Val?.HasValue == true)
        {
            return (rgb.Val.Value, ReadW14Alpha(rgb));
        }

        if (scheme != null)
        {
            // Scheme-colour resolution against the document theme is not yet modelled here.
            return (null, ReadW14Alpha(scheme));
        }

        return (null, null);
    }

    static int? ReadW14Alpha(OpenXmlElement parent)
    {
        var alpha = parent.GetFirstChild<DocumentFormat.OpenXml.Office2010.Word.Alpha>();
        if (alpha?.Val?.HasValue == true)
        {
            // w14:alpha is in 1000ths of a percent (100000 = 100%).
            return alpha.Val.Value / 1000;
        }

        return null;
    }
}
