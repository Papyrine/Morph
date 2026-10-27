/// <summary>
/// Serializes a <see cref="ParsedDocument"/> to HTML. By default emits a full self-contained
/// <c>&lt;!doctype html&gt;</c> document with an embedded stylesheet of Word-like defaults
/// (Calibri 11pt body, sized headings, paragraph margins, table padding) so the output renders
/// close to Word in any browser without inline CSS on every element. The document's page geometry
/// carries onto the <c>&lt;body&gt;</c> — page margins become the body padding and the content
/// width becomes a <c>max-width</c> — so text wraps at Word's measure instead of the viewport's.
/// Set <see cref="HtmlExportOptions.EmitDocument"/> to <c>false</c> to get just the body-level
/// fragment.
///
/// Inline content uses semantic tags — <c>&lt;strong&gt;</c>, <c>&lt;em&gt;</c>,
/// <c>&lt;u&gt;</c>, <c>&lt;s&gt;</c>, <c>&lt;sup&gt;</c>, <c>&lt;sub&gt;</c>,
/// <c>&lt;h1&gt;</c>-<c>&lt;h6&gt;</c>, <c>&lt;a&gt;</c>, <c>&lt;th&gt;</c>; <c>&lt;span
/// style="…"&gt;</c> appears only for ad-hoc per-run overrides (custom colour, small-caps,
/// all-caps text-transform, etc.).
/// </summary>
static class HtmlExporter
{
    /// <summary>
    /// Embedded stylesheet of Word-like defaults. Body picks up Calibri 11pt with 1.08 line-height
    /// (Word's "Normal" style) — its padding comes inline from the document's page margins;
    /// headings get the sizes Word's stock Heading styles resolve to (h1=16pt, h2=13pt, h3=12pt,
    /// h4=11pt-italic, h5=11pt, h6=11pt-italic) and clear floats — a heading typically opens a
    /// section below any wrapped image, matching Word's layout in the common case — so a stock
    /// document needs no per-heading override; paragraphs and lists get 8pt bottom margin; tables collapse
    /// borders with light grey 0.5pt cells; horizontal rules get a thin grey line. Applying these
    /// once via the <c>&lt;style&gt;</c> block — rather than repeating them as inline styles on every
    /// element — keeps the individual elements clean.
    /// </summary>
    const string defaultStylesheet = """
        body { font-family: Calibri, sans-serif; font-size: 11pt; line-height: 1.08; margin: 0; color: #000; }
        p { margin: 0 0 8pt; }
        /* Sizes only. Word's BUILT-IN Heading 4/6 are italic, but the document's own style decides:
           an italic one reaches us as italic runs, which emit <em>. Asserting it here instead
           forced italic onto every h4/h6 whose style is upright — of the 12 corpus scenarios using
           Heading 4, not one declares italic. Bold is different: it IS asserted here, and the run
           writer suppresses <strong> in a heading to match (an explicitly non-bold run still emits
           font-weight: normal). */
        h1, h2, h3, h4, h5, h6 { margin: 12pt 0 0; font-weight: bold; clear: both; }
        h1 { font-size: 16pt; }
        h2 { font-size: 13pt; }
        h3 { font-size: 12pt; }
        h4 { font-size: 11pt; }
        h5 { font-size: 11pt; }
        h6 { font-size: 11pt; }
        ul, ol { margin: 0 0 8pt; padding-left: 24pt; }
        li { margin: 0; }
        table { border-collapse: collapse; margin: 0 0 8pt; }
        /* border-box because w:tcW is the whole cell, its side margins included. Read as the
           content box, every column with a declared width came out wider than Word's by its
           padding: html_table_styled's 75pt column measured 91pt. */
        td, th { padding: 4pt 7pt; vertical-align: top; box-sizing: border-box; }
        /* w:tblHeader marks a row for repetition, not styling — any real bold/centring arrives on
           the runs and cells. Without this, the browser's th defaults bolded and centred header
           cells Word renders regular and left-aligned. */
        th { font-weight: inherit; text-align: inherit; }
        hr { border: 0; border-top: 0.75pt solid #a0a0a0; margin: 6pt 0; }
        header.doc-header { border-bottom: 0.75pt solid #a0a0a0; margin: 0 0 12pt; padding: 0 0 6pt; }
        footer.doc-footer { border-top: 0.75pt solid #a0a0a0; margin: 12pt 0 0; padding: 6pt 0 0; }
        """;

    public static string Export(ParsedDocument document, HtmlExportOptions? options = null)
    {
        options ??= new();

        // The document's dominant font (by character count) anchors the <body>; individual runs
        // only carry a font-family when they deviate from it. This mirrors how Word's own HTML
        // export structures fonts — one body default plus sparse per-run overrides — instead of
        // tagging every run with the same family.
        var bodyFont = DominantFont(document.Elements);

        var headerContent = PickHeaderFooter(document.Header, document.FirstPageHeader);
        var footerContent = PickHeaderFooter(document.Footer, document.FirstPageFooter);

        if (!options.EmitDocument)
        {
            var fragmentWriter = new HtmlWriter(options, bodyFont, document.PageSettings, document.Footnotes, document.Endnotes, pageFieldsPreEvaluated: document.PageFieldsPreEvaluated, comments: document.Comments);
            WriteHeaderFooter(fragmentWriter, headerContent, "header", "doc-header");
            fragmentWriter.WriteBody(document.Elements);
            fragmentWriter.WriteNoteDefinitions();
            fragmentWriter.WriteCommentDefinitions();
            WriteHeaderFooter(fragmentWriter, footerContent, "footer", "doc-footer");
            fragmentWriter.WriteRecolorFilters();
            return fragmentWriter.ToString();
        }

        // Everything the prelude needs is known before the body is written, so the writer
        // streams the body straight into the document builder — the whole output used to be
        // materialized as a body string and copied into a second builder at the end.
        var doc = new StringBuilder();
        doc.Append(
            """
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>

            """);
        doc.Append(defaultStylesheet);
        doc.Append("\n</style>\n</head>\n<body");

        var bodyStyle = new List<string>();
        if (bodyFont != null)
        {
            bodyStyle.Add($"font-family: {CssFontFamily(bodyFont)}, sans-serif");
        }

        // The page geometry travels onto the body: the content width becomes a max-width so text
        // wraps at Word's measure rather than the viewport's, and the page margins become the body
        // padding. Both use the real w:sectPr values, so a narrow-margin template stays dense while
        // a default letter page keeps its inch of whitespace.
        var page = document.PageSettings;
        bodyStyle.Add($"max-width: {Length(page.ContentWidth)}");
        bodyStyle.Add($"padding: {BoxShorthand(page.MarginTop, page.MarginRight, page.MarginBottom, page.MarginLeft)}");

        // Background shapes and wrap-NONE floating images are emitted as absolutely-positioned
        // children; they resolve their top/left against the body, so the body must be a positioning
        // context. Header and footer content counts too — a template whose only floating art lives
        // in its header (a full-page banner band) would otherwise resolve it against the viewport.
        if (HasAbsoluteFloat(document.Elements) ||
            HasAbsoluteFloat(headerContent?.Elements) ||
            HasAbsoluteFloat(footerContent?.Elements))
        {
            bodyStyle.Add("position: relative");
        }

        // A document-level page background (w:background) becomes the body background so the page
        // colour survives — many template documents (resumes, brochures) rely on it.
        var pageBackground = DocumentExportHelpers.NormalizeColor(document.PageSettings.BackgroundColorHex);
        if (pageBackground != null)
        {
            bodyStyle.Add($"background-color: {pageBackground}");
        }

        // Decorative page borders (w:pgBorders) ride on the body box — the export has no pages,
        // so one frame around the whole flow stands in for Word's per-page frame. The space
        // offsets are not modelled here; the body edge is close enough for a reflowing export.
        if (document.PageSettings.PageBorders is { HasAnyBorder: true } pageBorders)
        {
            AddBodyBorder(bodyStyle, "border-top", pageBorders.Top);
            AddBodyBorder(bodyStyle, "border-right", pageBorders.Right);
            AddBodyBorder(bodyStyle, "border-bottom", pageBorders.Bottom);
            AddBodyBorder(bodyStyle, "border-left", pageBorders.Left);
        }

        if (bodyStyle.Count > 0)
        {
            doc.Append(" style=\"").Append(string.Join("; ", bodyStyle)).Append('"');
        }

        doc.Append(">\n");
        var writer = new HtmlWriter(options, bodyFont, document.PageSettings, document.Footnotes, document.Endnotes, doc, document.PageFieldsPreEvaluated, document.Comments);
        WriteHeaderFooter(writer, headerContent, "header", "doc-header");
        writer.WriteBody(document.Elements);
        writer.WriteNoteDefinitions();
        writer.WriteCommentDefinitions();
        WriteHeaderFooter(writer, footerContent, "footer", "doc-footer");
        writer.WriteRecolorFilters();
        doc.Append("</body>\n</html>");
        return doc.ToString();
    }

    /// <summary>
    /// Emits a header or footer once, wrapped in its semantic element. HTML has no pagination, so
    /// per-page repetition is meaningless — but dropping the content entirely loses whatever the
    /// template put there, which for a marked-up document is the classification banner every
    /// rendered page carries.
    /// </summary>
    static void WriteHeaderFooter(HtmlWriter writer, HeaderFooterContent? content, string tag, string className)
    {
        if (content == null)
        {
            return;
        }

        writer.WriteRaw($"<{tag} class=\"{className}\">\n");
        if (tag == "footer")
        {
            writer.WriteFooter(content.Elements);
        }
        else
        {
            writer.WriteElements(content.Elements, 0);
        }

        writer.WriteRaw($"</{tag}>\n");
    }

    /// <summary>
    /// Chooses which of a header/footer pair to export. The DEFAULT one wins because it is what
    /// most of the document carries; the first-page variant is the fallback, for a document whose
    /// only header is a cover treatment. A first-page variant is often deliberately blank (Word's
    /// way of suppressing the header on page 1), so an empty one must not mask the default —
    /// hence the content test rather than a plain null check.
    /// </summary>
    static HeaderFooterContent? PickHeaderFooter(HeaderFooterContent? standard, HeaderFooterContent? firstPage)
    {
        if (HasContent(standard))
        {
            return standard;
        }

        if (HasContent(firstPage))
        {
            return firstPage;
        }

        return null;
    }

    /// <summary>
    /// Blankness as HTML sees it: an explicit line break renders as a &lt;br /&gt; and occupies a
    /// line, so a paragraph of nothing but breaks is content here even though it is all whitespace.
    /// </summary>
    static bool IsBlankForHtml(ParagraphElement paragraph) =>
        DocumentExportHelpers.IsBlank(paragraph, lineBreaksRender: true);

    // Whether a flow holds anything the writer places absolutely: a floating shape, or a floating
    // image or text box that neither wraps nor displaces (see WriteFloatingImage).
    static bool HasAbsoluteFloat(IReadOnlyList<DocumentElement>? elements)
    {
        if (elements == null)
        {
            return false;
        }

        foreach (var element in elements)
        {
            if (element is
                FloatingShapeElement or
                FloatingImageElement
                {
                    WrapType: WrapType.None
                } or
                FloatingTextBoxElement
                {
                    WrapType: WrapType.None
                })
            {
                return true;
            }
        }

        return false;
    }

    static void AddBodyBorder(List<string> bodyStyle, string property, BorderEdge edge)
    {
        if (edge.IsVisible)
        {
            bodyStyle.Add($"{property}: {HtmlWriter.BorderCss(edge)}");
        }
    }

    static bool HasContent(HeaderFooterContent? content) =>
        content != null &&
        content.Elements.Any(_ => _ is not ParagraphElement paragraph || !IsBlankForHtml(paragraph));

    /// <summary>
    /// The most-used run font in the document, weighted by text length so a long body in one font
    /// wins over a few short headings in another. Returns null for a document with no text runs.
    /// </summary>
    static string? DominantFont(IReadOnlyList<DocumentElement> elements)
    {
        var weights = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        AccumulateFontWeights(elements, weights);
        if (weights.Count == 0)
        {
            return null;
        }

        return weights.OrderByDescending(_ => _.Value)
            .ThenBy(_ => _.Key, StringComparer.Ordinal)
            .First()
            .Key;
    }

    static void AccumulateFontWeights(IReadOnlyList<DocumentElement> elements, Dictionary<string, long> weights)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    foreach (var run in paragraph.Runs)
                    {
                        if (run.Properties.Hidden ||
                            run.IsTab ||
                            string.IsNullOrEmpty(run.Text) ||
                            string.IsNullOrEmpty(run.Properties.FontFamily))
                        {
                            continue;
                        }

                        weights.TryGetValue(run.Properties.FontFamily, out var current);
                        weights[run.Properties.FontFamily] = current + run.Text.Length;
                    }

                    break;
                case TableElement table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            AccumulateFontWeights(cell.Content, weights);
                        }
                    }

                    break;
                case FloatingTextBoxElement textBox:
                    AccumulateFontWeights(textBox.Content, weights);
                    break;
                case PositionedFrameElement frame:
                    AccumulateFontWeights(frame.Content, weights);
                    break;
            }
        }
    }

    // CSS font-family values containing whitespace must be quoted (e.g. 'Calibri Light'); single
    // identifiers (Calibri) are left bare. Family names never contain quotes in practice.
    static string CssFontFamily(string font)
    {
        if (font.Contains(' '))
        {
            return $"'{font}'";
        }

        return font;
    }

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    static string Length(double points) => $"{Number(points)}pt";

    // EMU = English Metric Units. 1 point = 12700 EMU. Inline shape groups keep their geometry in

    // Shortest CSS box shorthand for top/right/bottom/left: one value when all four match,
    // "vertical horizontal" when opposite edges match, "top horizontal bottom" when only the
    // sides match, else all four.
    static string BoxShorthand(double top, double right, double bottom, double left)
    {
        if (Math.Abs(left - right) > 0.01)
        {
            return $"{Length(top)} {Length(right)} {Length(bottom)} {Length(left)}";
        }

        if (Math.Abs(top - bottom) > 0.01)
        {
            return $"{Length(top)} {Length(right)} {Length(bottom)}";
        }

        if (Math.Abs(top - right) > 0.01)
        {
            return $"{Length(top)} {Length(right)}";
        }

        return Length(top);
    }

    sealed class HtmlWriter(HtmlExportOptions options, string? bodyFont, PageSettings pageSettings, IReadOnlyList<Footnote> footnotes, IReadOnlyList<Endnote> endnotes, StringBuilder? output = null, bool pageFieldsPreEvaluated = false, IReadOnlyList<Comment>? comments = null)
    {
        // Mirrors the body font-size in DefaultStylesheet — runs at this size inherit it and need no
        // inline override.
        const double defaultBodyFontSizePoints = 11;

        // Mirror the stylesheet's paragraph spacing (p { margin: 0 0 8pt }) and line height
        // (body { line-height: 1.08 }), so only paragraphs that deviate carry an inline override.
        const double defaultSpacingAfterPoints = 8;
        const double defaultLineHeightMultiplier = 1.08;

        // Mirror the stylesheet's heading spacing (h1-h6 { margin: 12pt 0 0 }) — the values Word's
        // stock Heading styles resolve to — so only template headings that deviate carry an inline
        // override.
        const double defaultHeadingSpacingBeforePoints = 12;
        const double defaultHeadingSpacingAfterPoints = 0;

        // Mirrors the stylesheet's heading sizes (h1=16pt … h6=11pt); index = level - 1.
        static  double[] headingFontSizePoints = [16, 13, 12, 11, 11, 11];

        // What the element a run is written into already sets for its text, so that the run
        // carries only what differs: a heading is bold and has its own size, and a list item
        // takes the weight, size, colour and face of its marker. Colour and family are null where
        // the element sets none of its own — black, and the body's font.
        record struct TextContext(bool Bold, double FontSizePoints, string? Color = null, string? FontFamily = null)
        {
            public static TextContext Body { get; } = new(false, defaultBodyFontSizePoints);
        }

        // Full-document exports pass their prelude-seeded builder so the body streams straight
        // into the final output instead of being materialized and copied once more at the end.
        StringBuilder builder = output ?? new();
        HashSet<string> usedHeadingIds = [];

        // Distinct picture recolours used so far, in first-use order — the index is the filter's
        // id. CSS has no duotone, so each becomes an SVG feColorMatrix the images reference and
        // WriteRecolorFilters emits once at the end.
        List<ImageRecolor> recolorFilters = [];
        int imageIndex;
        int shapeIndex;

        // The paragraph siblings either side of the one currently being written, where the caller
        // knows them (the body walk and cell content). Word draws ONE box around a run of
        // consecutive paragraphs sharing the same w:pBdr (SharesBorderGroupWith), so the border
        // emission needs to know whether its neighbours continue the group: a non-first member
        // trades its top edge for the w:between rule and a non-last member drops its bottom edge —
        // without this, cover-letters/10's three Heading2 address lines each drew the style's top
        // rule where Word rules once, above the group.
        ParagraphProperties? borderGroupPrevious;
        ParagraphProperties? borderGroupNext;

        Dictionary<string, string> footnoteTexts = footnotes
            .GroupBy(_ => _.Id)
            .ToDictionary(_ => _.Key, _ => _.First().Text);

        Dictionary<string, string> endnoteTexts = endnotes
            .GroupBy(_ => _.Id)
            .ToDictionary(_ => _.Key, _ => _.First().Text);

        List<(int Number, string Text)> notes = [];
        Dictionary<string, int> noteNumbers = [];

        // The comments shown (ExportOptions.ShowComments), by id, and those whose range has been
        // written, in the order their ranges ended.
        Dictionary<string, Comment> commentsById = options.ShowComments
            ? (comments ?? []).Where(_ => _.Number != null).GroupBy(_ => _.Id).ToDictionary(_ => _.Key, _ => _.First())
            : [];
        List<Comment> commentsWritten = [];

        // How far down the page the flow being written starts, where that is not the top margin:
        // set while the footer is written (see WriteFooter).
        double? flowPageTop;

        public override string ToString() => builder.ToString();

        /// <summary>Appends already-escaped markup (the header/footer wrapper elements).</summary>
        public void WriteRaw(string markup) => builder.Append(markup);

        /// <summary>
        /// Writes the footer's content. The footer follows the body, so its place in the flow
        /// stands for where the page's content ENDS, and a page-relative drawing in it is
        /// measured from there rather than from the top margin. Measured from the top, letters/11's
        /// footer rule — declared 720pt down the page, at the foot — was placed 655pt below the
        /// footer, under a page of blank paper.
        /// </summary>
        public void WriteFooter(IReadOnlyList<DocumentElement> elements)
        {
            flowPageTop = pageSettings.HeightPoints - pageSettings.MarginBottom;
            WriteElements(elements, 0);
            flowPageTop = null;
        }

        /// <summary>
        /// Writes the document body, one page at a time where the author ended a page: at a page
        /// break, a section break that starts a new page, or a paragraph that breaks before
        /// itself. The export still reflows and never paginates — nothing is known of where a
        /// page fills up — but an authored break is content, and two things hang off it that a
        /// flat walk loses:
        ///
        /// <list type="bullet">
        /// <item>A page's floating art is placed from where its page starts, and reaches as far
        /// down the page as it was drawn however little text the page holds. With nothing holding
        /// the room open, the next page's content started under it: agendas-minutes/08's footer
        /// band ran across NEW BUSINESS, business-plans/09's cover wash cut through the first
        /// table, cover-letters/05's corner art sat on the next copy's phone line. So a page is
        /// at least as tall as its art reaches (<see cref="PageReserve"/>), and one that opens
        /// with art reaching up into the top margin gets that margin back.</item>
        /// <item>A picture the text wraps round stops wrapping at the end of its page;
        /// image_wrap_square's second paragraph of the next page ran on beside the first page's
        /// picture.</item>
        /// </list>
        ///
        /// A page that needs neither is written bare, so a document with no floating art reads
        /// exactly as it would without this.
        /// </summary>
        public void WriteBody(IReadOnlyList<DocumentElement> elements)
        {
            var start = 0;
            var wrapOpen = false;
            while (start < elements.Count)
            {
                var end = PageEnd(elements, start);
                var (above, below) = PageReserve(elements, start, end, first: start == 0, last: end == elements.Count);
                var styles = new List<string>();
                if (wrapOpen)
                {
                    styles.Add("clear: both");
                }

                if (above > 0.01)
                {
                    styles.Add($"padding-top: {Length(above)}");
                }

                if (below > 0.01)
                {
                    styles.Add($"min-height: {Length(below)}");
                }

                if (styles.Count == 0)
                {
                    WriteElements(elements, 0, start, end);
                }
                else
                {
                    Indent(0).Append("<div style=\"").Append(string.Join("; ", styles)).Append("\">\n");
                    WriteElements(elements, 1, start, end);
                    Indent(0).Append("</div>\n");
                }

                wrapOpen = false;
                for (var index = start; index < end; index++)
                {
                    if (elements[index] is FloatingImageElement {WrapType: WrapType.Square or WrapType.Tight or WrapType.Through})
                    {
                        wrapOpen = true;
                        break;
                    }
                }

                start = end;
            }
        }

        // The index one past the page that starts at `start`: just after the break that ends it,
        // or at the paragraph that breaks before itself — less the floating art declared directly
        // ahead of that paragraph, which is its own (the parser writes a drawing just before the
        // paragraph it is anchored to) and so belongs to the page it starts. A continuous section
        // break and a column break end no page.
        static int PageEnd(IReadOnlyList<DocumentElement> elements, int start)
        {
            for (var index = start; index < elements.Count; index++)
            {
                switch (elements[index])
                {
                    case PageBreakElement:
                    case SectionBreakElement
                    {
                        BreakType:
                        SectionBreakType.NextPage or
                        SectionBreakType.EvenPage or
                        SectionBreakType.OddPage
                    }:
                        return index + 1;
                    case ParagraphElement {Properties.PageBreakBefore: true}:
                        var boundary = index;
                        while (boundary > start &&
                               elements[boundary - 1] is FloatingShapeElement or FloatingImageElement or FloatingTextBoxElement or FloatingWordArtElement)
                        {
                            boundary--;
                        }

                        if (boundary > start)
                        {
                            return boundary;
                        }

                        break;
                }
            }

            return elements.Count;
        }

        /// <summary>
        /// The room a page's floating art needs beyond what its text takes: how far above the
        /// page's first content it reaches, and how far below. Both are measured from the art's
        /// own place in the flow, which is where its page's content starts for the art a page
        /// opens with — the template shape, every drawing hung off the first paragraph — and
        /// further down for the rest, so the reach below is a lower bound: the page is never held
        /// open further than its art goes. Only the art a page opens with counts upward, since
        /// anything anchored lower reaches up into its own page.
        ///
        /// The first page has the body's top padding for what reaches up, and the last has its
        /// bottom padding for what reaches into the bottom margin, so neither is reserved twice.
        /// </summary>
        (double Above, double Below) PageReserve(IReadOnlyList<DocumentElement> elements, int start, int end, bool first, bool last)
        {
            var above = 0.0;
            var below = 0.0;
            var opening = true;
            for (var index = start; index < end; index++)
            {
                if (AbsoluteExtent(elements[index]) is not { } extent)
                {
                    opening = false;
                    continue;
                }

                below = Math.Max(below, extent.Bottom);
                if (opening)
                {
                    above = Math.Max(above, -extent.Top);
                }
            }

            if (last)
            {
                below = Math.Min(below, pageSettings.HeightPoints - pageSettings.MarginTop - pageSettings.MarginBottom);
            }

            return (first ? 0 : Math.Min(above, pageSettings.MarginTop), below);
        }

        // Where a body-level element placed absolutely starts and ends, from its place in the
        // flow, or null for anything that takes part in the flow. Mirrors the boxes WriteShape,
        // WriteAbsoluteImage and WriteAbsoluteTextBox position.
        (double Top, double Bottom)? AbsoluteExtent(DocumentElement element)
        {
            while (true)
            {
                switch (element)
                {
                    case FloatingShapeElement shape:
                    {
                        var (pageLeft, pageTop, _, height) = ShapeBox(shape, pageSettings);
                        var top = FloatOrigin(pageLeft, pageTop, shape.VerticalAnchor).Top;
                        return (top, top + height);
                    }
                    case FloatingImageElement
                    {
                        WrapType: WrapType.None
                    } image:
                    {
                        var top = FloatOrigin(0, PageTop(image.VerticalAnchor, image.VerticalPositionPoints), image.VerticalAnchor).Top;
                        return (top, top + image.HeightPoints);
                    }
                    case FloatingTextBoxElement {WrapType: WrapType.None} textBox:
                        element = TextBoxShape(textBox);
                        continue;
                    default:
                        return null;
                }
            }
        }

        public void WriteElements(IReadOnlyList<DocumentElement> elements, int depth) =>
            WriteElements(elements, depth, 0, elements.Count);

        void WriteElements(IReadOnlyList<DocumentElement> elements, int depth, int start, int end)
        {
            for (var index = start; index < end; index++)
            {
                var element = elements[index];

                if (element is ParagraphElement
                    {
                        Properties.Numbering: not null
                    })
                {
                    var items = new List<ParagraphElement>();
                    while (index < end &&
                           elements[index] is ParagraphElement
                           {
                               Properties.Numbering: not null
                           } listItem)
                    {
                        items.Add(listItem);
                        index++;
                    }

                    index--;
                    WriteList(DocumentExportHelpers.BuildListForest(items), depth);
                    continue;
                }

                if (element is ParagraphElement quoteParagraph &&
                    DocumentExportHelpers.IsQuote(quoteParagraph.Properties))
                {
                    var quoteItems = new List<ParagraphElement>();
                    while (index < end &&
                           elements[index] is ParagraphElement candidate &&
                           DocumentExportHelpers.IsQuote(candidate.Properties))
                    {
                        quoteItems.Add(candidate);
                        index++;
                    }

                    index--;
                    WriteBlockQuote(quoteItems, depth);
                    continue;
                }

                var previous = index > start ? elements[index - 1] as ParagraphElement : null;
                var next = index + 1 < end ? elements[index + 1] as ParagraphElement : null;
                borderGroupPrevious = previous?.Properties;
                borderGroupNext = next?.Properties;
                WriteBlock(element is ParagraphElement paragraph ? WithContextualSpacing(paragraph, previous, next) : element, depth);
                borderGroupPrevious = null;
                borderGroupNext = null;
            }
        }

        /// <summary>
        /// The paragraph with what <c>w:contextualSpacing</c> leaves of its spacing against its
        /// neighbours, by the rule the layout engine was probed to (<see cref="ParagraphGap"/>):
        /// a paragraph that opts in drops its space-after against a following paragraph of its
        /// own style and its space-before against a preceding one, whether or not the neighbour
        /// opts in. Only the after half was dropped here, so a style that sets its lines apart
        /// with a large space-BEFORE kept it between them: business-plans/10's two cover lines
        /// each carry 342pt before, and the second fell off the cover art onto the white page
        /// beneath it, white on white.
        ///
        /// A browser collapses the margins that are left to the larger of the two, which is
        /// Word's <c>max(after, before)</c>. Word takes that maximum over the first paragraph's
        /// DECLARED after even where it was dropped, so the second paragraph's before is cut
        /// down by it.
        /// </summary>
        static ParagraphElement WithContextualSpacing(ParagraphElement paragraph, ParagraphElement? previous, ParagraphElement? next)
        {
            var properties = paragraph.Properties;
            var before = properties.SpacingBeforePoints;
            var after = properties.SpacingAfterPoints;
            var styleKey = ParagraphGap.StyleKey(properties);
            if (previous != null &&
                ParagraphGap.StyleKey(previous.Properties) == styleKey)
            {
                if (properties.ContextualSpacing)
                {
                    before = 0;
                }
                else if (previous.Properties.ContextualSpacing)
                {
                    before = Math.Max(0, before - previous.Properties.SpacingAfterPoints);
                }
            }

            if (properties.ContextualSpacing &&
                next != null &&
                ParagraphGap.StyleKey(next.Properties) == styleKey)
            {
                after = 0;
            }

            if (before == properties.SpacingBeforePoints &&
                after == properties.SpacingAfterPoints)
            {
                return paragraph;
            }

            return new()
            {
                Runs = paragraph.Runs,
                Properties = properties with
                {
                    SpacingBeforePoints = before,
                    SpacingAfterPoints = after
                },
                IsAnchorOnlyMark = paragraph.IsAnchorOnlyMark,
                IsCollapsedCellMark = paragraph.IsCollapsedCellMark,
                IsSectionBreakMark = paragraph.IsSectionBreakMark
            };
        }

        void WriteBlock(DocumentElement element, int depth)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    WriteParagraph(paragraph, depth);
                    break;
                case TableElement table:
                    WriteTable(table, depth);
                    break;
                case ImageElement image:
                    WriteImageParagraph(
                        image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description, depth,
                        image.Crop, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical,
                        ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), image.Opacity);
                    break;
                case FloatingImageElement floatingImage:
                    WriteFloatingImage(floatingImage, depth);
                    break;
                case HorizontalRuleElement:
                    Indent(depth).Append("<hr />\n");
                    break;
                case FloatingTextBoxElement {WrapType: WrapType.None} floatingTextBox:
                    WriteAbsoluteTextBox(floatingTextBox, depth);
                    break;
                case FloatingTextBoxElement textBox:
                    // In-flow placement (the reflowable export's model for wrapped text boxes),
                    // but with the box's own chrome — its fill and frame are what make a quote
                    // box or panel read as one (they were dropped, leaving bare paragraphs).
                    // A box with nothing visible in it stays a bare div, so an empty one costs
                    // the flow nothing.
                    var hasVisibleContent = textBox
                        .Content
                        .Any(_ => _ is not ParagraphElement contentParagraph ||
                                  !IsBlankForHtml(contentParagraph));
                    Indent(depth).Append(hasVisibleContent ? TextBoxOpenTag(textBox, positionStyle: null) : "<div>").Append('\n');
                    WriteElements(textBox.Content, depth + 1);
                    Indent(depth).Append("</div>\n");
                    break;
                case PositionedFrameElement frame:
                    Indent(depth).Append("<div>\n");
                    WriteElements(frame.Content, depth + 1);
                    Indent(depth).Append("</div>\n");
                    break;
                case ContentControlElement contentControl:
                    WriteContentControl(contentControl, depth);
                    break;
                case WordArtElement wordArt:
                    WriteInlineWordArt(wordArt, depth);
                    break;
                case FloatingWordArtElement floatingWordArt:
                    WriteFloatingWordArt(floatingWordArt, depth);
                    break;
                case TextFormFieldElement textField:
                    WriteTextParagraph(textField.Value.Length > 0 ? textField.Value : textField.DefaultText ?? "", depth);
                    break;
                case DropDownFormFieldElement dropDown:
                    WriteTextParagraph(SelectedItem(dropDown), depth);
                    break;
                case CheckBoxFormFieldElement checkBox:
                    WriteTextParagraph(checkBox.Checked ? "☑" : "☐", depth);
                    break;
                case FloatingShapeElement shape:
                    WriteShape(shape, depth);
                    break;
                case InkElement:
                    options.OnWarning?.Invoke(
                        new(
                            WarningKind.UnsupportedElement,
                            $"{element.GetType().Name} cannot be represented in reflowable HTML and was dropped."));
                    break;
                // PageBreak / ColumnBreak / SectionBreak / LineBreak have no semantic representation
                // in reflowable HTML and are intentionally omitted (no warning — they're flow hints,
                // not content).
            }
        }

        void WriteParagraph(ParagraphElement paragraph, int depth)
        {
            if (IsBlankForHtml(paragraph))
            {
                // Word's letter/résumé templates space their blocks (date, address, greeting, body
                // paragraphs, closing) with EMPTY paragraphs rather than paragraph spacing, and each
                // is a real line. Silently dropping them — the obvious "skip empty blocks" move —
                // collapses those gaps (cover-letters run together). Emit a one-line spacer instead,
                // carrying the paragraph's own before/after spacing; the &nbsp; forces the line box a
                // bare <p> would collapse. Matches the raster/PDF backends, which lay the empty line
                // out at its font height.
                Indent(depth).Append("<p");
                AppendParagraphStyle(paragraph.Properties);
                builder.Append(">&nbsp;</p>\n");
                return;
            }

            var level = DocumentExportHelpers.TryGetHeadingLevel(paragraph.Properties);
            if (level != null)
            {
                Indent(depth);
                AppendHeading(paragraph, level.Value);
                builder.Append('\n');
                return;
            }

            if (TryWriteTabbedParagraph(paragraph, depth, pageSettings.ContentWidth))
            {
                return;
            }

            Indent(depth).Append("<p");
            AppendParagraphStyle(paragraph.Properties);
            builder.Append('>');
            AppendInline(paragraph.Runs);
            builder.Append("</p>\n");
        }

        // A paragraph that lays its content out against DECLARED tab stops — a TOC entry with a
        // dot leader to a right stop, a multi-column row of left stops, a decimal figure column, a
        // signature underline — reflows as flex boxes sized from the stops, instead of collapsing
        // every tab to a single space. Only declared stops are handled: a tab riding the default
        // 36pt grid lands wherever the text before it ends, which a reflowing export cannot know.
        // Tabs pair with stops in declaration order (the common authoring shape — each of the
        // corpus fixtures declares exactly one stop per tab); a paragraph with more tabs than
        // stops, or a mid-line right/decimal stop that is not the final tab, keeps the fallback.
        //
        // `measure` is the width the paragraph is set in — the page's content width in the flow,
        // the cell's inside a table — which is what decides whether a right stop stands at the
        // right edge. Word draws a stop past that edge AT the edge, so anything from 40pt short
        // of it outward is the edge. A paragraph in a cell (`inCell`) is written without the
        // flow's indent and line break, as the rest of a cell's content is.
        bool TryWriteTabbedParagraph(ParagraphElement paragraph, int depth, double measure, bool inCell = false)
        {
            var properties = paragraph.Properties;
            if (properties.TabStops.Count == 0)
            {
                return false;
            }

            var stops = new List<TabStop>();
            var barStops = new List<TabStop>();
            foreach (var stop in properties.TabStops)
            {
                if (stop.Alignment == TabAlignment.Bar)
                {
                    barStops.Add(stop);
                }
                else
                {
                    stops.Add(stop);
                }
            }

            stops.Sort((a, b) => a.PositionPoints.CompareTo(b.PositionPoints));
            if (stops.Count == 0 &&
                barStops.Count == 0)
            {
                return false;
            }

            var segments = new List<List<Run>> {new()};
            foreach (var run in paragraph.Runs)
            {
                if (run.IsTab)
                {
                    segments.Add([]);
                }
                else if (run.InlineImageData != null ||
                         run.InlineShapeGroup != null)
                {
                    // Images inside a flex row would become flex items and shift; leave those
                    // paragraphs on the plain path.
                    return false;
                }
                else
                {
                    segments[^1].Add(run);
                }
            }

            if (stops.Count == 0)
            {
                // Bar stops only (bar_tabs): the bars are vertical rules at their positions,
                // independent of any tab characters, and the tabs themselves ride the default
                // grid — approximated as a fixed gap, since a reflowing export cannot know where
                // the grid lands. The rules are absolutely positioned children of the paragraph.
                OpenTabbedParagraph(depth, inCell);
                AppendParagraphStyle(properties, extraStyle: "position: relative");
                builder.Append('>');
                foreach (var bar in barStops)
                {
                    builder.Append("<span style=\"position: absolute; top: 0; bottom: 0; border-left: 0.75pt solid currentColor; left: ")
                        .Append(Length(bar.PositionPoints))
                        .Append("\"></span>");
                }

                for (var i = 0; i < segments.Count; i++)
                {
                    AppendInline(segments[i]);
                    if (i < segments.Count - 1)
                    {
                        builder.Append("<span style=\"display: inline-block; min-width: 18pt\"></span>");
                    }
                }

                builder.Append(inCell ? "</p>" : "</p>\n");
                return true;
            }

            var tabCount = segments.Count - 1;
            if (tabCount == 0 || ReachableStops(segments, stops, properties) is not { } reached)
            {
                return false;
            }

            stops = reached;

            // A right/decimal stop short of the right edge right-justifies the FOLLOWING segment
            // inside the span up to the stop, consuming two segments at once — only supportable
            // as the final tab.
            for (var i = 0; i < tabCount - 1; i++)
            {
                var stop = stops[i];
                if (stop.Alignment is
                        TabAlignment.Right or
                        TabAlignment.Decimal &&
                    stop.PositionPoints < measure - 40)
                {
                    return false;
                }
            }

            OpenTabbedParagraph(depth, inCell);
            AppendParagraphStyle(properties, extraStyle: "display: flex");
            builder.Append('>');
            WriteTabbedSegments(segments, stops, TextContext.Body, measure);
            builder.Append(inCell ? "</p>" : "</p>\n");
            return true;
        }

        void OpenTabbedParagraph(int depth, bool inCell)
        {
            if (!inCell)
            {
                Indent(depth);
            }

            builder.Append("<p");
        }

        // The flex-item spans of a tabbed paragraph, written into an already-opened
        // `display: flex` element (a <p> or a heading).
        void WriteTabbedSegments(List<List<Run>> segments, List<TabStop> stops, TextContext context, double measure)
        {
            var start = 0.0;
            var index = 0;
            while (index < segments.Count)
            {
                var isLastSegment = index == segments.Count - 1;
                if (isLastSegment)
                {
                    builder.Append("<span>");
                    AppendInline(segments[index], context);
                    builder.Append("</span>");
                    index++;
                    continue;
                }

                var stop = stops[index];
                if (stop.Alignment is
                    TabAlignment.Right or
                    TabAlignment.Decimal)
                {
                    if (stop.PositionPoints >= measure - 40)
                    {
                        // The stop sits at (or near) the content's right edge: a stretching filler
                        // pushes the rest of the line flush right, carrying the leader as a rule.
                        builder.Append("<span>");
                        AppendInline(segments[index], context);
                        builder.Append("</span><span style=\"flex: 1; ").Append(LeaderStyle(stop.Leader)).Append("\"></span>");
                        start = stop.PositionPoints;
                        index++;
                    }
                    else
                    {
                        // Mid-line right/decimal stop (guarded above to be the final tab): the box
                        // runs from the previous stop to this one, this segment at its left edge
                        // and the next right-justified at the stop. Decimal alignment degrades to
                        // right alignment, which coincides with it whenever the column's values
                        // carry the same number of fraction digits.
                        builder.Append("<span style=\"display: inline-flex; justify-content: space-between; min-width: ")
                            .Append(Length(stop.PositionPoints - start))
                            .Append("\"><span>");
                        AppendInline(segments[index], context);
                        builder.Append("</span><span>");
                        AppendInline(segments[index + 1], context);
                        builder.Append("</span></span>");
                        start = stop.PositionPoints;
                        index += 2;
                    }
                }
                else
                {
                    // Left stop (center approximated as left): this segment's box ends where the
                    // stop begins, so the next segment starts at the stop's position.
                    builder.Append("<span style=\"min-width: ")
                        .Append(Length(stop.PositionPoints - start))
                        .Append("\">");
                    AppendInline(segments[index], context);
                    builder.Append("</span>");
                    start = stop.PositionPoints;
                    index++;
                }
            }
        }

        // The tab-stop layout plan a HEADING can reuse: the sorted non-bar stops and per-tab run
        // segments, under the same restrictions the paragraph flex path applies — declared stops
        // only, tabs within the stop count, no inline images, mid-line right/decimal only as the
        // final tab. Null when the heading should keep the plain path. resumes/18's Experience
        // rows are the shape that demanded it: Heading2 declares a left stop and authors
        // "20XX – 20XX<tab>Senior Editor…", which collapsed to a single space-joined line.
        (List<List<Run>> Segments, List<TabStop> Stops)? PlanTabbedHeading(ParagraphElement paragraph)
        {
            if (paragraph.Properties.TabStops.Count == 0)
            {
                return null;
            }

            var stops = new List<TabStop>();
            foreach (var stop in paragraph.Properties.TabStops)
            {
                if (stop.Alignment != TabAlignment.Bar)
                {
                    stops.Add(stop);
                }
            }

            stops.Sort((a, b) => a.PositionPoints.CompareTo(b.PositionPoints));
            if (stops.Count == 0)
            {
                return null;
            }

            var segments = new List<List<Run>> {new()};
            foreach (var run in paragraph.Runs)
            {
                if (run.IsTab)
                {
                    segments.Add([]);
                }
                else if (run.InlineImageData != null ||
                         run.InlineShapeGroup != null)
                {
                    return null;
                }
                else
                {
                    segments[^1].Add(run);
                }
            }

            var tabCount = segments.Count - 1;
            if (tabCount == 0 || ReachableStops(segments, stops, paragraph.Properties) is not { } reached)
            {
                return null;
            }

            stops = reached;

            for (var i = 0; i < tabCount - 1; i++)
            {
                if (stops[i].Alignment is
                        TabAlignment.Right or
                        TabAlignment.Decimal &&
                    stops[i].PositionPoints < pageSettings.ContentWidth - 40)
                {
                    return null;
                }
            }

            return (segments, stops);
        }

        /// <summary>
        /// The stops a paragraph's tabs go to, one per tab in order, or null when a tab has no
        /// declared stop left to go to. A tab advances to the first stop PAST the pen, so a stop the
        /// text before it has already passed is skipped. The export measures nothing, so the pen
        /// is a lower bound — <see cref="leastAdvanceEm"/> an em for every character — and only a
        /// stop the text has certainly passed is skipped; one it may or may not have reached is
        /// taken, as every stop was before. business-plans/13's contents style declares four
        /// stops, the first two 22pt and 25pt in for a number the entries do not carry: paired
        /// with the first, each entry's page number was boxed to the left of where its title
        /// ends and ran on against it.
        /// </summary>
        static List<TabStop>? ReachableStops(List<List<Run>> segments, List<TabStop> stops, ParagraphProperties properties)
        {
            var reached = new List<TabStop>();
            var pen = properties.LeftIndentPoints;
            var next = 0;
            for (var index = 0; index < segments.Count - 1; index++)
            {
                foreach (var run in segments[index])
                {
                    if (!run.Properties.Hidden)
                    {
                        pen += run.Text.Length * run.Properties.FontSizePoints * leastAdvanceEm;
                    }
                }

                while (next < stops.Count &&
                       stops[next].PositionPoints <= pen)
                {
                    next++;
                }

                if (next == stops.Count)
                {
                    return null;
                }

                reached.Add(stops[next]);
                pen = stops[next].PositionPoints;
                next++;
            }

            return reached;
        }

        // The least a character of running text can be taken to advance, in ems: under the
        // narrowest letters of a proportional face, so that a width built from it is never more
        // than the text really sets.
        const double leastAdvanceEm = 0.2;

        // The flex filler that stands in for a leader tab: a rule along the gap, lifted to sit
        // near the baseline. A leaderless tab keeps a minimum gap so the two sides never touch.
        static string LeaderStyle(TabLeader leader) => leader switch
        {
            TabLeader.Dot => "border-bottom: 1pt dotted currentColor; margin: 0 2pt 0.28em",
            TabLeader.Hyphen => "border-bottom: 1pt dashed currentColor; margin: 0 2pt 0.28em",
            TabLeader.MiddleDot => "border-bottom: 1pt dotted currentColor; margin: 0 2pt 0.4em",
            TabLeader.Underscore or TabLeader.Heavy => "border-bottom: 1pt solid currentColor; margin: 0 2pt 0.28em",
            _ => "min-width: 8pt"
        };

        // Emits a heading paragraph as <hN ...>...</hN> (no indent or trailing newline — the body
        // and cell emit paths bracket it differently).
        void AppendHeading(ParagraphElement paragraph, int level)
        {
            var id = UniqueHeadingId(PlainText(paragraph.Runs));
            builder.Append("<h").Append(level);
            if (id.Length > 0)
            {
                builder.Append(" id=\"").Append(EncodeAttribute(id)).Append('"');
            }

            // The heading's stylesheet size (24pt for h1, …) sets the line-box strut even when
            // every run inside is smaller — a 10pt-styled Heading 1 in a table strip would still
            // get a ~25pt line and inflate the row well beyond Word. When the runs agree on one
            // size, lift it onto the <hN> so the strut matches the text; the spans then inherit
            // the size and stay clean.
            var headingDefaultSize = headingFontSizePoints[level - 1];
            var uniformSize = UniformRunFontSize(paragraph.Runs);
            var effectiveSize = uniformSize ?? headingDefaultSize;
            double? liftedSize;
            if (uniformSize != null &&
                Math.Abs(uniformSize.Value - headingDefaultSize) > 0.01)
            {
                liftedSize = uniformSize;
            }
            else
            {
                liftedSize = null;
            }

            // A heading laying its content out against declared tab stops (resumes/18's
            // "date<tab>title" Experience rows) takes the same flex form the paragraph path uses,
            // inside the heading tag so it keeps its semantics and stylesheet weight.
            var tabPlan = PlanTabbedHeading(paragraph);
            AppendParagraphStyle(
                paragraph.Properties,
                defaultHeadingSpacingBeforePoints,
                defaultHeadingSpacingAfterPoints,
                liftedSize,
                tabPlan != null ? "display: flex" : null,
                HeadingLineEms(paragraph));
            builder.Append('>');
            if (tabPlan is { } plan)
            {
                WriteTabbedSegments(plan.Segments, plan.Stops, new(true, effectiveSize), pageSettings.ContentWidth);
            }
            else
            {
                AppendInline(paragraph.Runs, new(true, effectiveSize));
            }

            builder.Append($"</h{level}>");
        }

        // The single font size shared by every visible text run, or null when the runs disagree or
        // carry no explicit size.
        // The faces' own line heights, for the line-height a heading's Auto multiple asks for
        // (HeadingLineEms). Built on first use, since most exports never ask.
        FontResolver<FontMetrics>? lineFonts;

        // Word's Auto line multiple scales the face's own single line (ascent and descent from
        // its metrics, FontMetrics.LinePitchPoints) where a CSS line-height number scales the em:
        // business-plans/07's 70pt title at 0.75 is a 64pt line in Word (0.75 x 70 x 1.2207 for
        // Calibri Light) and was a 52.5pt one here, its second line's caps on the first's
        // baseline; brochures/02's 36pt Franklin Gothic Demi title crowded the date under it the
        // same way. A heading lifts its uniform run size onto the <hN> (AppendHeading), so its
        // strut is its text and the em it scales is the right one; a paragraph's is not, which is
        // why only headings take the face's line. Null when the runs do not agree on one face or
        // the face cannot be found, and the multiple is then written as it was.
        double? HeadingLineEms(ParagraphElement paragraph)
        {
            if (paragraph.Properties.LineSpacingRule != LineSpacingRule.Auto ||
                UniformRunFontFamily(paragraph.Runs) is not { } family)
            {
                return null;
            }

            lineFonts ??= LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
            try
            {
                var metrics = lineFonts.Resolve(family, paragraph.Runs[0].Properties.Bold, false);
                return (double) metrics.LineBoxUnits / metrics.UnitsPerEm;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        static string? UniformRunFontFamily(IReadOnlyList<Run> runs)
        {
            string? family = null;
            foreach (var run in runs)
            {
                if (run.Properties.Hidden ||
                    run.IsTab ||
                    run.InlineImageData != null ||
                    string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(run.Properties.FontFamily))
                {
                    return null;
                }

                if (family == null)
                {
                    family = run.Properties.FontFamily;
                }
                else if (!family.Equals(run.Properties.FontFamily, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return family;
        }

        static double? UniformRunFontSize(IReadOnlyList<Run> runs)
        {
            double? size = null;
            foreach (var run in runs)
            {
                if (run.Properties.Hidden ||
                    run.IsTab ||
                    run.InlineImageData != null ||
                    run.FootnoteReferenceId != null ||
                    run.EndnoteReferenceId != null ||
                    string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var runSize = run.Properties.FontSizePoints;
                if (runSize <= 0)
                {
                    return null;
                }

                if (size == null)
                {
                    size = runSize;
                }
                else if (Math.Abs(size.Value - runSize) > 0.01)
                {
                    return null;
                }
            }

            return size;
        }

        void WriteTextParagraph(string text, int depth)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Indent(depth).Append($"<p>{EncodeText(text)}</p>\n");
        }

        // Consecutive Quote / Intense Quote paragraphs become one <blockquote> of plain <p>
        // children. The blockquote is the semantic stand-in for the Quote style's visual indent, so
        // the paragraph-level style (its indent / spacing) is dropped; run formatting — the style's
        // italic in particular — still flows through AppendInline.
        void WriteBlockQuote(IReadOnlyList<ParagraphElement> paragraphs, int depth)
        {
            var visible = paragraphs.Where(_ => !IsBlankForHtml(_)).ToList();
            if (visible.Count == 0)
            {
                return;
            }

            Indent(depth).Append("<blockquote>\n");
            foreach (var paragraph in visible)
            {
                Indent(depth + 1).Append("<p>");
                AppendInline(paragraph.Runs);
                builder.Append("</p>\n");
            }

            Indent(depth).Append("</blockquote>\n");
        }

        // A footnote / endnote reference becomes a <sup> link to its entry in the trailing notes
        // section. n counts up in first-reference order across footnotes and endnotes together;
        // repeat references to one note reuse its number, and a reference whose note body is
        // missing emits nothing.
        string NoteMarker(string id, bool endnote)
        {
            var key = (endnote ? "e" : "f") + id;

            // A repeat reference links to the note but omits the fnref id — the back-link already
            // targets the first citation, and a duplicate id would be invalid HTML.
            if (noteNumbers.TryGetValue(key, out var existing))
            {
                return $"<sup class=\"footnote-ref\"><a href=\"#fn-{existing}\">{existing}</a></sup>";
            }

            var texts = endnote ? endnoteTexts : footnoteTexts;
            if (!texts.TryGetValue(id, out var text))
            {
                return "";
            }

            var number = notes.Count + 1;
            noteNumbers[key] = number;
            notes.Add((number, text));
            return $"<sup class=\"footnote-ref\"><a href=\"#fn-{number}\" id=\"fnref-{number}\">{number}</a></sup>";
        }

        // Emits the trailing notes section (referenced footnotes and endnotes, in reference order),
        // each list item carrying a back-link to its citation. Nothing is emitted when no note was
        // referenced.
        public void WriteNoteDefinitions()
        {
            if (notes.Count == 0)
            {
                return;
            }

            Indent(0).Append("<section class=\"footnotes\">\n");
            Indent(1).Append("<ol>\n");
            foreach (var (number, text) in notes)
            {
                Indent(2)
                    .Append($"""<li id="fn-{number}">{EncodeText(text)} <a href="#fnref-{number}">↩</a></li>""")
                    .Append('\n');
            }

            Indent(1).Append("</ol>\n");
            Indent(0).Append("</section>\n");
        }

        // Resolves the image source before opening the <p> so a dropped image (no handler,
        // embedding disabled) produces no empty paragraph.
        void WriteImageParagraph(
            byte[] data,
            string? contentType,
            double widthPoints,
            double heightPoints,
            string? description,
            int depth,
            ImageCrop? crop = null,
            double rotationDegrees = 0,
            bool flipHorizontal = false,
            bool flipVertical = false,
            ImageRecolor? recolor = null,
            double opacity = 1)
        {
            var source = ResolveImageSource(data, contentType, widthPoints, heightPoints);
            if (source == null)
            {
                return;
            }

            Indent(depth).Append("<p>");
            AppendImageTag(source, widthPoints, heightPoints, description, null, crop, rotationDegrees, flipHorizontal, flipVertical, recolor, opacity);
            builder.Append("</p>\n");
        }

        // A wrap-enabled float becomes a CSS float so the following text flows beside it — the
        // same layout the raster backends produce for wrapSquare/Tight/Through. wrapNone
        // (overlap) and wrapTopAndBottom (text above and below) keep the block treatment,
        // which is already HTML's closest match for them.
        void WriteFloatingImage(FloatingImageElement image, int depth)
        {
            // Wrap NONE means the image floats over or under the text rather than displacing it, so
            // it is placed absolutely at its anchor coordinates exactly as a floating SHAPE is
            // (WriteShape) — same page-absolute arithmetic, same z-index rule. Emitting it as a
            // block paragraph instead cost it both its position and, worse, its transparency to the
            // flow: a full-width decorative banner then consumed its own height, pushing the text
            // that follows down the document. That is how a template's white cover text ends up
            // BELOW the dark panel meant to sit behind it, rendering white-on-white and
            // unreadable (agendas-minutes/02's masthead and roster; todo.md HTML #26). TopAndBottom
            // is the one non-wrapping type that genuinely does take flow space — text goes above and
            // below it, never beside — so it keeps the paragraph.
            if (image.WrapType == WrapType.TopAndBottom)
            {
                WriteImageParagraph(
                    image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description, depth,
                    image.Crop, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical,
                    ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), image.Opacity);
                return;
            }

            if (image.WrapType == WrapType.None)
            {
                WriteAbsoluteImage(image, depth);
                return;
            }

            var source = ResolveImageSource(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints);
            if (source == null)
            {
                return;
            }

            // An explicit @wrapText side dictates where the text goes (so the image floats the
            // opposite way); otherwise float to whichever half of the content area holds the
            // image's centre. The same-side clear keeps successive floats stacked vertically —
            // in Word each float's Y comes from its anchor paragraph, which flows below the
            // previous float's wrapped text, so side-by-side pile-ups don't happen there. The
            // wrap clearances (wp @dist*) become margins so the wrapped text keeps Word's gap.
            var side = image.WrapTextSide switch
            {
                WrapTextSide.Left => "right",
                WrapTextSide.Right => "left",
                _ => image.HorizontalPositionPoints + image.WidthPoints / 2 <= pageSettings.ContentWidth / 2 ? "left" : "right"
            };
            var style = $"float: {side}; clear: {side}";
            if (image.WrapDistanceTopPoints > 0.01 ||
                image.WrapDistanceRightPoints > 0.01 ||
                image.WrapDistanceBottomPoints > 0.01 ||
                image.WrapDistanceLeftPoints > 0.01)
            {
                style += $"; margin: {BoxShorthand(image.WrapDistanceTopPoints, image.WrapDistanceRightPoints, image.WrapDistanceBottomPoints, image.WrapDistanceLeftPoints)}";
            }

            style += PictureClip(image.ClipToEllipse, image.ClipSubpaths);

            Indent(depth);
            AppendImageTag(
                source, image.WidthPoints, image.HeightPoints, image.Description, style,
                image.Crop, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical,
                ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), image.Opacity);
            builder.Append('\n');
        }

        // A wrap-NONE float, placed at its anchor coordinates and layered by its behind-text flag.
        // The box arithmetic mirrors ShapeBox: a page-relative anchor measures from the paper edge,
        // anything else from the margin, and the absolute coordinates land against the body's
        // padding box — which starts at the page edge, since the body carries the page margins as
        // padding and no margin of its own.
        void WriteAbsoluteImage(FloatingImageElement image, int depth)
        {
            var source = ResolveImageSource(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints);
            if (source == null)
            {
                return;
            }

            var (left, top) = FloatOrigin(
                PageLeft(image.HorizontalAnchor, image.HorizontalPositionPoints),
                PageTop(image.VerticalAnchor, image.VerticalPositionPoints),
                image.VerticalAnchor);
            var style = $"position: absolute; left: {Length(left)}; top: {Length(top)}; z-index: {(image.BehindText ? "-1" : "1")}{PictureClip(image.ClipToEllipse, image.ClipSubpaths)}";

            Indent(depth).Append("""<div style="position: relative">""");
            AppendImageTag(
                source, image.WidthPoints, image.HeightPoints, image.Description, style,
                image.Crop, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical,
                ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), image.Opacity);
            builder.Append("</div>\n");
        }

        /// <summary>
        /// A text box the text does not wrap round floats over the page like a wrap-NONE picture,
        /// so it is placed the same way: absolutely, at its anchor offsets, at its own size, and
        /// out of the flow. Every body-level text box of the corpus is one of these, and written
        /// into the flow each took the room of its text where Word gives it none — which pushed
        /// everything after it down against the art placed around it. labels/06's twenty ADMIT
        /// ONE stubs stacked as a column at the head of the sheet, cards/02's two code boxes
        /// opened the page above the first ticket, cards/13's page-sized wash drew as a strip
        /// one line tall, and labels/10 and labels/11 ran a label late against their artwork.
        ///
        /// The box is laid out as the layout engine lays it (Fragmenter.PlaceTextBox): its fill
        /// and frame are a shape of the box's size and geometry behind the text, a box of fixed
        /// size hides what falls outside it, and a rotated box turns about its centre with its
        /// text.
        /// </summary>
        void WriteAbsoluteTextBox(FloatingTextBoxElement textBox, int depth)
        {
            var box = TextBoxShape(textBox);
            if (box.FillColorHex != null || box.LineColorHex != null)
            {
                WriteShape(box, depth);
            }

            if (textBox.Content.All(_ => _ is ParagraphElement paragraph &&
                                         IsBlankForHtml(paragraph)))
            {
                return;
            }

            var (pageLeft, pageTop, _, _) = ShapeBox(box, pageSettings);
            var (left, top) = FloatOrigin(pageLeft, pageTop, box.VerticalAnchor);
            var style = new StringBuilder();
            style.Append($"position: absolute; left: {Length(left)}; top: {Length(top)}; width: {Length(textBox.WidthPoints)}; ");
            style.Append(textBox.HidesOverflow
                ? $"height: {Length(textBox.HeightPoints)}; overflow: hidden; "
                : $"min-height: {Length(textBox.HeightPoints)}; ");
            if (Math.Abs(textBox.RotationDegrees) > 0.01)
            {
                style.Append($"transform: rotate({Number(textBox.RotationDegrees)}deg); ");
            }

            style.Append($"z-index: {(textBox.BehindText ? "-1" : "1")}");
            Indent(depth).Append("""<div style="position: relative">""").Append('\n');
            Indent(depth + 1).Append($"""<div style="{style}">""").Append('\n');
            WriteElements(textBox.Content, depth + 2);
            Indent(depth + 1).Append("</div>\n");
            Indent(depth).Append("</div>\n");
        }

        // A text box's own box as a shape: where it stands, its size, and the fill and frame
        // drawn behind its text.
        static FloatingShapeElement TextBoxShape(FloatingTextBoxElement textBox) =>
            new()
            {
                WidthPoints = textBox.WidthPoints,
                HeightPoints = textBox.HeightPoints,
                HorizontalPositionPoints = textBox.HorizontalPositionPoints,
                VerticalPositionPoints = textBox.VerticalPositionPoints,
                HorizontalAnchor = textBox.HorizontalAnchor,
                VerticalAnchor = textBox.VerticalAnchor,
                HorizontalPositionPercent = textBox.HorizontalPositionPercent,
                VerticalPositionPercent = textBox.VerticalPositionPercent,
                BehindText = textBox.BehindText,
                FillColorHex = textBox.BackgroundColorHex,
                LineColorHex = textBox.LineWidthPoints > 0 ? textBox.LineColorHex : null,
                LineWidthPoints = textBox.LineWidthPoints > 0 ? textBox.LineWidthPoints : null,
                LineAlpha = textBox.LineAlpha,
                Subpaths = textBox.Subpaths,
                RotationDegrees = textBox.RotationDegrees
            };

        // A picture cropped to its pic:spPr geometry, as the raster painters clip it. An ellipse
        // (a:prstGeom prst="ellipse") is the oval inscribed in the box, which a 50% radius on each
        // axis draws. A custom geometry arrives as contours in the unit square
        // (FloatingImageElement.ClipSubpaths), which is a polygon in percentages of the box — one
        // contour only, since a CSS polygon is one; a picture cut by several keeps its rectangle.
        // brochures/03's round portrait photo is cut by a custGeom circle, and came out square;
        // its inline photo on page 2 is cut by an ellipse.
        static string PictureClip(bool clipToEllipse, IReadOnlyList<IReadOnlyList<(double X, double Y)>>? clipSubpaths)
        {
            if (clipToEllipse)
            {
                return "; border-radius: 50%";
            }

            if (clipSubpaths is [{Count: > 2} contour])
            {
                var points = string.Join(", ", contour.Select(_ => $"{Number(_.X * 100)}% {Number(_.Y * 100)}%"));
                return $"; clip-path: polygon({points})";
            }

            return "";
        }

        /// <summary>
        /// Turns a float's page-absolute box into coordinates relative to an empty
        /// <c>position: relative</c> wrapper emitted at the float's own place in the flow — which is
        /// where the anchor paragraph sits, since the parser detaches a float from the paragraph that
        /// declared it. Anchoring there rather than at the document origin is what Word's dominant
        /// case actually asks for: across the corpus 128 of ~207 wrap-NONE floats declare
        /// <c>wp:positionV relativeFrom="paragraph"</c> against only 24 page-relative ones, so a
        /// document-origin placement mis-positions the majority and, worse, stacks every page's
        /// background art on page one — the HTML export has no pages to separate them. The wrapper
        /// is empty and zero-height, so it collapses through and costs the flow nothing.
        /// A page-relative anchor takes the wrapper for where its page's CONTENT starts, one top
        /// margin down the page; that is exact for art anchored at a page's first paragraph (the
        /// common template shape) and an approximation elsewhere, which beats collapsing pages
        /// together. The wrapper stood for the page's top EDGE until 2026-10-12, which set every
        /// page-relative drawing one top margin low against the text of its own page:
        /// business/02's shaded panel started under the COMPANY NAME heading it backs, and
        /// agendas-minutes/14's two top stripes lay across the title they sit above. What reaches
        /// up into the margin has the body's top padding to draw in on the first page and the
        /// room <see cref="WriteBody"/> reserves on a later one.
        /// </summary>
        (double Left, double Top) FloatOrigin(double pageLeft, double pageTop, VerticalAnchor anchor) =>
            (pageLeft - pageSettings.MarginLeft,
                pageTop - (anchor is VerticalAnchor.Page or VerticalAnchor.Margin ? flowPageTop ?? pageSettings.MarginTop : pageSettings.MarginTop));

        // A float's offset from the page's top-left corner: a page-relative anchor measures from
        // the paper edge, anything else from the margin.
        double PageLeft(HorizontalAnchor anchor, double offsetPoints) =>
            (anchor == HorizontalAnchor.Page ? 0 : pageSettings.MarginLeft) + offsetPoints;

        double PageTop(VerticalAnchor anchor, double offsetPoints) =>
            (anchor == VerticalAnchor.Page ? 0 : pageSettings.MarginTop) + offsetPoints;

        void WriteContentControl(ContentControlElement contentControl, int depth)
        {
            if (contentControl.Runs is {Count: > 0} runs)
            {
                Indent(depth).Append("<p>");
                AppendInline(runs);
                builder.Append("</p>\n");
                return;
            }

            WriteTextParagraph(contentControl.Content, depth);
        }

        void AppendParagraphStyle(
            ParagraphProperties properties,
            double defaultBeforePoints = 0,
            double defaultAfterPoints = defaultSpacingAfterPoints,
            double? fontSizePoints = null,
            string? extraStyle = null,
            double? lineEms = null)
        {
            // w:bidi — the paragraph reads right-to-left. dir="rtl" flips the default alignment
            // to the right and lets the browser run its own bidi reordering, matching the raster
            // backends' right-aligned layout.
            if (properties.IsRightToLeft)
            {
                builder.Append(" dir=\"rtl\"");
            }

            var parts = new List<string>();
            if (extraStyle != null)
            {
                parts.Add(extraStyle);
            }

            // A heading's lifted uniform run size (see AppendHeading) rides on the same attribute.
            if (fontSizePoints is {} fontSize)
            {
                parts.Add($"font-size: {Length(fontSize)}");
            }

            var align = properties.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "justify",
                _ => null
            };
            if (align != null)
            {
                parts.Add($"text-align: {align}");
            }

            // Vertical spacing — only deviations from the stylesheet default (0-before / 8pt-after
            // for paragraphs, 12pt-before / 8pt-after for headings).
            if (Math.Abs(properties.SpacingBeforePoints - defaultBeforePoints) > 0.01)
            {
                parts.Add($"margin-top: {Length(properties.SpacingBeforePoints)}");
            }

            if (Math.Abs(properties.SpacingAfterPoints - defaultAfterPoints) > 0.01)
            {
                parts.Add($"margin-bottom: {Length(properties.SpacingAfterPoints)}");
            }

            AppendIndentStyle(parts, properties);

            // Line spacing. Auto → a unitless multiplier (the body default is 1.08); Exactly / AtLeast
            // → a fixed point height (CSS has no "at least", so AtLeast is approximated by the value).
            if (properties.LineSpacingRule == LineSpacingRule.Auto)
            {
                // A heading's multiple scales its face's own line (HeadingLineEms); written even at
                // the default multiple, since the stylesheet's 1.08 is of the em.
                if (lineEms is { } ems)
                {
                    parts.Add($"line-height: {Number(properties.LineSpacingMultiplier * ems)}");
                }
                else if (Math.Abs(properties.LineSpacingMultiplier - defaultLineHeightMultiplier) > 0.001)
                {
                    parts.Add($"line-height: {Number(properties.LineSpacingMultiplier)}");
                }
            }
            else if (properties.LineSpacingPoints > 0.01)
            {
                parts.Add($"line-height: {Length(properties.LineSpacingPoints)}");
            }

            AppendParagraphBorderStyle(parts, properties);

            // Paragraph shading (w:shd, or a block background-color in HTML input) — Word draws a
            // full-width band behind the paragraph; the <p>'s own background box is the reflowable
            // equivalent.
            var shading = DocumentExportHelpers.NormalizeColor(properties.BackgroundColorHex);
            if (shading != null)
            {
                parts.Add($"background-color: {shading}");
            }

            if (parts.Count > 0)
            {
                builder.Append(" style=\"").Append(string.Join("; ", parts)).Append('"');
            }
        }

        // Indentation. The left indent applies to the whole block; a hanging indent then pulls
        // the first line back out of it (negative text-indent), while a first-line indent pushes
        // only the first line in (OOXML makes the two mutually exclusive). Negative values
        // (outdents) are valid CSS and emitted verbatim.
        static void AppendIndentStyle(List<string> parts, ParagraphProperties properties)
        {
            var marginLeft = properties.LeftIndentPoints;
            var textIndent = 0.0;
            if (properties.HangingIndentPoints > 0.01)
            {
                textIndent = -properties.HangingIndentPoints;
            }
            else if (properties.FirstLineIndentPoints > 0.01)
            {
                textIndent = properties.FirstLineIndentPoints;
            }

            if (Math.Abs(marginLeft) > 0.01)
            {
                parts.Add($"margin-left: {Length(marginLeft)}");
            }

            if (properties.RightIndentPoints > 0.01)
            {
                parts.Add($"margin-right: {Length(properties.RightIndentPoints)}");
            }

            if (Math.Abs(textIndent) > 0.01)
            {
                parts.Add($"text-indent: {Length(textIndent)}");
            }
        }

        static bool HasIndent(ParagraphProperties properties) =>
            Math.Abs(properties.LeftIndentPoints) > 0.01 ||
            properties.RightIndentPoints > 0.01 ||
            properties.HangingIndentPoints > 0.01 ||
            properties.FirstLineIndentPoints > 0.01;

        // Paragraph borders (w:pBdr). Word draws ONE box around a run of consecutive paragraphs
        // sharing the same w:pBdr (SharesBorderGroupWith), so where the neighbours are known
        // (borderGroupPrevious/Next) the group law applies per member: the top edge belongs to the
        // group's first member (a later member shows the w:between rule there, or nothing) and the
        // bottom edge to its last. Members still render as separate CSS boxes, so a full four-sided
        // group keeps small side gaps at member boundaries — the reflowable residue.
        void AppendParagraphBorderStyle(List<string> parts, ParagraphProperties properties)
        {
            if (properties.Borders is not {HasAnyBorder: true} borders)
            {
                return;
            }

            var joinsPrevious = borderGroupPrevious != null &&
                                borderGroupPrevious.SharesBorderGroupWith(properties);
            var joinsNext = borderGroupNext != null &&
                            properties.SharesBorderGroupWith(borderGroupNext);
            var top = properties.BorderTopSpacePoints;
            var bottom = properties.BorderBottomSpacePoints;
            var effective = borders;
            if (joinsPrevious)
            {
                effective = effective with
                {
                    Top = properties.BorderBetween
                };
                top = properties.BorderBetween.IsVisible ? properties.BorderBetweenSpacePoints : 0;
            }

            if (joinsNext)
            {
                effective = effective with
                {
                    Bottom = BorderEdge.None
                };
                bottom = 0;
            }

            if (effective.HasAnyBorder)
            {
                AppendBorders(parts, effective);
            }

            // w:pBdr/@w:space is the gap between each border and the text → CSS padding.
            var right = properties.BorderRightSpacePoints;
            var left = properties.BorderLeftSpacePoints;
            if (top > 0 || right > 0 || bottom > 0 || left > 0)
            {
                parts.Add($"padding: {BoxShorthand(top, right, bottom, left)}");
            }
        }

        void WriteList(IReadOnlyList<ListNode> nodes, int depth) =>
            WriteList(nodes, depth, TextContext.Body);

        void WriteList(IReadOnlyList<ListNode> nodes, int depth, TextContext context)
        {
            // Consecutive siblings of the same kind (ordered vs unordered) share one list element.
            var index = 0;
            while (index < nodes.Count)
            {
                var ordered = nodes[index].Ordered;
                var tag = ordered ? "ol" : "ul";
                Indent(depth).Append('<').Append(tag);
                if (ordered)
                {
                    var numbering = nodes[index].Paragraph.Properties.Numbering!;

                    // Word's roman/letter markers (w:numFmt) become the CSS counter style — an <ol>
                    // defaults to decimal, so without this every "I."/"a)" list renders "1.". Only
                    // the four non-decimal styles need an override; decimal inherits and stays clean.
                    var listStyle = ListStyleType(numbering.Format);
                    if (listStyle != null)
                    {
                        builder.Append(" style=\"list-style-type: ").Append(listStyle).Append('"');
                    }

                    // Carry the real start ordinal ("10." after a w:startOverride) so restarted /
                    // continued lists keep their numbers.
                    var start = DocumentExportHelpers.ListStartNumber(numbering);
                    if (start is > 1)
                    {
                        builder.Append(" start=\"").Append(start.Value).Append('"');
                    }
                }
                else
                {
                    // The level's own glyph decides the marker. Browsers cycle disc/circle/square by
                    // nesting depth and then stay on square, so a level-4 "•" renders as a square and
                    // Word's deeper Wingdings markers (▪, ►) are lost without this. Glyphs with a CSS
                    // keyword use it; the rest become a string marker of the glyph itself.
                    var listStyle = BulletStyleType(nodes[index].Paragraph.Properties.Numbering?.Text);
                    if (listStyle != null)
                    {
                        builder.Append(" style=\"list-style-type: ").Append(listStyle).Append('"');
                    }
                }

                builder.Append(">\n");
                while (index < nodes.Count &&
                       nodes[index].Ordered == ordered)
                {
                    WriteListItem(nodes[index], depth + 1, context);
                    index++;
                }

                Indent(depth).Append($"</{tag}>\n");
            }
        }

        /// <summary>
        /// A browser draws a list item's marker in the item's own font, size and colour, so the
        /// marker's formatting goes on the <c>&lt;li&gt;</c> and the runs inside carry only what
        /// differs from it. Left on the runs alone, the marker took the body's 11pt black
        /// whatever the item's text was: business-plans/08 and business-plans/12 numbered their
        /// 20pt and 22pt section headings with a body-sized "3.", and resumes/10's accent
        /// bullets and business-plans/12's SWOT bullets drew black.
        /// </summary>
        void WriteListItem(ListNode node, int depth, TextContext context)
        {
            var item = MarkerContext(node.Paragraph, context);
            var parts = new List<string>();
            if (item.Bold != context.Bold)
            {
                parts.Add(item.Bold ? "font-weight: bold" : "font-weight: normal");
            }

            if (item.FontFamily != context.FontFamily)
            {
                parts.Add($"font-family: {CssFontFamily(item.FontFamily ?? bodyFont ?? "sans-serif")}, sans-serif");
            }

            if (item.Color != context.Color)
            {
                parts.Add($"color: {item.Color ?? "#000000"}");
            }

            if (Math.Abs(item.FontSizePoints - context.FontSizePoints) > 0.01)
            {
                parts.Add($"font-size: {Length(item.FontSizePoints)}");
            }

            Indent(depth).Append("<li");
            if (parts.Count > 0)
            {
                builder.Append(" style=\"").Append(string.Join("; ", parts)).Append('"');
            }

            builder.Append('>');
            AppendInline(node.Paragraph.Runs, item);
            if (node.Children.Count > 0)
            {
                builder.Append('\n');
                WriteList(node.Children, depth + 1, item);
                Indent(depth);
            }

            builder.Append("</li>\n");
        }

        // What a list item sets for its text: its marker's formatting, by the layout engine's
        // rule (CanonicalParagraphMeasurer.MarkerProperties) — the paragraph's first run, with
        // the numbering level's own size and colour over it. A glyph bullet is drawn from the
        // bullet face whatever the text's, so it takes neither the run's weight nor its family.
        // An item with no runs has nothing to read a marker from and keeps what it inherits.
        TextContext MarkerContext(ParagraphElement paragraph, TextContext context)
        {
            if (paragraph.Properties.Numbering is not { } numbering ||
                paragraph.Runs.Count == 0)
            {
                return context;
            }

            var marker = CanonicalParagraphMeasurer.MarkerProperties(paragraph, numbering);
            var glyph = FontHelpers.UseBulletFont(numbering.Text, numbering.FontFamily);
            var color = DocumentExportHelpers.NormalizeColor(marker.ColorHex);
            var sameFamily = string.IsNullOrEmpty(marker.FontFamily) ||
                             string.Equals(marker.FontFamily, context.FontFamily ?? bodyFont, StringComparison.OrdinalIgnoreCase);
            return new(
                glyph ? context.Bold : marker.Bold,
                marker.FontSizePoints > 0 ? marker.FontSizePoints : context.FontSizePoints,
                color == "#000000" ? null : color,
                glyph || sameFamily ? context.FontFamily : marker.FontFamily);
        }

        // The CSS list-style-type for an ordered level's counter style, or null for Decimal (the
        // <ol> default, which needs no override). CSS names letter styles "alpha"; the ")" vs "."
        // marker suffix isn't expressible via list-style-type and is left as the CSS default ".".
        // The CSS marker for an unordered level's bullet glyph. Word's Symbol-font "•" is the
        // browser's disc and the Courier "o" its circle; everything else — the Wingdings small
        // square, pointer, and any other glyph the parser surfaced — is emitted as a CSS string
        // marker so the exact character shows. A trailing space keeps the string marker from
        // hugging the item text. Null (no numbering info) leaves the browser default.
        static string? BulletStyleType(string? glyph) => glyph switch
        {
            null or "" => null,
            "•" or "●" => "disc",
            "o" or "○" => "circle",
            _ => $"'{glyph} '"
        };

        static string? ListStyleType(ListNumberFormat format) => format switch
        {
            ListNumberFormat.UpperRoman => "upper-roman",
            ListNumberFormat.LowerRoman => "lower-roman",
            ListNumberFormat.UpperLetter => "upper-alpha",
            ListNumberFormat.LowerLetter => "lower-alpha",
            _ => null
        };

        void WriteTable(TableElement table, int depth)
        {
            Indent(depth).Append("<table");

            // Table-level geometry the stylesheet can't know: w:jc rides on the margins (a
            // centred/right table otherwise renders flush left), w:tblInd indents a left table,
            // and a pct-width table (w:tblW type="pct") fills its container instead of hugging
            // content. dxa widths stay off the table — their columns already carry cell widths.
            var tableStyles = new List<string>();
            var tableProperties = table.Properties;
            switch (tableProperties.Alignment)
            {
                case TextAlignment.Center:
                    tableStyles.Add("margin-left: auto; margin-right: auto");
                    break;
                case TextAlignment.Right:
                    tableStyles.Add("margin-left: auto");
                    break;
                default:
                    if (Math.Abs(tableProperties.IndentPoints) > 0.01)
                    {
                        tableStyles.Add($"margin-left: {Length(tableProperties.IndentPoints)}");
                    }

                    break;
            }

            if (tableProperties.FillContainer)
            {
                var fraction = tableProperties.PreferredWidthFraction ?? 1.0;
                tableStyles.Add($"width: {Number(fraction * 100)}%");
            }

            // w:tblCellSpacing switches Word to the detached-border model: an outer frame around
            // the table plus a box per cell, separated by twice the declared spacing. That is
            // exactly CSS's separate-borders table, so override the stylesheet's collapse and put
            // the table's own borders on the frame (the per-cell boxes already ride on the cells).
            if (tableProperties.CellSpacingPoints > 0)
            {
                tableStyles.Add($"border-collapse: separate; border-spacing: {Length(2 * tableProperties.CellSpacingPoints)}");
                if (tableProperties.DefaultBorders is {HasAnyBorder: true} frame)
                {
                    AppendBorders(tableStyles, frame);
                }
            }

            if (tableStyles.Count > 0)
            {
                builder.Append(" style=\"").Append(string.Join("; ", tableStyles)).Append('"');
            }

            builder.Append(">\n");

            var rows = table.Rows;
            var totalColumns = 0;
            foreach (var row in rows)
            {
                var width = row.GridBefore + row.GridAfter;
                foreach (var cell in row.Cells)
                {
                    width += Math.Max(1, cell.Properties.GridSpan);
                }

                totalColumns = Math.Max(totalColumns, width);
            }

            var headerRowCount = 0;
            while (headerRowCount < rows.Count &&
                   rows[headerRowCount].IsHeader)
            {
                headerRowCount++;
            }

            if (headerRowCount > 0)
            {
                Indent(depth + 1).Append("<thead>\n");
                for (var rowIndex = 0; rowIndex < headerRowCount; rowIndex++)
                {
                    WriteRow(table, rowIndex, depth + 2, header: true, totalColumns);
                }

                Indent(depth + 1).Append("</thead>\n");
            }

            Indent(depth + 1).Append("<tbody>\n");
            for (var rowIndex = headerRowCount; rowIndex < rows.Count; rowIndex++)
            {
                WriteRow(table, rowIndex, depth + 2, header: false, totalColumns);
            }

            Indent(depth + 1).Append("</tbody>\n");
            Indent(depth).Append("</table>\n");
        }

        void WriteRow(TableElement table, int rowIndex, int depth, bool header, int totalColumns)
        {
            var rows = table.Rows;
            Indent(depth).Append("<tr");

            // An explicit w:trHeight becomes a row height. CSS treats a table-row height as a
            // minimum (the row still grows with its content), which matches Word's default
            // "atLeast" rule; "exact" rows get the same treatment — close enough, since clipping
            // overflow is not representable on a <tr>.
            if (rows[rowIndex].HeightPoints is {} heightPoints and > 0)
            {
                builder.Append(" style=\"height: ").Append(Length(heightPoints)).Append('"');
            }

            builder.Append(">\n");

            var cells = rows[rowIndex].Cells;
            var gridColumn = rows[rowIndex].GridBefore;

            // w:gridBefore leaves grid columns empty ahead of the row's first cell; one bare cell keeps
            // the rest in their columns.
            if (gridColumn > 0)
            {
                Indent(depth + 1).Append("<td");
                if (gridColumn > 1)
                {
                    builder.Append(" colspan=\"").Append(gridColumn).Append('"');
                }

                builder.Append("></td>\n");
            }
            foreach (var cell in cells)
            {
                var properties = cell.Properties;
                var span = Math.Max(1, properties.GridSpan);
                if (properties.VerticalMerge == VerticalMergeType.Continue)
                {
                    gridColumn += span;
                    continue;
                }

                var tag = header ? "th" : "td";
                Indent(depth + 1).Append('<').Append(tag);
                if (span > 1)
                {
                    builder.Append(" colspan=\"").Append(span).Append('"');
                }

                var rowSpan = properties.VerticalMerge == VerticalMergeType.Restart
                    ? ComputeRowSpan(rows, rowIndex, gridColumn)
                    : 1;
                if (rowSpan > 1)
                {
                    builder.Append(" rowspan=\"").Append(rowSpan).Append('"');
                }

                // Resolve the cell's effective borders through the shared cell→table→inside cascade,
                // so a cell inherits the table's grid/outline borders when it sets none of its own.
                var borders = TableLayout.ResolveCellBorders(
                    properties, table.Properties, rowIndex, gridColumn, rows.Count, totalColumns, rows[rowIndex], rows);

                var cellStyle = CellStyle(properties, borders, table.Properties);

                // A cell holding anchored art becomes the positioning context for it (below).
                if (cell.Floats.Count > 0)
                {
                    cellStyle = cellStyle == null ? "position: relative" : $"{cellStyle}; position: relative";
                }

                // Cell paragraphs render inline (see AppendCellContent), so their alignment must
                // ride on the cell: when every non-heading paragraph shares one non-left alignment
                // (card/label templates centre their panel text), emit it as the td's text-align.
                var cellAlign = CommonCellAlignment(cell.Content);
                if (cellAlign != null)
                {
                    cellStyle = cellStyle == null ? cellAlign : $"{cellStyle}; {cellAlign}";
                }

                if (cellStyle != null)
                {
                    builder.Append(" style=\"").Append(cellStyle).Append('"');
                }

                builder.Append('>');
                AppendCellFloats(cell);
                AppendCellContent(cell.Content, depth + 1, CellMeasure(properties, table.Properties));
                builder.Append($"</{tag}>\n");
                gridColumn += span;
            }

            Indent(depth).Append("</tr>\n");
        }

        // Per-cell layout that the shared stylesheet can't express: explicit column width, shading,
        // a non-default vertical alignment, declared cell margins, text direction, diagonals, and
        // resolved borders. Top alignment is the td/th stylesheet default, so only middle/bottom
        // are emitted.
        static string? CellStyle(TableCellProperties properties, CellBorders? borders, TableProperties tableProperties)
        {
            var parts = new List<string>();

            if (properties.WidthPoints is > 0)
            {
                parts.Add($"width: {properties.WidthPoints.Value.ToString("0.##", CultureInfo.InvariantCulture)}pt");
            }
            else if (properties.WidthFraction is > 0)
            {
                // w:tcW type="pct" — a percentage column. Without it the browser sizes the column
                // to content, which collapsed menus/04's wide pct meal cells to ~40px stubs beside
                // their day labels.
                parts.Add($"width: {Number(properties.WidthFraction.Value * 100)}%");
            }

            var background = DocumentExportHelpers.NormalizeColor(properties.BackgroundColorHex);
            if (background != null)
            {
                parts.Add($"background-color: {background}");
            }

            var verticalAlign = properties.VerticalAlignment switch
            {
                CellVerticalAlignment.Center => "middle",
                CellVerticalAlignment.Bottom => "bottom",
                _ => null
            };
            if (verticalAlign != null)
            {
                parts.Add($"vertical-align: {verticalAlign}");
            }

            // Declared cell margins (w:tcMar / w:tblCellMar) become padding. A per-cell override is
            // always the author's explicit ask; the table-wide default is emitted only when it
            // exceeds the stylesheet's generic 4pt/7pt look — an ordinary Word default (0 vertical,
            // 5.4pt horizontal) keeps the shared rule.
            var padding = properties.Padding ??
                (ExceedsStylesheetPadding(tableProperties.DefaultCellPadding) ? tableProperties.DefaultCellPadding : null);
            if (padding != null)
            {
                parts.Add($"padding: {BoxShorthand(padding.Top, padding.Right, padding.Bottom, padding.Left)}");
            }

            // w:textDirection — btLr reads bottom-up along the left edge, tbRl top-down along the
            // right. Both are real CSS writing modes, so the browser wraps the vertical text itself.
            var writingMode = properties.TextDirection switch
            {
                CellTextDirection.BottomToTop => "writing-mode: sideways-lr",
                CellTextDirection.TopToBottom => "writing-mode: vertical-rl",
                _ => null
            };
            if (writingMode != null)
            {
                parts.Add(writingMode);
            }

            if (borders is {HasAnyBorder: true})
            {
                AppendBorders(parts, borders);
            }

            // w:tl2br / w:tr2bl — CSS has no diagonal border, but a corner-to-corner gradient band
            // draws the same line: the `to bottom left` gradient line's perpendicular through 50%
            // passes exactly through the top-left and bottom-right corners (and mirrored for the
            // other diagonal), so the coloured band is the diagonal at the declared width.
            if (properties.Diagonals is {HasAny: true} diagonals)
            {
                var lines = new List<string>();
                if (diagonals.Down.IsVisible)
                {
                    lines.Add(DiagonalGradient("to bottom left", diagonals.Down));
                }

                if (diagonals.Up.IsVisible)
                {
                    lines.Add(DiagonalGradient("to bottom right", diagonals.Up));
                }

                parts.Add($"background-image: {string.Join(", ", lines)}");
            }

            if (parts.Count == 0)
            {
                return null;
            }

            return string.Join(';', parts);
        }

        static string DiagonalGradient(string direction, BorderEdge edge)
        {
            var color = DocumentExportHelpers.NormalizeColor(edge.ColorHex) ?? "#000000";
            var half = Length(Math.Max(edge.WidthPoints, 0.5) / 2);
            return $"linear-gradient({direction}, transparent calc(50% - {half}), {color} calc(50% - {half}), {color} calc(50% + {half}), transparent calc(50% + {half}))";
        }

        // True when any edge of the table-wide cell margin visibly exceeds the stylesheet's
        // td/th padding (4pt vertical, 7pt horizontal).
        static bool ExceedsStylesheetPadding(CellSpacing padding) =>
            padding.Top > 4.05 || padding.Bottom > 4.05 || padding.Left > 7.05 || padding.Right > 7.05;

        // Emits the minimal CSS border declarations for a four-edge border set: a single shorthand
        // when all four edges are present and identical, otherwise one declaration per visible edge.
        static void AppendBorders(List<string> parts, CellBorders borders)
        {
            if (borders.Top.IsVisible &&
                borders.Right.IsVisible &&
                borders.Bottom.IsVisible &&
                borders.Left.IsVisible &&
                borders.Top == borders.Right &&
                borders.Right == borders.Bottom &&
                borders.Bottom == borders.Left)
            {
                parts.Add($"border: {BorderCss(borders.Top)}");
                return;
            }

            if (borders.Top.IsVisible)
            {
                parts.Add($"border-top: {BorderCss(borders.Top)}");
            }

            if (borders.Right.IsVisible)
            {
                parts.Add($"border-right: {BorderCss(borders.Right)}");
            }

            if (borders.Bottom.IsVisible)
            {
                parts.Add($"border-bottom: {BorderCss(borders.Bottom)}");
            }

            if (borders.Left.IsVisible)
            {
                parts.Add($"border-left: {BorderCss(borders.Left)}");
            }
        }

        public static string BorderCss(BorderEdge edge)
        {
            // CSS has no equivalent for OOXML's thin/thick pairs or the wave styles, so those map
            // to the nearest keyword: any multi-line style becomes `double`, any broken one
            // `dashed`/`dotted`. `groove`/`ridge`/`inset`/`outset` do exist in CSS and carry over.
            var style = edge.Style switch
            {
                BorderLineStyle.None => "none",
                BorderLineStyle.Dotted => "dotted",
                BorderLineStyle.Dashed or BorderLineStyle.DashSmallGap or BorderLineStyle.DashDotStroked or
                    BorderLineStyle.DotDash or BorderLineStyle.DotDotDash => "dashed",
                BorderLineStyle.Double or BorderLineStyle.Triple or BorderLineStyle.DoubleWave or
                    BorderLineStyle.ThinThickSmallGap or BorderLineStyle.ThickThinSmallGap or BorderLineStyle.ThinThickThinSmallGap or
                    BorderLineStyle.ThinThickMediumGap or BorderLineStyle.ThickThinMediumGap or BorderLineStyle.ThinThickThinMediumGap or
                    BorderLineStyle.ThinThickLargeGap or BorderLineStyle.ThickThinLargeGap or BorderLineStyle.ThinThickThinLargeGap => "double",
                BorderLineStyle.ThreeDEngrave => "groove",
                BorderLineStyle.ThreeDEmboss => "ridge",
                BorderLineStyle.Inset => "inset",
                BorderLineStyle.Outset => "outset",
                _ => "solid"
            };
            var color = DocumentExportHelpers.NormalizeColor(edge.ColorHex) ?? "#000000";

            // A CSS double's width is the whole three-band stack (line, gap, line in equal thirds),
            // while w:sz declares one LINE. Emitting the declared width gave the browser too little
            // room to draw two lines at all — "1.5pt double" is under the ~3px minimum and renders
            // as a single thin rule — so multi-line styles emit the stack the raster backends
            // actually reserve (BorderStroke.Extent), which the browser then divides back into
            // Word's two visible rules.
            var width = style == "double"
                ? BorderStroke.Extent(edge.Style, edge.WidthPoints, BorderStroke.Scope.Cell)
                : edge.WidthPoints;
            return $"{width.ToString("0.##", CultureInfo.InvariantCulture)}pt {style} {color}";
        }

        static int ComputeRowSpan(IReadOnlyList<TableRow> rows, int startRow, int gridColumn)
        {
            var span = 1;
            for (var rowIndex = startRow + 1; rowIndex < rows.Count; rowIndex++)
            {
                if (ColumnIsVerticalContinue(rows[rowIndex], gridColumn))
                {
                    span++;
                }
                else
                {
                    break;
                }
            }

            return span;
        }

        static bool ColumnIsVerticalContinue(TableRow row, int gridColumn)
        {
            var column = row.GridBefore;
            foreach (var cell in row.Cells)
            {
                if (column == gridColumn)
                {
                    return cell.Properties.VerticalMerge == VerticalMergeType.Continue;
                }

                column += Math.Max(1, cell.Properties.GridSpan);
            }

            return false;
        }

        // The single alignment shared by every non-blank, non-heading paragraph in the cell, as a
        // "text-align: ..." declaration — null when the cell has no such paragraphs, they disagree,
        // or they are left-aligned (the td default). Headings carry their own alignment already.
        static string? CommonCellAlignment(IReadOnlyList<DocumentElement> content)
        {
            TextAlignment? common = null;
            foreach (var element in content)
            {
                if (element is not ParagraphElement paragraph ||
                    IsBlankForHtml(paragraph) ||
                    DocumentExportHelpers.TryGetHeadingLevel(paragraph.Properties) != null)
                {
                    continue;
                }

                if (common == null)
                {
                    common = paragraph.Properties.Alignment;
                }
                else if (common != paragraph.Properties.Alignment)
                {
                    return null;
                }
            }

            return common switch
            {
                TextAlignment.Center => "text-align: center",
                TextAlignment.Right => "text-align: right",
                TextAlignment.Justify => "text-align: justify",
                _ => null
            };
        }

        // The width a cell's paragraphs are set in: its declared width less its side margins, or
        // the page's content width where the cell declares none (a percentage or an autofit
        // column, whose width only the browser will know).
        double CellMeasure(TableCellProperties properties, TableProperties tableProperties)
        {
            if (properties.WidthPoints is not > 0)
            {
                return pageSettings.ContentWidth;
            }

            var padding = properties.Padding ?? tableProperties.DefaultCellPadding;
            return Math.Max(0, properties.WidthPoints.Value - padding.Left - padding.Right);
        }

        void AppendCellContent(IReadOnlyList<DocumentElement> content, int depth, double measure)
        {
            // Cell paragraphs render inline (separated by <br />) except heading-styled ones, which
            // emit as real <hN> blocks — a heading inside a table cell stays a heading
            // ("<td><h1>SCHEDULE</h1></td>"). Headings being block-level also mean no <br /> is needed
            // either side of them. Nested tables and block images keep their content in the cell
            // instead of being dropped.
            var separatorPending = false;
            for (var index = 0; index < content.Count; index++)
            {
                var element = content[index];

                // Consecutive numbered paragraphs become a real <ul>/<ol>, exactly as at body
                // level — a bulleted cell keeps its bullets. Lists are block-level, so like
                // headings they need no <br /> on either side.
                if (element is ParagraphElement {Properties.Numbering: not null})
                {
                    var items = new List<ParagraphElement>();
                    while (index < content.Count &&
                           content[index] is ParagraphElement {Properties.Numbering: not null} listItem)
                    {
                        items.Add(listItem);
                        index++;
                    }

                    index--;
                    builder.Append('\n');
                    WriteList(DocumentExportHelpers.BuildListForest(items), depth + 1);
                    Indent(depth);
                    separatorPending = false;
                    continue;
                }

                switch (element)
                {
                    case ParagraphElement paragraph when !IsBlankForHtml(paragraph):
                        var level = DocumentExportHelpers.TryGetHeadingLevel(paragraph.Properties);
                        if (level != null)
                        {
                            borderGroupPrevious = index > 0 ? (content[index - 1] as ParagraphElement)?.Properties : null;
                            borderGroupNext = index + 1 < content.Count ? (content[index + 1] as ParagraphElement)?.Properties : null;
                            AppendHeading(paragraph, level.Value);
                            borderGroupPrevious = null;
                            borderGroupNext = null;
                            separatorPending = false;
                        }
                        else if (TryWriteTabbedParagraph(paragraph, depth, measure, inCell: true))
                        {
                            // A cell paragraph set against its own tab stops is a block of flex
                            // boxes like one in the flow. Joined inline with the rest of the cell
                            // each tab was a single space: table_of_contents/03's entries and the
                            // contents panels of brochures/05 and business-plans/13 ran their page
                            // numbers on after the titles, with no leader.
                            separatorPending = false;
                        }
                        else if (paragraph.Properties.SpacingBeforePoints > 0.01 ||
                                 paragraph.Properties.SpacingAfterPoints > 0.01 ||
                                 paragraph.Properties.Borders is {HasAnyBorder: true} ||
                                 DocumentExportHelpers.NormalizeColor(paragraph.Properties.BackgroundColorHex) != null ||
                                 HasIndent(paragraph.Properties))
                        {
                            // A cell paragraph that declares before/after spacing renders as a real
                            // block so the spacing survives — the <br /> join below drops it, which
                            // is how a letter template keeping its body in a cell and separating
                            // blocks with w:spacing (rather than empty paragraphs) read as one
                            // run-together mass (todo #35: cover-letters/05..12, letters/12,
                            // newsletters/03). Margins are explicit because the stylesheet's p rule
                            // would otherwise donate its own 8pt to every such paragraph. A bordered
                            // cell paragraph takes the same block form so its w:pBdr rules survive
                            // (resumes/08's separator rules above EXPERIENCE/ABOUT ME), with the
                            // border-group law applied against its paragraph siblings.
                            List<string> cellBlockParts =
                            [
                                $"margin: {Length(paragraph.Properties.SpacingBeforePoints)} 0 {Length(paragraph.Properties.SpacingAfterPoints)}"
                            ];

                            // An indented cell paragraph keeps its indents the same way: joined inline
                            // they were lost, and business/05's address, set in a Footer style indented
                            // 216pt and centred in what is left, centred on the whole cell instead.
                            AppendIndentStyle(cellBlockParts, paragraph.Properties);
                            borderGroupPrevious = index > 0 ? (content[index - 1] as ParagraphElement)?.Properties : null;
                            borderGroupNext = index + 1 < content.Count ? (content[index + 1] as ParagraphElement)?.Properties : null;
                            AppendParagraphBorderStyle(cellBlockParts, paragraph.Properties);
                            borderGroupPrevious = null;
                            borderGroupNext = null;

                            // Paragraph shading takes the block form too, as AppendParagraphStyle gives it
                            // in the flow: brochures/06's olive QuoteAlt/QuoteSource box exported as its
                            // outline alone, the white quote inside it invisible on the white page.
                            if (DocumentExportHelpers.NormalizeColor(paragraph.Properties.BackgroundColorHex) is { } cellShading)
                            {
                                cellBlockParts.Add($"background-color: {cellShading}");
                            }
                            builder
                                .Append("<p style=\"")
                                .Append(string.Join("; ", cellBlockParts))
                                .Append("\">");
                            AppendInline(paragraph.Runs);
                            builder.Append("</p>");
                            separatorPending = false;
                        }
                        else
                        {
                            if (separatorPending)
                            {
                                builder.Append("<br />");
                            }

                            AppendInline(paragraph.Runs);
                            separatorPending = true;
                        }

                        break;
                    case ParagraphElement:
                        // An empty cell paragraph is a blank line — Word's inter-line spacer inside
                        // the cell (a résumé's contact block, a letter body). Reserve it with a
                        // <br /> so "text / <empty> / text" keeps its gap instead of collapsing to a
                        // single break; the body path spaces the same empty paragraph with a
                        // one-line <p>. Left blank once (leading empty) contributes no break.
                        if (separatorPending)
                        {
                            builder.Append("<br />");
                        }

                        separatorPending = true;
                        break;
                    case TableElement nestedTable:
                        builder.Append('\n');
                        WriteTable(nestedTable, depth + 1);
                        Indent(depth);
                        separatorPending = false;
                        break;
                    case ImageElement image:
                        if (separatorPending)
                        {
                            builder.Append("<br />");
                        }

                        AppendImage(
                            image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description,
                            image.Crop, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical,
                            ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), image.Opacity);
                        separatorPending = true;
                        break;
                    case FloatingImageElement floatingImage:
                        if (separatorPending)
                        {
                            builder.Append("<br />");
                        }

                        AppendImage(
                            floatingImage.ImageData, floatingImage.ContentType, floatingImage.WidthPoints, floatingImage.HeightPoints, floatingImage.Description,
                            floatingImage.Crop, floatingImage.RotationDegrees, floatingImage.FlipHorizontal, floatingImage.FlipVertical);
                        separatorPending = true;
                        break;
                    case WordArtElement wordArt:
                        // A WordArt inside cell content fell off this switch with no output at
                        // all — wedding/08's circled "&" badge glyph, brochures/08's "Contoso
                        // Logo" frame. Written with its box chrome like the body path.
                        if (separatorPending)
                        {
                            builder.Append("<br />");
                        }

                        builder.Append('\n');
                        WriteInlineWordArt(wordArt, depth + 1);
                        Indent(depth);
                        separatorPending = false;
                        break;
                }
            }
        }

        /// <summary>
        /// Emits a cell's anchored art. The DOCX parser DETACHES a float out of the cell's flow
        /// content into <see cref="TableCell.Floats"/> (so the cell measures without it), and the
        /// HTML export read only the content — so every cell-anchored drawing was silently dropped:
        /// the missing hexagon accents, corner graphics and banner blobs logged against labels/14,
        /// business/04-05, brochures/04/06/07, letters/13 and menus/06. They are placed against the
        /// cell, which Word also does (<c>wp:anchor@layoutInCell</c>, default true), and the cell
        /// carries <c>position: relative</c> for them.
        /// </summary>
        void AppendCellFloats(TableCell cell)
        {
            foreach (var element in cell.Floats)
            {
                if (element is FloatingImageElement {WrapType: WrapType.None} image)
                {
                    var source = ResolveImageSource(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints);
                    if (source == null)
                    {
                        continue;
                    }

                    var style = $"position: absolute; left: {Length(image.HorizontalPositionPoints)}; top: {Length(image.VerticalPositionPoints)}; z-index: {(image.BehindText ? "-1" : "1")}";
                    AppendImageTag(
                        source, image.WidthPoints, image.HeightPoints, image.Description, style,
                        image.Crop, image.RotationDegrees, image.FlipHorizontal, image.FlipVertical,
                        ImageRecolor.For(image.ColorEffect, image.DuotoneColorHex, image.DuotoneLightColorHex), image.Opacity);
                }
                else if (element is FloatingShapeElement shape)
                {
                    WriteShape(shape, 0);
                }
                else if (element is FloatingTextBoxElement textBox)
                {
                    // The other half of the cell-float fix: a cell-anchored TEXT BOX fell off
                    // this dispatch with no output at all — brochures/06's quote text and
                    // attribution simply vanished from the export. Placed absolutely against
                    // the cell like the images and shapes above.
                    var textBoxStyle = $"position: absolute; left: {Length(textBox.HorizontalPositionPoints)}; top: {Length(textBox.VerticalPositionPoints)}; width: {Length(textBox.WidthPoints)}; z-index: {(textBox.BehindText ? "-1" : "1")}";
                    builder.Append(TextBoxOpenTag(textBox, textBoxStyle)).Append('\n');
                    WriteElements(textBox.Content, 0);
                    builder.Append("</div>\n");
                }
                else if (element is FloatingWordArtElement wordArt)
                {
                    var wordArtStyle = $"position: absolute; left: {Length(wordArt.HorizontalPositionPoints)}; top: {Length(wordArt.VerticalPositionPoints)}; z-index: {(wordArt.BehindText ? "-1" : "1")}";
                    AppendWordArtParagraph(wordArt, wordArtStyle);
                }
            }
        }

        void AppendInline(IReadOnlyList<Run> sourceRuns) =>
            AppendInline(sourceRuns, TextContext.Body);

        void AppendInline(IReadOnlyList<Run> sourceRuns, TextContext context)
        {
            var runs = DocumentExportHelpers.CoalesceRuns(sourceRuns);
            var index = 0;
            Comment? open = null;
            while (index < runs.Count)
            {
                var run = runs[index];
                var comment = Commented(run);
                if (comment != open)
                {
                    CloseComment(open);
                    if (comment != null)
                    {
                        builder.Append("""<mark class="comment" style="background-color: #F8DCDD">""");
                    }

                    open = comment;
                }
                if (run.HyperlinkUrl is {Length: > 0} url)
                {
                    // The link's LOOK comes from the document's own runs — a Hyperlink-styled run
                    // resolved blue+underline at parse and its spans carry that; a plain-styled
                    // link (resumes/03's email, black and bare in Word) must not pick up the
                    // browser's default blue underline on top.
                    builder.Append($"""<a href="{EncodeAttribute(url)}" style="color: inherit; text-decoration: inherit">""");
                    while (index < runs.Count &&
                           runs[index].HyperlinkUrl == url)
                    {
                        AppendRun(runs[index], context);
                        index++;
                    }

                    builder.Append("</a>");
                    continue;
                }

                AppendRun(run, context);
                index++;
            }

            CloseComment(open);
        }

        Comment? Commented(Run run)
        {
            if (run.Properties.CommentId is { } id &&
                commentsById.TryGetValue(id, out var comment))
            {
                return comment;
            }

            return null;
        }

        // Ends a comment's highlight with a link to its text in the trailing comments section, as
        // Word's balloon follows its range. A range broken by other text links each piece.
        void CloseComment(Comment? comment)
        {
            if (comment == null)
            {
                return;
            }

            if (!commentsWritten.Contains(comment))
            {
                commentsWritten.Add(comment);
            }

            builder.Append($"""</mark><sup class="comment-ref"><a href="#comment-{comment.Number}">[{EncodeText(Label(comment))}]</a></sup>""");
        }

        static string Label(Comment comment) =>
            $"{comment.Initials}{comment.Number}";

        // Emits the trailing comments section: each comment whose range was written, as Word's balloon
        // reads ("Commented [R1]: …"), one paragraph of it per line. Nothing when none was.
        public void WriteCommentDefinitions()
        {
            if (commentsWritten.Count == 0)
            {
                return;
            }

            Indent(0).Append("<section class=\"comments\">\n");
            foreach (var comment in commentsWritten)
            {
                var paragraphs = comment.Paragraphs.Count > 0 ? comment.Paragraphs : [comment.Text];
                Indent(1)
                    .Append($"""<p id="comment-{comment.Number}"><strong>Commented [{EncodeText(Label(comment))}]:</strong> {string.Join("<br>", paragraphs.Select(EncodeText))}</p>""")
                    .Append('\n');
            }

            Indent(0).Append("</section>\n");
        }

        void AppendRun(Run run, TextContext context)
        {
            if (run.InlineShapeGroup is {} shapeGroup)
            {
                AppendShapeGroup(shapeGroup, run.InlineImageWidthPoints, run.InlineImageHeightPoints);
                return;
            }

            if (run.InlineImageData is {} imageData)
            {
                AppendImage(
                    imageData, run.InlineImageContentType, run.InlineImageWidthPoints, run.InlineImageHeightPoints, run.InlineImageDescription,
                    run.InlineImageCrop, run.InlineImageRotationDegrees, run.InlineImageFlipHorizontal, run.InlineImageFlipVertical,
                    ImageRecolor.For(run.InlineImageColorEffect, run.InlineImageDuotoneColorHex, run.InlineImageDuotoneLightColorHex), run.InlineImageOpacity,
                    PictureClip(run.InlineImageClipToEllipse, run.InlineImageClipSubpaths));
                return;
            }

            if (run.FootnoteReferenceId is {} footnoteId)
            {
                builder.Append(NoteMarker(footnoteId, endnote: false));
                return;
            }

            if (run.EndnoteReferenceId is {} endnoteId)
            {
                builder.Append(NoteMarker(endnoteId, endnote: true));
                return;
            }

            if (run.IsTab)
            {
                builder.Append(' ');
                return;
            }

            if (string.IsNullOrEmpty(run.Text))
            {
                return;
            }

            var properties = run.Properties;
            var tags = AppendOpenTags(properties, context);

            // A page-numbering field in a pageless export evaluates as a one-page document —
            // "Page 1 of 1" — rather than shipping whatever total the source cached ("Page 1 of
            // 3", or a header reading "Page 2 of 2"). The reflow really is one page.
            var text = run.PageField == PageFieldKind.None || pageFieldsPreEvaluated ? run.Text : "1";

            // AllCaps is handled by CSS (text-transform: uppercase) on the wrapping span — see
            // InlineStyle — so the source text stays intact rather than being eagerly uppercased.
            AppendEncodedWithBreaks(SoftenNoBreakSpaces(text));

            AppendCloseTags(tags);
        }

        // A single no-break space BETWEEN words renders as an ordinary space — that is exactly what
        // Word draws, and what a browser draws in most faces. Kept as &#160; it is at the mercy of
        // the resolved font's own U+00A0 advance, and the fallback serif the render environment
        // substitutes for business-plans/05's Baskerville draws it visibly wider than a space
        // ("designed to  improve"). A no-break space that sits beside another space, or at a text
        // edge, keeps its entity — a plain space there could collapse or trim.
        static string SoftenNoBreakSpaces(string text)
        {
            if (!text.Contains('\u00A0'))
            {
                return text;
            }

            var chars = text.ToCharArray();
            for (var i = 1; i < chars.Length - 1; i++)
            {
                if (chars[i] != '\u00A0')
                {
                    continue;
                }

                if (chars[i - 1] is not (' ' or '\u00A0') &&
                    chars[i + 1] is not (' ' or '\u00A0'))
                {
                    chars[i] = ' ';
                }
            }

            return new(chars);
        }

        void AppendEncodedWithBreaks(string text)
        {
            if (!text.Contains('\n'))
            {
                builder.Append(EncodeText(text));
                return;
            }

            var segments = text.Split('\n');
            for (var index = 0; index < segments.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append("<br />");
                }

                builder.Append(EncodeText(segments[index]));
            }
        }

        readonly record struct RunTags(bool HasStyle, bool Strong, bool Em, bool U, bool S, bool Sup, bool Sub);

        // Opens the run's formatting tags directly into the output; the returned flags drive
        // AppendCloseTags (exact reverse order). Replaces a per-run pair of StringBuilders
        // that assembled the same fixed tag strings via Insert(0).
        RunTags AppendOpenTags(RunProperties properties, TextContext context)
        {
            var style = InlineStyle(properties, context, bodyFont);
            if (style != null)
            {
                builder.Append($"""<span style="{style}">""");
            }

            // A heading is bold by default — both the stylesheet's h1-h6 rule and every browser's
            // UA default — so a <strong> inside one is redundant and is skipped, mirroring the
            // Markdown exporter's heading bold suppression. A run that is explicitly non-bold in a
            // heading is still honoured: InlineStyle emits font-weight: normal for it.
            var strong = properties.Bold &&
                         !context.Bold;
            if (strong)
            {
                builder.Append("<strong>");
            }

            if (properties.Italic)
            {
                builder.Append("<em>");
            }

            if (properties.Underline)
            {
                // w:u/@w:color and w:u/@w:val="double" ride on the <u> as text-decoration
                // overrides; a plain underline stays a bare tag.
                if (properties.UnderlineColorHex != null || properties.DoubleUnderline)
                {
                    var decorations = new List<string>();
                    if (properties.DoubleUnderline)
                    {
                        decorations.Add("text-decoration-style: double");
                    }

                    if (properties.UnderlineColorHex != null)
                    {
                        decorations.Add($"text-decoration-color: {DocumentExportHelpers.NormalizeColor(properties.UnderlineColorHex)}");
                    }

                    builder.Append($"""<u style="{string.Join("; ", decorations)}">""");
                }
                else
                {
                    builder.Append("<u>");
                }
            }

            if (properties.Strikethrough)
            {
                builder.Append("<s>");
            }

            var sup = properties.VerticalAlignment == VerticalRunAlignment.Superscript;
            var sub = properties.VerticalAlignment == VerticalRunAlignment.Subscript;
            if (sup)
            {
                builder.Append("<sup>");
            }
            else if (sub)
            {
                builder.Append("<sub>");
            }

            return new(style != null, strong, properties.Italic, properties.Underline, properties.Strikethrough, sup, sub);
        }

        void AppendCloseTags(RunTags tags)
        {
            if (tags.Sub)
            {
                builder.Append("</sub>");
            }
            else if (tags.Sup)
            {
                builder.Append("</sup>");
            }

            if (tags.S)
            {
                builder.Append("</s>");
            }

            if (tags.U)
            {
                builder.Append("</u>");
            }

            if (tags.Em)
            {
                builder.Append("</em>");
            }

            if (tags.Strong)
            {
                builder.Append("</strong>");
            }

            if (tags.HasStyle)
            {
                builder.Append("</span>");
            }
        }

        static string? InlineStyle(RunProperties properties, TextContext context, string? bodyFontFamily)
        {
            // The element's own colour is what a run inherits — black unless a list item took its
            // marker's — so a run of that colour carries none, and a black run inside a coloured
            // item has to say so.
            var color = DocumentExportHelpers.NormalizeColor(properties.ColorHex) ?? "#000000";
            if (color == (context.Color ?? "#000000"))
            {
                color = null;
            }

            var background = DocumentExportHelpers.NormalizeColor(properties.BackgroundColorHex);

            // Only a run that deviates from the size its element already establishes needs an
            // inline size — 11pt from the body stylesheet in flow content, the heading's effective
            // size (stylesheet default or the lifted uniform run size) inside an <hN>.
            var hasFontSize = Math.Abs(properties.FontSizePoints - context.FontSizePoints) > 0.01 &&
                              properties.FontSizePoints > 0;

            // Headings are bold by default in the stylesheet; a non-bold run inside one (a "Prepared
            // for:" label styled Heading 2, say) must override back to normal weight to match Word.
            var overrideWeight = context.Bold &&
                                 !properties.Bold;

            // Only a run whose font differs from the one its element sets — the document's <body>
            // font, or a list item's marker face — needs an inline family; the common case (run
            // matches the body font) inherits and stays clean.
            var hasFont = !string.IsNullOrEmpty(properties.FontFamily) &&
                          !string.Equals(properties.FontFamily, context.FontFamily ?? bodyFontFamily, StringComparison.OrdinalIgnoreCase);

            // Expanded / condensed tracking (w:spacing on the run) — template heading and body
            // styles lean on it heavily, and without it text wraps at a different measure than
            // Word. Zero is the default and stays clean.
            var hasLetterSpacing = Math.Abs(properties.CharacterSpacingPoints) > 0.01;

            if (color == null &&
                background == null &&
                properties is {SmallCaps: false, AllCaps: false} &&
                !hasFontSize &&
                !overrideWeight &&
                !hasFont &&
                !hasLetterSpacing)
            {
                return null;
            }

            var style = new StringBuilder();
            if (overrideWeight)
            {
                style.Append("font-weight: normal; ");
            }

            if (hasFont)
            {
                // Append a generic fallback (as the body font does) so a run whose specific font
                // isn't installed degrades to a sans-serif rather than the browser's default serif.
                style.Append($"font-family: {CssFontFamily(properties.FontFamily)}, sans-serif; ");
            }

            if (color != null)
            {
                style.Append($"color: {color}; ");
            }

            if (background != null)
            {
                style.Append($"background-color: {background}; ");
            }

            if (hasFontSize)
            {
                style.Append($"font-size: {Length(properties.FontSizePoints)}; ");
            }

            if (hasLetterSpacing)
            {
                style.Append($"letter-spacing: {Length(properties.CharacterSpacingPoints)}; ");
            }

            if (properties.SmallCaps)
            {
                style.Append("font-variant: small-caps; ");
            }

            if (properties.AllCaps)
            {
                style.Append("text-transform: uppercase; ");
            }

            return style.ToString().TrimEnd();
        }

        // Maps embedded image bytes to an <img>/<image> source: a caller-supplied handler result, a
        // base64 data URI, or null when neither is available (the image is then dropped).
        string? ResolveImageSource(byte[] data, string? contentType, double widthPoints, double heightPoints)
        {
            var index = imageIndex++;
            if (options.ImageHandler != null)
            {
                return options.ImageHandler(new(data, contentType, widthPoints, heightPoints, index));
            }

            if (options.EmbedImagesAsBase64)
            {
                var mime = string.IsNullOrEmpty(contentType) ? "image/png" : contentType;
                return $"data:{mime};base64,{Convert.ToBase64String(data)}";
            }

            return null;
        }

        /// <summary>The opening div of a text box, carrying its chrome — fill, frame and Word's
        /// default inner inset — plus an optional positioning prefix. The chrome is what makes a
        /// quote box or panel read as one; it was dropped entirely (bare
        /// <c>&lt;div&gt;</c>), and cell-anchored text boxes were dropped whole.</summary>
        static string TextBoxOpenTag(FloatingTextBoxElement textBox, string? positionStyle)
        {
            var style = new StringBuilder();
            if (positionStyle != null)
            {
                style.Append(positionStyle).Append("; ");
            }

            var chrome = false;
            if (DocumentExportHelpers.NormalizeColor(textBox.BackgroundColorHex) is { } fill)
            {
                style.Append($"background-color: {fill}; ");
                chrome = true;
            }

            if (DocumentExportHelpers.NormalizeColor(textBox.LineColorHex) is { } line &&
                textBox.LineWidthPoints > 0)
            {
                style.Append($"border: {Number(textBox.LineWidthPoints)}pt solid {line}; ");
                chrome = true;
            }

            if (chrome)
            {
                // Word's default text-box inset: 0.05in top/bottom, 0.1in left/right.
                style.Append("padding: 3.6pt 7.2pt; ");
            }

            if (style.Length == 0)
            {
                return "<div>";
            }

            style.Length -= 2;
            return $"""<div style="{style}">""";
        }

        /// <summary>One WordArt text as a styled paragraph — family, size, weight and fill colour
        /// kept (they exported as plain small black body text); the warp itself has no
        /// reflowable-HTML form and is deliberately not attempted.</summary>
        void AppendWordArtParagraph(IWordArtVisual wordArt, string? extraStyle)
        {
            var style = new StringBuilder();
            style.Append($"font-family: '{wordArt.FontFamily}', sans-serif; font-size: {Number(wordArt.FontSizePoints)}pt; margin: 0; ");
            if (wordArt.Bold)
            {
                style.Append("font-weight: bold; ");
            }

            if (wordArt.Italic)
            {
                style.Append("font-style: italic; ");
            }

            if (DocumentExportHelpers.NormalizeColor(wordArt.FillColorHex) is { } fill)
            {
                style.Append($"color: {fill}; ");
            }

            if (extraStyle != null)
            {
                style.Append(extraStyle).Append("; ");
            }

            style.Length -= 2;
            builder.Append($"""<p style="{style}">{EncodeText(wordArt.Text)}</p>""").Append('\n');
        }

        void WriteInlineWordArt(WordArtElement wordArt, int depth)
        {
            // An UNWARPED WordArt is Word's inline text box, and its Box* chrome — the fill and
            // frame behind the text — is the visible thing (business/06's outlined LOGO box,
            // brochures/08's "Contoso Logo" frame ring). A BoxSubpaths frame must not fill solid
            // (the ring's centre is open), so it approximates as a border at the preset frame's
            // ring thickness (1/8 of the short side).
            var boxStyle = new StringBuilder();
            if (wordArt.BoxSubpaths is {Count: > 0} &&
                DocumentExportHelpers.NormalizeColor(wordArt.BoxFillColorHex) is { } ringFill)
            {
                var ring = Math.Min(wordArt.WidthPoints, wordArt.HeightPoints) / 8;
                boxStyle.Append($"border: {Number(ring)}pt solid {ringFill}; ");
            }
            else
            {
                if (wordArt.BoxSubpaths == null &&
                    DocumentExportHelpers.NormalizeColor(wordArt.BoxFillColorHex) is { } boxFill)
                {
                    boxStyle.Append($"background-color: {boxFill}; ");
                }

                if (DocumentExportHelpers.NormalizeColor(wordArt.BoxLineColorHex) is { } boxLine &&
                    wordArt.BoxLineWidthPoints > 0)
                {
                    boxStyle.Append($"border: {Number(wordArt.BoxLineWidthPoints)}pt solid {boxLine}; ");
                }
            }

            if (boxStyle.Length == 0)
            {
                Indent(depth);
                AppendWordArtParagraph(wordArt, null);
                return;
            }

            // An ellipse box is the oval inscribed in it, as the painters draw it: wedding/08's
            // "&" badge is a circle and came out as the square it is inscribed in.
            if (wordArt.BoxIsEllipse)
            {
                boxStyle.Append("border-radius: 50%; ");
            }

            boxStyle.Append($"width: {Length(wordArt.WidthPoints)}; min-height: {Length(wordArt.HeightPoints)}; ");
            boxStyle.Append("display: flex; align-items: center; justify-content: center; ");
            if (wordArt.Alignment == TextAlignment.Center)
            {
                boxStyle.Append("margin-left: auto; margin-right: auto; ");
            }

            boxStyle.Length -= 2;
            Indent(depth).Append($"""<div style="{boxStyle}">""").Append('\n');
            Indent(depth + 1);
            AppendWordArtParagraph(wordArt, null);
            Indent(depth).Append("</div>\n");
        }

        void WriteFloatingWordArt(FloatingWordArtElement wordArt, int depth)
        {
            // Same zero-height relative wrapper as WriteShape: the display text places at its
            // anchor offsets without consuming flow height.
            Indent(depth).Append("""<div style="position: relative">""").Append('\n');
            Indent(depth + 1);
            AppendWordArtParagraph(
                wordArt,
                $"position: absolute; left: {Length(wordArt.HorizontalPositionPoints)}; top: {Length(wordArt.VerticalPositionPoints)}; z-index: {(wordArt.BehindText ? "-1" : "1")}");
            Indent(depth).Append("</div>\n");
        }

        // A background shape (w:drawing anchored behindDoc) becomes an absolutely-positioned inline
        // <svg> in the body's content-area coordinate space, drawn behind text via a negative
        // z-index. The vector data — solid/gradient/image fill, stroke, preset rect/ellipse or a
        // flattened custom polygon, plus rotation/flip — all map straight onto SVG primitives.
        void WriteShape(FloatingShapeElement shape, int depth)
        {
            var (pageLeft, pageTop, width, height) = ShapeBox(shape, pageSettings);
            var (left, top) = FloatOrigin(pageLeft, pageTop, shape.VerticalAnchor);
            if (width <= 0.01 || height <= 0.01)
            {
                WriteLineShape(shape, depth, left, top, width, height);
                return;
            }

            // overflow: visible because nothing in DrawingML clips a shape to its own box: an
            // outline is stroked centred on the geometry, so half of it lies outside, and a rotated
            // shape turns about its centre out of the box it was declared in. Clipped to the
            // viewport, agendas-minutes/08's 15pt ring lost its outer half and read as a rounded
            // square, and business-plans/08's accent rule (a 374pt bar turned a quarter) kept only
            // the 2pt of it that crossed its unrotated box.
            var shapeId = shapeIndex++;
            Indent(depth).Append("""<div style="position: relative">""");
            builder.Append($"""<svg style="position: absolute; left: {Length(left)}; top: {Length(top)}; width: {Length(width)}; height: {Length(height)}; z-index: {(shape.BehindText ? "-1" : "1")}; overflow: visible" viewBox="0 0 {Number(width)} {Number(height)}" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">""");

            if (shape.ImageData is {Length: > 0} imageData)
            {
                // A picture fill wins over a fill colour the shape also resolved (a style's
                // fillRef under an explicit a:blipFill), as it does in the layout engine: tested
                // the other way round, newsletters/08's cover photo, newsletters/13's arch and
                // newsletters/14's portrait each drew as a block of that colour.
                if (ResolveImageSource(imageData, shape.ImageContentType, width, height) is { } source)
                {
                    AppendShapePicture(shape, source, width, height, shapeId);
                }

                if (ShapeStrokeAttributes(shape) is { } outline)
                {
                    AppendShapeOutline(shape, width, height);
                    builder.Append(" fill=\"none\"").Append(outline).Append(ShapeTransformAttribute(shape, width, height)).Append(" />");
                }
            }
            else
            {
                string fill;
                if (shape.Gradient is {} gradient)
                {
                    builder.Append("<defs>");
                    AppendGradient(gradient, shapeId);
                    builder.Append("</defs>");
                    fill = $"url(#shape-grad-{shapeId})";
                }
                else
                {
                    fill = DocumentExportHelpers.NormalizeColor(shape.FillColorHex) ?? "none";
                }

                AppendShapeGeometry(shape, width, height, fill);
            }

            builder.Append("</svg></div>\n");
        }

        // A shape with no extent on one axis is a line connector: the parser keeps the line's
        // length on the other axis and its a:ln as the stroke, and the painters stroke that
        // degenerate box. An <svg> with a zero width or height renders nothing at all, so the
        // line gets a viewport as thick as its own stroke, centred on where the line runs. They
        // were dropped whole, which is every fold, cut and tear guide of the card and label
        // sheets (cards/01, cards/12, cards/18, labels/03), cards/06's divider between the two
        // halves of an invitation and agendas-minutes/14's 10pt stripes.
        void WriteLineShape(FloatingShapeElement shape, int depth, double left, double top, double width, double height)
        {
            var horizontal = width > 0.01;
            if (horizontal == height > 0.01 ||
                ShapeStrokeAttributes(shape) is not { } stroke)
            {
                return;
            }

            var thickness = ShapeStroke.WidthPoints(shape.LineWidthPoints);
            var length = horizontal ? width : height;
            var (boxLeft, boxTop, boxWidth, boxHeight) = horizontal
                ? (left, top - thickness / 2, length, thickness)
                : (left - thickness / 2, top, thickness, length);
            var (startX, startY, endX, endY) = horizontal
                ? (0.0, thickness / 2, length, thickness / 2)
                : (thickness / 2, 0.0, thickness / 2, length);

            Indent(depth).Append("""<div style="position: relative">""");
            builder.Append($"""<svg style="position: absolute; left: {Length(boxLeft)}; top: {Length(boxTop)}; width: {Length(boxWidth)}; height: {Length(boxHeight)}; z-index: {(shape.BehindText ? "-1" : "1")}; overflow: visible" viewBox="0 0 {Number(boxWidth)} {Number(boxHeight)}" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">""");
            builder.Append("<line x1=\"").Append(Number(startX)).Append("\" y1=\"").Append(Number(startY))
                .Append("\" x2=\"").Append(Number(endX)).Append("\" y2=\"").Append(Number(endY)).Append('"')
                .Append(stroke)
                .Append(" /></svg></div>\n");
        }

        // DrawingML states a preset dash in multiples of the line's width; SVG wants lengths.
        static string? DashArray(IReadOnlyList<double>? pattern, double thickness)
        {
            if (pattern is not {Count: > 0})
            {
                return null;
            }

            return string.Join(' ', pattern.Select(_ => Number(_ * thickness)));
        }

        // The stroke attributes of a shape's outline, or null when it has none. The width follows
        // the painters (ShapeStroke): an outline that declares no width takes DrawingML's default
        // and one that declares zero is a hairline, where requiring a positive width here drew
        // neither.
        static string? ShapeStrokeAttributes(FloatingShapeElement shape)
        {
            if (DocumentExportHelpers.NormalizeColor(shape.LineColorHex) is not { } stroke)
            {
                return null;
            }

            var thickness = ShapeStroke.WidthPoints(shape.LineWidthPoints);
            var attributes = new StringBuilder();
            attributes.Append(" stroke=\"").Append(stroke)
                .Append("\" stroke-width=\"").Append(Number(thickness)).Append('"');
            if (shape.LineAlpha < 0.999)
            {
                attributes.Append(" stroke-opacity=\"").Append(Number(shape.LineAlpha)).Append('"');
            }

            if (DashArray(shape.LineDashPattern, thickness) is { } dashes)
            {
                attributes.Append(" stroke-dasharray=\"").Append(dashes).Append('"');
            }

            return attributes.ToString();
        }

        // A picture-filled shape is the picture cut to the shape, as Fragmenter.ShapeImage hands
        // it to the painters: the part of the picture the fill shows (a:srcRect folded with
        // a:fillRect) stretched over the box, or the picture repeated at its own size (a:tile),
        // and the shape's contours or its ellipse as the clip. Drawn as the whole bitmap squeezed
        // into the box, a photo cut to an arch or a freeform came out as a rectangle.
        void AppendShapePicture(FloatingShapeElement shape, string source, double width, double height, int shapeId)
        {
            var translucent = shape.ImageOpacity < 1
                ? $" opacity=\"{Number(Math.Clamp(shape.ImageOpacity, 0, 1))}\""
                : "";
            var transform = ShapeTransformAttribute(shape, width, height);
            if (shape.ImageTile is {WidthPoints: > 0, HeightPoints: > 0} tile)
            {
                var (originX, originY) = tile.Origin(width, height);
                builder.Append($"""<defs><pattern id="shape-tile-{shapeId}" patternUnits="userSpaceOnUse" x="{Number(originX)}" y="{Number(originY)}" width="{Number(tile.WidthPoints)}" height="{Number(tile.HeightPoints)}"><image href="{EncodeAttribute(source)}" width="{Number(tile.WidthPoints)}" height="{Number(tile.HeightPoints)}" preserveAspectRatio="none" /></pattern></defs>""");
                AppendShapeOutline(shape, width, height);
                builder.Append($""" fill="url(#shape-tile-{shapeId})"{translucent}{transform} />""");
                return;
            }

            var box = shape.ImageCrop?.Expand(0, 0, width, height) ?? (0, 0, width, height);
            var cropped = box != (0, 0, width, height);
            var clipped = cropped || shape.Subpaths is {Count: > 0} || shape.Preset == PresetShape.Ellipse;
            if (clipped)
            {
                builder.Append($"""<defs><clipPath id="shape-clip-{shapeId}">""");
                AppendShapeOutline(shape, width, height);
                builder.Append($""" /></clipPath></defs><g clip-path="url(#shape-clip-{shapeId})"{transform}>""");
                transform = "";
            }

            builder.Append($"""<image href="{EncodeAttribute(source)}" x="{Number(box.X)}" y="{Number(box.Y)}" width="{Number(box.Width)}" height="{Number(box.Height)}" preserveAspectRatio="none"{translucent}{transform} />""");
            if (clipped)
            {
                builder.Append("</g>");
            }
        }

        void AppendShapeGeometry(FloatingShapeElement shape, double width, double height, string fill)
        {
            AppendShapeOutline(shape, width, height);
            builder.Append(" fill=\"").Append(fill).Append('"');
            if (shape.FillAlpha < 0.999)
            {
                builder.Append(" fill-opacity=\"").Append(Number(shape.FillAlpha)).Append('"');
            }

            builder.Append(ShapeStrokeAttributes(shape)).Append(ShapeTransformAttribute(shape, width, height)).Append(" />");
        }

        // The shape's outline as an SVG element left open, like AppendGroupGeometry, so the
        // caller appends the paint: its contours, its ellipse, or its box.
        void AppendShapeOutline(FloatingShapeElement shape, double width, double height)
        {
            if (shape.Subpaths is {Count: > 0} subpaths)
            {
                // One SVG sub-path (M…L…Z) per contour; the default nonzero fill-rule matches
                // SkiaSharp/DrawingML so disjoint pieces and holes stay separate.
                builder.Append("<path d=\"");
                for (var contourIndex = 0; contourIndex < subpaths.Count; contourIndex++)
                {
                    var contour = subpaths[contourIndex];
                    if (contour.Count == 0)
                    {
                        continue;
                    }

                    if (contourIndex > 0)
                    {
                        builder.Append(' ');
                    }

                    for (var index = 0; index < contour.Count; index++)
                    {
                        if (index > 0)
                        {
                            builder.Append(' ');
                        }

                        builder.Append(index == 0 ? 'M' : 'L')
                            .Append(Number(contour[index].X * width)).Append(',').Append(Number(contour[index].Y * height));
                    }

                    builder.Append(" Z");
                }

                builder.Append('"');
            }
            else if (shape.Preset == PresetShape.Ellipse)
            {
                builder.Append("<ellipse cx=\"").Append(Number(width / 2))
                    .Append("\" cy=\"").Append(Number(height / 2))
                    .Append("\" rx=\"").Append(Number(width / 2))
                    .Append("\" ry=\"").Append(Number(height / 2)).Append('"');
            }
            else
            {
                builder.Append("<rect x=\"0\" y=\"0\" width=\"").Append(Number(width))
                    .Append("\" height=\"").Append(Number(height)).Append('"');
            }
        }

        void AppendGradient(GradientFill gradient, int shapeId)
        {
            // DirectionDegrees: 0 = top-to-bottom, clockwise positive. Project onto an
            // objectBoundingBox axis through the box centre.
            var radians = gradient.DirectionDegrees * Math.PI / 180.0;
            var directionX = Math.Sin(radians);
            var directionY = Math.Cos(radians);
            var start = DocumentExportHelpers.NormalizeColor(gradient.StartColorHex) ?? "#000000";
            var end = DocumentExportHelpers.NormalizeColor(gradient.EndColorHex) ?? "#000000";

            var startOpacity = gradient.StartAlpha < 1 ? $" stop-opacity=\"{Number(gradient.StartAlpha)}\"" : "";
            var endOpacity = gradient.EndAlpha < 1 ? $" stop-opacity=\"{Number(gradient.EndAlpha)}\"" : "";
            builder.Append($"""<linearGradient id="shape-grad-{shapeId}" x1="{Number(0.5 - directionX / 2)}" y1="{Number(0.5 - directionY / 2)}" x2="{Number(0.5 + directionX / 2)}" y2="{Number(0.5 + directionY / 2)}"><stop offset="0" stop-color="{start}"{startOpacity} /><stop offset="1" stop-color="{end}"{endOpacity} /></linearGradient>""");
        }

        static string ShapeTransformAttribute(FloatingShapeElement shape, double width, double height)
        {
            var transforms = new List<string>();
            if (Math.Abs(shape.RotationDegrees) > 0.01)
            {
                transforms.Add($"rotate({Number(shape.RotationDegrees)} {Number(width / 2)} {Number(height / 2)})");
            }

            if (shape.FlipHorizontal)
            {
                transforms.Add($"translate({Number(width)} 0) scale(-1 1)");
            }

            if (shape.FlipVertical)
            {
                transforms.Add($"translate(0 {Number(height)}) scale(1 -1)");
            }

            if (transforms.Count == 0)
            {
                return "";
            }

            return $" transform=\"{string.Join(' ', transforms)}\"";
        }

        // Resolves a shape's box into page coordinates. The body's padding equals the page margins,
        // and CSS resolves absolutely-positioned children against the padding box, so the
        // positioning origin is the page origin and shapes place at their raw page position.
        // Mirrors the deleted FloatingPosition.ResolveShapeBounds, which needed a render context the exporter
        // lacks.
        static (double left, double top, double width, double height) ShapeBox(FloatingShapeElement shape, PageSettings page)
        {
            var contentWidth = page.ContentWidth;
            var contentHeight = page.HeightPoints - page.MarginTop - page.MarginBottom;

            var width = shape.WidthPercent is {} widthPercent
                ? widthPercent * (shape.WidthRelativeFrom == SizeRelativeFrom.Page ? page.WidthPoints : contentWidth)
                : shape.WidthPoints;
            var height = shape.HeightPercent is {} heightPercent
                ? heightPercent * (shape.HeightRelativeFrom == SizeRelativeFrom.Page ? page.HeightPoints : contentHeight)
                : shape.HeightPoints;

            var baseX = shape.HorizontalAnchor == HorizontalAnchor.Page ? 0.0 : page.MarginLeft;
            var pageX = shape.HorizontalPositionPercent is {} horizontalPercent
                ? baseX + horizontalPercent * (shape.HorizontalAnchor == HorizontalAnchor.Page ? page.WidthPoints : contentWidth)
                : baseX + shape.HorizontalPositionPoints;

            var baseY = shape.VerticalAnchor == VerticalAnchor.Page ? 0.0 : page.MarginTop;
            var pageY = shape.VerticalPositionPercent is {} verticalPercent
                ? baseY + verticalPercent * (shape.VerticalAnchor == VerticalAnchor.Page ? page.HeightPoints : contentHeight)
                : baseY + shape.VerticalPositionPoints;

            return (pageX, pageY, width, height);
        }

        // An inline wpg:wgp group becomes an inline <svg> whose viewBox IS the group's child
        // coordinate space, so every shape keeps its authored geometry and the browser scales the
        // whole thing into the run's box. preserveAspectRatio="none" matches the raster backends,
        // which scale x and y independently. Stroke widths ride out of that scale on
        // vector-effect="non-scaling-stroke": a:ln/@w is absolute EMU and the child coordinate
        // space is not always EMU (see docs/floating-art-pipeline.md).
        void AppendShapeGroup(InlineShapeGroup group, double widthPoints, double heightPoints)
        {
            if (widthPoints <= 0 ||
                heightPoints <= 0 ||
                group.ChildExtentX <= 0 ||
                group.ChildExtentY <= 0)
            {
                return;
            }

            // overflow: visible because a stroke sits centred on its geometry — a zero-height
            // connector's rule would otherwise be half-clipped by the viewport. The raster
            // backends don't clip the group either.
            builder.Append("<svg width=\"").Append(ToPixels(widthPoints))
                .Append("\" height=\"").Append(ToPixels(heightPoints))
                .Append("\" viewBox=\"0 0 ").Append(Number(group.ChildExtentX)).Append(' ').Append(Number(group.ChildExtentY))
                .Append("\" preserveAspectRatio=\"none\" style=\"overflow: visible\" xmlns=\"http://www.w3.org/2000/svg\">");

            var rotated = group.RotationDegrees != 0;
            if (rotated)
            {
                builder.Append("<g transform=\"rotate(").Append(Number(group.RotationDegrees))
                    .Append(' ').Append(Number(group.ChildExtentX / 2))
                    .Append(' ').Append(Number(group.ChildExtentY / 2))
                    .Append(")\">");
            }

            foreach (var shape in group.Shapes)
            {
                AppendGroupShape(shape, group, widthPoints, heightPoints);
            }

            if (rotated)
            {
                builder.Append("</g>");
            }

            builder.Append("</svg>");
        }

        void AppendGroupShape(GroupShape shape, InlineShapeGroup group, double widthPoints, double heightPoints)
        {
            if (shape.Geometry == GroupShapeGeometry.Line)
            {
                var (startX, endX) = shape.FlipHorizontal ? (shape.X + shape.Width, shape.X) : (shape.X, shape.X + shape.Width);
                var (startY, endY) = shape.FlipVertical ? (shape.Y + shape.Height, shape.Y) : (shape.Y, shape.Y + shape.Height);
                builder.Append("<line x1=\"").Append(Number(startX)).Append("\" y1=\"").Append(Number(startY))
                    .Append("\" x2=\"").Append(Number(endX)).Append("\" y2=\"").Append(Number(endY))
                    .Append("\" stroke-linecap=\"square\"");
                // Default to 0.75pt when the shape carries no explicit a:ln/@w, as the raster
                // backends do. Widths stay in EMU — the same space as the viewBox.
                AppendStroke(shape, shape.LineWidthEmu > 0 ? shape.LineWidthEmu : 0.75 * OoxmlUnits.EmusPerPoint);
                builder.Append(" />");
                return;
            }

            var isEllipse = shape.Geometry == GroupShapeGeometry.Ellipse;

            // The shadow is an offset copy of the shape's geometry, emitted before the shape itself
            // so it paints behind it.
            if (shape.Shadow is {} shadow)
            {
                AppendGroupGeometry(shape, isEllipse, shadow.OffsetX, shadow.OffsetY);
                builder.Append(" fill=\"").Append(DocumentExportHelpers.NormalizeColor(shadow.ColorHex) ?? "#000000").Append('"');
                if (shadow.Alpha < 0.999)
                {
                    builder.Append(" fill-opacity=\"").Append(Number(shadow.Alpha)).Append('"');
                }

                builder.Append(" />");
            }

            if (shape.ImageData is {} imageData)
            {
                // The handler sees the picture's own rendered size, not the group's.
                var pictureWidth = shape.Width / group.ChildExtentX * widthPoints;
                var pictureHeight = shape.Height / group.ChildExtentY * heightPoints;
                if (ResolveImageSource(imageData, shape.ImageContentType, pictureWidth, pictureHeight) is {} source)
                {
                    // An a:srcRect crop is drawn by enlarging the picture so its visible
                    // sub-rectangle covers the shape's box; SVG's <image> has no source rectangle.
                    var box = shape.ImageCrop?.Expand(shape.X, shape.Y, shape.Width, shape.Height)
                              ?? (shape.X, shape.Y, shape.Width, shape.Height);
                    var cropped = box != (shape.X, shape.Y, shape.Width, shape.Height);

                    // Word crops the picture to its pic:spPr geometry. A rect only needs a clip once
                    // the picture overflows it.
                    string? clipId = null;
                    if (isEllipse || cropped)
                    {
                        clipId = $"group-clip-{shapeIndex++}";
                        builder.Append("<defs><clipPath id=\"").Append(clipId).Append("\">");
                        AppendGroupGeometry(shape, isEllipse);
                        builder.Append(" /></clipPath></defs>");
                    }

                    builder.Append("<image href=\"").Append(EncodeAttribute(source)).Append('"')
                        .Append(" x=\"").Append(Number(box.X)).Append("\" y=\"").Append(Number(box.Y))
                        .Append("\" width=\"").Append(Number(box.Width)).Append("\" height=\"").Append(Number(box.Height)).Append('"')
                        .Append(" preserveAspectRatio=\"none\"");
                    if (clipId != null)
                    {
                        builder.Append(" clip-path=\"url(#").Append(clipId).Append(")\"");
                    }

                    if (shape.ImageDescription is {Length: > 0} description)
                    {
                        builder.Append("><title>").Append(EncodeText(description)).Append("</title></image>");
                    }
                    else
                    {
                        builder.Append(" />");
                    }
                }

                // The picture already filled its box, so its a:ln — the ring Word draws around a
                // circular photo — strokes as a separate unfilled shape on top.
                if (shape.LineWidthEmu > 0)
                {
                    AppendGroupGeometry(shape, isEllipse);
                    builder.Append(" fill=\"none\"");
                    AppendStroke(shape, shape.LineWidthEmu);
                    builder.Append(" />");
                }

                return;
            }

            AppendGroupGeometry(shape, isEllipse);
            builder
                .Append(" fill=\"")
                .Append(DocumentExportHelpers.NormalizeColor(shape.FillColorHex) ?? "none").Append('"');
            if (shape is
                {
                    FillColorHex: not null,
                    FillAlpha: < 0.999
                })
            {
                builder.Append(" fill-opacity=\"").Append(Number(shape.FillAlpha)).Append('"');
            }

            AppendStroke(shape, shape.LineWidthEmu);
            builder.Append(" />");
        }

        void AppendGroupGeometry(GroupShape shape, bool isEllipse, double offsetX = 0, double offsetY = 0)
        {
            // Contours (custGeom or a built preset like hexagon/roundRect) take precedence over the
            // Geometry primitive; even-odd fill keeps ring shapes (frame) hollow. The element is
            // left open like <rect>/<ellipse> so callers can append fill/stroke attributes.
            if (shape.Subpaths != null)
            {
                builder.Append("<path fill-rule=\"evenodd\" d=\"");
                foreach (var contour in shape.Subpaths)
                {
                    if (contour.Count < 3)
                    {
                        continue;
                    }

                    for (var index = 0; index < contour.Count; index++)
                    {
                        var (pointX, pointY) = contour[index];
                        var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                        var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                        builder.Append(index == 0 ? 'M' : 'L')
                            .Append(Number(shape.X + offsetX + unitX * shape.Width)).Append(' ')
                            .Append(Number(shape.Y + offsetY + unitY * shape.Height));
                    }

                    // SVG fills an open path as if closed and strokes it open — DrawingML's reading of
                    // a custGeom with no a:close.
                    if (!shape.OpenOutline)
                    {
                        builder.Append('Z');
                    }
                }

                builder.Append('"');
                return;
            }

            if (isEllipse)
            {
                AppendEllipseGeometry(shape, offsetX, offsetY);
                return;
            }

            builder.Append("<rect");
            AppendBox(shape, offsetX, offsetY);
        }

        void AppendBox(GroupShape shape, double offsetX = 0, double offsetY = 0) =>
            builder.Append(" x=\"").Append(Number(shape.X + offsetX)).Append("\" y=\"").Append(Number(shape.Y + offsetY))
                .Append("\" width=\"").Append(Number(shape.Width)).Append("\" height=\"").Append(Number(shape.Height)).Append('"');

        void AppendEllipseGeometry(GroupShape shape, double offsetX = 0, double offsetY = 0) =>
            builder.Append("<ellipse cx=\"").Append(Number(shape.X + offsetX + shape.Width / 2))
                .Append("\" cy=\"").Append(Number(shape.Y + offsetY + shape.Height / 2))
                .Append("\" rx=\"").Append(Number(shape.Width / 2))
                .Append("\" ry=\"").Append(Number(shape.Height / 2)).Append('"');

        // Stroke widths are given in CSS pixels and pinned with vector-effect, NOT in the viewBox's
        // EMU: SVG scales a stroke by sqrt(|det(CTM)|), which under this viewBox's non-uniform (and,
        // for a zero-height connector, near-degenerate) scale bears no relation to the width Word
        // asked for. A 0.5pt divider rule would come out ~65px thick.
        void AppendStroke(GroupShape shape, double lineWidthEmu)
        {
            if (lineWidthEmu <= 0)
            {
                return;
            }

            builder.Append(" stroke=\"").Append(DocumentExportHelpers.NormalizeColor(shape.ColorHex) ?? "#000000").Append('"')
                .Append(" stroke-width=\"").Append(Number(lineWidthEmu / OoxmlUnits.EmusPerPoint * 96.0 / 72.0)).Append('"')
                .Append(" vector-effect=\"non-scaling-stroke\"");
            if (shape.LineAlpha < 0.999)
            {
                builder.Append(" stroke-opacity=\"").Append(Number(shape.LineAlpha)).Append('"');
            }

            if (shape.RoundCap)
            {
                builder.Append(" stroke-linecap=\"round\"");
            }
        }

        void AppendImage(
            byte[] data,
            string? contentType,
            double widthPoints,
            double heightPoints,
            string? alt,
            ImageCrop? crop = null,
            double rotationDegrees = 0,
            bool flipHorizontal = false,
            bool flipVertical = false,
            ImageRecolor? recolor = null,
            double opacity = 1,
            string clip = "")
        {
            var source = ResolveImageSource(data, contentType, widthPoints, heightPoints);
            if (source == null)
            {
                // No handler and embedding disabled — drop the <img> entirely; surrounding markup
                // (the paragraph it sits in) is still produced so the surrounding content remains.
                return;
            }

            // The clip arrives as a declaration to append ("; border-radius: 50%"); the tag takes it bare.
            var style = clip.Length > 0 ? clip[2..] : null;
            AppendImageTag(source, widthPoints, heightPoints, alt, style, crop, rotationDegrees, flipHorizontal, flipVertical, recolor, opacity);
        }

        // Word's Recolor transforms have no CSS equivalent — filter: grayscale() covers one of
        // the three and nothing covers a duotone ramp — so each becomes an SVG colour matrix the
        // <img> references by id. The matrix is the shared recipe's rows verbatim, in the same
        // row-per-output-channel order feColorMatrix takes.
        string RecolorFilterId(ImageRecolor recolor)
        {
            var index = recolorFilters.IndexOf(recolor);
            if (index < 0)
            {
                index = recolorFilters.Count;
                recolorFilters.Add(recolor);
            }

            return $"morph-recolor-{index}";
        }

        /// <summary>
        /// Emits the SVG filter definitions the exported images reference, or nothing when no image
        /// carried a recolour. The element is zero-sized and hidden from assistive technology: it
        /// exists only to be referenced. Filters resolve by id wherever they sit in the document,
        /// so this can be written after the content that uses it.
        /// </summary>
        public void WriteRecolorFilters()
        {
            if (recolorFilters.Count == 0)
            {
                return;
            }

            // SVG filters interpolate in linearRGB by default, which would lighten every ramp;
            // sRGB is the space the recipe's weights are stated in.
            builder.Append("<svg width=\"0\" height=\"0\" style=\"position: absolute\" aria-hidden=\"true\">\n");
            for (var index = 0; index < recolorFilters.Count; index++)
            {
                var (red, green, blue) = recolorFilters[index].Rows();
                builder
                    .Append("<filter id=\"morph-recolor-").Append(index).Append("\" color-interpolation-filters=\"sRGB\">")
                    .Append("<feColorMatrix type=\"matrix\" values=\"")
                    .Append(Matrix(red)).Append(' ')
                    .Append(Matrix(green)).Append(' ')
                    .Append(Matrix(blue)).Append(' ')
                    .Append("0 0 0 1 0")
                    .Append("\" /></filter>\n");
            }

            builder.Append("</svg>\n");

            static string Matrix(ImageRecolor.Row row) =>
                $"{Coefficient(row.Red)} {Coefficient(row.Green)} {Coefficient(row.Blue)} 0 {Coefficient(row.Offset)}";

            // Five decimals rather than the two HtmlExporter.Number formats to: a colour matrix
            // weight is a luminance coefficient, and rounding 0.2126 to 0.21 visibly shifts a ramp.
            static string Coefficient(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        // Flip before rotation, matching DrawingML: a:xfrm's rotation applies to the already
        // flipped picture. CSS transform lists apply right-to-left, so the scale goes last.
        static string? ImageTransform(double rotationDegrees, bool flipHorizontal, bool flipVertical)
        {
            var transforms = new List<string>();
            if (Math.Abs(rotationDegrees) > 0.01)
            {
                transforms.Add($"rotate({Number(rotationDegrees)}deg)");
            }

            if (flipHorizontal || flipVertical)
            {
                transforms.Add($"scale({(flipHorizontal ? "-1" : "1")}, {(flipVertical ? "-1" : "1")})");
            }

            if (transforms.Count == 0)
            {
                return null;
            }

            return $"transform: {string.Join(' ', transforms)}";
        }

        void AppendImageTag(
            string source,
            double widthPoints,
            double heightPoints,
            string? alt,
            string? style = null,
            ImageCrop? crop = null,
            double rotationDegrees = 0,
            bool flipHorizontal = false,
            bool flipVertical = false,
            ImageRecolor? recolor = null,
            double opacity = 1)
        {
            var transform = ImageTransform(rotationDegrees, flipHorizontal, flipVertical);

            // a:srcRect — draw the full image at the enlarged rectangle its crop implies and clip
            // back to the frame: a hidden-overflow box the frame's size, the full image inside it
            // offset by the cropped-away edges. The transform (and any float/absolute placement
            // style) rides on the box, since it is the frame that Word rotates and positions.
            if (crop is {IsCropped: true} &&
                widthPoints > 0 &&
                heightPoints > 0)
            {
                var (offsetX, offsetY, fullWidth, fullHeight) = crop.Expand(0, 0, widthPoints, heightPoints);
                if (Math.Abs(fullWidth - widthPoints) > 0.01 || Math.Abs(fullHeight - heightPoints) > 0.01 ||
                    Math.Abs(offsetX) > 0.01 || Math.Abs(offsetY) > 0.01)
                {
                    var boxStyle = $"display: inline-block; overflow: hidden; width: {ToPixels(widthPoints)}px; height: {ToPixels(heightPoints)}px";
                    if (transform != null)
                    {
                        boxStyle += $"; {transform}";
                    }

                    if (style != null)
                    {
                        boxStyle += $"; {style}";
                    }

                    builder.Append("<span style=\"").Append(boxStyle).Append("\">");
                    var imageStyle = $"margin: {Length(offsetY)} 0 0 {Length(offsetX)}";
                    AppendImageTag(source, fullWidth, fullHeight, alt, imageStyle, recolor: recolor, opacity: opacity);
                    builder.Append("</span>");
                    return;
                }
            }

            if (transform != null)
            {
                style = style == null ? transform : $"{style}; {transform}";
            }

            // The recolour rides on the <img> rather than any crop wrapper around it, so a cropped
            // picture is filtered and its clipping box is not.
            if (recolor != null)
            {
                var filter = $"filter: url(#{RecolorFilterId(recolor)})";
                style = style == null ? filter : $"{style}; {filter}";
            }

            // a:alphaModFix — CSS opacity composites the whole element against what is behind it,
            // which is what Word does with a translucent picture. The multiplier may legally exceed
            // 100%, which on an opaque source means no change.
            if (opacity < 1)
            {
                var translucent = $"opacity: {Number(Math.Clamp(opacity, 0, 1))}";
                style = style == null ? translucent : $"{style}; {translucent}";
            }

            builder.Append("<img src=\"").Append(EncodeAttribute(source)).Append('"');
            if (widthPoints > 0)
            {
                builder.Append(" width=\"").Append(ToPixels(widthPoints)).Append('"');
            }

            if (heightPoints > 0)
            {
                builder.Append(" height=\"").Append(ToPixels(heightPoints)).Append('"');
            }

            if (style != null)
            {
                builder.Append(" style=\"").Append(style).Append('"');
            }

            builder.Append(" alt=\"").Append(EncodeAttribute(alt ?? "")).Append("\" />");
        }

        string UniqueHeadingId(string text)
        {
            var slug = Slug(text);
            if (slug.Length == 0)
            {
                return "";
            }

            var candidate = slug;
            var suffix = 1;
            while (!usedHeadingIds.Add(candidate))
            {
                candidate = $"{slug}-{suffix}";
                suffix++;
            }

            return candidate;
        }

        static string SelectedItem(DropDownFormFieldElement dropDown)
        {
            if (dropDown.SelectedIndex >= 0 &&
                dropDown.SelectedIndex < dropDown.Items.Count)
            {
                return dropDown.Items[dropDown.SelectedIndex];
            }

            return "";
        }

        StringBuilder Indent(int depth)
        {
            if (options.PrettyFormat)
            {
                return builder.Append(' ', depth * 2);
            }

            return builder;
        }

        static int ToPixels(double points) => (int) Math.Round(points * 96.0 / 72.0);

        static string EncodeText(string text) => WebUtility.HtmlEncode(text);

        static string EncodeAttribute(string text) => WebUtility.HtmlEncode(text);

        static string PlainText(IReadOnlyList<Run> runs)
        {
            var text = new StringBuilder();
            foreach (var run in runs)
            {
                if (run.Properties.Hidden || run.InlineImageData != null)
                {
                    continue;
                }

                text.Append(run.IsTab ? " " : run.Text);
            }

            return text.ToString();
        }

        static string Slug(string text)
        {
            var slug = new StringBuilder();
            var pendingHyphen = false;
            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character))
                {
                    if (pendingHyphen &&
                        slug.Length > 0)
                    {
                        slug.Append('-');
                    }

                    pendingHyphen = false;
                    slug.Append(char.ToLowerInvariant(character));
                }
                else if (character is ' ' or '-' or '_')
                {
                    pendingHyphen = true;
                }
            }

            return slug.ToString();
        }
    }
}
