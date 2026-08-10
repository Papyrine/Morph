using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// A sheet's column and row extents in points, so cell positions can be resolved the same way twice:
/// once to build the grid, and once to place the drawings anchored to it.
///
/// A drawing anchors to cells rather than to the page — <c>from</c> column 1 offset 6350 EMU — so
/// turning that into a rectangle needs exactly the widths and heights the grid was built from. Any
/// disagreement puts the artwork somewhere the cells are not.
/// </summary>
sealed class SheetGeometry
{
    /// <summary>
    /// Excel's column width counts '0' glyphs of the body font, so a real width needs that glyph's
    /// advance. 7px matches the 11pt Calibri-class fonts the corpus uses at Excel's 96 DPI
    /// reference; the extra 5px is the cell's fixed padding (ECMA-376 §18.3.1.13).
    /// </summary>
    const double maxDigitWidthPixels = 7;
    const double cellPaddingPixels = 5;
    const double pointsPerPixel = 72.0 / 96.0;

    const double defaultColumnWidthChars = 8.43;
    const double defaultRowHeightPoints = 15;

    readonly SheetRange range;
    readonly double[] columnWidths;
    readonly Dictionary<int, double> rowHeights;
    readonly double defaultRowHeight;

    public SheetGeometry(S.Worksheet worksheet, SheetRange range, double scale)
    {
        this.range = range;

        var declared = worksheet.GetFirstChild<S.Columns>()?.Elements<S.Column>().ToArray() ?? [];
        var fallbackWidth = worksheet.SheetFormatProperties?.DefaultColumnWidth?.Value ?? defaultColumnWidthChars;

        columnWidths = new double[range.ColumnCount];
        for (var i = 0; i < columnWidths.Length; i++)
        {
            var column = range.FirstColumn + i;
            var match = declared.FirstOrDefault(_ => _.Min?.Value <= column && _.Max?.Value >= column);

            // A hidden column occupies no width at all rather than its declared one.
            var chars = match?.Hidden?.Value == true ? 0 : match?.Width?.Value ?? fallbackWidth;
            columnWidths[i] = ToPoints(chars) * scale;
        }

        defaultRowHeight = (worksheet.SheetFormatProperties?.DefaultRowHeight?.Value ?? defaultRowHeightPoints) * scale;
        rowHeights = worksheet.GetFirstChild<S.SheetData>()?
            .Elements<S.Row>()
            .Where(_ => _.RowIndex?.Value != null && _.Height?.Value != null)
            .GroupBy(_ => (int) _.RowIndex!.Value)
            .ToDictionary(_ => _.Key, _ => _.First().Height!.Value * scale) ?? [];
    }

    public IReadOnlyList<double> ColumnWidths => columnWidths;

    public double RowHeight(int row) =>
        rowHeights.TryGetValue(row, out var height) ? height : defaultRowHeight;

    /// <summary>
    /// Distance in points from the grid's left edge to the left edge of a 1-based column. Columns
    /// before the range contribute nothing, and one past the end returns the full width, so a
    /// drawing anchored outside the range clamps to it rather than landing at a negative offset.
    /// </summary>
    public double ColumnLeft(int column)
    {
        var offset = 0d;
        for (var i = 0; i < columnWidths.Length && range.FirstColumn + i < column; i++)
        {
            offset += columnWidths[i];
        }

        return offset;
    }

    /// <summary>Distance in points from the grid's top edge to the top of a 1-based row.</summary>
    public double RowTop(int row)
    {
        var offset = 0d;
        for (var r = range.FirstRow; r < row && r <= range.LastRow; r++)
        {
            offset += RowHeight(r);
        }

        return offset;
    }

    public double TotalWidth => columnWidths.Sum();

    static double ToPoints(double characters) =>
        characters <= 0
            ? 0
            : Math.Truncate((characters * maxDigitWidthPixels) + cellPaddingPixels) * pointsPerPixel;
}
