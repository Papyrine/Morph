namespace Morph;

// What the grid draws of the cells in view, as Excel draws them: a cell's fill and edges as a box, its text
// over the boxes — a left-aligned text running on over the empty cells beside it, a number too wide for its
// column shortened or shown as #### — and a merged area as one cell.
public partial class SheetEditor
{
    const double pixelsPerPoint = 96.0 / 72.0;
    const int spillColumns = 30;

    /// <summary>A cell drawn: its box (fill and edges), if it has one, and its text, if it shows any.</summary>
    internal readonly record struct Drawn(string Key, string? Box, string? BoxClass, string? Text, string? TextStyle, string? TextClass);

    readonly Dictionary<GridLook, string> fonts = [];

    internal List<Drawn> Draw()
    {
        var drawn = new List<Drawn>();
        if (Sheet is not { IsChart: false } sheet)
        {
            return drawn;
        }

        var (firstRow, lastRow, firstColumn, lastColumn) = Window;
        var covered = new HashSet<(int, int)>();
        var heads = new Dictionary<(int, int), SheetRange>();
        foreach (var merge in sheet.Merges)
        {
            if (merge.FirstRow > lastRow ||
                merge.LastRow < firstRow ||
                merge.FirstColumn > lastColumn ||
                merge.LastColumn < firstColumn)
            {
                continue;
            }

            heads[(merge.FirstRow, merge.FirstColumn)] = merge;
            for (var row = merge.FirstRow; row <= merge.LastRow; row++)
            {
                for (var column = merge.FirstColumn; column <= merge.LastColumn; column++)
                {
                    covered.Add((row, column));
                }
            }
        }

        // A merged area whose first cell is out of view is still drawn whole.
        foreach (var (place, merge) in heads)
        {
            if (place.Item1 < firstRow ||
                place.Item2 < firstColumn)
            {
                Cell(sheet, drawn, merge.FirstRow, merge.FirstColumn, merge, lastColumn, covered);
            }
        }

        for (var row = firstRow; row <= lastRow; row++)
        {
            if (sheet.RowHeight(row) <= 0)
            {
                continue;
            }

            for (var column = firstColumn; column <= lastColumn; column++)
            {
                if (heads.TryGetValue((row, column), out var merge))
                {
                    Cell(sheet, drawn, row, column, merge, lastColumn, covered);
                    continue;
                }

                if (covered.Contains((row, column)) ||
                    sheet.ColumnWidth(column) <= 0)
                {
                    continue;
                }

                Cell(sheet, drawn, row, column, null, lastColumn, covered);
            }
        }

        return drawn;
    }

    void Cell(SheetGrid sheet, List<Drawn> drawn, int row, int column, SheetRange? merge, int lastColumn, HashSet<(int, int)> covered)
    {
        var cell = sheet.Cell(row, column);
        var look = cell?.Look ?? sheet.Look(row, column);
        var text = cell?.Display ?? string.Empty;
        if (showFormulas &&
            cell is { HasFormula: true })
        {
            text = cell.Input;
        }

        if (cell == null &&
            look.IsPlain &&
            merge == null)
        {
            return;
        }

        var x = Left(column);
        var y = Top(row);
        var width = sheet.ColumnWidth(column);
        var height = sheet.RowHeight(row);
        if (merge is { } area)
        {
            width = Width(area.FirstColumn, area.LastColumn);
            height = Height(area.FirstRow, area.LastRow);
        }

        string? box = null;
        string? boxClass = null;
        if (!look.IsPlain ||
            merge != null)
        {
            box = Invariant($"left:{x}px;top:{y}px;width:{width}px;height:{height}px;{Fill(look)}{Edges(look.Borders)}");
            if (merge != null &&
                look.FillHex == null)
            {
                boxClass = "sheet-merge";
            }
        }

        if (text.Length == 0)
        {
            if (box != null)
            {
                drawn.Add(new($"{row}:{column}", box, boxClass, null, null, null));
            }

            return;
        }

        var horizontal = look.Horizontal;
        if (horizontal == null)
        {
            if (cell is { Numeric: true } && !showFormulas)
            {
                horizontal = TextAlignment.Right;
            }
            else if (cell is { Centred: true })
            {
                horizontal = TextAlignment.Center;
            }
            else
            {
                horizontal = TextAlignment.Left;
            }
        }

        // A number too wide for its column: General's shortened to fit, any other format's ####.
        if (cell is { Numeric: true } && !showFormulas &&
            Measure(text, look) > width - 4)
        {
            text = Fitted(cell, look, width - 4);
        }

        // Text set to the left, not wrapped, runs on over the empty cells to its right.
        var spill = width;
        if (horizontal == TextAlignment.Left &&
            !look.Wrap &&
            merge == null &&
            cell is not { Numeric: true })
        {
            var needed = Measure(text, look);
            var next = column + 1;
            while (spill < needed &&
                   next <= Math.Min(ExtentColumns, lastColumn + spillColumns) &&
                   !covered.Contains((row, next)) &&
                   sheet.Cell(row, next) is null or { Display.Length: 0 })
            {
                spill += sheet.ColumnWidth(next);
                next++;
            }
        }

        var color = cell?.FormatColorHex ?? look.ColorHex;
        var style = Invariant($"left:{x}px;top:{y}px;width:{spill}px;height:{height}px;{Font(look)}{Color(color)}{Align(horizontal.Value, look)}");
        var textClass = look.Wrap ? "sheet-text sheet-wrap" : "sheet-text";
        drawn.Add(new($"{row}:{column}", box, boxClass, text, style, textClass));
    }

    // The width text takes, estimated from the digit's width: the grid has no font to measure with.
    static double Measure(string text, GridLook look) =>
        text.Length * 7.0 * (look.FontSizePoints ?? 11) / 11 * (look.Bold ? 1.08 : 1);

    // A General number fewer digits, then in E notation, until it fits; any other ####, as Excel shows.
    static string Fitted(GridCell cell, GridLook look, double room)
    {
        var hashes = new string('#', Math.Max(1, (int)(room / (7.0 * (look.FontSizePoints ?? 11) / 11))));
        if (cell.Number is not { } number ||
            look.NumberFormat is not (null or "General"))
        {
            return hashes;
        }

        for (var digits = 10; digits >= 1; digits--)
        {
            var text = number.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (Measure(text, look) <= room)
            {
                return text;
            }
        }

        return hashes;
    }

    static string Fill(GridLook look)
    {
        if (look.FillHex is { } fill)
        {
            return $"background:#{fill};";
        }

        return string.Empty;
    }

    static string Color(string? hex)
    {
        if (hex is { Length: 6 })
        {
            return $"color:#{hex};";
        }

        return string.Empty;
    }

    string Font(GridLook look)
    {
        if (fonts.TryGetValue(look, out var known))
        {
            return known;
        }

        var builder = new StringBuilder();
        if (look.FontFamily is { } family)
        {
            builder.Append("font-family:'").Append(family.Replace("'", string.Empty, StringComparison.Ordinal)).Append("',Aptos,Calibri,sans-serif;");
        }

        builder.Append(Invariant($"font-size:{look.FontSizePoints ?? 11}pt;"));
        if (look.Bold)
        {
            builder.Append("font-weight:700;");
        }

        if (look.Italic)
        {
            builder.Append("font-style:italic;");
        }

        if (look is
            {
                Underline: true,
                Strikethrough: true
            })
        {
            builder.Append("text-decoration:underline line-through;");
        }
        else if (look.Underline)
        {
            builder.Append("text-decoration:underline;");
        }
        else if (look.Strikethrough)
        {
            builder.Append("text-decoration:line-through;");
        }

        var font = builder.ToString();
        fonts[look] = font;
        return font;
    }

    static string Align(TextAlignment horizontal, GridLook look)
    {
        var justify = horizontal switch
        {
            TextAlignment.Center => "center",
            TextAlignment.Right => "flex-end",
            _ => "flex-start"
        };
        var text = horizontal switch
        {
            TextAlignment.Center => "center",
            TextAlignment.Right => "right",
            _ => "left"
        };
        var vertical = look.Vertical switch
        {
            CellVerticalAlignment.Top => "flex-start",
            CellVerticalAlignment.Center => "center",
            _ => "flex-end"
        };
        var indent = string.Empty;
        if (look.Indent > 0)
        {
            var side = horizontal == TextAlignment.Right ? "right" : "left";
            indent = Invariant($"padding-{side}:{look.Indent * 9 * pixelsPerPoint + 2}px;");
        }

        return $"justify-content:{justify};text-align:{text};align-items:{vertical};{indent}";
    }

    static string Edges(CellBorders borders)
    {
        if (!borders.HasAnyBorder)
        {
            return string.Empty;
        }

        return $"{Edge("left", borders.Left)}{Edge("right", borders.Right)}{Edge("top", borders.Top)}{Edge("bottom", borders.Bottom)}";
    }

    static string Edge(string side, BorderEdge edge)
    {
        if (!edge.IsVisible)
        {
            return string.Empty;
        }

        var pixels = Math.Max(1, Math.Round(edge.WidthPoints * pixelsPerPoint));
        var style = edge.Style switch
        {
            BorderLineStyle.Double => "double",
            BorderLineStyle.Dotted => "dotted",
            BorderLineStyle.Dashed or BorderLineStyle.DashSmallGap or BorderLineStyle.DotDash or BorderLineStyle.DotDotDash or BorderLineStyle.DashDotStroked => "dashed",
            _ => "solid"
        };
        if (style == "double")
        {
            pixels = Math.Max(3, pixels);
        }

        var color = edge.ColorHex ?? "000000";
        return Invariant($"border-{side}:{pixels}px {style} #{color};");
    }

    internal static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    // Headers, gridlines and outlines

    internal string CanvasStyle =>
        Invariant($"zoom:{zoom};grid-template-columns:{RowHeaderWidth}px {lefts[^1]}px;grid-template-rows:{headerHeight}px {tops[^1]}px;");

    internal string HeadClass(bool column, int index)
    {
        var selection = Selection;
        var whole = column
            ? selection is
            {
                FirstRow: 1,
                LastRow: CellReference.MaxRow
            }
            : selection is
            {
                FirstColumn: 1,
                LastColumn: CellReference.MaxColumn
            };

        var inside = column ? index >= selection.FirstColumn && index <= selection.LastColumn : selection.ContainsRow(index);
        if (inside && whole)
        {
            return "sheet-head sheet-head-all";
        }

        if (inside)
        {
            return "sheet-head sheet-head-on";
        }

        return "sheet-head";
    }

    internal string ColumnHeadStyle(int column) =>
        Invariant($"left:{Left(column)}px;width:{Math.Max(0, Sheet?.ColumnWidth(column) ?? 0)}px;");

    internal string RowHeadStyle(int row) =>
        Invariant($"top:{Top(row)}px;height:{Math.Max(0, Sheet?.RowHeight(row) ?? 0)}px;");

    internal IEnumerable<string> Gridlines()
    {
        if (Sheet is not { ShowGridLines: true, IsChart: false } sheet)
        {
            yield break;
        }

        var (firstRow, lastRow, firstColumn, lastColumn) = Window;
        var top = Top(firstRow);
        var bottom = Top(lastRow + 1);
        var left = Left(firstColumn);
        var right = Left(lastColumn + 1);
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            if (sheet.ColumnWidth(column) > 0)
            {
                yield return Invariant($"left:{Left(column + 1) - 1}px;top:{top}px;width:1px;height:{bottom - top}px;");
            }
        }

        for (var row = firstRow; row <= lastRow; row++)
        {
            if (sheet.RowHeight(row) > 0)
            {
                yield return Invariant($"left:{left}px;top:{Top(row + 1) - 1}px;width:{right - left}px;height:1px;");
            }
        }
    }

    // A range's outline, clipped to as far as the grid reaches.
    string Outline(SheetRange range)
    {
        var lastRow = Math.Min(range.LastRow, ExtentRows);
        var lastColumn = Math.Min(range.LastColumn, ExtentColumns);
        var left = Left(range.FirstColumn);
        var top = Top(range.FirstRow);
        return Invariant($"left:{left - 1}px;top:{top - 1}px;width:{Left(lastColumn + 1) - left + 1}px;height:{Top(lastRow + 1) - top + 1}px;");
    }

    internal string SelectionStyle => Outline(Selection);

    internal bool ManyCells => Selection is { RowCount: > 1 } or { ColumnCount: > 1 } && Selection != ActiveArea;

    internal string ActiveStyle => Outline(ActiveArea);

    internal string? PointedStyle
    {
        get
        {
            if (Pointed is not var (row, column, lastRow, lastColumn) ||
                editMode == null)
            {
                return null;
            }

            return Outline(new(Math.Min(row, lastRow), Math.Min(column, lastColumn), Math.Max(row, lastRow), Math.Max(column, lastColumn)));
        }
    }

    internal string? MarqueeStyle
    {
        get
        {
            if (clipboard is not var (fromSheet, range, _, _) ||
                fromSheet != sheetIndex)
            {
                return null;
            }

            return Outline(range);
        }
    }

    // The box typed into, over the active cell and set in its font.
    internal string InputStyle
    {
        get
        {
            var area = ActiveArea;
            var left = Left(area.FirstColumn);
            var top = Top(area.FirstRow);
            var width = Math.Max(24, Width(area.FirstColumn, Math.Min(area.LastColumn, ExtentColumns)));
            var height = Math.Max(16, Height(area.FirstRow, Math.Min(area.LastRow, ExtentRows)));
            var look = ActiveLook;
            return Invariant($"left:{left}px;top:{top}px;min-width:{width}px;min-height:{height}px;{Font(look)}{Fill(look)}{Color(look.ColorHex)}");
        }
    }
}
