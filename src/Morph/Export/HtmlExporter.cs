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
        h1, h2, h3, h4, h5, h6 { margin: 12pt 0 0; font-weight: bold; clear: both; }
        h1 { font-size: 16pt; }
        h2 { font-size: 13pt; }
        h3 { font-size: 12pt; }
        h4 { font-size: 11pt; font-style: italic; }
        h5 { font-size: 11pt; }
        h6 { font-size: 11pt; font-style: italic; }
        ul, ol { margin: 0 0 8pt; padding-left: 24pt; }
        li { margin: 0; }
        table { border-collapse: collapse; margin: 0 0 8pt; }
        td, th { padding: 4pt 7pt; vertical-align: top; }
        hr { border: 0; border-top: 0.75pt solid #a0a0a0; margin: 6pt 0; }
        """;

    public static string Export(ParsedDocument document, HtmlExportOptions? options = null)
    {
        options ??= new();

        // The document's dominant font (by character count) anchors the <body>; individual runs
        // only carry a font-family when they deviate from it. This mirrors how Word's own HTML
        // export structures fonts — one body default plus sparse per-run overrides — instead of
        // tagging every run with the same family.
        var bodyFont = DominantFont(document.Elements);

        if (!options.EmitDocument)
        {
            var fragmentWriter = new HtmlWriter(options, bodyFont, document.PageSettings, document.Footnotes, document.Endnotes);
            fragmentWriter.WriteElements(document.Elements, 0);
            fragmentWriter.WriteNoteDefinitions();
            return fragmentWriter.ToString();
        }

        // Everything the prelude needs is known before the body is written, so the writer
        // streams the body straight into the document builder — the whole output used to be
        // materialized as a body string and copied into a second builder at the end.
        var doc = new StringBuilder();
        doc.Append("<!doctype html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<style>\n");
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

        // Background shapes are emitted as absolutely-positioned <svg> children; they resolve their
        // top/left against the body, so the body must be a positioning context.
        if (document.Elements.OfType<FloatingShapeElement>().Any())
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

        if (bodyStyle.Count > 0)
        {
            doc.Append(" style=\"").Append(string.Join("; ", bodyStyle)).Append('"');
        }

        doc.Append(">\n");
        var writer = new HtmlWriter(options, bodyFont, document.PageSettings, document.Footnotes, document.Endnotes, doc);
        writer.WriteElements(document.Elements, 0);
        writer.WriteNoteDefinitions();
        doc.Append("</body>\n</html>");
        return doc.ToString();
    }

    /// <summary>
    /// The most-used run font in the document, weighted by text length so a long body in one font
    /// wins over a few short headings in another. Returns null for a document with no text runs.
    /// </summary>
    static string? DominantFont(IReadOnlyList<DocumentElement> elements)
    {
        var weights = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        AccumulateFontWeights(elements, weights);
        return weights.Count == 0
            ? null
            : weights.OrderByDescending(_ => _.Value).ThenBy(_ => _.Key, StringComparer.Ordinal).First().Key;
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
                        if (run.Properties.Hidden || run.IsTab || string.IsNullOrEmpty(run.Text) ||
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
    static string CssFontFamily(string font) => font.Contains(' ') ? $"'{font}'" : font;

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    static string Length(double points) => $"{Number(points)}pt";

    // EMU = English Metric Units. 1 point = 12700 EMU. Inline shape groups keep their geometry in
    // the group's EMU child coordinate space, which becomes the emitted <svg>'s viewBox.
    const double emusPerPoint = 12700;

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

    sealed class HtmlWriter(HtmlExportOptions options, string? bodyFont, PageSettings pageSettings, IReadOnlyList<Footnote> footnotes, IReadOnlyList<Endnote> endnotes, StringBuilder? output = null)
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
        static readonly double[] headingFontSizePoints = [16, 13, 12, 11, 11, 11];

        // Full-document exports pass their prelude-seeded builder so the body streams straight
        // into the final output instead of being materialized and copied once more at the end.
        readonly StringBuilder builder = output ?? new();
        readonly HashSet<string> usedHeadingIds = [];
        int imageIndex;
        int shapeIndex;

        readonly Dictionary<long, string> footnoteTexts = footnotes.GroupBy(_ => _.Id).ToDictionary(_ => _.Key, _ => _.First().Text);
        readonly Dictionary<long, string> endnoteTexts = endnotes.GroupBy(_ => _.Id).ToDictionary(_ => _.Key, _ => _.First().Text);
        readonly List<(int Number, string Text)> notes = [];
        readonly Dictionary<string, int> noteNumbers = [];

        public override string ToString() => builder.ToString();

        public void WriteElements(IReadOnlyList<DocumentElement> elements, int depth)
        {
            for (var index = 0; index < elements.Count; index++)
            {
                var element = elements[index];

                if (element is ParagraphElement {Properties.Numbering: not null})
                {
                    var items = new List<ParagraphElement>();
                    while (index < elements.Count &&
                           elements[index] is ParagraphElement {Properties.Numbering: not null} listItem)
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
                    while (index < elements.Count &&
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

                WriteBlock(element, depth);
            }
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
                    WriteImageParagraph(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description, depth);
                    break;
                case FloatingImageElement floatingImage:
                    WriteFloatingImage(floatingImage, depth);
                    break;
                case HorizontalRuleElement:
                    Indent(depth).Append("<hr />\n");
                    break;
                case FloatingTextBoxElement textBox:
                    Indent(depth).Append("<div>\n");
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
                    WriteTextParagraph(wordArt.Text, depth);
                    break;
                case FloatingWordArtElement floatingWordArt:
                    WriteTextParagraph(floatingWordArt.Text, depth);
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
                    options.OnWarning?.Invoke(new(WarningKind.UnsupportedElement,
                        $"{element.GetType().Name} cannot be represented in reflowable HTML and was dropped."));
                    break;
                // PageBreak / ColumnBreak / SectionBreak / LineBreak have no semantic representation
                // in reflowable HTML and are intentionally omitted (no warning — they're flow hints,
                // not content).
            }
        }

        void WriteParagraph(ParagraphElement paragraph, int depth)
        {
            if (DocumentExportHelpers.IsBlank(paragraph))
            {
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

            Indent(depth).Append("<p");
            AppendParagraphStyle(paragraph.Properties);
            builder.Append('>');
            AppendInline(paragraph.Runs);
            builder.Append("</p>\n");
        }

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
            var liftedSize = uniformSize != null && Math.Abs(uniformSize.Value - headingDefaultSize) > 0.01
                ? uniformSize
                : null;

            AppendParagraphStyle(paragraph.Properties, defaultHeadingSpacingBeforePoints, defaultHeadingSpacingAfterPoints, liftedSize);
            builder.Append('>');
            AppendInline(paragraph.Runs, inHeading: true, effectiveSize);
            builder.Append($"</h{level}>");
        }

        // The single font size shared by every visible text run, or null when the runs disagree or
        // carry no explicit size.
        static double? UniformRunFontSize(IReadOnlyList<Run> runs)
        {
            double? size = null;
            foreach (var run in runs)
            {
                if (run.Properties.Hidden || run.IsTab || run.InlineImageData != null ||
                    run.FootnoteReferenceId != null || run.EndnoteReferenceId != null ||
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
            var visible = paragraphs.Where(_ => !DocumentExportHelpers.IsBlank(_)).ToList();
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
        string NoteMarker(long id, bool endnote)
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
        void WriteImageParagraph(byte[] data, string? contentType, double widthPoints, double heightPoints, string? description, int depth)
        {
            var source = ResolveImageSource(data, contentType, widthPoints, heightPoints);
            if (source == null)
            {
                return;
            }

            Indent(depth).Append("<p>");
            AppendImageTag(source, widthPoints, heightPoints, description);
            builder.Append("</p>\n");
        }

        // A wrap-enabled float becomes a CSS float so the following text flows beside it — the
        // same layout the raster backends produce for wrapSquare/Tight/Through. wrapNone
        // (overlap) and wrapTopAndBottom (text above and below) keep the block treatment,
        // which is already HTML's closest match for them.
        void WriteFloatingImage(FloatingImageElement image, int depth)
        {
            if (image.WrapType is not (WrapType.Square or WrapType.Tight or WrapType.Through))
            {
                WriteImageParagraph(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description, depth);
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

            Indent(depth);
            AppendImageTag(source, image.WidthPoints, image.HeightPoints, image.Description, style);
            builder.Append('\n');
        }

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
            double? fontSizePoints = null)
        {
            var parts = new List<string>();

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

            // Indentation. The left indent applies to the whole block; a hanging indent then pulls
            // the first line back out of it (negative text-indent), while a first-line indent pushes
            // only the first line in (OOXML makes the two mutually exclusive). Negative values
            // (outdents) are valid CSS and emitted verbatim.
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

            // Line spacing. Auto → a unitless multiplier (the body default is 1.08); Exactly / AtLeast
            // → a fixed point height (CSS has no "at least", so AtLeast is approximated by the value).
            if (properties.LineSpacingRule == LineSpacingRule.Auto)
            {
                if (Math.Abs(properties.LineSpacingMultiplier - defaultLineHeightMultiplier) > 0.001)
                {
                    parts.Add($"line-height: {Number(properties.LineSpacingMultiplier)}");
                }
            }
            else if (properties.LineSpacingPoints > 0.01)
            {
                parts.Add($"line-height: {Length(properties.LineSpacingPoints)}");
            }

            // Paragraph borders (w:pBdr) — each bordered paragraph renders as its own box. Word merges
            // consecutive same-bordered paragraphs into one box; this independent-box form is the
            // close-enough reflowable approximation.
            if (properties.Borders is {HasAnyBorder: true} borders)
            {
                AppendBorders(parts, borders);

                // w:pBdr/@w:space is the gap between each border and the text → CSS padding.
                var top = properties.BorderTopSpacePoints;
                var right = properties.BorderRightSpacePoints;
                var bottom = properties.BorderBottomSpacePoints;
                var left = properties.BorderLeftSpacePoints;
                if (top > 0 || right > 0 || bottom > 0 || left > 0)
                {
                    parts.Add($"padding: {BoxShorthand(top, right, bottom, left)}");
                }
            }

            if (parts.Count > 0)
            {
                builder.Append(" style=\"").Append(string.Join("; ", parts)).Append('"');
            }
        }

        void WriteList(IReadOnlyList<ListNode> nodes, int depth)
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
                    // Carry the real start ordinal ("10." after a w:startOverride) so restarted /
                    // continued lists keep their numbers.
                    var start = DocumentExportHelpers.ListStartNumber(nodes[index].Paragraph.Properties.Numbering!);
                    if (start is > 1)
                    {
                        builder.Append(" start=\"").Append(start.Value).Append('"');
                    }
                }

                builder.Append(">\n");
                while (index < nodes.Count && nodes[index].Ordered == ordered)
                {
                    WriteListItem(nodes[index], depth + 1);
                    index++;
                }

                Indent(depth).Append($"</{tag}>\n");
            }
        }

        void WriteListItem(ListNode node, int depth)
        {
            Indent(depth).Append("<li>");
            AppendInline(node.Paragraph.Runs);
            if (node.Children.Count > 0)
            {
                builder.Append('\n');
                WriteList(node.Children, depth + 1);
                Indent(depth);
            }

            builder.Append("</li>\n");
        }

        void WriteTable(TableElement table, int depth)
        {
            Indent(depth).Append("<table>\n");

            var rows = table.Rows;
            var totalColumns = 0;
            foreach (var row in rows)
            {
                var width = 0;
                foreach (var cell in row.Cells)
                {
                    width += Math.Max(1, cell.Properties.GridSpan);
                }

                totalColumns = Math.Max(totalColumns, width);
            }

            var headerRowCount = 0;
            while (headerRowCount < rows.Count && rows[headerRowCount].IsHeader)
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
            var gridColumn = 0;
            foreach (var cell in cells)
            {
                var span = Math.Max(1, cell.Properties.GridSpan);
                if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
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

                var rowSpan = cell.Properties.VerticalMerge == VerticalMergeType.Restart
                    ? ComputeRowSpan(rows, rowIndex, gridColumn)
                    : 1;
                if (rowSpan > 1)
                {
                    builder.Append(" rowspan=\"").Append(rowSpan).Append('"');
                }

                // Resolve the cell's effective borders through the shared cell→table→inside cascade,
                // so a cell inherits the table's grid/outline borders when it sets none of its own.
                var borders = TableLayout.ResolveCellBorders(
                    cell.Properties, table.Properties, rowIndex, gridColumn, rows.Count, totalColumns, rows[rowIndex]);

                var cellStyle = CellStyle(cell.Properties, borders);
                if (cellStyle != null)
                {
                    builder.Append(" style=\"").Append(cellStyle).Append('"');
                }

                builder.Append('>');
                AppendCellContent(cell.Content, depth + 1);
                builder.Append($"</{tag}>\n");
                gridColumn += span;
            }

            Indent(depth).Append("</tr>\n");
        }

        // Per-cell layout that the shared stylesheet can't express: explicit column width, shading,
        // a non-default vertical alignment, and resolved borders. Top alignment is the td/th
        // stylesheet default, so only middle/bottom are emitted.
        static string? CellStyle(TableCellProperties properties, CellBorders? borders)
        {
            var parts = new List<string>();

            if (properties.WidthPoints is > 0)
            {
                parts.Add($"width: {properties.WidthPoints.Value.ToString("0.##", CultureInfo.InvariantCulture)}pt");
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

            if (borders is {HasAnyBorder: true})
            {
                AppendBorders(parts, borders);
            }

            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        // Emits the minimal CSS border declarations for a four-edge border set: a single shorthand
        // when all four edges are present and identical, otherwise one declaration per visible edge.
        static void AppendBorders(List<string> parts, CellBorders borders)
        {
            if (borders.Top.IsVisible && borders.Right.IsVisible &&
                borders.Bottom.IsVisible && borders.Left.IsVisible &&
                borders.Top == borders.Right && borders.Right == borders.Bottom && borders.Bottom == borders.Left)
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

        static string BorderCss(BorderEdge edge)
        {
            var style = edge.Style switch
            {
                BorderLineStyle.Double => "double",
                BorderLineStyle.Dotted => "dotted",
                BorderLineStyle.Dashed => "dashed",
                _ => "solid"
            };
            var color = DocumentExportHelpers.NormalizeColor(edge.ColorHex) ?? "#000000";
            return $"{edge.WidthPoints.ToString("0.##", CultureInfo.InvariantCulture)}pt {style} {color}";
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
            var column = 0;
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

        void AppendCellContent(IReadOnlyList<DocumentElement> content, int depth)
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
                    case ParagraphElement paragraph when !DocumentExportHelpers.IsBlank(paragraph):
                        var level = DocumentExportHelpers.TryGetHeadingLevel(paragraph.Properties);
                        if (level != null)
                        {
                            AppendHeading(paragraph, level.Value);
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

                        AppendImage(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description);
                        separatorPending = true;
                        break;
                    case FloatingImageElement floatingImage:
                        if (separatorPending)
                        {
                            builder.Append("<br />");
                        }

                        AppendImage(floatingImage.ImageData, floatingImage.ContentType, floatingImage.WidthPoints, floatingImage.HeightPoints, floatingImage.Description);
                        separatorPending = true;
                        break;
                }
            }
        }

        void AppendInline(IReadOnlyList<Run> sourceRuns, bool inHeading = false, double inheritedFontSizePoints = defaultBodyFontSizePoints)
        {
            var runs = DocumentExportHelpers.CoalesceRuns(sourceRuns);
            var index = 0;
            while (index < runs.Count)
            {
                var run = runs[index];
                if (run.HyperlinkUrl is {Length: > 0} url)
                {
                    builder.Append($"""<a href="{EncodeAttribute(url)}">""");
                    while (index < runs.Count && runs[index].HyperlinkUrl == url)
                    {
                        AppendRun(runs[index], inHeading, inheritedFontSizePoints);
                        index++;
                    }

                    builder.Append("</a>");
                    continue;
                }

                AppendRun(run, inHeading, inheritedFontSizePoints);
                index++;
            }
        }

        void AppendRun(Run run, bool inHeading, double inheritedFontSizePoints)
        {
            if (run.InlineShapeGroup is {} shapeGroup)
            {
                AppendShapeGroup(shapeGroup, run.InlineImageWidthPoints, run.InlineImageHeightPoints);
                return;
            }

            if (run.InlineImageData is {} imageData)
            {
                AppendImage(imageData, run.InlineImageContentType, run.InlineImageWidthPoints, run.InlineImageHeightPoints, run.InlineImageDescription);
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
            var tags = AppendOpenTags(properties, inHeading, inheritedFontSizePoints);

            // AllCaps is handled by CSS (text-transform: uppercase) on the wrapping span — see
            // InlineStyle — so the source text stays intact rather than being eagerly uppercased.
            AppendEncodedWithBreaks(run.Text);

            AppendCloseTags(tags);
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
        RunTags AppendOpenTags(RunProperties properties, bool inHeading, double inheritedFontSizePoints)
        {
            var style = InlineStyle(properties, inHeading, bodyFont, inheritedFontSizePoints);
            if (style != null)
            {
                builder.Append($"""<span style="{style}">""");
            }

            // A heading is bold by default — both the stylesheet's h1-h6 rule and every browser's
            // UA default — so a <strong> inside one is redundant and is skipped, mirroring the
            // Markdown exporter's heading bold suppression. A run that is explicitly non-bold in a
            // heading is still honoured: InlineStyle emits font-weight: normal for it.
            var strong = properties.Bold && !inHeading;
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
                builder.Append("<u>");
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

        static string? InlineStyle(RunProperties properties, bool inHeading, string? bodyFont, double inheritedFontSizePoints)
        {
            var color = DocumentExportHelpers.NormalizeColor(properties.ColorHex);
            // Black is the default text colour; emitting a span for it is just noise.
            if (color == "#000000")
            {
                color = null;
            }

            var background = DocumentExportHelpers.NormalizeColor(properties.BackgroundColorHex);

            // Only a run that deviates from the size its element already establishes needs an
            // inline size — 11pt from the body stylesheet in flow content, the heading's effective
            // size (stylesheet default or the lifted uniform run size) inside an <hN>.
            var hasFontSize = Math.Abs(properties.FontSizePoints - inheritedFontSizePoints) > 0.01 &&
                              properties.FontSizePoints > 0;

            // Headings are bold by default in the stylesheet; a non-bold run inside one (a "Prepared
            // for:" label styled Heading 2, say) must override back to normal weight to match Word.
            var overrideWeight = inHeading && !properties.Bold;

            // Only a run whose font differs from the document's <body> font needs an inline family;
            // the common case (run matches the body font) inherits and stays clean.
            var hasFont = !string.IsNullOrEmpty(properties.FontFamily) &&
                          !string.Equals(properties.FontFamily, bodyFont, StringComparison.OrdinalIgnoreCase);

            // Expanded / condensed tracking (w:spacing on the run) — template heading and body
            // styles lean on it heavily, and without it text wraps at a different measure than
            // Word. Zero is the default and stays clean.
            var hasLetterSpacing = Math.Abs(properties.CharacterSpacingPoints) > 0.01;

            if (color == null &&
                background == null &&
                properties is {SmallCaps: false, AllCaps: false} &&
                !hasFontSize &&
                !overrideWeight && !hasFont && !hasLetterSpacing)
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

        // A background shape (w:drawing anchored behindDoc) becomes an absolutely-positioned inline
        // <svg> in the body's content-area coordinate space, drawn behind text via a negative
        // z-index. The vector data — solid/gradient/image fill, stroke, preset rect/ellipse or a
        // flattened custom polygon, plus rotation/flip — all map straight onto SVG primitives.
        void WriteShape(FloatingShapeElement shape, int depth)
        {
            var (left, top, width, height) = ShapeBox(shape, pageSettings);
            if (width <= 0.01 || height <= 0.01)
            {
                return;
            }

            var shapeId = shapeIndex++;
            Indent(depth).Append($"""<svg style="position: absolute; left: {Length(left)}; top: {Length(top)}; width: {Length(width)}; height: {Length(height)}; z-index: {(shape.BehindText ? "-1" : "1")}" viewBox="0 0 {Number(width)} {Number(height)}" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">""");

            if (shape is {ImageData: {} imageData, FillColorHex: null, Gradient: null})
            {
                // Image-filled shape: draw the bitmap stretched to the box (geometry clipping for
                // image fills is rare and not worth a clipPath here).
                var source = ResolveImageSource(imageData, shape.ImageContentType, width, height);
                if (source != null)
                {
                    builder.Append($"""<image href="{EncodeAttribute(source)}" width="{Number(width)}" height="{Number(height)}" preserveAspectRatio="none"{ShapeTransformAttribute(shape, width, height)} />""");
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

            builder.Append("</svg>\n");
        }

        void AppendShapeGeometry(FloatingShapeElement shape, double width, double height, string fill)
        {
            var attributes = new StringBuilder();
            attributes.Append(" fill=\"").Append(fill).Append('"');
            if (shape.FillAlpha < 0.999)
            {
                attributes.Append(" fill-opacity=\"").Append(Number(shape.FillAlpha)).Append('"');
            }

            var stroke = DocumentExportHelpers.NormalizeColor(shape.LineColorHex);
            if (stroke != null && shape.LineWidthPoints is > 0 and var lineWidth)
            {
                attributes.Append(" stroke=\"").Append(stroke)
                    .Append("\" stroke-width=\"").Append(Number(lineWidth)).Append('"');
            }

            attributes.Append(ShapeTransformAttribute(shape, width, height));
            var common = attributes.ToString();

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

                builder.Append('"').Append(common).Append(" />");
            }
            else if (shape.Preset == PresetShape.Ellipse)
            {
                builder.Append($"""<ellipse cx="{Number(width / 2)}" cy="{Number(height / 2)}" rx="{Number(width / 2)}" ry="{Number(height / 2)}"{common} />""");
            }
            else
            {
                builder.Append($"""<rect x="0" y="0" width="{Number(width)}" height="{Number(height)}"{common} />""");
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

            builder.Append($"""<linearGradient id="shape-grad-{shapeId}" x1="{Number(0.5 - directionX / 2)}" y1="{Number(0.5 - directionY / 2)}" x2="{Number(0.5 + directionX / 2)}" y2="{Number(0.5 + directionY / 2)}"><stop offset="0" stop-color="{start}" /><stop offset="1" stop-color="{end}" /></linearGradient>""");
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
        // Mirrors FloatingPosition.ResolveShapeBounds, which needs a render context the exporter
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
        // coordinate space, so every shape keeps its EMU geometry — including stroke widths — and
        // the browser scales the whole thing into the run's box. preserveAspectRatio="none" matches
        // the raster backends, which scale x and y independently.
        void AppendShapeGroup(InlineShapeGroup group, double widthPoints, double heightPoints)
        {
            if (widthPoints <= 0 || heightPoints <= 0 || group.ChildExtentX <= 0 || group.ChildExtentY <= 0)
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
                AppendStroke(shape, shape.LineWidthEmu > 0 ? shape.LineWidthEmu : 0.75 * emusPerPoint);
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
            builder.Append(" fill=\"").Append(DocumentExportHelpers.NormalizeColor(shape.FillColorHex) ?? "none").Append('"');
            if (shape is {FillColorHex: not null, FillAlpha: < 0.999})
            {
                builder.Append(" fill-opacity=\"").Append(Number(shape.FillAlpha)).Append('"');
            }

            AppendStroke(shape, shape.LineWidthEmu);
            builder.Append(" />");
        }

        void AppendGroupGeometry(GroupShape shape, bool isEllipse, double offsetX = 0, double offsetY = 0)
        {
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
                .Append(" stroke-width=\"").Append(Number(lineWidthEmu / emusPerPoint * 96.0 / 72.0)).Append('"')
                .Append(" vector-effect=\"non-scaling-stroke\"");
            if (shape.LineAlpha < 0.999)
            {
                builder.Append(" stroke-opacity=\"").Append(Number(shape.LineAlpha)).Append('"');
            }
        }

        void AppendImage(byte[] data, string? contentType, double widthPoints, double heightPoints, string? alt)
        {
            var source = ResolveImageSource(data, contentType, widthPoints, heightPoints);
            if (source == null)
            {
                // No handler and embedding disabled — drop the <img> entirely; surrounding markup
                // (the paragraph it sits in) is still produced so the surrounding content remains.
                return;
            }

            AppendImageTag(source, widthPoints, heightPoints, alt);
        }

        void AppendImageTag(string source, double widthPoints, double heightPoints, string? alt, string? style = null)
        {
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
                if (run.Properties.Hidden ||
                    run.InlineImageData != null)
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
                    if (pendingHyphen && slug.Length > 0)
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
