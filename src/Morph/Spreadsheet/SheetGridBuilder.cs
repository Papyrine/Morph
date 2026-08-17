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
sealed class SheetGridBuilder(CellStyles styles, SharedStrings sharedStrings, string defaultFont, double maxDigitWidthPixels)
{
    const double pointsPerPixel = 72.0 / 96.0;

    /// <summary>Excel's default when a sheet declares no width of its own.</summary>
    const double defaultColumnWidthChars = 8.43;

    /// <summary>Excel's default row height for an 11pt body font.</summary>
    const double defaultRowHeightPoints = 15;

    /// <summary>
    /// What Excel auto-sizes a row to, as a multiple of its tallest font.
    ///
    /// Probe (<c>_probe_rowheight</c>: one row per font size, no declared height, row heights read
    /// off the reference as the distance between successive ink bands) — 10pt to 12.75, 14pt to 18,
    /// 18pt to 24, 24pt to 32.25, 36pt to 46.5. That is 1.275 to 1.344, averaging 1.31, and it also
    /// squares with Excel's own 15pt default for an 11pt body font.
    ///
    /// The layout engine grows a row to the font's OpenType line height instead, around 1.2x, which
    /// is 8% short. Small per row, but it compounds down a sheet: basic-business-invoice fitted 27
    /// rows onto one page where Excel needs two.
    /// </summary>
    const double autoRowHeightFactor = 1.31;

    public TableElement? Build(S.Worksheet worksheet, SheetRange range, double scale, (int First, int Last)? titleRows, ConditionalFormats conditional, bool horizontallyCentered)
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
            rows.Add(BuildRow(row, rowIndex, range, merges, widths, defaultHeight, scale, isHeader, conditional));
        }

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                GridColumnWidths = widths,
                IsAutoFit = false,
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
        double defaultHeight,
        double scale,
        bool isHeader,
        ConditionalFormats conditional)
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
                Spill(found, merge, cellsByColumn, merges, widths, rowIndex, column, range, width),
                scale,
                conditional,
                rowIndex,
                column));

            column += Math.Max(merge.ColumnSpan, overflow) - 1;
        }

        return new()
        {
            Cells = cells,
            IsHeader = isHeader,
            // A declared height is used as given; an auto-height row takes Excel's own sizing rule
            // rather than being left to the engine's OpenType line height, which runs short.
            HeightPoints = row?.Height?.Value is { } height
                ? height * scale
                : Math.Max(defaultHeight, AutoRowHeight(cellsByColumn.Values) * scale),
            // customHeight="1" means the author fixed the height, and Excel then CLIPS wrapped text
            // to it rather than growing the row. Treating every height as a floor instead let one
            // sheet of paragraph-length text in a 2.6-character column grow into six blank pages,
            // where Excel renders a single page and simply shows almost none of that text. Only an
            // auto-height row (no customHeight) grows to its content.
            IsExactHeight = row?.CustomHeight?.Value == true
        };
    }

    /// <summary>
    /// The height Excel would auto-size a row to: its tallest font times
    /// <see cref="autoRowHeightFactor"/>. Zero when the row holds nothing, leaving the sheet default.
    /// </summary>
    double AutoRowHeight(IEnumerable<S.Cell> cells)
    {
        var tallest = 0d;
        foreach (var cell in cells)
        {
            var size = styles.Resolve(cell.StyleIndex?.Value ?? 0).FontSizePoints ?? styles.DefaultFont.SizePoints;
            tallest = Math.Max(tallest, size);
        }

        return tallest * autoRowHeightFactor;
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

    /// <summary>
    /// How far a cell's clip may reach OUTSIDE its own box, in points, as (left, right).
    ///
    /// The clip stops overflowing text at the first occupied neighbour, so it has to know where the
    /// empty ones end — and Excel spills in the direction the alignment implies, not always
    /// rightwards. LEFT-aligned text is not this method's business: <see cref="OverflowSpan"/>
    /// already widened the cell itself over its empty right-hand neighbours, so the box is the clip
    /// and a second reckoning here would double it. RIGHT-aligned text spills LEFT (the line ends at
    /// the cell's right edge and grows backwards) and CENTRED text spills both ways, and neither can
    /// be modelled by widening: the cell would re-anchor, moving every short label that fits.
    ///
    /// Wrapped text never overflows at all — it breaks instead — so it reaches nowhere.
    /// </summary>
    (double Left, double Right) Spill(
        S.Cell? cell,
        MergeInfo merge,
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

        return (0, 0);
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
        int rowIndex,
        int column)
    {
        var style = styles.Resolve(cell?.StyleIndex?.Value ?? 0);
        var (text, colorOverride) = Value(cell, style);

        // A conditional rule overlays the cell's own style rather than replacing it, so each member
        // falls back to what the style already resolved.
        var overlay = conditional.IsEmpty
            ? null
            : conditional.For(rowIndex, column, text, Number(cell));

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
                        Bold = style.Bold || overlay?.Bold == true,
                        Italic = style.Italic || overlay?.Italic == true,
                        Underline = style.Underline,
                        Strikethrough = style.Strikethrough,
                        ColorHex = overlay?.ColorHex ?? colorOverride ?? style.ColorHex
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
                BackgroundColorHex = overlay?.BackgroundColorHex ?? style.BackgroundColorHex,
                Borders = style.Borders,
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
                Padding = new(0, 2 * scale, 0, 2 * scale)
            }
        };
    }

    /// <summary>The cell's numeric value, for the conditional rules that compare against one.</summary>
    static double? Number(S.Cell? cell) =>
        cell?.DataType?.Value == null &&
        double.TryParse(cell?.CellValue?.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

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
    double[] ColumnWidths(S.Worksheet worksheet, SheetRange range, double scale)
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
    public double NaturalWidthPoints(S.Worksheet worksheet, SheetRange range) =>
        ColumnWidths(worksheet, range, 1).Sum();

    /// <summary>
    /// The sheet's unscaled height, for the fit-to-height half of the scale. Declared row heights
    /// are what Excel fits against; a row that would grow to its content is not known until layout,
    /// and Excel does not fit against that either.
    /// </summary>
    public static double NaturalHeightPoints(S.Worksheet worksheet, SheetRange range)
    {
        var declared = worksheet.GetFirstChild<S.SheetData>()?
            .Elements<S.Row>()
            .Where(_ => _.RowIndex?.Value is { } index && range.ContainsRow((int) index))
            .ToDictionary(_ => (int) _.RowIndex!.Value, _ => _.Height?.Value) ?? [];

        var fallback = worksheet.SheetFormatProperties?.DefaultRowHeight?.Value ?? defaultRowHeightPoints;

        var total = 0d;
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            total += declared.TryGetValue(row, out var height) && height is { } value ? value : fallback;
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
    double ToPoints(double characters) =>
        characters <= 0
            ? 0
            : characters * maxDigitWidthPixels * pointsPerPixel;

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

        return units <= 0
            ? calibriFallback
            : (double) units / metrics.UnitsPerEm * sizePoints / pointsPerPixel;
    }
}
