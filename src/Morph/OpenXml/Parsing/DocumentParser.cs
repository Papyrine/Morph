using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

/// <summary>
/// Parses DOCX files using OpenXML.
/// </summary>
[SuppressMessage("Style", "IDE0028:Simplify collection initialization")]
[SuppressMessage("Style", "IDE0306:Simplify collection initialization")]
sealed partial class DocumentParser(string? defaultFont = null, bool? useLetterPageSize = null, bool captureSources = false, CultureInfo? culture = null)
{
    // Word's built-in defaults, supplied when a document declares no styles part or no
    // docDefaults at all: APTOS 12pt with w:spacing w:line="278" w:lineRule="auto" w:after="160".
    // The family follows the Word build (an October 2026 build gave Calibri); Word 16.0.20527 gives
    // Aptos 12pt — Styles("Normal").Font reads Aptos 12 and a render embeds Aptos alone, in bare
    // packages with no settings and at compatibility modes 14 and 15 alike — and the 147 style-less
    // references were regenerated on it on 2026-11-08 (docs/fidelity-audit.md).
    // The line pitch is unchanged by the family: Calibri's GDI cell and Aptos's hhea box are
    // both 1.2207em, so 12pt * 1.2207 * 278/240 = 35.35px at 150 DPI either way.
    // A package with no styles part but a theme takes the theme's minor font instead
    // (StylelessThemeFont), and a caller-supplied default font (ExportOptions.DefaultFont or a
    // customized DefaultFontSettings.DefaultFont) still overrides the family - see the constructor.
    const string builtInDefaultFontFamily = "Aptos";

    // A package with no styles part but a theme is set in the theme's minor font, at the built-in
    // size: a bare package with only labels/12's theme added (minor font Georgia) came out in
    // Georgia 12pt on Word 16.0.20527 (2026-11-08, read off its XPS). With a styles part the
    // docDefaults decide, and with no theme the built-in family does.
    string? StylelessThemeFont(MainDocumentPart mainPart) =>
        mainPart.StyleDefinitionsPart == null &&
        mainPart.ThemePart != null &&
        currentThemeFonts?.MinorFont is {Length: > 0} minor
            ? minor
            : null;

    const double builtInDefaultFontSizePoints = 12.0;

    // Word's built-in Normal kerns (measured, threshold unprobed - kerning was observed at every
    // size tried, 10pt and up, so 1pt errs toward kerning small text the way Word appears to).
    const double builtInKerningMinPoints = 1.0;

    const double builtInLineSpacingMultiplier = 278 / 240.0;

    const double builtInSpacingAfterPoints = 8;

    // ECMA-376 §17.3.2.38: an omitted w:sz is 20 half-points. This spec default — not the
    // normal.dotm 12pt above — applies whenever a document declares docDefaults at all, because
    // Word reads the omission as an explicit 10pt, exactly as it reads an omitted pPrDefault as
    // zero spacing (see ExtractDefaultParagraphProperties). Only a document with no styles part
    // or no docDefaults element falls through to the built-in.
    // Word-probe verified against brochures/05, which declares docDefaults and no w:sz anywhere:
    // injecting an explicit w:sz="20" reproduces Word's render with zero differing text pixels
    // (the only delta is JPEG noise in the re-zipped photo), while w:sz="24" repaginates it from
    // 4 pages to 5. See docs/fidelity-audit.md.
    const double specDefaultFontSizePoints = 10.0;

    // Caller-customized default family for documents that do not specify one in docDefaults:
    // the export options' DefaultFont, or a customized DefaultFontSettings.DefaultFont. Null
    // means uncustomized, and the Word-probed built-in above applies.

    public DocumentParser()
        : this(DefaultFontSettings.CustomizedDefaultFont)
    {
    }

    // Theme colors for the current document being parsed
    ThemeColors? currentThemeColors;

    // The main part's runs numbered in document order, when captureSources asks for every model run
    // to be stamped with where its text came from (Run.Source). Null otherwise: the index costs a walk
    // of the whole body, and only the viewer's review features read the stamps.
    Dictionary<OoxmlRun, int>? runOrdinals;

    // The main part's paragraphs numbered the same way and on the same condition, for
    // ParagraphElement.Source: what lets the viewer's editor find the w:p a paragraph on a page is.
    Dictionary<Paragraph, int>? paragraphSources;

    // Each w:p a bookmark starts in, and the first paragraph of the model it was read as, for
    // Bookmark.Paragraph. Null when the body has no bookmark.
    Dictionary<Paragraph, ParagraphElement?>? bookmarkParagraphs;

    // Theme fonts for the current document being parsed
    ThemeFonts? currentThemeFonts;

    // Section transitions: for a given sectPr (end of a section), what settings apply to the next section?
    Dictionary<SectionProperties, PageSettings?>? nextSectionSettings;

    Dictionary<SectionProperties, SectionProperties?>? nextSectionProps;

    PageSettings? anchorAlignmentPage;

    int lastRenderedPageBreakCount;

    // The body's runs that stand in a comment's range (BodyScan.CommentedRuns), which take its id.
    Dictionary<OoxmlRun, string>? commentedRuns;

    // Style definitions cached during parsing (styleId -> full run properties)
    Dictionary<string, RunProperties>? styleRunProperties;

    // What each paragraph style's basedOn chain actually DECLARES, as against the resolved values in
    // styleRunProperties (which have the document defaults folded in and so cannot say who set what).
    // A table style outranks the document defaults but is outranked by a paragraph style, so layering it
    // in needs to know which of a style's values are its own. Populated beside styleRunProperties.
    Dictionary<string, DeclaredRunProperties>? styleDeclaredRunProperties;

    // The table-style run properties in force for the cell currently being parsed — the whole-table rPr
    // with the matching w:tblStylePr region layered over it. Saved and restored around each cell so a
    // nested table's context does not leak to its parent. Null outside a styled table, which is what
    // keeps every other document on the untouched resolution path.
    DeclaredRunProperties? tableStyleRunDefaults;

    // Raw style elements by styleId, first wins. ParseRunProperties consults the original style
    // XML for every run that carries w:rStyle (hyperlinks, TOC entries, note refs) to know which
    // properties the character style explicitly defines.
    Dictionary<string, Style>? stylesById;

    // Hyperlink relationship id → resolved URL. mainPart.HyperlinkRelationships is an OfType<>
    // filter over the part's full relationship list, re-enumerated per w:hyperlink — quadratic
    // on link-heavy documents (TOCs, bibliographies).
    Dictionary<string, string>? hyperlinkUrlsByRelId;

    // One decompressed buffer per image part, shared by every drawing that references it, so
    // repeated logos/icons hold one array and the render-side byte[]-identity caches dedupe them.
    readonly Dictionary<OpenXmlPart, byte[]> imagePartBytes = [];

    // Document default text colour from w:docDefaults/w:rPrDefault/w:color (theme-resolved). The
    // base of the colour cascade: every run that doesn't get a colour from a style or inline rPr
    // inherits this — including a white default (dark-board templates like menus/07 set FFFFFF
    // background1 and Word paints fall-through runs white). Null when docDefaults sets no colour
    // (the common case — runs stay automatic).
    string? defaultRunColorHex;

    // Citation-order numbering for note reference marks: Word displays the ordinal of the
    // citation (1, 2, 3...), not the footnotes.xml id (which starts at 2 - ids 0/-1 are the
    // separator stubs). First citation of an id assigns the next number; repeats reuse it.
    readonly Dictionary<string, int> footnoteCitationNumbers = [];

    readonly Dictionary<string, int> endnoteCitationNumbers = [];

    // The notes cited so far in the current numbering run: the whole document, or the section when its
    // w:footnotePr / w:endnotePr restarts each section (ParseSectionBreak resets them).
    int footnotesInSection;

    int endnotesInSection;

    // While a note body parses: the number its w:footnoteRef / w:endnoteRef run draws (ParseNoteBody).
    string? noteReferenceText;

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

    // Styles whose resolved indentation belongs to their numbering — declared beside an active
    // w:numPr, or inherited from such a style unchanged — and, for every style, the indentation that
    // survives when numbering is switched off (w:numPr/w:numId="0"): Word drops the numbering's
    // indent with the numbering. business-plans/08's Title is basedOn a numbered Heading 1 whose
    // w:ind left=720 hanging=720 is the level's; Title sets numId=0 and Word sets both title lines
    // flush at the margin (XPS: x=60.05 on the cell's 60.05 edge), not the second at 36pt.
    readonly HashSet<string> styleIndentBound = new(StringComparer.OrdinalIgnoreCase);

    readonly Dictionary<string, (double Left, double FirstLine, double Hanging, double Right)> styleUnboundIndents = new(StringComparer.OrdinalIgnoreCase);

    // Numbering definitions: numId -> ilvl -> NumberingLevelDefinition
    Dictionary<int, Dictionary<int, NumberingLevelDefinition>>? numberingDefinitions;

    // Style numbering: styleId -> (numId, ilvl) for styles that define numbering
    Dictionary<string, (int numId, int ilvl)>? styleNumbering;

    // The styles that put a paragraph in a list themselves (their own numPr, or a level's pStyle naming
    // them) rather than through w:basedOn, and each paragraph style's own w:ind and base: what decides
    // whose indent a style-numbered paragraph takes (StyleIndentBeforeList).
    HashSet<string>? styleNumberingOwners;

    Dictionary<string, (bool Left, bool Hanging, string? BasedOn)>? styleOwnIndents;

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

    // docDefaults w:rFonts/@w:eastAsia (or its theme slot): the East Asian face a run with
    // w:hint="eastAsia" draws its ambiguous characters in when its own w:rFonts names none.
    string? defaultEastAsiaFontFamily;

    // w:compatSetting compatibilityMode, read before the body is parsed because table placement
    // depends on it (see ResolveTableIndent). 12 is what Word assumes for a document that declares
    // none (ExtractCompatibilitySettings).
    int compatibilityMode = 12;

    // w:compat/w:doNotUseHTMLParagraphAutoSpacing (CompatibilitySettings.UsesFixedAutoSpacing).
    bool usesFixedAutoSpacing;

    // The table-style step of the paragraph cascade: docDefaults -> table style w:pPr ->
    // paragraph style chain -> direct w:pPr (ECMA-376). A table style's own w:pPr applies to
    // every paragraph in tables that reference it, so it overrides the document defaults but
    // yields to anything the paragraph's style chain declares — which is why the paragraph-style
    // declarations are tracked separately from their resolved values.
    Dictionary<string, StyleParagraphSpacing> tableStyleSpacing = new(StringComparer.OrdinalIgnoreCase);

    Dictionary<string, StyleParagraphSpacing> paragraphStyleDeclaredSpacing = new(StringComparer.OrdinalIgnoreCase);

    // Style id of the table whose cells are being parsed, or null outside a table. Nested tables
    // save and restore it, so an inner table's style does not leak to the outer table's later rows.
    string? currentTableStyleId;

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

    // Document default line spacing, used when no style supplies one. Word's built-in default
    // (line=278) applies when the document declares no styles.xml, no docDefaults, or docDefaults
    // with no pPrDefault element (see ExtractDefaultParagraphProperties); a document whose
    // docDefaults DO carry a pPrDefault, empty or without a w:line, is single-spaced
    // (Word-probed 2026-08-04).
    double defaultLineSpacingMultiplier = 1.0;

    // Document-wide line multiplier from docDefaults/pPrDefault/w:spacing/@w:line, read only under
    // the auto rule (under exact/atLeast it is a twip measurement and belongs to the per-paragraph
    // path). Null when the document declares none, leaving the invented fallbacks to apply.
    //
    // What the Word probes settled (each rendering a doctored copy through Word itself):
    //   * The cascade is REAL. Doubling agendas-minutes/07's declared w:line="264" to 480 takes
    //     Word's own render from 2 pages to 3; removing it leaves Word single-spaced.
    //   * It DOES reach table-cell paragraphs, so the long-suspected "cells are exempt" rule is
    //     wrong. brochures/05 is almost entirely cell text and doubling its docDefault takes
    //     Word from 4 pages to 7.
    //   * A style's explicit w:line="240" correctly means single and wins over the docDefault
    //     (agendas-minutes/07's Title and ListBullet), which is why only its Normal- and
    //     Heading1-derived paragraphs move.
    //
    // It pairs with specDefaultFontSizePoints (those documents declare docDefaults with no w:sz:
    // brochures/05's pitch is 10pt x 1.2), the table-style w:pPr cascade step, and an Auto
    // multiplier never scaling an inline image. Measurements: src/page_counts.md experiments 17-21.
    double? docDefaultLineSpacingMultiplier;

    // Document-level w:defaultTabStop in points (720 twips = 36 pt = 0.5 inch, OOXML default).
    double defaultTabStopPoints = 36;

    // The document grid's line pitch (GridLinePitchPoints) from the body's own w:sectPr, 0 off a grid.
    // Styles are resolved once for the whole document, so the line unit beforeLines / afterLines count
    // in, and the grid paragraphs snap to, are the document's rather than each section's.
    double documentGridPitchPoints;

    // w:compat/w:adjustLineHeightInTable: table cells snap to the grid only when it is set.
    bool adjustLineHeightInTable;

    // Note reference-mark number formats (settings.xml w:footnotePr / w:endnotePr). Word defaults:
    // decimal footnotes, lowercase-roman endnotes.
    NumberFormatValues footnoteNumberFormat = NumberFormatValues.Decimal;

    NumberFormatValues endnoteNumberFormat = NumberFormatValues.LowerRoman;

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

    // Document-default kerning threshold in points; zero = kerning off (see
    // ResolveDocDefaultKerningMinPoints). Runs without an inline w:kern inherit this.
    double effectiveDefaultKerningMinPoints;

    // The document's automatic hyphenation, read before the body (ParagraphProperties.AutoHyphenation).
    HyphenationSettings documentHyphenation = new();

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

    // Set while a borrowed package is parsed: how to undo each change parsing makes to its tree.
    List<Action>? restore;

    /// <summary>
    /// Parses a package another owner keeps open — the editor's live one (<c>DocumentReading.Borrow</c>),
    /// whose tree is the file it just saved — and leaves it as it was found. Parsing changes the tree in
    /// two places, dropping a style defined twice and folding relative sizes into anchors' extents, and
    /// both are undone, in reverse order, once the document is read; left in, the next save would write
    /// them into the file.
    /// </summary>
    public ParsedDocument Parse(WordprocessingDocument document)
    {
        restore = [];
        try
        {
            return ParseDocument(document);
        }
        finally
        {
            for (var index = restore.Count - 1; index >= 0; index--)
            {
                restore[index]();
            }

            restore = null;
        }
    }

    ParsedDocument ParseDocument(WordprocessingDocument doc)
    {
        var mainPart = doc.MainDocumentPart
                       ?? throw new InvalidOperationException("Document has no main part");

        var body = mainPart.Document?.Body
                   ?? throw new InvalidOperationException("Document has no body");

        var scan = new BodyScan(body);
        commentedRuns = scan.CommentedRuns;
        lastRenderedPageBreakCount = scan.LastRenderedPageBreaks;
        // Sources are counted across the body, the headers and footers, and the notes, the body first,
        // so that what an editor opens anywhere on a page can be found again in the markup.
        var stories = captureSources ? SourceRuns.Stories(doc) : [];
        runOrdinals = captureSources ? SourceRuns.IndexAll(stories) : null;
        paragraphSources = captureSources ? SourceRuns.IndexAllParagraphs(stories) : null;

        DropRedefinedStyles(mainPart, restore);

        // Extract and store theme colors early (needed for background color and other theme-resolved values)
        currentThemeColors = ThemeParser.ExtractThemeColors(mainPart.ThemePart);

        currentThemeFonts = ThemeParser.ExtractThemeFonts(mainPart.ThemePart);

        // Resolve the document's default font from docDefaults (w:rPrDefault/w:rFonts). Every run
        // that doesn't carry an explicit font inherits this. Falls back to the theme's minor font
        // for a package with no styles part, then to the constructor's defaultFont.
        effectiveDefaultFont = ResolveDocDefaultFont(mainPart) ?? StylelessThemeFont(mainPart) ?? defaultFont ?? builtInDefaultFontFamily;
        effectiveDefaultFontSizePoints = ResolveDocDefaultFontSizePoints(mainPart) ?? builtInDefaultFontSizePoints;
        effectiveDefaultKerningMinPoints = ResolveDocDefaultKerningMinPoints(mainPart);

        // Before any paragraph is read: each one carries the rule unless it suppresses it.
        documentHyphenation = ExtractHyphenationSettings(mainPart);

        // Extract document-level background color (w:background element)
        documentBackgroundColor = ExtractDocumentBackgroundColor(mainPart.Document);
        automaticRunColorHex = ComputeAutomaticRunColor(documentBackgroundColor);

        // The document grid, before anything resolves a spacing counted in lines.
        documentGridPitchPoints = body.GetFirstChild<SectionProperties>() is { } bodySection ? GridLinePitchPoints(bodySection) : 0;
        adjustLineHeightInTable = mainPart.DocumentSettingsPart?.Settings?
            .GetFirstChild<Compatibility>()?.GetFirstChild<AdjustLineHeightInTable>() != null;

        ExtractDefaultParagraphProperties(mainPart);

        ExtractDefaultTabStop(mainPart);

        // Extract gutterAtTop setting (so ExtractPageSettings can apply gutter to the right margin).
        gutterAtTopSetting = mainPart.DocumentSettingsPart?.Settings?
            .GetFirstChild<GutterAtTop>().IsOn() ?? false;

        // Note reference-mark number formats (w:footnotePr / w:endnotePr in settings.xml). Word's
        // defaults differ: footnotes count decimal, endnotes lowercase roman — an endnote mark with
        // no declared w:numFmt is "i", not "1" (document_capture/01's reference reads that way).
        footnoteNumberFormat = mainPart.DocumentSettingsPart?.Settings?
            .GetFirstChild<FootnoteDocumentWideProperties>()?
            .GetFirstChild<NumberingFormat>()?.Val?.Value ?? NumberFormatValues.Decimal;
        endnoteNumberFormat = mainPart.DocumentSettingsPart?.Settings?
            .GetFirstChild<EndnoteDocumentWideProperties>()?
            .GetFirstChild<NumberingFormat>()?.Val?.Value ?? NumberFormatValues.LowerRoman;

        // SectionProperties (sectPr) describes the section it belongs to, and the section break is stored
        // on the last paragraph of the section. The next section's properties are stored in the next sectPr.
        var sectionPropsList = scan.Sections;
        stylesById = null;
        hyperlinkUrlsByRelId = null;
        imagePartBytes.Clear();
        nextSectionSettings = new();
        nextSectionProps = new();
        for (var i = 0; i < sectionPropsList.Count; i++)
        {
            var current = sectionPropsList[i];
            var next = i + 1 < sectionPropsList.Count
                ? ExtractPageSettings(sectionPropsList[i + 1])
                : null;
            nextSectionSettings[current] = next;
            nextSectionProps[current] = i + 1 < sectionPropsList.Count ? sectionPropsList[i + 1] : null;
        }

        var pageSettings = sectionPropsList.Count > 0
            ? ExtractPageSettings(sectionPropsList[0])
            : new();

        // First section's metrics, used to fold wp:align anchor alignment into positions at
        // parse (see ParsePositioning). Later sections with different page boxes would fold
        // slightly off — acceptable: align-positioned art overwhelmingly sits in section 1
        // covers/headers, and multi-section documents rarely change the page box.
        anchorAlignmentPage = pageSettings;
        if (scan.HasAnchors)
        {
            FoldRelativeSizes(body, pageSettings);
        }

        styleRunProperties = ExtractStyleRunProperties(mainPart);

        styleParagraphProperties = ExtractStyleParagraphProperties(mainPart);
        tableStyleSpacing = ExtractDeclaredSpacing(mainPart, StyleValues.Table);
        paragraphStyleDeclaredSpacing = ExtractDeclaredSpacing(mainPart, StyleValues.Paragraph);
        numberingDefinitions = NumberingParser.ExtractNumberingDefinitions(mainPart, numberingAbstractIds, numberingStartOverrides);

        (styleNumbering, styleNumberingOwners) = ExtractStyleNumbering(mainPart);
        styleOwnIndents = ExtractStyleOwnIndents(mainPart);

        tableStyleBorders = ExtractTableStyleBorders(mainPart);

        var compatibility = ExtractCompatibilitySettings(mainPart);
        compatibilityMode = compatibility.CompatibilityMode;
        usesFixedAutoSpacing = compatibility.UsesFixedAutoSpacing;

        // Each section's header/footer set, inheritance applied, folded into the section settings the
        // section breaks will carry — so it has to exist before the body parses (ParseSectionBreak reads
        // nextSectionSettings), and after the styles the band paragraphs resolve against.
        // w:settings/w:evenAndOddHeaders opts the document into separate even-page parts.
        var evenAndOddHeaders = mainPart.DocumentSettingsPart?.Settings?
            .GetFirstChild<EvenAndOddHeaders>().IsOn() ?? false;
        var sectionBands = ExtractSectionBands(sectionPropsList, mainPart, evenAndOddHeaders);
        for (var i = 0; i + 1 < sectionPropsList.Count; i++)
        {
            if (nextSectionSettings[sectionPropsList[i]] is { } nextSettings)
            {
                nextSectionSettings[sectionPropsList[i]] = nextSettings with {Bands = sectionBands[i + 1]};
            }
        }

        if (sectionBands.Count > 0)
        {
            pageSettings = pageSettings with
            {
                Bands = sectionBands[0]
            };
        }

        bookmarkParagraphs = null;
        foreach (var start in scan.BookmarkStarts)
        {
            if (start.Ancestors<Paragraph>().FirstOrDefault() is { } bookmarked)
            {
                bookmarkParagraphs ??= [];
                bookmarkParagraphs[bookmarked] = null;
            }
        }

        var elements = ParseElements(body, mainPart);

        // A page break that ends the document makes no page: Word keeps the final paragraph's mark on
        // the page before it. Word 16.0.20527's own page count over COM (2026-11-08, compatibility
        // modes 12, 14 and 15 alike): a break alone in the last paragraph, or ending its text, leaves
        // one page; text after it, an empty paragraph after it, or a paragraph of text, two; two breaks
        // there, two — only the last goes. The paragraph holding nothing but the break leaves only the
        // break here, so a break that ends the list is exactly that one.
        if (elements.Count > 0 &&
            elements[^1] is PageBreakElement)
        {
            elements.RemoveAt(elements.Count - 1);
        }

        // Pull consecutive w:framePr-positioned paragraphs out of normal flow into floating
        // text-frame elements (Word's text-frame feature).
        elements = FrameGrouper.Group(elements);

        // Word draws every floating drawing in one z-space ordered by wp:anchor@relativeHeight —
        // a shape can sit entirely beneath a sibling anchored later in the document (brochures/04
        // hides its construction photo under the teal quote panel). The renderers draw in list
        // order, so stable-sort each maximal run of consecutive floating elements by that value;
        // behind-text vs in-front routing is untouched, and children of one group share their
        // anchor's value so the stable sort preserves their intra-group order.
        SortFloatingBatchesByZ(elements);

        // The document-level parts are section 1's — what the text exporters (which do not paginate)
        // and a caller without section settings see; the engine reads each page's own section.
        var firstSection = sectionBands.Count > 0 ? sectionBands[0] : new();
        var header = firstSection.Header;
        var footer = firstSection.Footer;
        var firstPageHeader = pageSettings.DifferentFirstPage ? firstSection.FirstPageHeader : null;
        var firstPageFooter = pageSettings.DifferentFirstPage ? firstSection.FirstPageFooter : null;
        var evenPageHeader = firstSection.EvenPageHeader;
        var evenPageFooter = firstSection.EvenPageFooter;
        var hyphenation = ExtractHyphenationSettings(mainPart);

        var bookmarks = ExtractBookmarks(scan);
        var comments = ExtractComments(mainPart, scan);
        var trackedChanges = ExtractTrackedChanges(scan);
        var protection = ExtractDocumentProtection(mainPart);
        var fieldCodes = FieldParser.ExtractFieldCodes(scan);
        var (footnotes, footnoteSeparator, footnoteContinuation) = ExtractFootnotes(mainPart);
        var (endnotes, endnoteSeparator, endnoteContinuation) = ExtractEndnotes(mainPart);
        var embeddedObjects = ExtractEmbeddedObjects(scan);
        var watermarks = ExtractWatermarks(mainPart);
        var features = scan.Features(watermarks.Count > 0);

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
            FootnoteSeparator = footnoteSeparator,
            FootnoteContinuationSeparator = footnoteContinuation,
            EndnoteSeparator = endnoteSeparator,
            EndnoteContinuationSeparator = endnoteContinuation,
            Bookmarks = bookmarks,
            Comments = comments,
            TrackedChanges = trackedChanges,
            Protection = protection,
            FieldCodes = fieldCodes,
            RequiresTotalPageCount = requiresTotalPageCount,
            Footnotes = footnotes,
            Endnotes = endnotes,
            EndnoteNumberFormat = NumberingParser.MapNumberFormat(endnoteNumberFormat),
            EndnotesAtSectionEnd = mainPart.DocumentSettingsPart?.Settings?
                .GetFirstChild<EndnoteDocumentWideProperties>()?
                .GetFirstChild<EndnotePosition>()?.Val?.Value == EndnotePositionValues.SectionEnd,
            EmbeddedObjects = embeddedObjects,
            Watermarks = watermarks,
            Features = features
        };
    }

    static List<EmbeddedObject> ExtractEmbeddedObjects(BodyScan scan)
    {
        var result = new List<EmbeddedObject>();
        foreach (var obj in scan.EmbeddedObjects)
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

    static List<TrackedChange> ExtractTrackedChanges(BodyScan scan)
    {
        var result = new List<TrackedChange>();

        foreach (var ins in scan.Insertions)
        {
            if (ins.Id?.Value is not { } id)
            {
                continue;
            }

            result.Add(BuildChange(id, ins.Author?.Value, ins.Date?.Value, TrackedChangeType.Insertion, ins));
        }

        foreach (var del in scan.Deletions)
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

    HyphenationSettings? AutoHyphenationFor(bool suppressed)
    {
        if (suppressed ||
            !documentHyphenation.AutoHyphenation)
        {
            return null;
        }

        return documentHyphenation;
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

        var autoHyphen = settings.GetFirstChild<AutoHyphenation>();
        if (autoHyphen != null)
        {
            autoHyphenation = autoHyphen.IsOn();
        }

        var hyphenZone = settings.GetFirstChild<HyphenationZone>();
        if (hyphenZone?.Val?.HasValue == true)
        {
            hyphenationZonePoints = double.Parse(hyphenZone.Val.Value!) / OoxmlUnits.TwipsPerPoint;
        }

        var consecutiveLimit = settings.GetFirstChild<ConsecutiveHyphenLimit>();
        if (consecutiveLimit?.Val?.HasValue == true)
        {
            consecutiveHyphenLimit = consecutiveLimit.Val.Value;
        }

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
            defaultTabStopPoints = defaultTabStop.Val.Value / OoxmlUnits.TwipsPerPoint;
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

        var compatMode = 12;

        foreach (var compatSetting in compat.Elements<CompatibilitySetting>())
        {
            // Use InnerText to get the raw attribute value since the SDK doesn't have enum values for all settings
            var name = compatSetting.Name?.InnerText;
            var uri = compatSetting.Uri?.Value;
            var val = compatSetting.Val?.Value;

            if (string.Equals(name, "compatibilityMode", StringComparison.OrdinalIgnoreCase) &&
                uri == "http://schemas.microsoft.com/office/word" &&
                val != null)
            {
                if (int.TryParse(val, out var mode))
                {
                    compatMode = mode;
                }
            }
        }

        return new()
        {
            CompatibilityMode = compatMode,
            UsesFixedAutoSpacing = compat
                .Elements()
                .Any(_ => _.LocalName == "doNotUseHTMLParagraphAutoSpacing" &&
                          OnOff(_))
        };

        // An on/off element is on when it is there, unless its w:val says otherwise.
        static bool OnOff(OpenXmlElement element) =>
            element
                .GetAttributes()
                .FirstOrDefault(_ => _.LocalName == "val")
                .Value is not ("0" or "false" or "off");
    }

    // Word's "automatic" text colour is contrast-aware: black on an ordinary page, white when the
    // page background (w:background) is dark — brochures/03 paints its automatic titles white on
    // navy. Null means "leave unset" so renderers fall back to their default black, keeping models
    // for ordinary documents free of a stamped 000000.
    static string? ComputeAutomaticRunColor(string? pageBackgroundHex)
    {
        // NOTE the threshold here is 128, where the shading rule below measured 59. Only shading was
        // probed, and the corpus's page backgrounds (brochures/03's navy, luma 38) are white under
        // either; Word most likely applies one rule to both, which wants a page-background probe.
        if (Brightness(pageBackgroundHex) is not { } brightness)
        {
            return null;
        }

        if (brightness < 128)
        {
            return "FFFFFF";
        }

        return null;
    }

    /// <summary>
    /// Whether a fill is dark enough that Word draws automatic text white on it.
    ///
    /// Word-probed over a greyscale ramp and saturated fills (see docs/word-features.md, "Table Cell
    /// Shading"). Two rows fix the FORMULA as ITU-R BT.601 luma: <c>00FF00</c> (luma 150) takes black
    /// while <c>FF00FF</c> (luma 105) takes white, which a simple channel mean gets backwards both
    /// times; HSL lightness and max-channel are ruled out because <c>0000FF</c> and <c>FF0000</c>
    /// share both yet differ. Four rows bracket the THRESHOLD to (58, 59.9]: greys <c>3A3A3A</c>
    /// (58.0) white against <c>3C3C3C</c> (60.0) black, and greens <c>006000</c> (56.4) white against
    /// <c>006600</c> (59.9) black — grey and green agreeing confirms it keys off the luma value
    /// rather than any single channel. A WCAG contrast crossover would sit near 122 and is refuted.
    /// </summary>
    static bool IsDarkForAutomaticText(string? fillHex) =>
        Brightness(fillHex) is < 59;

    // ITU-R BT.601 perceived brightness, 0-255. Null when the value is not a six-digit hex colour
    // (a theme name, "auto", "none").
    static int? Brightness(string? hex)
    {
        if (hex is not {Length: 6} ||
            !uint.TryParse(hex, NumberStyles.HexNumber, null, out var rgb))
        {
            return null;
        }

        return (int) ((299 * ((rgb >> 16) & 0xFF) + 587 * ((rgb >> 8) & 0xFF) + 114 * (rgb & 0xFF)) / 1000);
    }

    string? ExtractDocumentBackgroundColor(DocumentFormat.OpenXml.Wordprocessing.Document document)
    {
        var background = document.GetFirstChild<DocumentBackground>();
        if (background == null)
        {
            return null;
        }

        if (background.Color?.HasValue == true)
        {
            var colorValue = background.Color.Value;
            if (!string.IsNullOrEmpty(colorValue) &&
                colorValue != "auto")
            {
                return colorValue;
            }
        }

        if (background.ThemeColor?.HasValue == true &&
            currentThemeColors != null)
        {
            var themeColorName = background.ThemeColor.Value.ToString();

            byte? tint = null;
            byte? shade = null;

            if (background.ThemeTint?.HasValue == true)
            {
                var tintHex = background.ThemeTint.Value;
                if (!string.IsNullOrEmpty(tintHex) &&
                    byte.TryParse(tintHex, NumberStyles.HexNumber, null, out var tintByte))
                {
                    tint = tintByte;
                }
            }

            if (background.ThemeShade?.HasValue == true)
            {
                var shadeHex = background.ThemeShade.Value;
                if (!string.IsNullOrEmpty(shadeHex) &&
                    byte.TryParse(shadeHex, NumberStyles.HexNumber, null, out var shadeByte))
                {
                    shade = shadeByte;
                }
            }

            return currentThemeColors.ResolveColor(themeColorName, shade, tint);
        }

        return null;
    }

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

    // Decompressed part bytes, one buffer per part per parse (see imagePartBytes).
    byte[] GetPartBytes(OpenXmlPart part)
    {
        if (!imagePartBytes.TryGetValue(part, out var bytes))
        {
            // Read-only: a part of a package open for writing that is opened read-write is written back
            // out, deflated again, by every save after — and the parse reads the editor's live package.
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            imagePartBytes[part] = bytes;
        }

        return bytes;
    }
}
