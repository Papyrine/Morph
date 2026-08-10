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
sealed class SheetGridBuilder(CellStyles styles, SharedStrings sharedStrings, string defaultFont)
{
    /// <summary>
    /// Excel's column width counts '0' glyphs of the workbook's body font, so converting to a real
    /// width needs that glyph's advance. 7px is the value for the 11pt Calibri-class fonts every
    /// corpus workbook uses, at Excel's own 96 DPI reference; the extra 5px is the cell's fixed
    /// padding (ECMA-376 §18.3.1.13).
    /// </summary>
    const double maxDigitWidthPixels = 7;
    const double cellPaddingPixels = 5;
    const double pointsPerPixel = 72.0 / 96.0;

    /// <summary>Excel's default when a sheet declares no width of its own.</summary>
    const double defaultColumnWidthChars = 8.43;

    /// <summary>Excel's default row height for an 11pt body font.</summary>
    const double defaultRowHeightPoints = 15;

    public TableElement? Build(S.Worksheet worksheet, SheetRange range, double scale, (int First, int Last)? titleRows)
    {
        var sheetData = worksheet.GetFirstChild<S.SheetData>();
        if (sheetData == null || range.IsEmpty)
        {
            return null;
        }

        var merges = MergeMap.For(worksheet, range);
        var widths = ColumnWidths(worksheet, range, scale);
        var rowsByIndex = sheetData.Elements<S.Row>()
            .Where(_ => _.RowIndex?.Value is { } index && range.ContainsRow((int) index))
            .ToDictionary(_ => (int) _.RowIndex!.Value);

        var defaultHeight = (worksheet.SheetFormatProperties?.DefaultRowHeight?.Value ?? defaultRowHeightPoints) * scale;

        var rows = new List<TableRow>(range.RowCount);
        for (var rowIndex = range.FirstRow; rowIndex <= range.LastRow; rowIndex++)
        {
            rowsByIndex.TryGetValue(rowIndex, out var row);
            var isHeader = titleRows is { } titles && rowIndex >= titles.First && rowIndex <= titles.Last;
            rows.Add(BuildRow(row, rowIndex, range, merges, widths, defaultHeight, scale, isHeader));
        }

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                GridColumnWidths = widths,
                IsAutoFit = false
            }
        };
    }

    TableRow BuildRow(
        S.Row? row,
        int rowIndex,
        SheetRange range,
        MergeMap merges,
        double[] widths,
        double defaultHeight,
        double scale,
        bool isHeader)
    {
        var cellsByColumn = row?.Elements<S.Cell>()
            .Select(cell => (Column: CellReference.ColumnOf(cell.CellReference?.Value), Cell: cell))
            .Where(_ => _.Column >= range.FirstColumn && _.Column <= range.LastColumn)
            .GroupBy(_ => _.Column)
            .ToDictionary(_ => _.Key, _ => _.First().Cell) ?? [];

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
            var overflow = OverflowSpan(found, merge, cellsByColumn, merges, rowIndex, column, range);

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
                scale));

            column += Math.Max(merge.ColumnSpan, overflow) - 1;
        }

        return new()
        {
            Cells = cells,
            IsHeader = isHeader,
            HeightPoints = row?.Height?.Value is { } height ? height * scale : defaultHeight,
            // A row grows to fit wrapped text even when it declares a height, so the declared value
            // is a floor rather than a clamp.
            IsExactHeight = false
        };
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
        SheetRange range)
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

            span++;
        }

        return span;
    }

    TableCell BuildCell(S.Cell? cell, MergeInfo merge, double widthPoints, double scale)
    {
        var style = styles.Resolve(cell?.StyleIndex?.Value ?? 0);
        var (text, colorOverride) = Value(cell, style);

        var runs = text.Length == 0
            ? []
            : new[]
            {
                new Run
                {
                    Text = text,
                    Properties = new()
                    {
                        FontFamily = style.FontFamily ?? styles.DefaultFont.Family ?? defaultFont,
                        FontSizePoints = (style.FontSizePoints ?? styles.DefaultFont.SizePoints) * scale,
                        Bold = style.Bold,
                        Italic = style.Italic,
                        Underline = style.Underline,
                        Strikethrough = style.Strikethrough,
                        ColorHex = colorOverride ?? style.ColorHex
                    }
                }
            };

        var paragraph = new ParagraphElement
        {
            Runs = runs,
            Properties = new()
            {
                // Excel's default is right for numbers and left for text; an explicit alignment wins.
                Alignment = style.HorizontalAlignment ?? DefaultAlignment(cell),
                LeftIndentPoints = style.IndentLevel * 9 * scale,
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
                BackgroundColorHex = style.BackgroundColorHex,
                Borders = style.Borders,
                GridSpan = merge.ColumnSpan,
                VerticalMerge = merge.VerticalMerge,
                VerticalAlignment = style.VerticalAlignment,
                NoWrap = !style.WrapText,
                Padding = new(0, 2 * scale, 0, 2 * scale)
            }
        };
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
            return (cell.GetFirstChild<S.InlineString>()?.Text?.Text ?? string.Empty, null);
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
            return (raw!, null);
        }

        var formatted = NumberFormat.Format(number, style.EffectiveFormatCode);
        return (formatted.Text, formatted.ColorHex);
    }

    static TextAlignment DefaultAlignment(S.Cell? cell) =>
        cell?.DataType?.Value is { } type &&
        (type == S.CellValues.SharedString || type == S.CellValues.InlineString || type == S.CellValues.String)
            ? TextAlignment.Left
            : cell?.CellValue == null
                ? TextAlignment.Left
                : TextAlignment.Right;

    /// <summary>
    /// Column widths in points, from the <c>cols</c> runs. Each <c>col</c> covers an inclusive
    /// <c>min..max</c> span rather than a single column, so the runs are expanded across the range.
    /// </summary>
    static double[] ColumnWidths(S.Worksheet worksheet, SheetRange range, double scale)
    {
        var declared = worksheet.GetFirstChild<S.Columns>()?.Elements<S.Column>().ToArray() ?? [];
        var fallback = worksheet.SheetFormatProperties?.DefaultColumnWidth?.Value ?? defaultColumnWidthChars;

        var widths = new double[range.ColumnCount];
        for (var i = 0; i < widths.Length; i++)
        {
            var column = range.FirstColumn + i;
            var match = declared.FirstOrDefault(_ =>
                _.Min?.Value <= column && _.Max?.Value >= column);

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
    public static double NaturalWidthPoints(S.Worksheet worksheet, SheetRange range) =>
        ColumnWidths(worksheet, range, 1).Sum();

    static double ToPoints(double characters) =>
        characters <= 0
            ? 0
            : Math.Truncate(characters * maxDigitWidthPixels + cellPaddingPixels) * pointsPerPixel;
}
