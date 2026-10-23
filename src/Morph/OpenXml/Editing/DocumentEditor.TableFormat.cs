using OoxmlTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using OoxmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;
using OoxmlTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableProperties = DocumentFormat.OpenXml.Wordprocessing.TableProperties;
using OoxmlTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;

// How a table looks and how its cells lie: its borders, its cells' shading, cells merged and split,
// and the widths of its columns. Word-probed 2026-10-19 (Word 365 over COM, a 3x3 Table Grid table):
//
//   - Borders are the table's (w:tblBorders), and only the edges that differ from its style are
//     written: no borders writes all six edges none (sz 0, space 0, colour auto), outside only writes
//     insideH and insideV none, and all borders on Table Grid writes nothing at all.
//   - Shading is the cell's: w:shd val="clear" color="auto" fill="FFFF00"; automatic takes it away.
//   - Cells merged across a row are the first, spanning the others' columns (w:gridSpan) and as wide
//     as they were together, holding the paragraphs of each; merged down a column, the top cell is
//     w:vMerge="restart" holding them all, and each below w:vMerge with an empty paragraph.
//   - A merged cell split again is cells one column wide, a paragraph to each. A cell one column wide
//     split into two divides its grid column (3009 into 1504 and 1505), the cells of other rows
//     spanning both halves; split into two rows, it gains a row whose other cells are merged down
//     into the row above.
//   - A column's width is its grid column's and its cells'. Distributed, the columns share the
//     table's width alike, the later ones taking what does not divide (8897 into 2965, 2966, 2966).
//     AutoFit to contents takes away the fixed layout and makes every width automatic; to the window
//     makes the table 100% of the text and each cell its share of it in fiftieths of a percent; fixed
//     puts the layout and the widths back.
//
// None of it is tracked: tracked or not, a table's formatting is changed as it stands.
static partial class DocumentEditor
{
    /// <summary>Draws a preset of borders on the table the paragraph is in, <paramref name="size"/> eighths of a point wide.</summary>
    public static byte[] SetTableBorders(byte[] docx, int paragraph, BorderPreset preset, int size, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Borders(edit.TableOf(paragraph), preset, size);
                return null;
            }).Document;

    /// <summary>Shades the cells the paragraphs from <paramref name="first"/> to <paramref name="last"/> are in; a null fill takes the shading away.</summary>
    public static byte[] ShadeCells(byte[] docx, int first, int last, string? fill, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Shade(first, last, fill);
                return null;
            }).Document;

    /// <summary>Merges the cells the paragraphs from <paramref name="first"/> to <paramref name="last"/> are in, which have to make a rectangle.</summary>
    public static EditResult MergeCells(byte[] docx, int first, int last, EditOptions options) =>
        Edit(docx, options, edit => edit.Merge(first, last));

    /// <summary>Splits the cell the paragraph is in into columns and rows of cells.</summary>
    public static EditResult SplitCell(byte[] docx, int paragraph, int columns, int rows, EditOptions options) =>
        Edit(docx, options, edit => edit.Split(paragraph, columns, rows));

    /// <summary>Sets the width, in twips, of the column the paragraph's cell is in.</summary>
    public static byte[] SetColumnWidth(byte[] docx, int paragraph, int twips, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.ColumnWidth(paragraph, twips);
                return null;
            }).Document;

    /// <summary>Makes the columns of the table the paragraph is in alike in width.</summary>
    public static byte[] DistributeColumns(byte[] docx, int paragraph, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                edit.Distribute(paragraph);
                return null;
            }).Document;

    /// <summary>Sizes the columns of the table the paragraph is in, as Word's AutoFit does.</summary>
    public static byte[] FitTable(byte[] docx, int paragraph, TableFit fit, EditOptions options) =>
        Edit(
            docx,
            options,
            edit =>
            {
                Editing.Fit(edit.TableOf(paragraph), fit);
                return null;
            }).Document;

    sealed partial class Editing
    {
        public OoxmlTable TableOf(int ordinal) =>
            CellOf(Find(ordinal)).Ancestors<OoxmlTable>().First();

        static OoxmlTableCell CellOf(Paragraph paragraph) =>
            paragraph.Ancestors<OoxmlTableCell>().FirstOrDefault() ??
            throw new InvalidOperationException("Click in a table first.");

        // Borders

        static readonly string[] edges = ["top", "left", "bottom", "right", "insideH", "insideV"];

        public void Borders(OoxmlTable table, BorderPreset preset, int size)
        {
            var properties = table.GetFirstChild<OoxmlTableProperties>() ?? table.PrependChild(new OoxmlTableProperties());
            var styled = StyleBorders(properties.TableStyle?.Val?.Value);
            var borders = new OoxmlTableBorders();
            foreach (var edge in edges)
            {
                var on = preset switch
                {
                    BorderPreset.All => true,
                    BorderPreset.Outside => edge is "top" or "left" or "bottom" or "right",
                    BorderPreset.Inside => edge is "insideH" or "insideV",
                    _ => false
                };
                var wanted = on ? ("single", size) : ("none", 0);
                if (styled.TryGetValue(edge, out var style) &&
                    style == wanted)
                {
                    continue;
                }

                // An edge the style leaves without one is drawn by none, as an edge set to none is.
                if (!styled.ContainsKey(edge) &&
                    !on)
                {
                    continue;
                }

                borders.AppendChild(Edge(edge, wanted.Item1, wanted.Item2));
            }

            properties.TableBorders = borders.HasChildren ? borders : null;
        }

        // The edges a table style draws, by name: what kind of line, and how wide. A style's own
        // edges are its, and the rest come from what it is based on.
        Dictionary<string, (string Kind, int Size)> StyleBorders(string? styleId)
        {
            var found = new Dictionary<string, (string, int)>();
            if (styleId == null)
            {
                return found;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var id = styleId;
            while (seen.Add(id) &&
                   StyleOf(id) is { } style)
            {
                foreach (var edge in style.StyleTableProperties?.TableBorders?.ChildElements.OfType<BorderType>() ?? [])
                {
                    var name = edge.LocalName switch
                    {
                        "start" => "left",
                        "end" => "right",
                        _ => edge.LocalName
                    };
                    var kind = edge.Val?.InnerText ?? "none";
                    if (kind == "nil")
                    {
                        kind = "none";
                    }

                    found.TryAdd(name, (kind, kind == "none" ? 0 : (int) (edge.Size?.Value ?? 4)));
                }

                if (style.BasedOn?.Val?.Value is not { } basedOn)
                {
                    break;
                }

                id = basedOn;
            }

            return found;
        }

        static BorderType Edge(string edge, string kind, int size)
        {
            BorderType border = edge switch
            {
                "top" => new TopBorder(),
                "left" => new LeftBorder(),
                "bottom" => new BottomBorder(),
                "right" => new RightBorder(),
                "insideH" => new InsideHorizontalBorder(),
                _ => new InsideVerticalBorder()
            };
            border.Val = kind == "single" ? BorderValues.Single : BorderValues.None;
            border.Size = (uint) size;
            border.Space = 0;
            border.Color = "auto";
            return border;
        }

        // Shading

        public void Shade(int first, int last, string? fill)
        {
            foreach (var cell in Rectangle(first, last).Rows.SelectMany(_ => _.Cells))
            {
                var properties = cell.TableCellProperties ?? cell.PrependChild(new OoxmlTableCellProperties());
                if (fill == null)
                {
                    properties.Shading = null;
                    continue;
                }

                properties.Shading = new()
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = fill.ToUpperInvariant()
                };
            }
        }

        // The cells of a table between those two paragraphs are in, as a rectangle, as Word selects
        // cells from one to another: the rows from the one's to the other's, and in each the cells
        // across the grid columns from the leftmost of the two to the rightmost.
        (OoxmlTable Table, List<(OoxmlTableRow Row, List<OoxmlTableCell> Cells)> Rows, int Start, int End) Rectangle(int first, int last)
        {
            var paragraphs = FindAll([first, Math.Max(first, last)]);
            var table = CellOf(paragraphs[0]).Ancestors<OoxmlTable>().First();
            var rows = Rows(table);
            int top = int.MaxValue, bottom = -1, start = int.MaxValue, end = -1;
            foreach (var paragraph in paragraphs)
            {
                var cell = paragraph.Ancestors<OoxmlTableCell>().FirstOrDefault(_ => ReferenceEquals(_.Ancestors<OoxmlTable>().First(), table));
                if (cell == null)
                {
                    continue;
                }

                var row = cell.Ancestors<OoxmlTableRow>().First();
                var index = rows.IndexOf(row);
                var (from, to) = Columns(row, cell);
                top = Math.Min(top, index);
                bottom = Math.Max(bottom, index);
                start = Math.Min(start, from);
                end = Math.Max(end, to);
            }

            var block = new List<(OoxmlTableRow, List<OoxmlTableCell>)>();
            for (var index = top; index <= bottom; index++)
            {
                var row = rows[index];
                var cells = Cells(row).Where(_ => Columns(row, _) is var (from, to) && from < end && to > start).ToList();
                if (cells.Count > 0)
                {
                    block.Add((row, cells));
                }
            }

            return (table, block, start, end);
        }

        // Merging and splitting

        public (Paragraph Paragraph, int Offset) Merge(int first, int last)
        {
            var (table, rows, start, end) = Rectangle(first, last);
            if (rows.Count == 0 ||
                (rows.Count == 1 && rows[0].Cells.Count == 1))
            {
                throw new InvalidOperationException("Select cells across more than one cell to merge them.");
            }

            var widths = GridWidths(table);
            var merged = new List<OoxmlTableCell>();
            foreach (var (row, cells) in rows)
            {
                if (Columns(row, cells[0]).Start != start ||
                    Columns(row, cells[^1]).End != end)
                {
                    throw new InvalidOperationException("The cells selected do not make a rectangle, and cannot be merged.");
                }

                var keep = cells[0];
                foreach (var other in cells.Skip(1))
                {
                    Move(other, keep);
                    other.Remove();
                }

                SetSpan(keep, end - start);
                SetWidth(keep, Sum(widths, start, end));
                merged.Add(keep);
            }

            if (merged.Count > 1)
            {
                Properties(merged[0]).VerticalMerge = new()
                {
                    Val = MergedCellValues.Restart
                };
                foreach (var below in merged.Skip(1))
                {
                    Move(below, merged[0]);
                    Properties(below).VerticalMerge = new();
                    if (!below.Elements<Paragraph>().Any())
                    {
                        below.AppendChild(new Paragraph());
                    }
                }
            }

            return (First(merged[0]), 0);
        }

        // What is in one cell put after what is in another: nothing, from a cell with one empty
        // paragraph; and in place of the other's, when that is all it has.
        static void Move(OoxmlTableCell from, OoxmlTableCell to)
        {
            if (Empty(from))
            {
                return;
            }

            if (Empty(to))
            {
                foreach (var block in Blocks(to))
                {
                    block.Remove();
                }
            }

            foreach (var block in Blocks(from))
            {
                block.Remove();
                to.AppendChild(block);
            }
        }

        static List<OpenXmlElement> Blocks(OoxmlTableCell cell) =>
            [.. cell.ChildElements.Where(_ => _ is not OoxmlTableCellProperties)];

        static bool Empty(OoxmlTableCell cell) =>
            Blocks(cell) is [Paragraph paragraph] &&
            !ParagraphContent.Runs(paragraph).Any();

        static OoxmlTableCellProperties Properties(OoxmlTableCell cell) =>
            cell.TableCellProperties ?? cell.PrependChild(new OoxmlTableCellProperties());

        static void SetSpan(OoxmlTableCell cell, int span)
        {
            var properties = Properties(cell);
            properties.GridSpan = span > 1 ? new() { Val = span } : null;
        }

        static void SetWidth(OoxmlTableCell cell, int twips)
        {
            if (Properties(cell).TableCellWidth is {Type.Value: var type} width &&
                type == TableWidthUnitValues.Dxa)
            {
                width.Width = twips.ToString(CultureInfo.InvariantCulture);
            }
        }

        public (Paragraph Paragraph, int Offset) Split(int ordinal, int columns, int rows)
        {
            var cell = CellOf(Find(ordinal));
            var row = cell.Ancestors<OoxmlTableRow>().First();
            var table = row.Ancestors<OoxmlTable>().First();
            columns = Math.Clamp(columns, 1, 63);
            rows = Math.Clamp(rows, 1, 63);

            // A cell merged down a column is split back into the cells it was.
            if (cell.TableCellProperties?.VerticalMerge is { } vertical &&
                rows > 1)
            {
                if (vertical.Val?.Value != MergedCellValues.Restart)
                {
                    throw new InvalidOperationException("Click in the top of the merged cells to split them.");
                }

                Unmerge(table, row, cell);
                return (First(cell), 0);
            }

            if (cell.TableCellProperties?.VerticalMerge != null)
            {
                throw new InvalidOperationException("Split the merged rows first.");
            }

            var cells = columns > 1 ? SplitColumns(table, row, cell, columns) : [cell];
            if (rows > 1)
            {
                SplitRows(row, cells, rows);
            }

            return (First(cell), 0);
        }

        List<OoxmlTableCell> SplitColumns(OoxmlTable table, OoxmlTableRow row, OoxmlTableCell cell, int columns)
        {
            var (start, end) = Columns(row, cell);
            var span = end - start;
            var grid = table.GetFirstChild<TableGrid>() ??
                       table.InsertAfter(new TableGrid(), table.GetFirstChild<OoxmlTableProperties>());
            var widths = grid.Elements<GridColumn>().Select(Width).ToList();
            var spans = new List<int>();
            if (columns <= span)
            {
                for (var index = 0; index < columns; index++)
                {
                    spans.Add(span / columns + (index >= columns - span % columns ? 1 : 0));
                }
            }
            else
            {
                // The grid itself divided: the cell's last column into as many as it needs, the
                // cells of other rows spanning them all.
                var column = end - 1;
                var parts = columns - span + 1;
                var width = column < widths.Count ? widths[column] : 1440;
                var pieces = Shares(width, parts);
                if (column < widths.Count)
                {
                    widths.RemoveAt(column);
                }

                widths.InsertRange(Math.Min(column, widths.Count), pieces);
                foreach (var other in Rows(table))
                {
                    if (ReferenceEquals(other, row))
                    {
                        continue;
                    }

                    if (CellAt(other, column) is { } across)
                    {
                        Widen(across, parts - 1);
                    }
                }

                spans.AddRange(Enumerable.Repeat(1, columns));
            }

            // The cell's paragraphs shared out, one to a cell, when there are enough; the last
            // cell takes what is left. Fewer, and they stay where they are.
            var blocks = Blocks(cell);
            List<OoxmlTableCell> made = [cell];
            var after = cell;
            for (var index = 1; index < columns; index++)
            {
                var added = CopyCell(cell);
                added.TableCellProperties?.RemoveAllChildren<Shading>();
                after.InsertAfterSelf(added);
                after = added;
                made.Add(added);
            }

            if (blocks.Count >= columns)
            {
                for (var index = 1; index < columns; index++)
                {
                    foreach (var block in Blocks(made[index]))
                    {
                        block.Remove();
                    }

                    var share = index < columns - 1 ? blocks.Skip(index).Take(1) : blocks.Skip(index);
                    foreach (var block in share.ToList())
                    {
                        block.Remove();
                        made[index].AppendChild(block);
                    }
                }
            }

            for (var index = 0; index < columns; index++)
            {
                SetSpan(made[index], spans[index]);
            }

            Regrid(table, grid, widths);
            return made;
        }

        // A row added after the row for each row the cells are split into: the cells split are
        // empty in it, and every other cell of the row is merged down into it.
        void SplitRows(OoxmlTableRow row, List<OoxmlTableCell> split, int rows)
        {
            var after = row;
            for (var count = 1; count < rows; count++)
            {
                var added = CopyRow(row);
                var originals = Cells(row);
                var copies = Cells(added);
                for (var index = 0; index < originals.Count && index < copies.Count; index++)
                {
                    if (split.Contains(originals[index]))
                    {
                        continue;
                    }

                    var properties = Properties(originals[index]);
                    properties.VerticalMerge ??= new()
                    {
                        Val = MergedCellValues.Restart
                    };
                    Properties(copies[index]).VerticalMerge = new();
                }

                after.InsertAfterSelf(added);
                after = added;
            }
        }

        // The cells below a cell merged down into it made cells of their own again, each given
        // one of its paragraphs while there are more than one.
        static void Unmerge(OoxmlTable table, OoxmlTableRow row, OoxmlTableCell cell)
        {
            var (start, _) = Columns(row, cell);
            Properties(cell).VerticalMerge = null;
            var rows = Rows(table);
            var below = new List<OoxmlTableCell>();
            for (var index = rows.IndexOf(row) + 1; index < rows.Count; index++)
            {
                if (CellAt(rows[index], start) is not { } next ||
                    next.TableCellProperties?.VerticalMerge is not { } merge ||
                    merge.Val?.Value == MergedCellValues.Restart)
                {
                    break;
                }

                next.TableCellProperties.VerticalMerge = null;
                below.Add(next);
            }

            var blocks = Blocks(cell);
            for (var index = 0; index < below.Count && index + 1 < blocks.Count; index++)
            {
                var share = index < below.Count - 1 ? blocks.Skip(index + 1).Take(1) : blocks.Skip(index + 1);
                var moving = share.ToList();
                if (moving.Count == 0)
                {
                    break;
                }

                foreach (var block in Blocks(below[index]))
                {
                    block.Remove();
                }

                foreach (var block in moving)
                {
                    block.Remove();
                    below[index].AppendChild(block);
                }
            }
        }

        // Widths

        public void ColumnWidth(int ordinal, int twips)
        {
            var cell = CellOf(Find(ordinal));
            var row = cell.Ancestors<OoxmlTableRow>().First();
            var table = row.Ancestors<OoxmlTable>().First();
            var (start, end) = Columns(row, cell);
            var grid = table.GetFirstChild<TableGrid>() ??
                       table.InsertAfter(new TableGrid(), table.GetFirstChild<OoxmlTableProperties>());
            var widths = grid.Elements<GridColumn>().Select(Width).ToList();
            end = Math.Min(end, widths.Count);
            if (start >= end)
            {
                return;
            }

            // Spanning more than one column, the cell's width is shared out as the columns shared it.
            var current = Sum(widths, start, end);
            var given = 0;
            for (var index = start; index < end; index++)
            {
                var width = index == end - 1
                    ? twips - given
                    : current > 0 ? (int) Math.Round(twips * (double) widths[index] / current) : twips / (end - start);
                widths[index] = Math.Max(1, width);
                given += widths[index];
            }

            Resize(table, grid, widths);
        }

        public void Distribute(int ordinal)
        {
            var table = TableOf(ordinal);
            var grid = table.GetFirstChild<TableGrid>();
            if (grid == null)
            {
                return;
            }

            var widths = grid.Elements<GridColumn>().Select(Width).ToList();
            Resize(table, grid, Shares(widths.Sum(), widths.Count));
        }

        public static void Fit(OoxmlTable table, TableFit fit)
        {
            var properties = table.GetFirstChild<OoxmlTableProperties>() ?? table.PrependChild(new OoxmlTableProperties());
            var widths = GridWidths(table);
            switch (fit)
            {
                case TableFit.Contents:
                    properties.TableLayout = null;
                    properties.TableWidth = new()
                    {
                        Width = "0",
                        Type = TableWidthUnitValues.Auto
                    };
                    foreach (var cell in Rows(table).SelectMany(Cells))
                    {
                        Properties(cell).TableCellWidth = new()
                        {
                            Width = "0",
                            Type = TableWidthUnitValues.Auto
                        };
                    }

                    return;
                case TableFit.Window:
                    properties.TableLayout = null;
                    properties.TableWidth = new()
                    {
                        Width = "5000",
                        Type = TableWidthUnitValues.Pct
                    };
                    Percent(table, widths);
                    return;
                default:
                    properties.TableLayout = new()
                    {
                        Type = TableLayoutValues.Fixed
                    };
                    properties.TableWidth = new()
                    {
                        Width = "0",
                        Type = TableWidthUnitValues.Auto
                    };
                    foreach (var row in Rows(table))
                    {
                        foreach (var cell in Cells(row))
                        {
                            var (start, end) = Columns(row, cell);
                            Properties(cell).TableCellWidth = new()
                            {
                                Width = Sum(widths, start, end).ToString(CultureInfo.InvariantCulture),
                                Type = TableWidthUnitValues.Dxa
                            };
                        }
                    }

                    return;
            }
        }

        // The grid set to widths, and each cell's width with it, in the unit it is given in.
        static void Resize(OoxmlTable table, TableGrid grid, List<int> widths)
        {
            Regrid(table, grid, widths);
            if (table.GetFirstChild<OoxmlTableProperties>()?.TableWidth?.Type?.Value == TableWidthUnitValues.Pct)
            {
                Percent(table, widths);
            }
        }

        // Each cell's share of a table as wide as the text, in fiftieths of a percent, the shares of
        // a row adding up to the whole.
        static void Percent(OoxmlTable table, List<int> widths)
        {
            var total = Math.Max(1, widths.Sum());
            foreach (var row in Rows(table))
            {
                var cells = Cells(row);
                var given = 0;
                for (var index = 0; index < cells.Count; index++)
                {
                    var (start, end) = Columns(row, cells[index]);
                    var share = index == cells.Count - 1
                        ? 5000 - given
                        : (int) Math.Round(5000.0 * Sum(widths, start, end) / total);
                    given += share;
                    Properties(cells[index]).TableCellWidth = new()
                    {
                        Width = share.ToString(CultureInfo.InvariantCulture),
                        Type = TableWidthUnitValues.Pct
                    };
                }
            }
        }

        static List<int> GridWidths(OoxmlTable table) =>
            table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Select(Width).ToList() ?? [];

        static int Sum(List<int> widths, int start, int end)
        {
            var sum = 0;
            for (var index = start; index < end && index < widths.Count; index++)
            {
                sum += widths[index];
            }

            return sum;
        }

        // A width shared into parts alike, the later parts taking what does not divide.
        static List<int> Shares(int total, int parts)
        {
            var shares = new List<int>();
            var each = total / parts;
            var over = total % parts;
            for (var index = 0; index < parts; index++)
            {
                shares.Add(each + (index >= parts - over ? 1 : 0));
            }

            return shares;
        }
    }
}
