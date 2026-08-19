/// <summary>
/// Serializes a <see cref="ParsedDocument"/> to Markdown: CommonMark with GFM pipe tables and
/// strikeout. Where Markdown has no syntax the exporter falls back to inline HTML —
/// <c>&lt;u&gt;</c>, <c>&lt;sup&gt;</c>, <c>&lt;sub&gt;</c>, and <c>&lt;br&gt;</c> for line
/// breaks in headings and table cells.
/// </summary>
static class MarkdownExporter
{
    public static string Export(ParsedDocument document, MarkdownExportOptions? options = null)
    {
        var writer = new MarkdownWriter(options ?? new(), document.Footnotes, document.Endnotes, document.PageFieldsPreEvaluated);
        writer.WriteElements(document.Elements);
        writer.WriteNoteDefinitions();
        return writer.Finish();
    }

    sealed class MarkdownWriter(MarkdownExportOptions options, IReadOnlyList<Footnote> footnotes, IReadOnlyList<Endnote> endnotes, bool pageFieldsPreEvaluated = false)
    {
        readonly StringBuilder builder = new();
        int imageIndex;

        readonly Dictionary<string, string> footnoteTexts = footnotes.GroupBy(_ => _.Id).ToDictionary(_ => _.Key, _ => _.First().Text);
        readonly Dictionary<string, string> endnoteTexts = endnotes.GroupBy(_ => _.Id).ToDictionary(_ => _.Key, _ => _.First().Text);
        readonly List<(int Number, string Text)> notes = [];
        readonly Dictionary<string, int> noteNumbers = [];

        public string Finish()
        {
            // Trim trailing newlines in place — ToString().TrimEnd() duplicated the whole
            // document to drop a few characters.
            var length = builder.Length;
            while (length > 0 && builder[length - 1] == '\n')
            {
                length--;
            }

            builder.Length = length;
            builder.Append('\n');
            return builder.ToString();
        }

        public void WriteElements(IReadOnlyList<DocumentElement> elements)
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
                    WriteList(DocumentExportHelpers.BuildListForest(items), "");
                    builder.Append('\n');
                    continue;
                }

                if (element is ParagraphElement {} quoteParagraph &&
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
                    WriteBlockQuote(quoteItems);
                    continue;
                }

                WriteBlock(element);
            }
        }

        void WriteBlock(DocumentElement element)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    WriteParagraph(paragraph);
                    break;
                case TableElement table:
                    WriteTable(table);
                    break;
                case ImageElement image:
                    AppendBlock(Image(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description));
                    break;
                case FloatingImageElement floatingImage:
                    AppendBlock(Image(floatingImage.ImageData, floatingImage.ContentType, floatingImage.WidthPoints, floatingImage.HeightPoints, floatingImage.Description));
                    break;
                case HorizontalRuleElement:
                    AppendBlock("---");
                    break;
                case FloatingTextBoxElement textBox:
                    WriteElements(textBox.Content);
                    break;
                case PositionedFrameElement frame:
                    WriteElements(frame.Content);
                    break;
                case ContentControlElement contentControl:
                    if (contentControl.Runs is {Count: > 0} runs)
                    {
                        AppendInlineBlock(Inline(runs, inTable: false));
                    }
                    else
                    {
                        AppendTextBlock(contentControl.Content);
                    }

                    break;
                case WordArtElement wordArt:
                    AppendTextBlock(wordArt.Text);
                    break;
                case FloatingWordArtElement floatingWordArt:
                    AppendTextBlock(floatingWordArt.Text);
                    break;
                case TextFormFieldElement textField:
                    AppendTextBlock(textField.Value.Length > 0 ? textField.Value : textField.DefaultText ?? "");
                    break;
                case CheckBoxFormFieldElement checkBox:
                    AppendBlock(checkBox.Checked ? "☑" : "☐");
                    break;
                case DropDownFormFieldElement dropDown:
                    AppendTextBlock(SelectedItem(dropDown));
                    break;
                case InkElement:
                case FloatingShapeElement:
                    options.OnWarning?.Invoke(
                        new(
                            WarningKind.UnsupportedElement,
                            $"{element.GetType().Name} cannot be represented in Markdown and was dropped."));
                    break;
            }
        }

        void WriteParagraph(ParagraphElement paragraph)
        {
            if (DocumentExportHelpers.IsBlank(paragraph, vectorShapesRender: false))
            {
                return;
            }

            var level = DocumentExportHelpers.TryGetHeadingLevel(paragraph.Properties);
            var inline = Inline(paragraph.Runs, inTable: false, inHeading: level != null);
            if (inline.Length == 0)
            {
                return;
            }

            if (level != null)
            {
                AppendBlock($"{new string('#', level.Value)} {HtmlBreaks(inline)}");
                return;
            }

            AppendInlineBlock(inline);
        }

        // Consecutive Quote / Intense Quote paragraphs become one CommonMark block quote: every
        // line is prefixed with "> ", and paragraphs are separated by a bare ">" line so they stay
        // in the same quote instead of splitting into adjacent ones.
        void WriteBlockQuote(IReadOnlyList<ParagraphElement> paragraphs)
        {
            var lines = new List<string>();
            foreach (var paragraph in paragraphs)
            {
                if (DocumentExportHelpers.IsBlank(paragraph, vectorShapesRender: false))
                {
                    continue;
                }

                var inline = EscapeLineStart(HardBreaks(Inline(paragraph.Runs, inTable: false)).TrimStart(' ', '\t'));
                if (inline.Length == 0)
                {
                    continue;
                }

                if (lines.Count > 0)
                {
                    lines.Add("");
                }

                lines.AddRange(inline.Split('\n'));
            }

            if (lines.Count == 0)
            {
                return;
            }

            EnsureBlankLine();
            foreach (var line in lines)
            {
                builder.Append(line.Length == 0 ? ">" : "> ").Append(line).Append('\n');
            }

            builder.Append('\n');
        }

        void WriteList(IReadOnlyList<ListNode> nodes, string indent)
        {
            var ordinal = 0;
            var previousOrdered = false;
            foreach (var node in nodes)
            {
                if (node.Ordered != previousOrdered)
                {
                    // A new ordered sibling run resumes at the ordinal its first marker shows
                    // ("10." after a w:startOverride), so restarted / continued lists keep their
                    // real numbers instead of being renumbered from 1.
                    ordinal = node.Ordered
                        ? (DocumentExportHelpers.ListStartNumber(node.Paragraph.Properties.Numbering!) ?? 1) - 1
                        : 0;
                    previousOrdered = node.Ordered;
                }

                ordinal++;
                var marker = node.Ordered ? $"{ordinal}. " : "- ";
                var inline = EscapeLineStart(HardBreaks(Inline(node.Paragraph.Runs, inTable: false)));
                builder.Append(indent).Append(marker).Append(inline).Append('\n');

                if (node.Children.Count > 0)
                {
                    WriteList(node.Children, indent + "    ");
                }
            }
        }

        void WriteTable(TableElement table)
        {
            if (table.Rows.Count == 0)
            {
                return;
            }

            var columnCount = 0;
            foreach (var row in table.Rows)
            {
                var width = 0;
                foreach (var cell in row.Cells)
                {
                    width += Math.Max(1, cell.Properties.GridSpan);
                }

                columnCount = Math.Max(columnCount, width);
            }

            // A table with no cell content is decoration, not data — templates use bordered or
            // shaded empty tables as dividers and colour blocks. An empty pipe table renders as a
            // blank header strip, so a decorated blank table degrades to a thematic break and a
            // bare one is dropped.
            if (IsBlankTable(table))
            {
                if (HasVisibleDecoration(table, columnCount))
                {
                    AppendBlock("---");
                }

                return;
            }

            EnsureBlankLine();

            WriteTableRow(table.Rows[0], columnCount);
            builder.Append('|');
            for (var column = 0; column < columnCount; column++)
            {
                builder.Append(" --- |");
            }

            builder.Append('\n');

            for (var rowIndex = 1; rowIndex < table.Rows.Count; rowIndex++)
            {
                WriteTableRow(table.Rows[rowIndex], columnCount);
            }

            builder.Append('\n');
        }

        void WriteTableRow(TableRow row, int columnCount)
        {
            builder.Append('|');
            var column = 0;
            foreach (var cell in row.Cells)
            {
                var span = Math.Max(1, cell.Properties.GridSpan);
                var text = cell.Properties.VerticalMerge == VerticalMergeType.Continue
                    ? ""
                    : CellText(cell.Content);
                builder.Append($" {text} |");
                // GFM has no column spanning; pad spanned columns with empty cells.
                for (var extra = 1; extra < span; extra++)
                {
                    builder.Append("  |");
                }

                column += span;
            }

            for (; column < columnCount; column++)
            {
                builder.Append("  |");
            }

            builder.Append('\n');
        }

        string CellText(IReadOnlyList<DocumentElement> content)
        {
            var parts = new List<string>();
            foreach (var element in content)
            {
                switch (element)
                {
                    case ParagraphElement paragraph when !DocumentExportHelpers.IsBlank(paragraph, vectorShapesRender: false):
                    {
                        var paragraphText = HtmlBreaks(Inline(paragraph.Runs, inTable: true));

                        // A pipe-table cell cannot hold a real Markdown list, so a list paragraph
                        // keeps its marker as literal text — "• item" / "1. item" — the same way
                        // paragraph boundaries survive as <br>.
                        if (paragraph.Properties.Numbering is {} numbering)
                        {
                            var marker = DocumentExportHelpers.IsOrderedList(numbering) ? numbering.Text : "•";
                            paragraphText = $"{marker} {paragraphText}";
                        }

                        parts.Add(paragraphText);
                        break;
                    }
                    case TableElement nestedTable:
                        // A pipe-table cell cannot hold a table, so flatten the nested cells'
                        // text into the host cell rather than dropping the content.
                        foreach (var row in nestedTable.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                var text = CellText(cell.Content);
                                if (text.Length > 0)
                                {
                                    parts.Add(text);
                                }
                            }
                        }

                        break;
                    case ImageElement image:
                        parts.Add(Image(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, image.Description));
                        break;
                    case FloatingImageElement floatingImage:
                        parts.Add(Image(floatingImage.ImageData, floatingImage.ContentType, floatingImage.WidthPoints, floatingImage.HeightPoints, floatingImage.Description));
                        break;
                }
            }

            // A pipe-table cell cannot hold real block structure, so consecutive paragraphs join
            // with <br> — keeping the paragraph boundaries visible instead of flowing into one line.
            return string.Join("<br>", parts);
        }

        // Blank in the same sense CellText renders empty: no non-blank paragraph, no image, and no
        // nested table with content anywhere in the cell tree.
        static bool IsBlankTable(TableElement table)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var element in cell.Content)
                    {
                        switch (element)
                        {
                            case ParagraphElement paragraph when !DocumentExportHelpers.IsBlank(paragraph, vectorShapesRender: false):
                                return false;
                            case ImageElement:
                            case FloatingImageElement:
                                return false;
                            case TableElement nested when !IsBlankTable(nested):
                                return false;
                        }
                    }
                }
            }

            return true;
        }

        // Whether any cell shows a border (through the cell → row-override → table cascade) or a
        // shading fill — the signals that a blank table is a visual divider / colour block.
        static bool HasVisibleDecoration(TableElement table, int columnCount)
        {
            var rows = table.Rows;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var gridColumn = 0;
                foreach (var cell in rows[rowIndex].Cells)
                {
                    var properties = cell.Properties;
                    if (DocumentExportHelpers.NormalizeColor(properties.BackgroundColorHex) != null)
                    {
                        return true;
                    }

                    var borders = TableLayout.ResolveCellBorders(
                        properties,
                        table.Properties,
                        rowIndex,
                        gridColumn,
                        rows.Count,
                        columnCount,
                        rows[rowIndex],
                        rows);
                    if (borders is
                        {
                            HasAnyBorder: true
                        })
                    {
                        return true;
                    }

                    gridColumn += Math.Max(1, properties.GridSpan);
                }
            }

            return false;
        }

        string Inline(IReadOnlyList<Run> sourceRuns, bool inTable, bool inHeading = false)
        {
            var runs = DocumentExportHelpers.CoalesceRuns(sourceRuns);
            var inline = new StringBuilder();
            var index = 0;
            while (index < runs.Count)
            {
                var run = runs[index];
                if (run.HyperlinkUrl is {Length: > 0} url)
                {
                    var linkText = new StringBuilder();
                    while (index < runs.Count &&
                           runs[index].HyperlinkUrl == url)
                    {
                        AppendRun(linkText, runs[index], inTable, inHeading);
                        index++;
                    }

                    inline.Append($"[{linkText}]({EscapeUrl(url)})");
                    continue;
                }

                AppendRun(inline, run, inTable, inHeading);
                index++;
            }

            return inline.ToString();
        }

        void AppendRun(StringBuilder target, Run run, bool inTable, bool inHeading)
        {
            if (run.InlineShapeGroup is {} shapeGroup)
            {
                AppendShapeGroup(target, shapeGroup, run.InlineImageWidthPoints, run.InlineImageHeightPoints);
                return;
            }

            if (run.InlineImageData is {} imageData)
            {
                AppendImage(target, imageData, run.InlineImageContentType, run.InlineImageWidthPoints, run.InlineImageHeightPoints, run.InlineImageDescription);
                return;
            }

            if (run.FootnoteReferenceId is {} footnoteId)
            {
                target.Append(NoteMarker(footnoteId, endnote: false));
                return;
            }

            if (run.EndnoteReferenceId is {} endnoteId)
            {
                target.Append(NoteMarker(endnoteId, endnote: true));
                return;
            }

            if (run.IsTab)
            {
                target.Append(' ');
                return;
            }

            if (string.IsNullOrEmpty(run.Text))
            {
                return;
            }

            // Preserve source case for AllCaps — Markdown has no styling layer to recover the
            // visual uppercasing, but keeping the case makes the text reusable.
            // A w:br arrives as '\n' in run text and survives Inline(): body text renders it as a
            // backslash hard break (HardBreaks); headings and table cells — single-line constructs
            // where a real newline would end the block — render it as an inline <br> (HtmlBreaks).
            // A page-numbering field evaluates as a one-page document ("Page 1 of 1") rather than
            // shipping the source's cached total — mirroring the HTML export.
            var raw = run.PageField == PageFieldKind.None || pageFieldsPreEvaluated ? run.Text : "1";

            var leadingLength = LeadingWhitespaceLength(raw);
            var trailingLength = TrailingWhitespaceLength(raw, leadingLength);
            if (leadingLength + trailingLength >= raw.Length)
            {
                target.Append(raw);
                return;
            }

            var core = raw.Substring(leadingLength, raw.Length - leadingLength - trailingLength);
            target.Append(raw, 0, leadingLength);
            AppendDecorated(target, EscapeInline(core, inTable), run.Properties, suppressBold: inHeading);
            target.Append(raw, raw.Length - trailingLength, trailingLength);
        }

        // A heading's leading "#" already carries the emphasis, so heading runs skip the bold marker
        // (Word's Heading styles are bold by default, which would otherwise yield non-idiomatic
        // "## **Title**"). Explicit italic/strike/under stay — they signal intent beyond the style.
        // Underline, superscript and subscript have no Markdown syntax, so they fall back to inline
        // HTML (<u>, <sup>, <sub>), which Markdown renderers pass through; literal '<' in the text
        // is already escaped by EscapeInline, so the generated tags stay unambiguous.
        // Appends the decorated run directly into the destination: markers open in fixed order
        // (bold, italic, strike, sup/sub) and close in exact reverse, with underline's HTML
        // fallback wrapping the whole thing — the same nesting the old
        // prefix/Insert(0)-suffix builders produced, without the per-run builder churn.
        static void AppendDecorated(StringBuilder target, string text, RunProperties properties, bool suppressBold = false)
        {
            var bold = properties.Bold && !suppressBold;
            var sup = properties.VerticalAlignment == VerticalRunAlignment.Superscript;
            var sub = properties.VerticalAlignment == VerticalRunAlignment.Subscript;

            if (properties.Underline)
            {
                target.Append("<u>");
            }

            if (bold)
            {
                target.Append("**");
            }

            if (properties.Italic)
            {
                target.Append('*');
            }

            if (properties.Strikethrough)
            {
                target.Append("~~");
            }

            if (sup)
            {
                target.Append("<sup>");
            }
            else if (sub)
            {
                target.Append("<sub>");
            }

            target.Append(text);

            if (sup)
            {
                target.Append("</sup>");
            }
            else if (sub)
            {
                target.Append("</sub>");
            }

            if (properties.Strikethrough)
            {
                target.Append("~~");
            }

            if (properties.Italic)
            {
                target.Append('*');
            }

            if (bold)
            {
                target.Append("**");
            }

            if (properties.Underline)
            {
                target.Append("</u>");
            }
        }

        // A footnote / endnote reference becomes a GFM footnote marker "[^n]", where n counts up
        // in first-reference order across footnotes and endnotes together (GFM has no separate
        // endnote concept). Repeat references to one note reuse its number; a reference whose note
        // body is missing emits nothing. WriteNoteDefinitions() emits the matching definitions.
        string NoteMarker(string id, bool endnote)
        {
            var key = (endnote ? "e" : "f") + id;
            if (!noteNumbers.TryGetValue(key, out var number))
            {
                var texts = endnote ? endnoteTexts : footnoteTexts;
                if (!texts.TryGetValue(id, out var text))
                {
                    return "";
                }

                number = notes.Count + 1;
                noteNumbers[key] = number;
                notes.Add((number, text));
            }

            return $"[^{number}]";
        }

        // Emits the "[^n]: text" definitions for every referenced note, in reference order, after
        // the body. GFM collects them into a footnotes section regardless of position.
        public void WriteNoteDefinitions()
        {
            foreach (var (number, text) in notes)
            {
                AppendBlock($"[^{number}]: {EscapeInline(text.Replace('\n', ' '), inTable: false)}");
            }
        }

        /// <summary>Emits run-built inline content as a body block: hard breaks rendered, leading
        /// whitespace dropped (4+ spaces would re-parse as an indented code block; fewer are
        /// stripped by the parser anyway), and block-start characters escaped so the text cannot
        /// re-parse as a heading / quote / list / rule.</summary>
        void AppendInlineBlock(string inline) =>
            AppendBlock(EscapeLineStart(HardBreaks(inline).TrimStart(' ', '\t')));

        /// <summary>Emits a plain string (form field value, WordArt text, …) as a body block.</summary>
        void AppendTextBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            AppendInlineBlock(EscapeInline(text, inTable: false));
        }

        void AppendBlock(string content)
        {
            if (content.Length == 0)
            {
                return;
            }

            EnsureBlankLine();
            builder.Append(content).Append("\n\n");
        }

        void EnsureBlankLine()
        {
            if (builder.Length == 0)
            {
                return;
            }

            if (builder[^1] != '\n')
            {
                builder.Append("\n\n");
                return;
            }

            if (builder.Length < 2 || builder[^2] != '\n')
            {
                builder.Append('\n');
            }
        }

        // Block-level call sites (standalone image elements, table-cell flattening) still want a
        // string; the run-level path appends straight into the destination via AppendImage.
        string Image(byte[] data, string? contentType, double widthPoints, double heightPoints, string? description)
        {
            var image = new StringBuilder();
            AppendImage(image, data, contentType, widthPoints, heightPoints, description);
            return image.ToString();
        }

        /// <summary>
        /// Markdown has no vector primitives, so an inline <c>wpg:wgp</c> group contributes only its
        /// pictures — Word's icon graphics and circle-cropped photos, the parts that carry meaning.
        /// The surrounding decoration (the coloured bubble behind an icon, connector-line divider
        /// rules, arrow glyphs) is dropped rather than approximated.
        /// </summary>
        void AppendShapeGroup(StringBuilder target, InlineShapeGroup group, double widthPoints, double heightPoints)
        {
            foreach (var shape in group.Shapes)
            {
                if (shape.ImageData is not {} imageData)
                {
                    continue;
                }

                // The handler sees the picture's own rendered size, not the group's.
                var pictureWidth = group.ChildExtentX > 0 ? shape.Width / group.ChildExtentX * widthPoints : widthPoints;
                var pictureHeight = group.ChildExtentY > 0 ? shape.Height / group.ChildExtentY * heightPoints : heightPoints;
                AppendImage(target, imageData, shape.ImageContentType, pictureWidth, pictureHeight, shape.ImageDescription);
            }
        }

        void AppendImage(StringBuilder target, byte[] data, string? contentType, double widthPoints, double heightPoints, string? description)
        {
            var index = imageIndex++;
            if (!string.IsNullOrEmpty(description))
            {
                target.Append($"![{EscapeImageAlt(description)}](");
            }
            else
            {
                target.Append("![](");
            }

            if (options.ImageHandler != null)
            {
                target.Append(EscapeUrl(options.ImageHandler(new(data, contentType, widthPoints, heightPoints, index))));
            }
            else
            {
                // Base64 never contains the characters EscapeUrl guards against (space and
                // parens), so the data URI is appended directly — a multi-megabyte image used
                // to be copied several times through interpolation and escaping on its way in.
                var mime = string.IsNullOrEmpty(contentType) ? "image/png" : contentType;
                target.Append("data:").Append(mime).Append(";base64,").Append(Convert.ToBase64String(data));
            }

            target.Append(')');
        }

        // Alt text sits inside the ![...] brackets: flatten line breaks and escape the characters
        // that would otherwise unbalance or terminate the bracket.
        static string EscapeImageAlt(string text)
        {
            var escaped = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                switch (character)
                {
                    case '\r' or '\n':
                        escaped.Append(' ');
                        break;
                    case '\\' or '[' or ']':
                        escaped.Append('\\').Append(character);
                        break;
                    default:
                        escaped.Append(character);
                        break;
                }
            }

            return escaped.ToString();
        }

        static string SelectedItem(DropDownFormFieldElement dropDown) =>
            dropDown.SelectedIndex >= 0 && dropDown.SelectedIndex < dropDown.Items.Count
                ? dropDown.Items[dropDown.SelectedIndex]
                : "";

        static int LeadingWhitespaceLength(string text)
        {
            var count = 0;
            while (count < text.Length && char.IsWhiteSpace(text[count]))
            {
                count++;
            }

            return count;
        }

        static int TrailingWhitespaceLength(string text, int leadingLength)
        {
            var count = 0;
            while (count < text.Length - leadingLength && char.IsWhiteSpace(text[text.Length - 1 - count]))
            {
                count++;
            }

            return count;
        }

        // A '\n' surviving Inline() is a w:br hard line break. Render it as a backslash before
        // the newline. Continuation lines are left-trimmed (paragraph parsing
        // strips the whitespace anyway, and 0-3 leading spaces would not stop a block construct
        // from matching) and line-start escaped so "- next" cannot re-parse as a nested block.
        // Leading / trailing breaks are dropped: a backslash at the very start or end of a
        // paragraph reads as a literal backslash, not a break.
        static string HardBreaks(string inline)
        {
            if (!inline.Contains('\n'))
            {
                return inline;
            }

            var lines = new List<string>(inline.Split('\n'));
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            {
                lines.RemoveAt(0);
            }

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var joined = new StringBuilder(inline.Length + lines.Count);
            for (var index = 0; index < lines.Count; index++)
            {
                if (index == 0)
                {
                    joined.Append(lines[index]);
                    continue;
                }

                joined
                    .Append("\\\n")
                    .Append(EscapeLineStart(lines[index].TrimStart()));
            }

            return joined.ToString();
        }

        // A '\n' surviving Inline() is a w:br. Headings and table cells occupy a single source
        // line — a real newline would end the heading / break the row — so the break becomes an
        // inline <br>, matching the HTML exporter. Leading / trailing breaks are dropped.
        static string HtmlBreaks(string inline)
        {
            if (!inline.Contains('\n'))
            {
                return inline;
            }

            var segments = new List<string>(inline.Split('\n'));
            while (segments.Count > 0 && string.IsNullOrWhiteSpace(segments[0]))
            {
                segments.RemoveAt(0);
            }

            while (segments.Count > 0 && string.IsNullOrWhiteSpace(segments[^1]))
            {
                segments.RemoveAt(segments.Count - 1);
            }

            return string.Join("<br>", segments);
        }

        // Escapes the leading character(s) of a line that would otherwise open a block construct
        // when the Markdown is re-parsed: ATX headings ("# "), block quotes (">"), bullet items
        // ("- ", "+ "), thematic breaks ("---"), setext underlines ("==="), tilde code fences
        // ("~~~"), and ordered-list items ("12. x" / "12) x"). '*', '`' and '_' starters are
        // already escaped by EscapeInline; generated markup ("**", "[", "![") is left untouched.
        static string EscapeLineStart(string line)
        {
            if (line.Length == 0)
            {
                return line;
            }

            switch (line[0])
            {
                case '>':
                    return $"\\{line}";
                case '#':
                {
                    var hashes = 0;
                    while (hashes < line.Length && line[hashes] == '#')
                    {
                        hashes++;
                    }

                    if (hashes <= 6 &&
                        (hashes == line.Length || line[hashes] is ' ' or '\t'))
                    {
                        return $"\\{line}";
                    }

                    return line;
                }
                case '-' or '+':
                    if (line.Length == 1 ||
                        line[1] is ' ' or '\t' ||
                        (line[0] == '-' &&
                         line[1] == '-'))
                    {
                        return $"\\{line}";
                    }

                    return line;
                case '=':
                {
                    // Only a line consisting entirely of '=' is a setext-heading underline.
                    var equalsCount = 0;
                    while (equalsCount < line.Length && line[equalsCount] == '=')
                    {
                        equalsCount++;
                    }

                    return equalsCount == line.TrimEnd().Length ? $"\\{line}" : line;
                }
                case '~' when line.StartsWith("~~~", StringComparison.Ordinal):
                    return $"\\{line}";
                case >= '0' and <= '9':
                {
                    var digits = 0;
                    while (digits < line.Length && char.IsAsciiDigit(line[digits]))
                    {
                        digits++;
                    }

                    // "1998. was ..." — escape the separator ("1998\.") so it stays prose; the
                    // digits themselves are not escapable characters.
                    if (digits <= 9 && digits < line.Length &&
                        line[digits] is '.' or ')' &&
                        (digits + 1 == line.Length ||
                         line[digits + 1] is ' ' or '\t'))
                    {
                        return $"{line[..digits]}\\{line[digits..]}";
                    }

                    return line;
                }
                default:
                    return line;
            }
        }

        static string EscapeInline(string text, bool inTable)
        {
            var escaped = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                switch (character)
                {
                    // '<' is escaped because inline HTML and autolinks pass straight through
                    // Markdown — "use <b> here" would otherwise bold the following text.
                    case '\\' or '`' or '*' or '_' or '[' or ']' or '<':
                        escaped.Append('\\').Append(character);
                        break;
                    case '&' when IsEntityLike(text, index):
                        escaped.Append("\\&");
                        break;
                    case '|' when inTable:
                        escaped.Append("\\|");
                        break;
                    default:
                        escaped.Append(character);
                        break;
                }
            }

            return escaped.ToString();
        }

        // "&amp;", "&#169;" and "&#x2019;" would be decoded as character references by a Markdown
        // renderer (entities pass through to the HTML layer), so an ampersand introducing one
        // needs escaping. A bare ampersand ("AT&T") stays — escaping every '&' is pure noise.
        static bool IsEntityLike(string text, int ampersand)
        {
            var index = ampersand + 1;
            if (index < text.Length && text[index] == '#')
            {
                index++;
                if (index < text.Length &&
                    text[index] is 'x' or 'X')
                {
                    index++;
                }
            }

            var nameLength = 0;
            while (index < text.Length &&
                   char.IsAsciiLetterOrDigit(text[index]))
            {
                index++;
                nameLength++;
            }

            if (nameLength <= 0)
            {
                return false;
            }

            if (index >= text.Length)
            {
                return false;
            }

            return text[index] == ';';
        }

        // '(' and ')' would end (or unbalance) the "](…)" destination; spaces are invalid in a
        // bare destination.
        static string EscapeUrl(string url) => url
            .Replace(" ", "%20")
            .Replace("(", "%28")
            .Replace(")", "%29");
    }
}
