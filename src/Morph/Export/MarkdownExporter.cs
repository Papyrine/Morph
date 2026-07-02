/// <summary>
/// Serializes a <see cref="ParsedDocument"/> to Markdown: CommonMark with GFM pipe tables and
/// strikeout, plus <c>^sup^</c> / <c>~sub~</c> superscript-subscript spans and
/// <c>[x]{.underline}</c> underline spans.
/// </summary>
static class MarkdownExporter
{
    public static string Export(ParsedDocument document, MarkdownExportOptions? options = null)
    {
        var writer = new MarkdownWriter(options ?? new());
        writer.WriteElements(document.Elements);
        return writer.Finish();
    }

    sealed class MarkdownWriter(MarkdownExportOptions options)
    {
        readonly StringBuilder builder = new();
        int imageIndex;

        public string Finish() => builder.ToString().TrimEnd('\n') + "\n";

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
                    AppendBlock(Image(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints));
                    break;
                case FloatingImageElement floatingImage:
                    AppendBlock(Image(floatingImage.ImageData, floatingImage.ContentType, floatingImage.WidthPoints, floatingImage.HeightPoints));
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
                    options.OnWarning?.Invoke(new(WarningKind.UnsupportedElement,
                        $"{element.GetType().Name} cannot be represented in Markdown and was dropped."));
                    break;
            }
        }

        void WriteParagraph(ParagraphElement paragraph)
        {
            if (DocumentExportHelpers.IsBlank(paragraph))
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
                AppendBlock($"{new string('#', level.Value)} {HeadingBreaks(inline)}");
                return;
            }

            AppendInlineBlock(inline);
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

            EnsureBlankLine();

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
                builder.Append(' ').Append(text).Append(" |");
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
                    case ParagraphElement paragraph when !DocumentExportHelpers.IsBlank(paragraph):
                        parts.Add(Inline(paragraph.Runs, inTable: true));
                        break;
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
                        parts.Add(Image(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints));
                        break;
                    case FloatingImageElement floatingImage:
                        parts.Add(Image(floatingImage.ImageData, floatingImage.ContentType, floatingImage.WidthPoints, floatingImage.HeightPoints));
                        break;
                }
            }

            return string.Join(" ", parts);
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
                    while (index < runs.Count && runs[index].HyperlinkUrl == url)
                    {
                        AppendRun(linkText, runs[index], inTable, inHeading);
                        index++;
                    }

                    inline.Append('[').Append(linkText).Append("](").Append(EscapeUrl(url)).Append(')');
                    continue;
                }

                AppendRun(inline, run, inTable, inHeading);
                index++;
            }

            return inline.ToString();
        }

        void AppendRun(StringBuilder target, Run run, bool inTable, bool inHeading)
        {
            if (run.InlineImageData is {} imageData)
            {
                target.Append(Image(imageData, run.InlineImageContentType, run.InlineImageWidthPoints, run.InlineImageHeightPoints));
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
            // A w:br arrives as '\n' in run text. A table cell is a single-line construct, so the
            // break degrades to a space there. Body text keeps the newline, which HardBreaks()
            // later renders as a backslash hard break; a heading keeps it too, and WriteParagraph
            // turns it into an inline <br> (an ATX heading cannot span source lines).
            var raw = inTable ? run.Text.Replace('\n', ' ') : run.Text;

            var leadingLength = LeadingWhitespaceLength(raw);
            var trailingLength = TrailingWhitespaceLength(raw, leadingLength);
            if (leadingLength + trailingLength >= raw.Length)
            {
                target.Append(raw);
                return;
            }

            var core = raw.Substring(leadingLength, raw.Length - leadingLength - trailingLength);
            target.Append(raw, 0, leadingLength);
            target.Append(Decorate(EscapeInline(core, inTable), run.Properties, suppressBold: inHeading));
            target.Append(raw, raw.Length - trailingLength, trailingLength);
        }

        // A heading's leading "#" already carries the emphasis, so heading runs skip the bold marker
        // (Word's Heading styles are bold by default, which would otherwise yield non-idiomatic
        // "## **Title**"). Explicit italic/strike/under stay — they signal intent beyond the style.
        static string Decorate(string text, RunProperties properties, bool suppressBold = false)
        {
            var prefix = new StringBuilder();
            var suffix = new StringBuilder();

            void Wrap(string marker)
            {
                prefix.Append(marker);
                suffix.Insert(0, marker);
            }

            if (properties.Bold && !suppressBold)
            {
                Wrap("**");
            }

            if (properties.Italic)
            {
                Wrap("*");
            }

            if (properties.Strikethrough)
            {
                Wrap("~~");
            }

            switch (properties.VerticalAlignment)
            {
                case VerticalRunAlignment.Superscript:
                    Wrap("^");
                    break;
                case VerticalRunAlignment.Subscript:
                    Wrap("~");
                    break;
            }

            var decorated = $"{prefix}{text}{suffix}";
            return properties.Underline ? $"[{decorated}]{{.underline}}" : decorated;
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

        string Image(byte[] data, string? contentType, double widthPoints, double heightPoints)
        {
            var index = imageIndex++;
            string source;
            if (options.ImageHandler != null)
            {
                source = options.ImageHandler(new(data, contentType, widthPoints, heightPoints, index));
            }
            else
            {
                var mime = string.IsNullOrEmpty(contentType) ? "image/png" : contentType;
                source = $"data:{mime};base64,{Convert.ToBase64String(data)}";
            }

            return $"![]({EscapeUrl(source)})";
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

                joined.Append("\\\n").Append(EscapeLineStart(lines[index].TrimStart()));
            }

            return joined.ToString();
        }

        // A '\n' surviving Inline() in a heading is a w:br. An ATX heading occupies a single source
        // line — a real newline would end it — so the break becomes an inline <br>, matching the
        // HTML exporter's <br /> in an <hN>. Leading / trailing breaks are dropped.
        static string HeadingBreaks(string inline)
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

                    return hashes <= 6 && (hashes == line.Length || line[hashes] is ' ' or '\t')
                        ? $"\\{line}"
                        : line;
                }
                case '-' or '+':
                    return line.Length == 1 || line[1] is ' ' or '\t' || (line[0] == '-' && line[1] == '-')
                        ? $"\\{line}"
                        : line;
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
                    return digits <= 9 && digits < line.Length &&
                           line[digits] is '.' or ')' &&
                           (digits + 1 == line.Length || line[digits + 1] is ' ' or '\t')
                        ? $"{line[..digits]}\\{line[digits..]}"
                        : line;
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
                if (index < text.Length && text[index] is 'x' or 'X')
                {
                    index++;
                }
            }

            var nameLength = 0;
            while (index < text.Length && char.IsAsciiLetterOrDigit(text[index]))
            {
                index++;
                nameLength++;
            }

            return nameLength > 0 && index < text.Length && text[index] == ';';
        }

        // '(' and ')' would end (or unbalance) the "](…)" destination; spaces are invalid in a
        // bare destination.
        static string EscapeUrl(string url) => url
            .Replace(" ", "%20")
            .Replace("(", "%28")
            .Replace(")", "%29");
    }
}
