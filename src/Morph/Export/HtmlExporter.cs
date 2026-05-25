using System.Net;

namespace Morph;

/// <summary>
/// Serializes a <see cref="ParsedDocument"/> to a semantic HTML fragment (body-level content, no
/// <c>&lt;html&gt;</c> wrapper), mirroring the structure Pandoc produces for DOCX → HTML: headings,
/// paragraphs, ordered/unordered lists, tables, images (as data URIs), links and inline formatting.
/// </summary>
static class HtmlExporter
{
    public static string Export(ParsedDocument document)
    {
        var writer = new HtmlWriter();
        writer.WriteElements(document.Elements, 0);
        return writer.ToString();
    }

    sealed class HtmlWriter
    {
        readonly StringBuilder builder = new();
        readonly HashSet<string> usedHeadingIds = [];

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
                    Indent(depth).Append("<p>");
                    AppendImage(image.ImageData, image.ContentType, image.WidthPoints, image.HeightPoints, null);
                    builder.Append("</p>\n");
                    break;
                case FloatingImageElement floatingImage:
                    Indent(depth).Append("<p>");
                    AppendImage(floatingImage.ImageData, floatingImage.ContentType, floatingImage.WidthPoints, floatingImage.HeightPoints, null);
                    builder.Append("</p>\n");
                    break;
                case HorizontalRuleElement:
                    Indent(depth).Append("<hr />\n");
                    break;
                case FloatingTextBoxElement textBox:
                    Indent(depth).Append("<div>\n");
                    WriteElements(textBox.Content, depth + 1);
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
                // PageBreak / ColumnBreak / SectionBreak / LineBreak / Ink / FloatingShape have no
                // semantic representation in reflowable HTML and are intentionally omitted.
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
                var id = UniqueHeadingId(PlainText(paragraph.Runs));
                Indent(depth).Append("<h").Append(level);
                if (id.Length > 0)
                {
                    builder.Append(" id=\"").Append(EncodeAttribute(id)).Append('"');
                }

                builder.Append('>');
                AppendInline(paragraph.Runs);
                builder.Append("</h").Append(level).Append(">\n");
                return;
            }

            Indent(depth).Append("<p");
            AppendParagraphStyle(paragraph.Properties);
            builder.Append('>');
            AppendInline(paragraph.Runs);
            builder.Append("</p>\n");
        }

        void WriteTextParagraph(string text, int depth)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Indent(depth).Append("<p>").Append(EncodeText(text)).Append("</p>\n");
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

        void AppendParagraphStyle(ParagraphProperties properties)
        {
            var align = properties.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "justify",
                _ => null
            };

            if (align != null)
            {
                builder.Append(" style=\"text-align: ").Append(align).Append(";\"");
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
                Indent(depth).Append('<').Append(tag).Append(">\n");
                while (index < nodes.Count && nodes[index].Ordered == ordered)
                {
                    WriteListItem(nodes[index], depth + 1);
                    index++;
                }

                Indent(depth).Append("</").Append(tag).Append(">\n");
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
                    WriteRow(rows, rowIndex, depth + 2, header: true);
                }

                Indent(depth + 1).Append("</thead>\n");
            }

            Indent(depth + 1).Append("<tbody>\n");
            for (var rowIndex = headerRowCount; rowIndex < rows.Count; rowIndex++)
            {
                WriteRow(rows, rowIndex, depth + 2, header: false);
            }

            Indent(depth + 1).Append("</tbody>\n");
            Indent(depth).Append("</table>\n");
        }

        void WriteRow(IReadOnlyList<TableRow> rows, int rowIndex, int depth, bool header)
        {
            Indent(depth).Append("<tr>\n");

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

                builder.Append('>');
                AppendCellContent(cell.Content);
                builder.Append("</").Append(tag).Append(">\n");
                gridColumn += span;
            }

            Indent(depth).Append("</tr>\n");
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

        void AppendCellContent(IReadOnlyList<DocumentElement> content)
        {
            var first = true;
            foreach (var element in content)
            {
                if (element is not ParagraphElement paragraph)
                {
                    continue;
                }

                if (DocumentExportHelpers.IsBlank(paragraph))
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append("<br />");
                }

                AppendInline(paragraph.Runs);
                first = false;
            }
        }

        void AppendInline(IReadOnlyList<Run> runs)
        {
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
                    builder.Append("<a href=\"").Append(EncodeAttribute(url)).Append("\">");
                    while (index < runs.Count && runs[index].HyperlinkUrl == url)
                    {
                        AppendRun(runs[index]);
                        index++;
                    }

                    builder.Append("</a>");
                    continue;
                }

                AppendRun(run);
                index++;
            }
        }

        void AppendRun(Run run)
        {
            if (run.InlineImageData is {} imageData)
            {
                AppendImage(imageData, run.InlineImageContentType, run.InlineImageWidthPoints, run.InlineImageHeightPoints, null);
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
            var (open, close) = FormattingTags(properties);
            builder.Append(open);

            var text = properties.AllCaps ? run.Text.ToUpperInvariant() : run.Text;
            AppendEncodedWithBreaks(text);

            builder.Append(close);
        }

        void AppendEncodedWithBreaks(string text)
        {
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

        static (string Open, string Close) FormattingTags(RunProperties properties)
        {
            var open = new StringBuilder();
            var close = new StringBuilder();

            void Wrap(string tag)
            {
                open.Append('<').Append(tag).Append('>');
                close.Insert(0, $"</{tag}>");
            }

            var style = InlineStyle(properties);
            if (style != null)
            {
                open.Append("<span style=\"").Append(style).Append("\">");
                close.Insert(0, "</span>");
            }

            if (properties.Bold)
            {
                Wrap("strong");
            }

            if (properties.Italic)
            {
                Wrap("em");
            }

            if (properties.Underline)
            {
                Wrap("u");
            }

            if (properties.Strikethrough)
            {
                Wrap("s");
            }

            switch (properties.VerticalAlignment)
            {
                case VerticalRunAlignment.Superscript:
                    Wrap("sup");
                    break;
                case VerticalRunAlignment.Subscript:
                    Wrap("sub");
                    break;
            }

            return (open.ToString(), close.ToString());
        }

        static string? InlineStyle(RunProperties properties)
        {
            var color = DocumentExportHelpers.NormalizeColor(properties.ColorHex);
            var background = DocumentExportHelpers.NormalizeColor(properties.BackgroundColorHex);
            if (color == null && background == null && !properties.SmallCaps)
            {
                return null;
            }

            var style = new StringBuilder();
            if (color != null)
            {
                style.Append("color: ").Append(color).Append("; ");
            }

            if (background != null)
            {
                style.Append("background-color: ").Append(background).Append("; ");
            }

            if (properties.SmallCaps)
            {
                style.Append("font-variant: small-caps; ");
            }

            return style.ToString().TrimEnd();
        }

        void AppendImage(byte[] data, string? contentType, double widthPoints, double heightPoints, string? alt)
        {
            var mime = string.IsNullOrEmpty(contentType) ? "image/png" : contentType;
            builder.Append("<img src=\"data:").Append(mime).Append(";base64,")
                .Append(Convert.ToBase64String(data)).Append('"');
            if (widthPoints > 0)
            {
                builder.Append(" width=\"").Append(ToPixels(widthPoints)).Append('"');
            }

            if (heightPoints > 0)
            {
                builder.Append(" height=\"").Append(ToPixels(heightPoints)).Append('"');
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

        static string SelectedItem(DropDownFormFieldElement dropDown) =>
            dropDown.SelectedIndex >= 0 && dropDown.SelectedIndex < dropDown.Items.Count
                ? dropDown.Items[dropDown.SelectedIndex]
                : "";

        StringBuilder Indent(int depth) => builder.Append(' ', depth * 2);

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
