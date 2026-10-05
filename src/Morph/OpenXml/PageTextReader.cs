/// <summary>
/// The text of each page of a laid-out document, for
/// <see cref="DocumentConverter.GetPageTexts(Stream, ImageExportOptions?)"/>.
/// </summary>
/// <remarks>
/// What is on a page is read off the placed lines, which is the only place that knows where a page
/// ends. The order it is read in comes from the document instead: a page's items are in paint
/// order, with its header and footer bands and its floats among them, and text read in that order
/// is not text in reading order. So the body is walked as it was parsed, and each paragraph gives
/// each page the lines of it that landed there.
/// </remarks>
static class PageTextReader
{
    public static IReadOnlyList<string> Read(ParsedDocument document, LaidOutDocument laidOut)
    {
        var pageCount = 0;
        var placed = new Dictionary<ParagraphElement, SortedDictionary<int, SortedDictionary<int, string>>>();
        foreach (var page in laidOut.Pages)
        {
            pageCount = Math.Max(pageCount, page.Number);
            foreach (var line in DocumentConverter.Lines(page.Items))
            {
                if (!placed.TryGetValue(line.Paragraph, out var pages))
                {
                    placed[line.Paragraph] = pages = [];
                }

                if (!pages.TryGetValue(page.Number, out var lines))
                {
                    pages[page.Number] = lines = [];
                }

                // A w:tblHeader row is placed again on every page its table continues on, with the
                // same paragraphs and line indexes, so the row is in the text of each of those pages
                // and once in each.
                lines[line.LineIndex] = LineText(line);
            }
        }

        var builders = new StringBuilder?[pageCount];
        Append(document.Elements, placed, builders);

        var texts = new string[pageCount];
        for (var index = 0; index < pageCount; index++)
        {
            texts[index] = builders[index]?.ToString().TrimEnd('\n') ?? "";
        }

        return texts;
    }

    // Body content in document order. A paragraph is a line of text on each page it has any on; a
    // table row is a line too, its cells separated by tabs.
    static void Append(
        IEnumerable<DocumentElement> elements,
        Dictionary<ParagraphElement, SortedDictionary<int, SortedDictionary<int, string>>> placed,
        StringBuilder?[] builders)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    if (!placed.TryGetValue(paragraph, out var pages))
                    {
                        break;
                    }

                    foreach (var (page, lines) in pages)
                    {
                        AppendLine(builders, page, JoinLines(lines.Values));
                    }

                    break;
                case TableElement table:
                    foreach (var row in table.Rows)
                    {
                        AppendRow(row, placed, builders);
                    }

                    break;
                case FloatingTextBoxElement textBox:
                    Append(textBox.Content, placed, builders);
                    break;
                case PositionedFrameElement frame:
                    Append(frame.Content, placed, builders);
                    break;
            }
        }
    }

    static void AppendRow(
        TableRow row,
        Dictionary<ParagraphElement, SortedDictionary<int, SortedDictionary<int, string>>> placed,
        StringBuilder?[] builders)
    {
        // A row that is split across a page boundary has text on both pages
        var rowPages = new SortedSet<int>();
        foreach (var cell in row.Cells)
        {
            foreach (var paragraph in DocumentConverter.Flatten(cell.Content))
            {
                if (placed.TryGetValue(paragraph, out var pages))
                {
                    rowPages.UnionWith(pages.Keys);
                }
            }
        }

        foreach (var page in rowPages)
        {
            var cells = new List<string>();
            foreach (var cell in row.Cells)
            {
                cells.Add(CellText(cell, page, placed));
            }

            AppendLine(builders, page, string.Join('\t', cells));
        }
    }

    // What a cell has on a page stays on the line of its row, so its paragraphs, and those of any
    // table nested in it, are joined by spaces.
    static string CellText(
        TableCell cell,
        int page,
        Dictionary<ParagraphElement, SortedDictionary<int, SortedDictionary<int, string>>> placed)
    {
        var parts = new List<string>();
        foreach (var paragraph in DocumentConverter.Flatten(cell.Content))
        {
            if (placed.TryGetValue(paragraph, out var pages) &&
                pages.TryGetValue(page, out var lines))
            {
                var text = JoinLines(lines.Values);
                if (text.Length > 0)
                {
                    parts.Add(text);
                }
            }
        }

        return string.Join(' ', parts);
    }

    static void AppendLine(StringBuilder?[] builders, int page, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var builder = builders[page - 1] ??= new();
        builder.Append(text);
        builder.Append('\n');
    }

    // The wrapped lines of a paragraph, back as the one line of text they were wrapped from. Where
    // the wrap fell between words the space may have gone with either line or with neither, so one
    // is put back only where there is none.
    static string JoinLines(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0 &&
                !char.IsWhiteSpace(builder[^1]) &&
                !char.IsWhiteSpace(line[0]))
            {
                builder.Append(' ');
            }

            builder.Append(line);
        }

        return builder.ToString().Trim();
    }

    // The runs of a line, left to right. A list marker is a run of its own, set apart from the text
    // by the indent rather than by a space, and so is text either side of a tab: a gap between two
    // runs is read as a space, and a tab leader as a tab.
    static string LineText(PlacedLine line)
    {
        var builder = new StringBuilder();
        var previousRight = float.NaN;
        foreach (var run in line.Runs)
        {
            if (run.Leader != TabLeader.None)
            {
                builder.Append('\t');
                previousRight = float.NaN;
                continue;
            }

            if (run.Text.Length == 0)
            {
                continue;
            }

            if (run.X - previousRight > gapPoints &&
                builder.Length > 0 &&
                !char.IsWhiteSpace(builder[^1]) &&
                !char.IsWhiteSpace(run.Text[0]))
            {
                builder.Append(' ');
            }

            builder.Append(run.Text);
            previousRight = run.X + run.Width;
        }

        return builder.ToString();
    }

    // Wider than the rounding between two runs that abut, and narrower than any space
    const float gapPoints = 1f;
}
