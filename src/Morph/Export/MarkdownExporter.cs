namespace Morph;

/// <summary>
/// Serializes a <see cref="ParsedDocument"/> to Pandoc-flavoured Markdown (CommonMark + GFM pipe
/// tables, strikeout, and Pandoc's <c>^sup^</c> / <c>~sub~</c> / <c>[x]{.underline}</c> spans),
/// mirroring what Pandoc produces for DOCX → Markdown.
/// </summary>
static class MarkdownExporter
{
    public static string Export(ParsedDocument document)
    {
        var writer = new MarkdownWriter();
        writer.WriteElements(document.Elements);
        return writer.Finish();
    }

    sealed class MarkdownWriter
    {
        readonly StringBuilder builder = new();

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
                    AppendBlock(Image(image.ImageData, image.ContentType));
                    break;
                case FloatingImageElement floatingImage:
                    AppendBlock(Image(floatingImage.ImageData, floatingImage.ContentType));
                    break;
                case HorizontalRuleElement:
                    AppendBlock("---");
                    break;
                case FloatingTextBoxElement textBox:
                    WriteElements(textBox.Content);
                    break;
                case ContentControlElement contentControl:
                    if (contentControl.Runs is {Count: > 0} runs)
                    {
                        AppendBlock(Inline(runs, inTable: false));
                    }
                    else if (!string.IsNullOrWhiteSpace(contentControl.Content))
                    {
                        AppendBlock(EscapeInline(contentControl.Content, inTable: false));
                    }

                    break;
                case WordArtElement wordArt:
                    AppendBlock(EscapeInline(wordArt.Text, inTable: false));
                    break;
                case FloatingWordArtElement floatingWordArt:
                    AppendBlock(EscapeInline(floatingWordArt.Text, inTable: false));
                    break;
                case TextFormFieldElement textField:
                    var fieldText = textField.Value.Length > 0 ? textField.Value : textField.DefaultText ?? "";
                    AppendBlock(EscapeInline(fieldText, inTable: false));
                    break;
                case CheckBoxFormFieldElement checkBox:
                    AppendBlock(checkBox.Checked ? "☑" : "☐");
                    break;
                case DropDownFormFieldElement dropDown:
                    AppendBlock(EscapeInline(SelectedItem(dropDown), inTable: false));
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
            var inline = Inline(paragraph.Runs, inTable: false);
            if (inline.Length == 0)
            {
                return;
            }

            if (level != null)
            {
                AppendBlock($"{new string('#', level.Value)} {inline}");
                return;
            }

            AppendBlock(inline);
        }

        void WriteList(IReadOnlyList<ListNode> nodes, string indent)
        {
            var ordinal = 0;
            var previousOrdered = false;
            foreach (var node in nodes)
            {
                if (node.Ordered != previousOrdered)
                {
                    ordinal = 0;
                    previousOrdered = node.Ordered;
                }

                ordinal++;
                var marker = node.Ordered ? $"{ordinal}. " : "- ";
                var inline = Inline(node.Paragraph.Runs, inTable: false);
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

        static string CellText(IReadOnlyList<DocumentElement> content)
        {
            var parts = new List<string>();
            foreach (var element in content)
            {
                if (element is ParagraphElement paragraph && !DocumentExportHelpers.IsBlank(paragraph))
                {
                    parts.Add(Inline(paragraph.Runs, inTable: true));
                }
            }

            return string.Join(" ", parts);
        }

        static string Inline(IReadOnlyList<Run> runs, bool inTable)
        {
            var inline = new StringBuilder();
            var index = 0;
            while (index < runs.Count)
            {
                var run = runs[index];
                if (run.Properties.Hidden)
                {
                    index++;
                    continue;
                }

                if (run.HyperlinkUrl is {Length: > 0} url)
                {
                    var linkText = new StringBuilder();
                    while (index < runs.Count && runs[index].HyperlinkUrl == url)
                    {
                        AppendRun(linkText, runs[index], inTable);
                        index++;
                    }

                    inline.Append('[').Append(linkText).Append("](").Append(EscapeUrl(url)).Append(')');
                    continue;
                }

                AppendRun(inline, run, inTable);
                index++;
            }

            return inline.ToString();
        }

        static void AppendRun(StringBuilder target, Run run, bool inTable)
        {
            if (run.InlineImageData is {} imageData)
            {
                target.Append(Image(imageData, run.InlineImageContentType));
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

            var raw = run.Properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            raw = raw.Replace('\n', ' ');

            var leadingLength = LeadingWhitespaceLength(raw);
            var trailingLength = TrailingWhitespaceLength(raw);
            if (leadingLength + trailingLength >= raw.Length)
            {
                target.Append(raw);
                return;
            }

            var core = raw.Substring(leadingLength, raw.Length - leadingLength - trailingLength);
            target.Append(raw, 0, leadingLength);
            target.Append(Decorate(EscapeInline(core, inTable), run.Properties));
            target.Append(raw, raw.Length - trailingLength, trailingLength);
        }

        static string Decorate(string text, RunProperties properties)
        {
            var prefix = new StringBuilder();
            var suffix = new StringBuilder();

            void Wrap(string marker)
            {
                prefix.Append(marker);
                suffix.Insert(0, marker);
            }

            if (properties.Bold)
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
            if (builder.Length > 0 && !builder.ToString().EndsWith("\n\n", StringComparison.Ordinal))
            {
                if (builder[^1] == '\n')
                {
                    builder.Append('\n');
                }
                else
                {
                    builder.Append("\n\n");
                }
            }
        }

        static string Image(byte[] data, string? contentType)
        {
            var mime = string.IsNullOrEmpty(contentType) ? "image/png" : contentType;
            return $"![](data:{mime};base64,{Convert.ToBase64String(data)})";
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

        static int TrailingWhitespaceLength(string text)
        {
            var count = 0;
            while (count < text.Length - LeadingWhitespaceLength(text) && char.IsWhiteSpace(text[text.Length - 1 - count]))
            {
                count++;
            }

            return count;
        }

        static string EscapeInline(string text, bool inTable)
        {
            var escaped = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                switch (character)
                {
                    case '\\' or '`' or '*' or '_' or '[' or ']':
                        escaped.Append('\\').Append(character);
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

        static string EscapeUrl(string url) => url.Replace(" ", "%20").Replace(")", "%29");
    }
}
