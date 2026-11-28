using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Turns a worksheet's sparse cell soup into the dense table the layout engine expects.
///
/// Three impedance mismatches have to be bridged. A worksheet stores only the cells that exist,
/// addressed by name (<c>B7</c>), while a <see cref="TableElement"/> is a dense positional grid — so
/// the used range is walked and the gaps filled. Column widths are in "characters" rather than any
/// unit of length. And merges are declared once, in a separate list, for a region the grid must then
/// be told about cell by cell.
/// </summary>
sealed class SheetGridBuilder(CellStyles styles, SharedStrings sharedStrings, string defaultFont, double maxDigitWidthPixels, ExcelRowHeights rowHeights, CultureInfo culture, Func<string, bool, bool, FontMetrics?>? resolveFont = null, ThemeFonts? themeFonts = null)
{
    const double pointsPerPixel = 72.0 / 96.0;

    /// <summary>Excel's default when a sheet declares no width of its own.</summary>
    const double defaultColumnWidthChars = 8.43;

    double? indentPoints;

    /// <summary>
    /// One level of <c>alignment/@indent</c>, in points: three spaces of the body font (the Normal
    /// style's), whatever the cell's own font, and NOT scaled by the print zoom. Probed 2026-11-22 over
    /// COM and Excel's XPS export, five levels each under twelve body fonts: 0.676pt per point of
    /// Calibri, 0.84 of Arial, 0.75 of Times New Roman and 1.05 of Verdana — three times each face's
    /// space — with the cells in Calibri 11, Arial 12 and Arial 24 alike; at 50% and 75% zoom the text
    /// shrank and the step stayed 10.08pt under Arial 12. social-media-editorial-theme-calendar's
    /// five-level "Week 1" prints clipped to "Week" for it, in a sheet fitted to 59%. Every step
    /// measured is a whole number of the 600-dpi printer's dots — 45, 57, 90 and 186 under Calibri 8,
    /// 10, 16 and 33, 84 under Arial 12 — three times the space rounded to a dot: Arial 12's space is
    /// 27.78 dots, so 28, and 84.
    /// </summary>
    double IndentPoints
    {
        get
        {
            if (indentPoints is { } known)
            {
                return known;
            }

            const double dotsPerPoint = 600.0 / 72;
            var (family, size) = styles.DefaultFont;
            var metrics = resolveFont?.Invoke(family ?? defaultFont, false, false);
            return (indentPoints = metrics is { UnitsPerEm: > 0 }
                ? 3 * Math.Round((double) metrics.AdvanceUnits(' ') / metrics.UnitsPerEm * size * dotsPerPoint, MidpointRounding.AwayFromZero) / dotsPerPoint
                : 9).Value;
        }
    }

    /// <summary>
    /// Every row's height in points, as Excel prints the sheet at <paramref name="scale"/> (or, not
    /// <paramref name="printed"/>, as its screen lays the sheet out): the rows <c>sheetData</c>
    /// holds, and the height of a row it leaves out. The grid and <see cref="SheetGeometry"/> both
    /// take their heights from here, so a drawing anchored to a row stands where that row is drawn.
    ///
    /// <list type="bullet">
    /// <item>A row with <c>customHeight</c> is the height it declares.</item>
    /// <item>A row without it is sized by Excel to its tallest font, WHATEVER <c>ht</c> it carries:
    /// Excel sizes such a row again when it opens the file. Probed 2026-11-20: rows declaring
    /// <c>ht="40"</c> without <c>customHeight</c>, text in the body font, came back 13.2pt (Arial
    /// 10's standard height) and printed as standard rows, under a custom sheet default and
    /// without one. It is the same row Excel would make of an edit.</item>
    /// <item>A row the file leaves out is <c>defaultRowHeight</c> only when <c>sheetFormatPr</c>
    /// says <c>customHeight</c>; otherwise Excel ignores the stored default (30, in the same probe)
    /// and gives it the body font's standard height.</item>
    /// </list>
    /// </summary>
    public SheetRows RowHeights(S.Worksheet worksheet, double scale, bool printed)
    {
        if (rowHeightCache.TryGetValue((worksheet, scale, printed), out var cached))
        {
            return cached;
        }

        var measured = MeasureRows(worksheet, scale, printed);
        rowHeightCache[(worksheet, scale, printed)] = measured;
        return measured;
    }

    // the grid, its strips and the drawings over it all ask for one sheet's rows
    readonly Dictionary<(S.Worksheet Worksheet, double Scale, bool Printed), SheetRows> rowHeightCache = [];

    SheetRows MeasureRows(S.Worksheet worksheet, double scale, bool printed)
    {
        int? zoom = printed ? (int) Math.Round(scale * 100, MidpointRounding.AwayFromZero) : null;
        var format = worksheet.SheetFormatProperties;
        var absent = format?.CustomHeight?.Value == true && format.DefaultRowHeight?.Value is { } declared
            ? rowHeights.Declared(declared, zoom)
            : rowHeights.Standard(zoom);

        var rows = new Dictionary<int, double>();
        foreach (var row in worksheet.GetFirstChild<S.SheetData>()?.Elements<S.Row>() ?? [])
        {
            if (row.RowIndex?.Value is not { } index)
            {
                continue;
            }

            // A hidden row prints nothing and takes no room. Probed over COM (Excel 16, A4,
            // 2026-11-22): twelve 30pt rows with 3-5 hidden outright and 8-9 collapsed in an
            // outline group print Row1, Row2, Row6, Row7, Row10 to Row12 at one even pitch
            rows[(int) index] = row.Hidden?.Value == true
                ? 0
                : row.CustomHeight?.Value == true && row.Height?.Value is { } height
                    ? rowHeights.Declared(height, zoom)
                    : rowHeights.Auto(Fonts(row), zoom);
        }

        return new(rows, absent);
    }

    // The font of every cell in a row, which is what Excel sizes the row to: an empty cell counts,
    // since giving one a larger font grows its row in Excel.
    IEnumerable<(string Family, double SizePoints)> Fonts(S.Row row)
    {
        foreach (var cell in row.Elements<S.Cell>())
        {
            var style = styles.Resolve(cell.StyleIndex?.Value ?? 0);
            yield return (style.FontFamily ?? styles.DefaultFont.Family, style.FontSizePoints ?? styles.DefaultFont.SizePoints);
        }
    }

    public TableElement? Build(S.Worksheet worksheet, SheetRange range, double scale, bool printed, (int First, int Last)? titleRows, ConditionalFormats conditional, TableStyles tableStyles, bool horizontallyCentered)
    {
        var sheetData = worksheet.GetFirstChild<S.SheetData>();
        if (sheetData == null || range.IsEmpty)
        {
            return null;
        }

        building = worksheet;
        var merges = MergeMap.For(worksheet, range);
        var widths = ColumnWidths(worksheet, range, scale);
        var rowsByIndex = sheetData.Elements<S.Row>()
            .Where(_ => _.RowIndex?.Value is { } index &&
                        range.ContainsRow((int) index))
            .ToDictionary(_ => (int) _.RowIndex!.Value);

        var heights = RowHeights(worksheet, scale, printed);
        var columnStyles = ColumnStyles(worksheet, range);

        var rows = new List<TableRow>(range.RowCount);
        for (var rowIndex = range.FirstRow; rowIndex <= range.LastRow; rowIndex++)
        {
            rowsByIndex.TryGetValue(rowIndex, out var row);
            var isHeader = titleRows is { } titles &&
                           rowIndex >= titles.First && rowIndex <= titles.Last;
            rows.Add(BuildRow(row, rowIndex, range, merges, widths, heights.Height(rowIndex), scale, isHeader, conditional, tableStyles, columnStyles));
        }

        HandEdgesDown(rows);

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                GridColumnWidths = widths,
                IsAutoFit = false,
                IsSheetGrid = true,
                // printOptions/@horizontalCentered (ECMA-376 §18.3.1.70) centres the print area
                // between the margins, which is what a centre-aligned table already does with its
                // slack. 38 of the 40 corpus workbooks ask for it — it is Excel's template default,
                // not an exotic setting — so ignoring it put every one of their grids hard against
                // the left margin. The caller offsets the sheet's drawings by the same slack.
                Alignment = horizontallyCentered ? TextAlignment.Center : TextAlignment.Left
            }
        };
    }

    TableRow BuildRow(
        S.Row? row,
        int rowIndex,
        SheetRange range,
        MergeMap merges,
        double[] widths,
        double height,
        double scale,
        bool isHeader,
        ConditionalFormats conditional,
        TableStyles tableStyles,
        Dictionary<int, uint> columnStyles)
    {
        var cellsByColumn = row?.Elements<S.Cell>()
            .Select(cell => (Column: CellReference.ColumnOf(cell.CellReference?.Value), Cell: cell))
            .Where(_ => _.Column >= range.FirstColumn &&
                        _.Column <= range.LastColumn)
            .GroupBy(_ => _.Column)
            .ToDictionary(_ => _.Key, _ => _.First().Cell) ?? [];

        // A position the row holds no cell for still prints a style: the row's, where the row says
        // customFormat, else its column's. address-and-phone-list fills every column cream through
        // col/@style and writes cells only where there is text, and its margins and the gaps of its
        // title row printed white.
        var rowStyle = row is { CustomFormat.Value: true, StyleIndex.Value: var ownStyle } ? ownStyle : (uint?) null;
        for (var column = range.FirstColumn; column <= range.LastColumn; column++)
        {
            if (!cellsByColumn.ContainsKey(column) &&
                (rowStyle ?? columnStyles.GetValueOrDefault(column)) is var implied and not 0)
            {
                cellsByColumn[column] = new() { StyleIndex = implied };
            }
        }

        var cells = new List<TableCell>();
        for (var column = range.FirstColumn; column <= range.LastColumn; column++)
        {
            // A cell swallowed by a merge to its LEFT contributes nothing: the anchor's GridSpan
            // already covers this position, and emitting it would push the row a column too wide.
            if (merges.IsCoveredHorizontally(rowIndex, column))
            {
                continue;
            }

            cellsByColumn.TryGetValue(column, out var found);
            var merge = merges.At(rowIndex, column);
            var overflow = OverflowSpan(found, merge, cellsByColumn, merges, rowIndex, column, range, tableStyles);

            var width = 0d;
            for (var span = 0; span < Math.Max(merge.ColumnSpan, overflow); span++)
            {
                var index = column - range.FirstColumn + span;
                if (index < widths.Length)
                {
                    width += widths[index];
                }
            }

            cells.Add(BuildCell(
                found,
                overflow > merge.ColumnSpan ? merge with { ColumnSpan = overflow } : merge,
                width,
                Spill(found, merge, overflow, cellsByColumn, merges, widths, rowIndex, column, range, width),
                scale,
                conditional,
                tableStyles,
                rowIndex,
                column));

            column += Math.Max(merge.ColumnSpan, overflow) - 1;
        }

        return new()
        {
            Cells = cells,
            IsHeader = isHeader,
            // Excel's own height for the row (RowHeights); an auto-height row still grows past it
            // to wrapped text
            HeightPoints = height,
            // customHeight="1" means the author fixed the height, and Excel then CLIPS wrapped text
            // to it rather than growing the row. Treating every height as a floor instead let one
            // sheet of paragraph-length text in a 2.6-character column grow into six blank pages,
            // where Excel renders a single page and simply shows almost none of that text. Only an
            // auto-height row (no customHeight) grows to its content. A hidden row is pinned at the
            // nothing it prints (MeasureRows), or its text would grow it back.
            IsExactHeight = row?.CustomHeight?.Value == true || row?.Hidden?.Value == true
        };
    }

    /// <summary>
    /// Excel draws a cell's edges over every fill, its neighbours' included, where the engine paints a
    /// row's fills over the row above it, and a bottom edge hangs into the row below: simple-blue-
    /// timesheet-invoice's rule under Tax vanished under the white of the Total row. A bottom edge whose
    /// cell beneath states no top edge of its own is handed down to be that cell's top, which it paints
    /// after its fill. Only a cell beneath that starts and ends where the cell above does takes it.
    /// </summary>
    static void HandEdgesDown(List<TableRow> rows)
    {
        for (var index = 0; index + 1 < rows.Count; index++)
        {
            var below = Starts(rows[index + 1]);
            var cells = rows[index].Cells.ToList();
            var lower = rows[index + 1].Cells.ToList();
            var changed = false;
            var column = 0;
            for (var position = 0; position < cells.Count; position++)
            {
                var cell = cells[position];
                var span = cell.Properties.GridSpan;
                if (cell.Properties.Borders is { Bottom.IsVisible: true } borders &&
                    cell.Properties.VerticalMerge == VerticalMergeType.None &&
                    Beneath(lower, below, column, span) is { } matches)
                {
                    var edge = borders.Bottom;
                    cells[position] = Retouched(cell, borders with { Bottom = BorderEdge.None });
                    foreach (var match in matches)
                    {
                        lower[match] = Retouched(lower[match], (lower[match].Properties.Borders ?? new()) with { Top = edge });
                    }

                    changed = true;
                }

                column += span;
            }

            if (changed)
            {
                rows[index] = Rebuilt(rows[index], cells);
                rows[index + 1] = Rebuilt(rows[index + 1], lower);
            }
        }

        // The cells beneath a cell above, when they start where it starts and end where it ends —
        // one cell or several: simple-basic-pink-blue-timesheet's "Email" runs across C and D, which
        // makes its cell span the three, and its rule under the three vanished under the spacer row's
        // fill. Null when they do not line up, or one of them states a top edge or is merged.
        static List<int>? Beneath(List<TableCell> lower, Dictionary<int, int> below, int column, int span)
        {
            var matches = new List<int>();
            var at = column;
            while (at < column + span)
            {
                if (!below.TryGetValue(at, out var match) ||
                    lower[match].Properties.VerticalMerge != VerticalMergeType.None ||
                    lower[match].Properties.Borders is { Top.IsVisible: true })
                {
                    return null;
                }

                matches.Add(match);
                at += lower[match].Properties.GridSpan;
            }

            return at == column + span ? matches : null;
        }

        static Dictionary<int, int> Starts(TableRow row)
        {
            Dictionary<int, int> starts = [];
            var column = 0;
            for (var position = 0; position < row.Cells.Count; position++)
            {
                starts[column] = position;
                column += row.Cells[position].Properties.GridSpan;
            }

            return starts;
        }

        static TableCell Retouched(TableCell cell, CellBorders borders) =>
            new()
            {
                Content = cell.Content,
                Floats = cell.Floats,
                FloatAnchorParagraphOrdinals = cell.FloatAnchorParagraphOrdinals,
                Properties = cell.Properties with { Borders = borders }
            };

        static TableRow Rebuilt(TableRow row, List<TableCell> cells) =>
            new()
            {
                Cells = cells,
                IsHeader = row.IsHeader,
                HeightPoints = row.HeightPoints,
                IsExactHeight = row.IsExactHeight
            };
    }

    // The worksheet being built, for the widths of a merge's columns outside the range (CutWidth)
    S.Worksheet? building;

    /// <summary>The width in points of a merge's columns a strip cuts off, at the sheet's scale.</summary>
    double CutWidth((int First, int Count) cut, double scale) =>
        cut.Count <= 0 || building == null
            ? 0
            : ColumnWidths(building, new(1, cut.First, 1, cut.First + cut.Count - 1), scale).Sum();

    /// <summary>The style each column of the range gives a position no cell is written for (<c>col/@style</c>).</summary>
    static Dictionary<int, uint> ColumnStyles(S.Worksheet worksheet, SheetRange range)
    {
        Dictionary<int, uint> found = [];
        foreach (var column in worksheet.GetFirstChild<S.Columns>()?.Elements<S.Column>() ?? [])
        {
            if (column.Style?.Value is not { } style ||
                style == 0)
            {
                continue;
            }

            var first = Math.Max((int) (column.Min?.Value ?? 0), range.FirstColumn);
            var last = Math.Min((int) (column.Max?.Value ?? 0), range.LastColumn);
            for (var index = first; index <= last; index++)
            {
                found[index] = style;
            }
        }

        return found;
    }

    /// <summary>
    /// How many columns an unwrapped cell's text is allowed to run across.
    ///
    /// Excel does not clip text at its column: while the cells to the right are EMPTY, the text
    /// simply runs on over them, which is how a title in a narrow column or an instruction sheet
    /// built on one 2.6-character column reads at all. Clipping to the column instead renders those
    /// pages blank. The span stops at the first cell holding anything, at a merge, and at the range
    /// edge.
    ///
    /// Restricted to left-aligned text on purpose. Excel overflows centred and right-aligned text in
    /// both directions, and widening the cell would re-centre a short label that Excel leaves put —
    /// a visible regression in exchange for a rarer case.
    /// </summary>
    int OverflowSpan(
        S.Cell? cell,
        MergeInfo merge,
        Dictionary<int, S.Cell> cellsByColumn,
        MergeMap merges,
        int rowIndex,
        int column,
        SheetRange range,
        TableStyles tableStyles)
    {
        if (cell == null || merge.ColumnSpan > 1)
        {
            return merge.ColumnSpan;
        }

        var style = styles.Resolve(cell.StyleIndex?.Value ?? 0);
        if (style.WrapText || (style.HorizontalAlignment ?? DefaultAlignment(cell)) != TextAlignment.Left)
        {
            return merge.ColumnSpan;
        }

        if (Value(cell, style).Text.Length == 0)
        {
            return merge.ColumnSpan;
        }

        var span = 1;
        for (var next = column + 1; next <= range.LastColumn; next++)
        {
            if (merges.IsCoveredHorizontally(rowIndex, next) || merges.At(rowIndex, next).ColumnSpan > 1)
            {
                break;
            }

            if (cellsByColumn.TryGetValue(next, out var neighbour) &&
                Value(neighbour, styles.Resolve(neighbour.StyleIndex?.Value ?? 0)).Text.Length > 0)
            {
                break;
            }

            // Text running over a ruled line or into another fill does not take either away:
            // simple-list's "Item 1" runs into empty cells whose table draws a rule between each,
            // and its "Column 5" header out of the table, whose fill stops at the table's edge in
            // Excel's print. The clip still spills past the box, so the text draws on as before.
            if (RuleBetween(cellsByColumn, tableStyles, rowIndex, next - 1, next))
            {
                break;
            }

            span++;
        }

        return span;
    }

    /// <summary>
    /// How far a cell's clip may reach OUTSIDE its own box, in points, as (left, right).
    ///
    /// The clip stops overflowing text at the first occupied neighbour, so it has to know where the
    /// empty ones end — and Excel spills in the direction the alignment implies, not always
    /// rightwards. LEFT-aligned text has had its box widened by <see cref="OverflowSpan"/> over the
    /// empty right-hand neighbours up to the first rule or change of fill, so it reaches only past
    /// THAT: the empty run beyond the box (<paramref name="overflow"/> is the box's span), none when
    /// the span stopped at a filled cell, a merge or the range edge. RIGHT-aligned text spills LEFT (the line ends at
    /// the cell's right edge and grows backwards) and CENTRED text spills both ways, and neither can
    /// be modelled by widening: the cell would re-anchor, moving every short label that fits.
    ///
    /// Wrapped text never overflows at all — it breaks instead — so it reaches nowhere.
    /// </summary>
    (double Left, double Right) Spill(
        S.Cell? cell,
        MergeInfo merge,
        int overflow,
        Dictionary<int, S.Cell> cellsByColumn,
        MergeMap merges,
        double[] widths,
        int rowIndex,
        int column,
        SheetRange range,
        double widthPoints)
    {
        // A HIDDEN column is zero-width, and Excel shows nothing of what it holds — not even the
        // overflow the same text would produce one column over. Reaching out of a box with no width
        // is how check-register's hidden column B leaked its "√" into the empty column A beside it,
        // a glyph Word's reference does not have.
        if (cell == null || widthPoints <= 0)
        {
            return (0, 0);
        }

        var style = styles.Resolve(cell.StyleIndex?.Value ?? 0);
        if (style.WrapText || Value(cell, style).Text.Length == 0)
        {
            return (0, 0);
        }

        var alignment = style.HorizontalAlignment ?? DefaultAlignment(cell);
        if (alignment == TextAlignment.Right)
        {
            return (EmptyRunPoints(cellsByColumn, merges, widths, rowIndex, column, range, step: -1), 0);
        }

        if (alignment == TextAlignment.Center)
        {
            // The right-hand scan starts past the merge, which is the cell's own last column.
            var last = column + merge.ColumnSpan - 1;
            return (
                EmptyRunPoints(cellsByColumn, merges, widths, rowIndex, column, range, step: -1),
                EmptyRunPoints(cellsByColumn, merges, widths, rowIndex, last, range, step: 1));
        }

        // Left-aligned text has taken the empty columns up to the first rule or change of fill as
        // its box (OverflowSpan); past that it still runs on over the empty ones, drawn across the
        // neighbour's fill rather than taking it. back-to-school-planner's sheet titles stand in
        // unfilled cells of a workbook whose Normal style is filled white, so every empty cell
        // beside them is a "different fill", and "Budget breakdown" was cut off at column B where
        // Excel's print runs it on over C.
        return (0, EmptyRunPoints(cellsByColumn, merges, widths, rowIndex, column + Math.Max(merge.ColumnSpan, overflow) - 1, range, step: 1));
    }

    /// <summary>
    /// Total width, in points, of the unbroken run of EMPTY columns starting one step from
    /// <paramref name="column"/> in the direction <paramref name="step"/>. Stops at the first column
    /// holding anything, at any merge, and at the range edge — the same three walls
    /// <see cref="OverflowSpan"/> stops at.
    /// </summary>
    double EmptyRunPoints(
        Dictionary<int, S.Cell> cellsByColumn,
        MergeMap merges,
        double[] widths,
        int rowIndex,
        int column,
        SheetRange range,
        int step)
    {
        var total = 0d;
        for (var next = column + step; next >= range.FirstColumn && next <= range.LastColumn; next += step)
        {
            if (merges.IsCoveredHorizontally(rowIndex, next) || merges.At(rowIndex, next).ColumnSpan > 1)
            {
                break;
            }

            if (cellsByColumn.TryGetValue(next, out var neighbour) &&
                Value(neighbour, styles.Resolve(neighbour.StyleIndex?.Value ?? 0)).Text.Length > 0)
            {
                break;
            }

            var index = next - range.FirstColumn;
            if (index < 0 || index >= widths.Length)
            {
                break;
            }

            total += widths[index];
        }

        return total;
    }

    TableCell BuildCell(
        S.Cell? cell,
        MergeInfo merge,
        double widthPoints,
        (double Left, double Right) spill,
        double scale,
        ConditionalFormats conditional,
        TableStyles tableStyles,
        int rowIndex,
        int column)
    {
        var style = styles.Resolve(cell?.StyleIndex?.Value ?? 0);
        var (text, colorOverride) = Value(cell, style);

        // A table's style lies under the cell's own formatting, where what the cell has is its own
        // rather than the Normal style's: its colour shows on a cell whose font keeps the body
        // colour, its fill where the cell's is Normal's (back-to-school-planner's Normal fills white,
        // and Excel prints the table's grey stripes over it), its edges where the cell draws none.
        var table = tableStyles.IsEmpty ? null : tableStyles.For(rowIndex, column);
        var normal = styles.Resolve(0);
        var tableColor = table?.ColorHex is { } stated && style.ColorHex == normal.ColorHex ? stated : null;

        // A conditional rule overlays the cell's own style rather than replacing it, so each member
        // falls back to what the style already resolved.
        var overlay = conditional.IsEmpty
            ? null
            : conditional.For(rowIndex, column, text, Number(cell));

        // A data bar draws in the cell, and hides the value when its rule says showValue="0":
        // to-do-list's progress cell is the bar alone
        var dataBar = conditional.IsEmpty ? null : conditional.DataBarFor(rowIndex, column, Number(cell));
        // So does an icon set's "show icon only"; a place given no icon (NoIcons) still hides it
        var icon = conditional.IsEmpty ? null : conditional.IconFor(rowIndex, column, Number(cell));
        if (dataBar is { ShowValue: false } ||
            icon is { ShowValue: false })
        {
            text = string.Empty;
        }

        var properties = new RunProperties
        {
            FontFamily = style.FontFamily ?? styles.DefaultFont.Family ?? defaultFont,
            FontSizePoints = (style.FontSizePoints ?? styles.DefaultFont.SizePoints) * scale,
            Bold = style.Bold || overlay?.Bold == true || table?.Bold == true,
            Italic = style.Italic || overlay?.Italic == true || table?.Italic == true,
            Underline = style.Underline,
            Strikethrough = overlay?.Strikethrough ?? style.Strikethrough,
            ColorHex = overlay?.ColorHex ?? colorOverride ?? tableColor ?? style.ColorHex
        };

        // A *x fill: the text before it set against the cell's left edge and the rest against its right,
        // by a right tab at the far side of the cell (FillLayout)
        var fill = text.IndexOf(NumberFormat.FillMark);
        Run[] runs;
        if (text.Length == 0)
        {
            runs = [];
        }
        else if (fill >= 0)
        {
            var after = text[(fill + 1)..].Replace(NumberFormat.FillMark.ToString(), string.Empty, StringComparison.Ordinal);
            runs = new[]
                {
                    new Run { Text = text[..fill], Properties = properties },
                    new Run { Text = "\t", IsTab = true, Properties = properties },
                    new Run { Text = after, Properties = properties }
                }
                .Where(_ => _.Text.Length > 0)
                .ToArray();
        }
        else if (RichRuns(cell) is { } rich)
        {
            runs = rich
                .Select(_ => new Run { Text = _.Text?.Text ?? string.Empty, Properties = RichProperties(_.RunProperties, properties, overlay?.ColorHex, scale) })
                .Where(_ => _.Text.Length > 0)
                .ToArray();
        }
        else
        {
            runs = [new() { Text = text, Properties = properties }];
        }

        // Excel's default is right for numbers and left for text; an explicit alignment wins. A fill
        // spans the cell whatever the alignment says — Excel sets "$" at the left and "55.00" at the
        // right of back-to-school-planner's accounting cells, where they printed together at the
        // right. The tab runs the line to the cell's far side, and is set from the left: a right-aligned
        // line moves by its own width, which pushed the number out of the cell.
        var alignment = fill >= 0 ? TextAlignment.Left : style.HorizontalAlignment ?? DefaultAlignment(cell);
        var paragraph = new ParagraphElement
        {
            Runs = runs,
            Properties = new()
            {
                Alignment = alignment,
                TabStops = fill >= 0
                    ? [new() { PositionPoints = Math.Max(0, widthPoints - 4 * scale), Alignment = TabAlignment.Right }]
                    : [],
                // Excel indents from the side the text is set against: a right-aligned cell's indent
                // stands it in from the right (modern-basic-black-yellow-standard's prices and totals)
                // A merge cut by the strip's edge lays its text out over the whole merge, the part past
                // the edge cut off by the cell's clip: Excel centres to-do-list's title over B:L though
                // its first page ends at K, where centring over B:K put it 39px left
                LeftIndentPoints = (alignment == TextAlignment.Right ? 0 : style.IndentLevel * IndentPoints) - CutWidth(merge.CutBefore, scale),
                RightIndentPoints = (alignment == TextAlignment.Right ? style.IndentLevel * IndentPoints : 0) - CutWidth(merge.CutAfter, scale),
                SpacingBeforePoints = 0,
                SpacingAfterPoints = 0,
                LineSpacingMultiplier = 1,
                WidowControl = false
            }
        };

        return new()
        {
            Content = [paragraph],
            Properties = new()
            {
                WidthPoints = widthPoints,
                BackgroundColorHex = overlay?.BackgroundColorHex ?? OwnFill(style, table),
                Borders = table is { } drawn ? Under(style.Borders, drawn) : style.Borders,
                GridSpan = merge.ColumnSpan,
                VerticalMerge = merge.VerticalMerge,
                VerticalAlignment = style.VerticalAlignment,
                // Wrapping is OPT-IN in a spreadsheet: without alignment/@wrapText Excel keeps the
                // text on one line and lets it overflow (into empty neighbours) or clip, where a Word
                // cell would break it. The engine's default is Word's, so every cell whose text ran a
                // hair past its column split in two and painted over the row below — "United States"
                // measures 60.03pt against a 63.75pt column at Arial 10.
                SingleLine = !style.WrapText,
                // Excel stops a cell's ink at the first OCCUPIED neighbour, so the box plus whatever
                // empty columns Spill found is the drawing area and the clip is exactly that. It
                // bounds the cell vertically too, which is the other half of a customHeight row:
                // Excel shows only as much wrapped text as the pinned height holds (see
                // IsExactHeight above), where the engine drew the rest over the row below.
                ClipOverflow = true,
                ClipSpillLeftPoints = spill.Left,
                ClipSpillRightPoints = spill.Right,
                DataBar = dataBar is { } bar ? new(bar.Fraction, bar.ColorHex, scale) : null,
                Icon = icon is { Id: >= 0 } shown
                    // An icon alone follows an alignment the cell states (inventory-list-with-highlighting's
                    // centred flags); under General it stands at the left, as back-to-school-planner's
                    // checks do, where the number it replaces would have stood at the right
                    ? new(shown.Set, shown.Id, shown.ShowValue ? TextAlignment.Left : style.HorizontalAlignment ?? TextAlignment.Left, style.VerticalAlignment, scale)
                    : null,
                Padding = new(0, 2 * scale, 0, 2 * scale)
            }
        };
    }

    /// <summary>Whether the cells either side of a column boundary draw a line along it or differ in fill.</summary>
    bool RuleBetween(Dictionary<int, S.Cell> cellsByColumn, TableStyles tableStyles, int rowIndex, int left, int right)
    {
        (CellBorders Borders, string? Fill) Look(int column)
        {
            var own = styles.Resolve(cellsByColumn.TryGetValue(column, out var cell) ? cell.StyleIndex?.Value ?? 0 : 0);
            return tableStyles.IsEmpty || tableStyles.For(rowIndex, column) is not { } table
                ? (own.Borders, own.BackgroundColorHex)
                : (Under(own.Borders, table), OwnFill(own, table));
        }

        var (leftBorders, leftFill) = Look(left);
        var (rightBorders, rightFill) = Look(right);
        return leftBorders.Right.IsVisible ||
               rightBorders.Left.IsVisible ||
               !string.Equals(leftFill, rightFill, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The cell's fill, or its table's where the cell keeps the Normal style's.</summary>
    string? OwnFill(CellStyle style, TableStyleOverlay? table) =>
        table?.BackgroundColorHex is { } stated && style.BackgroundColorHex == styles.Resolve(0).BackgroundColorHex
            ? stated
            : style.BackgroundColorHex;

    /// <summary>The cell's own edges, with the table's where the cell draws none.</summary>
    static CellBorders Under(CellBorders own, TableStyleOverlay table) =>
        own with
        {
            Left = own.Left.IsVisible ? own.Left : table.Left ?? own.Left,
            Right = own.Right.IsVisible ? own.Right : table.Right ?? own.Right,
            Top = own.Top.IsVisible ? own.Top : table.Top ?? own.Top,
            Bottom = own.Bottom.IsVisible ? own.Bottom : table.Bottom ?? own.Bottom
        };

    /// <summary>The cell's numeric value, for the conditional rules that compare against one.</summary>
    static double? Number(S.Cell? cell)
    {
        if (cell?.DataType?.Value == null &&
                double.TryParse(cell?.CellValue?.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// The cell's display text, plus any colour the number format's own section demands (the
    /// <c>[Red]</c> on an accounting negative, which the cell's style knows nothing about).
    /// </summary>
    (string Text, string? ColorHex) Value(S.Cell? cell, CellStyle style)
    {
        if (cell == null)
        {
            return (string.Empty, null);
        }

        var raw = cell.CellValue?.Text;
        var type = cell.DataType?.Value;

        if (type == S.CellValues.SharedString)
        {
            return (int.TryParse(raw, out var index) ? sharedStrings.Get(index) : string.Empty, null);
        }

        if (type == S.CellValues.InlineString)
        {
            var inline = cell.GetFirstChild<S.InlineString>();
            return (inline == null ? string.Empty : SharedStrings.Flatten(inline), null);
        }

        if (type == S.CellValues.Boolean)
        {
            return (raw == "1" ? "TRUE" : "FALSE", null);
        }

        if (type == S.CellValues.String || type == S.CellValues.Error)
        {
            return (raw ?? string.Empty, null);
        }

        if (string.IsNullOrEmpty(raw))
        {
            // A formula cell with no cached value: Excel recalculates on open, and there is no
            // evaluator here, so the cell renders empty rather than showing a stale or invented one.
            return (string.Empty, null);
        }

        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return (raw, null);
        }

        var formatted = NumberFormat.FormatWithFill(number, NumberFormat.Localize(style.EffectiveFormatCode, culture));
        return (formatted.Text, formatted.ColorHex);
    }

    /// <summary>The formatted runs of a cell holding rich text, or null for plain text or a value.</summary>
    IReadOnlyList<S.Run>? RichRuns(S.Cell? cell)
    {
        var type = cell?.DataType?.Value;
        if (type == S.CellValues.SharedString)
        {
            return int.TryParse(cell!.CellValue?.Text, out var index) ? sharedStrings.RunsOf(index) : null;
        }

        if (type == S.CellValues.InlineString &&
            cell!.GetFirstChild<S.InlineString>() is { } inline)
        {
            return SharedStrings.Runs(inline);
        }

        return null;
    }

    /// <summary>
    /// A rich run's look. A run that carries <c>rPr</c> is set in that font whole — Excel writes every
    /// member, and one it leaves out (no <c>b</c>) is off rather than the cell's — while a run without
    /// one keeps the cell's. business-plan-checklist's "Strengths:" is a bold run before a regular one
    /// in a regular cell. A conditional format's colour still wins over the run's, as over the cell's.
    /// </summary>
    RunProperties RichProperties(S.RunProperties? run, RunProperties cell, string? conditionalColor, double scale)
    {
        if (run == null)
        {
            return cell;
        }

        static bool On(OpenXmlLeafElement? element) =>
            element switch
            {
                null => false,
                S.Bold bold => bold.Val?.Value != false,
                S.Italic italic => italic.Val?.Value != false,
                S.Strike strike => strike.Val?.Value != false,
                _ => true
            };

        var underline = run.GetFirstChild<S.Underline>();
        var vertical = run.GetFirstChild<S.VerticalTextAlignment>()?.Val?.Value;
        return cell with
        {
            // A run in a theme font is set in the theme's face, whatever rFont caches:
            // inventory-list-accessibility-guide's runs name Gill Sans Nova over a theme of Gill Sans MT
            FontFamily = run.GetFirstChild<S.FontScheme>()?.Val?.Value is { } scheme && themeFonts != null && scheme != S.FontSchemeValues.None
                ? scheme == S.FontSchemeValues.Major ? themeFonts.MajorFont : themeFonts.MinorFont
                : run.GetFirstChild<S.RunFont>()?.Val?.Value ?? cell.FontFamily,
            FontSizePoints = run.GetFirstChild<S.FontSize>()?.Val?.Value * scale ?? cell.FontSizePoints,
            Bold = On(run.GetFirstChild<S.Bold>()),
            Italic = On(run.GetFirstChild<S.Italic>()),
            Strikethrough = On(run.GetFirstChild<S.Strike>()),
            Underline = underline != null && underline.Val?.Value != S.UnderlineValues.None,
            ColorHex = conditionalColor ?? (run.GetFirstChild<S.Color>() is { } color ? styles.Resolve(color) : null),
            VerticalAlignment = vertical == S.VerticalAlignmentRunValues.Superscript
                ? VerticalRunAlignment.Superscript
                : vertical == S.VerticalAlignmentRunValues.Subscript
                    ? VerticalRunAlignment.Subscript
                    : VerticalRunAlignment.Baseline
        };
    }

    static TextAlignment DefaultAlignment(S.Cell? cell)
    {
        if (cell?.DataType?.Value is { } type &&
                (type == S.CellValues.SharedString || type == S.CellValues.InlineString || type == S.CellValues.String))
        {
            return TextAlignment.Left;
        }

        if (cell?.CellValue == null)
        {
            return TextAlignment.Left;
        }

        return TextAlignment.Right;
    }

    /// <summary>
    /// Whether the cell prints something of its own: a formula, a number, or text that is not all
    /// white space. to-do-list's M1 holds a single space, and Excel prints no page for it.
    /// </summary>
    public bool ShowsValue(S.Cell cell)
    {
        if (cell.CellFormula != null)
        {
            return true;
        }

        if (cell.InlineString != null)
        {
            return !string.IsNullOrWhiteSpace(SharedStrings.Flatten(cell.InlineString));
        }

        if (cell.CellValue?.Text is not {Length: > 0} raw)
        {
            return false;
        }

        return cell.DataType?.Value != S.CellValues.SharedString ||
               !int.TryParse(raw, out var index) ||
               !string.IsNullOrWhiteSpace(sharedStrings.Get(index));
    }

    /// <summary>
    /// How far text that flows to the right runs from its cell's left edge, in points: the cell's
    /// padding and the text's measured width in its own font. Null for what cannot overflow —
    /// a number (Excel shows #s), wrapped text, text aligned anything but left, an empty cell — or
    /// when the font will not resolve to measure with.
    /// </summary>
    public double? OverflowPoints(S.Cell cell)
    {
        if (resolveFont == null ||
            CellText(cell) is not { } text ||
            string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var style = styles.Resolve(cell.StyleIndex?.Value ?? 0);
        if (style.WrapText ||
            style.HorizontalAlignment is not (null or TextAlignment.Left))
        {
            return null;
        }

        var (bodyFamily, bodySize) = styles.DefaultFont;
        var family = style.FontFamily ?? bodyFamily;
        var size = style.FontSizePoints ?? bodySize;
        if (resolveFont(family, style.Bold, style.Italic) is not { UnitsPerEm: > 0 } metrics)
        {
            return null;
        }

        var units = 0L;
        foreach (var character in text)
        {
            units += metrics.AdvanceUnits(character);
        }

        return 2 + style.IndentLevel * IndentPoints + (double) units / metrics.UnitsPerEm * size;
    }

    /// <summary>The text a cell holds as a string, or null when it holds a number, a boolean or nothing.</summary>
    string? CellText(S.Cell cell)
    {
        var raw = cell.CellValue?.Text;
        var type = cell.DataType?.Value;
        if (type == S.CellValues.SharedString)
        {
            return int.TryParse(raw, out var index) ? sharedStrings.Get(index) : null;
        }

        if (type == S.CellValues.InlineString)
        {
            return cell.InlineString == null ? null : SharedStrings.Flatten(cell.InlineString);
        }

        return type == S.CellValues.String ? raw : null;
    }

    /// <summary>
    /// Column widths in points, from the <c>cols</c> runs. Each <c>col</c> covers an inclusive
    /// <c>min..max</c> span rather than a single column, so the runs are expanded across the range.
    /// </summary>
    public double[] ColumnWidths(S.Worksheet worksheet, SheetRange range, double scale)
    {
        var declared = worksheet.GetFirstChild<S.Columns>()?.Elements<S.Column>().ToArray() ?? [];
        var fallback = worksheet.SheetFormatProperties?.DefaultColumnWidth?.Value ?? defaultColumnWidthChars;

        var widths = new double[range.ColumnCount];
        for (var i = 0; i < widths.Length; i++)
        {
            var column = range.FirstColumn + i;
            var match = declared.FirstOrDefault(_ =>
                _.Min?.Value <= column &&
                _.Max?.Value >= column);

            // A hidden column occupies no width at all rather than its declared one.
            var chars = match?.Hidden?.Value == true
                ? 0
                : match?.Width?.Value ?? fallback;

            widths[i] = ToPoints(chars) * scale;
        }

        return widths;
    }

    /// <summary>
    /// The sheet's unscaled width, which is what the fit-to-page factor is computed against before
    /// any grid is built.
    /// </summary>
    public double NaturalWidthPoints(S.Worksheet worksheet, SheetRange range) =>
        ColumnWidths(worksheet, range, 1).Sum();

    /// <summary>
    /// The sheet's unscaled height, for the fit-to-height half of the scale: its rows as Excel prints
    /// them at 100%. A row that would grow to wrapped text is not known until layout, and Excel does
    /// not fit against that either.
    /// </summary>
    public double NaturalHeightPoints(S.Worksheet worksheet, SheetRange range)
    {
        var heights = RowHeights(worksheet, 1, printed: true);
        var total = 0d;
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            total += heights.Height(row);
        }

        return total;
    }

    /// <summary>
    /// A declared <c>col/@width</c> as a length: a plain multiple of the max digit width, nothing
    /// added and nothing rounded. The 5px cell padding is ALREADY IN the stored width — ECMA-376
    /// §18.3.1.13 writes it <c>(chars*MDW + 5)/MDW</c> — so adding 5 again double-counts it. Probed
    /// at A4 across six faces with printed gridlines (see MaxDigitWidth): the fitted padding
    /// measures −0.22 to +0.22px, i.e. zero.
    /// </summary>
    double ToPoints(double characters)
    {
        if (characters <= 0)
        {
            return 0;
        }

        return characters * maxDigitWidthPixels * pointsPerPixel;
    }

    /// <summary>
    /// Excel's column-width unit: the widest of the digits 0-9 in the workbook's body font, at
    /// Excel's own 96 DPI reference (ECMA-376 §18.3.1.13).
    ///
    /// Both halves of that were probed at A4, six faces, column boundaries read off printed
    /// gridlines. The advance is EXACT, never rounded to whole pixels: Excel's fitted unit is 7.519
    /// for Calibri 11, 7.370 for Arial 10, 7.685 for Corbel 11, 8.963 for Georgia 11, 8.000 for
    /// Segoe UI 11 and 8.167 for Consolas 11, against max-digit advances of 7.434 / 7.415 / 7.691 /
    /// 9.002 / 7.906 / 8.064 — ratios 0.994 to 1.020. The earlier whole-pixel rounding cost up to
    /// 5.8% per column, worst on the faces that round UP. And it is the MAXIMUM digit rather than
    /// the zero, which shows only on a face with proportional figures: of the six probed, only
    /// Corbel has a digit wider than its zero (7.691 against 7.534), and taking the maximum moves
    /// its predicted width from 2.0% wide to 0.1%.
    ///
    /// An earlier reading of this taken against a LETTER printer showed a phantom ~8% shrink,
    /// because Excel reports the A4 it was asked for while silently exporting the driver's paper.
    /// Verify the paper by rendering a fixture and checking for a 1123x794 page, never with
    /// Get-PrintConfiguration.
    /// </summary>
    public static double MaxDigitWidth(Func<string, bool, bool, FontMetrics?> resolveFont, string family, double sizePoints)
    {
        // Calibri 11's own value, for a face that will not resolve at all.
        const double calibriFallback = 7.434;
        if (resolveFont(family, false, false) is not { UnitsPerEm: > 0 } metrics)
        {
            return calibriFallback;
        }

        var units = 0;
        for (var digit = '0'; digit <= '9'; digit++)
        {
            units = Math.Max(units, metrics.AdvanceUnits(digit));
        }

        if (units <= 0)
        {
            return calibriFallback;
        }

        return (double) units / metrics.UnitsPerEm * sizePoints / pointsPerPixel;
    }
}
