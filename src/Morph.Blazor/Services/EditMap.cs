/// <summary>
/// Where the document's paragraphs are on its pages, for editing them in place: which paragraph a
/// click on a page lands in, and the box its text is set in — the one an editor laid over it has to
/// fill. Read off the laid-out tree, as <see cref="SourceIndex"/> is, and for the same reason: the
/// layout engine measures for every converter and keeps nothing for the viewer's sake.
///
/// A paragraph is known by the ordinal of its <c>w:p</c> (<see cref="ParagraphElement.Source"/>). A
/// paragraph that runs over a page break, or a column's end, is a block on each side of it.
///
/// The engine keeps where each line starts and how wide its text is, not the measure it was set in,
/// so a block's box is worked out: its near edge from the lines themselves — exactly, for text that
/// starts at it — and its far edge from what the lines stand in, a table cell or the page's margins.
/// Inside anything else, a text box for one, the lines are all there is to go by.
/// </summary>
sealed class EditMap
{
    // A cell's padding when its lines cannot say: Word's default, 0.075 inch.
    const float cellPadding = 5.4f;

    // The least room a block is given to type in, where only its lines say how wide it is.
    const float leastWidth = 144;

    readonly List<EditBlock>[] pages;
    readonly Dictionary<int, List<EditBlock>> byParagraph = [];
    readonly Dictionary<int, RunProperties> formats = [];

    // How much room each run's text took where it was drawn, and how many characters of it.
    readonly Dictionary<int, (float Width, int Characters)> drawn = [];

    EditMap(int pageCount) =>
        pages = Enumerable.Range(0, pageCount).Select(_ => new List<EditBlock>()).ToArray();

    public static EditMap Empty { get; } = new(0);

    /// <summary>
    /// The map of a laid-out document. <paramref name="paragraphOf"/> names the paragraph a run of
    /// the main part belongs to, for the paragraphs the layout made copies of; <paramref name="body"/>
    /// holds the paragraphs that stand in the page's own columns.
    /// </summary>
    public static EditMap Build(LaidOutDocument document, SourceIndex? sources, Func<int, int?> paragraphOf, IReadOnlySet<int> body)
    {
        var map = new EditMap(document.Pages.Count);
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var walk = new Walk(map, sources, index, page.Settings, paragraphOf, body);
            walk.Items(page.Items, null);
            walk.Close();
        }

        return map;
    }

    /// <summary>A paragraph's blocks, in page order; empty for a paragraph that is on no page.</summary>
    public IReadOnlyList<EditBlock> Blocks(int paragraph)
    {
        if (byParagraph.TryGetValue(paragraph, out var blocks))
        {
            return blocks;
        }

        return [];
    }

    /// <summary>How a run of the main part is formatted, as it was drawn; null for one that was not.</summary>
    public RunProperties? Format(int run) =>
        formats.GetValueOrDefault(run);

    /// <summary>
    /// The room a character of a run took on average where it was drawn, in points; null when the
    /// run's text cannot be told apart from its neighbours' on the page. A face standing in for the
    /// document's own is set to the width of the one it stands in for, and a run can be spaced out
    /// besides, so this is not what the face would give left to itself — and an editor that set the
    /// text as the face gives would break its lines somewhere else.
    /// </summary>
    public float? Advance(int run)
    {
        if (drawn.TryGetValue(run, out var measure) &&
            measure.Characters > 0)
        {
            return measure.Width / measure.Characters;
        }

        return null;
    }

    /// <summary>
    /// The block a point of a page is in: the smallest whose box holds it, so a paragraph in a cell
    /// is found rather than whatever the cell stands in.
    /// </summary>
    public EditBlock? At(int page, double x, double y)
    {
        if (page < 0 ||
            page >= pages.Length)
        {
            return null;
        }

        EditBlock? found = null;
        foreach (var block in pages[page])
        {
            if (x < block.Left ||
                x > block.Right ||
                y < block.Top ||
                y > block.Bottom)
            {
                continue;
            }

            if (found == null ||
                block.Width * block.Height < found.Width * found.Height)
            {
                found = block;
            }
        }

        return found;
    }

    void Add(EditBlock block)
    {
        pages[block.Page].Add(block);
        if (!byParagraph.TryGetValue(block.Paragraph, out var blocks))
        {
            byParagraph[block.Paragraph] = blocks = [];
        }

        blocks.Add(block);
    }

    sealed class Walk(EditMap map, SourceIndex? sources, int page, PageSettings settings, Func<int, int?> paragraphOf, IReadOnlySet<int> body)
    {
        // The block being gathered: lines of one paragraph that follow one another in one container.
        int paragraph = -1;
        PlacedCell? container;
        readonly List<PlacedLine> lines = [];

        public void Items(IReadOnlyList<PlacedItem> items, PlacedCell? cell)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case PlacedLine line:
                        Line(line, cell);
                        break;
                    case PlacedTableRow row:
                        Close();
                        foreach (var inner in row.Cells)
                        {
                            Items(inner.Floats, null);
                            Close();
                            Items(inner.Content, inner);
                            Close();
                        }

                        break;
                    default:
                        // A rotated group's lines are turned with it, and an editor laid over them
                        // would not be: they are read, but not edited in place.
                        Close();
                        break;
                }
            }
        }

        void Line(PlacedLine line, PlacedCell? cell)
        {
            foreach (var run in line.Paragraph.Runs)
            {
                if (run.Source is {Atomic: false} source)
                {
                    map.formats.TryAdd(source.Run, run.Properties);
                }
            }

            Measure(line);

            if (Paragraph(line.Paragraph) is not { } ordinal)
            {
                Close();
                return;
            }

            if (ordinal != paragraph ||
                !ReferenceEquals(cell, container) ||
                (lines.Count > 0 && line.Y < lines[^1].Y))
            {
                Close();
                paragraph = ordinal;
                container = cell;
            }

            lines.Add(line);
        }

        // A placed run says how wide the text of the runs it was drawn from was set. The layout draws
        // neighbouring runs of one formatting as one, so a placed run's width is shared out among
        // them by how much of its text each gave: alike as they are, their characters were set
        // alike. A justified line is left out — its spaces are as wide as the line needed.
        void Measure(PlacedLine line)
        {
            if (sources == null ||
                line.Paragraph.Properties.Alignment == TextAlignment.Justify)
            {
                return;
            }

            for (var index = 0; index < line.Runs.Count; index++)
            {
                var run = line.Runs[index];
                if (run.Text.Length == 0 ||
                    sources.Pieces(line, index) is not { } pieces)
                {
                    continue;
                }

                foreach (var piece in pieces)
                {
                    if (piece.Source.Atomic)
                    {
                        continue;
                    }

                    var measure = map.drawn.GetValueOrDefault(piece.Source.Run);
                    map.drawn[piece.Source.Run] = (
                        measure.Width + run.Width * piece.Length / run.Text.Length,
                        measure.Characters + piece.Length);
                }
            }
        }

        int? Paragraph(ParagraphElement element)
        {
            if (element.Source is { } ordinal)
            {
                return ordinal;
            }

            foreach (var run in element.Runs)
            {
                if (run.Source is { } source)
                {
                    return paragraphOf(source.Run);
                }
            }

            return null;
        }

        public void Close()
        {
            if (lines.Count > 0)
            {
                map.Add(Block());
            }

            lines.Clear();
            paragraph = -1;
            container = null;
        }

        EditBlock Block()
        {
            var element = lines[0].Paragraph;
            var properties = element.Properties;
            var starts = lines[0].LineIndex == 0;
            var indent = (float) (properties.FirstLineIndentPoints - properties.HangingIndentPoints);

            // An edge the lines themselves give exactly: the near one for text that starts at it —
            // from any line but a paragraph's first, which its indent sets in or out — and the far
            // one for text that ends at it.
            float? near = null;
            float? far = null;
            if (properties.Alignment is TextAlignment.Left or TextAlignment.Justify)
            {
                var rest = lines.Skip(starts ? 1 : 0).ToList();
                if (rest.Count == 0)
                {
                    near = lines[0].X - indent;
                }
                else
                {
                    near = rest.Min(_ => _.X);
                    if (starts)
                    {
                        indent = lines[0].X - near.Value;
                    }
                }
            }
            else if (properties.Alignment == TextAlignment.Right)
            {
                far = lines.Max(_ => _.X + _.Width);
            }

            if (!starts)
            {
                indent = 0;
            }

            var (left, right) = Measure(near, far, (float) properties.LeftIndentPoints, (float) properties.RightIndentPoints);

            // Whatever was worked out, the box holds the lines it is the box of.
            left = Math.Min(left, lines.Min(Start));
            right = Math.Max(right, lines.Max(_ => _.X + _.Width));
            return new(paragraph, page, element, lines.ToArray())
            {
                Left = left,
                Right = right,
                Top = lines[0].Y,
                Bottom = lines[^1].Y + lines[^1].Height,
                Indent = indent,
                Fill = properties.BackgroundColorHex ?? container?.BackgroundColorHex ?? settings.BackgroundColorHex
            };

            // Where a line would start were it not the paragraph's first.
            float Start(PlacedLine line)
            {
                if (line.LineIndex == 0)
                {
                    return line.X - indent;
                }

                return line.X;
            }
        }

        // The edges of the measure the paragraph is set in.
        (float Left, float Right) Measure(float? near, float? far, float leftIndent, float rightIndent)
        {
            if (container is { } cell)
            {
                // A cell pads both sides alike, and an edge the lines give says by how much.
                var padding = cellPadding;
                if (near is { } start)
                {
                    padding = Math.Max(start - leftIndent - cell.X, 0);
                }
                else if (far is { } end)
                {
                    padding = Math.Max(cell.X + cell.Width - rightIndent - end, 0);
                }

                return (near ?? cell.X + padding + leftIndent, far ?? cell.X + cell.Width - padding - rightIndent);
            }

            if (body.Contains(paragraph))
            {
                var width = settings.ColumnCount > 1 ? (float) settings.ColumnWidth : (float) settings.ContentWidth;
                if (near is { } start)
                {
                    return (start, start - leftIndent + width - rightIndent);
                }

                if (far is { } end)
                {
                    return (end + rightIndent - width + leftIndent, end);
                }

                if (settings.ColumnCount <= 1)
                {
                    var margin = (float) settings.MarginLeft;
                    return (margin + leftIndent, margin + width - rightIndent);
                }
            }

            // In a text box, or a column that cannot be told from its neighbours: only the lines
            // say where the text is, and they say nothing of the room there was beside it.
            var first = near ?? lines.Min(_ => _.X);
            var last = far ?? lines.Max(_ => _.X + _.Width);
            if (far != null)
            {
                return (Math.Min(first, last - leastWidth), last);
            }

            return (first, Math.Max(last, first + leastWidth));
        }
    }
}
