/// <summary>
/// Shared table row/cell height computation. Backend-agnostic: paragraph layout
/// is delegated to <see cref="IParagraphMeasurer"/>.
/// </summary>
static class TableHeightCalculator
{
    /// <summary>
    /// Computes the final height of every row in <paramref name="table"/>, accounting for
    /// non-merged cells, explicit row heights (atLeast vs exact, vMerge-strict tables),
    /// and vertically merged content overflow.
    /// </summary>
    public static float[] CalculateRowHeights(
        TableElement table,
        float[] colWidths,
        IParagraphMeasurer measurer,
        bool hasVerticalMerge)
    {
        var heights = new float[table.Rows.Count];
        var colCount = colWidths.Length;

        // First pass: Calculate heights for non-merged cells only.
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            // Row height comes from the measured content; MeasureCellHeight already floors each
            // cell at its (possibly empty) paragraph's line height, so no artificial row minimum is
            // needed. A blanket 20pt floor inflated short rows — e.g. the thin coloured spacer rows
            // in header banner tables (content ~3-6pt) ballooned to 20pt each. Rows WITH an explicit
            // w:trHeight are applied verbatim in the second pass.
            float maxHeight = 0;

            var gridColIndex = 0;
            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                // Skip cells that are part of a vertical merge — handled in the third pass.
                if (cell.Properties.VerticalMerge is VerticalMergeType.Continue or VerticalMergeType.Restart)
                {
                    gridColIndex += span;
                    continue;
                }

                // Sum column widths for horizontally merged cells.
                float cellWidth = 0;
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    cellWidth += colWidths[gridColIndex + i];
                }

                var cellHeight = MeasureCellHeight(cell, cellWidth, table.Properties, measurer, row);
                maxHeight = Math.Max(maxHeight, cellHeight);

                gridColIndex += span;
            }

            // w:tblCellSpacing expands the row slot by 2 × spacing — the cell box stays
            // the same size, but its row gets extra room above and below so the gaps
            // between rows show up the way Word renders them.
            heights[rowIndex] = maxHeight + 2 * (float) table.Properties.CellSpacingPoints;
        }

        // Second pass: Apply explicit row heights (w:trHeight). "exact" (w:hRule="exact") pins the row
        // to that height verbatim; every other rule ("atLeast", or the omitted default) treats it as a
        // FLOOR — the row still grows to fit its content, per Word. An earlier rule forced heights
        // verbatim on any vMerge table whose rows all carried an explicit height, on the theory that
        // such letterhead grids are authored to fixed heights; but that clamped genuinely overflowing
        // rows — e.g. a cell whose 1-inch w:tcMar top margin dwarfs a 342-twip trHeight — so the next
        // row rendered on top of the overflow (business-plans/08's "Prepared for:" heading collided
        // with the contact block). Honouring atLeast everywhere matches Word and, across the corpus,
        // improves far more pages than it shifts.
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (!row.HeightPoints.HasValue)
            {
                continue;
            }

            var explicitHeight = (float) row.HeightPoints.Value;
            heights[rowIndex] = row.IsExactHeight
                ? explicitHeight
                : Math.Max(heights[rowIndex], explicitHeight);
        }

        // Third pass: distribute vMerge-Restart cell content across spanned rows.
        // Runs after explicit heights so vMerge can expand rows when content overflows.
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
                {
                    var rowSpan = TableLayout.CalculateVerticalMergeRowSpan(table, rowIndex, gridColIndex);

                    float cellWidth = 0;
                    for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                    {
                        cellWidth += colWidths[gridColIndex + i];
                    }

                    var contentHeight = MeasureCellHeight(cell, cellWidth, table.Properties, measurer, row);

                    float currentTotalHeight = 0;
                    for (var r = rowIndex; r < rowIndex + rowSpan && r < table.Rows.Count; r++)
                    {
                        currentTotalHeight += heights[r];
                    }

                    if (contentHeight > currentTotalHeight)
                    {
                        var extraHeight = contentHeight - currentTotalHeight;
                        var extraPerRow = extraHeight / rowSpan;

                        for (var r = rowIndex; r < rowIndex + rowSpan && r < table.Rows.Count; r++)
                        {
                            heights[r] += extraPerRow;
                        }
                    }
                }

                gridColIndex += span;
            }
        }

        // Fourth pass: border-collapse height. Word grows the table box by its OUTER horizontal
        // border widths — the top border of the first row and the bottom border of the last row
        // occupy layout space above/below the content. Shared inner edges (between two rows)
        // collapse onto the content boundary and add no measurable row height, so only the outer
        // edges count. Without this a fully-bordered table renders up to ~2pt tighter than Word once
        // the (correct) after-spacing overlap removes the bottom-margin slack that used to mask it —
        // e.g. a 1-row table whose only borders are its own top+bottom edges. A single-row table is
        // both first and last, so it correctly accrues both edges.
        if (heights.Length > 0)
        {
            heights[0] += OuterHorizontalBorderWidth(table, colCount, rowIndex: 0, top: true);
            var lastRowIndex = table.Rows.Count - 1;
            heights[^1] += OuterHorizontalBorderWidth(table, colCount, lastRowIndex, top: false);
        }

        return heights;
    }

    /// <summary>
    /// Widest visible top (or bottom) border across the cells of one row, in points — the table's
    /// outer horizontal edge. Used to grow the first/last row by the collapsed outer border width.
    /// </summary>
    static float OuterHorizontalBorderWidth(TableElement table, int colCount, int rowIndex, bool top)
    {
        var row = table.Rows[rowIndex];
        var width = 0f;
        var gridColIndex = 0;
        for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
        {
            var cell = row.Cells[cellIndex];
            var borders = TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row);
            var edge = top ? borders?.Top : borders?.Bottom;
            if (edge is {IsVisible: true})
            {
                width = Math.Max(width, (float) edge.WidthPoints);
            }

            gridColIndex += cell.Properties.GridSpan;
        }

        return width;
    }

    /// <summary>
    /// Measures the natural height of <paramref name="cell"/> at the given width, including
    /// padding, margin, paragraph spacing-collapse rules, and bullet indent. Routes
    /// non-LeftToRight cells to <see cref="MeasureVerticalCellHeight"/>.
    /// </summary>
    public static float MeasureCellHeight(
        TableCell cell,
        float cellWidth,
        TableProperties tableProps,
        IParagraphMeasurer measurer,
        TableRow? row = null)
    {
        var padding = TableLayout.GetEffectivePadding(cell.Properties, tableProps, row);
        var margin = TableLayout.GetEffectiveMargin(cell.Properties, tableProps);

        if (cell.Properties.TextDirection != CellTextDirection.LeftToRight)
        {
            return MeasureVerticalCellHeight(cell, padding, margin, measurer);
        }

        var contentWidth = cellWidth - (float) (padding.Horizontal + margin.Horizontal);
        var height = (float) (padding.Vertical + margin.Vertical);

        // w:hideMark: when the cell's only content is an empty end-of-cell paragraph mark,
        // suppress its height contribution so the cell can collapse below one line of text.
        if (cell.Properties.HideMark && IsOnlyEmptyParagraph(cell.Content))
        {
            return height;
        }

        // Collect paragraphs (and content-control wrappers) so we know first/last for spacing collapse.
        var paragraphs = new List<ParagraphElement>();
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                paragraphs.Add(para);
            }
            else if (element is ContentControlElement contentControl)
            {
                // The shared wrapper keeps the layout-cache key identical across the
                // measure/render pipeline stages.
                if (contentControl.CellParagraph is { } measurePara)
                {
                    paragraphs.Add(measurePara);
                }
            }
            else if (element is TableElement {Properties.IsFloating: false})
            {
                height += 50;
            }
            else if (element is WordArtElement wordArt)
            {
                // Inline WordArt occupies its own band in the cell exactly as it does in the body
                // flow, where RenderWordArt advances CurrentY by the same height. Leaving it out
                // sized the row without it while the render still consumed the space, so the cell
                // overflowed and pushed content onto a further page (brochures/08 went 2 pages to
                // 3). Measure and render have to agree.
                height += (float) wordArt.HeightPoints;
            }
        }

        float previousAfter = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            // Measured at exactly the width the cell render lays out at — the render path
            // subtracts the paragraph's own indents internally and draws list markers into the
            // hanging area, so no extra marker inset belongs here (an earlier 12pt inset for
            // numbered paragraphs measured narrower lines than the render produced, inflating
            // row heights and splitting the layout cache per paragraph).
            var para = paragraphs[i];
            var lines = measurer.LayoutParagraphForMeasurement(para, contentWidth);
            var props = para.Properties;

            // Cell padding sits between the border and the content area; paragraph
            // spacing-before/after lives inside that content area, so the two stack.
            // (An earlier version subtracted padding from spacing on the assumption that
            // padding "absorbed" leading/trailing spacing, but Word doesn't collapse
            // them — tables with explicit w:tblCellMar rendered too tight as a result.)
            // Between consecutive paragraphs Word charges max(after, before), not the sum
            // (verified against Word XPS baselines), so only the excess of this paragraph's
            // before over the previous paragraph's after adds height.
            height += Math.Max(0, (float) props.SpacingBeforePoints - previousAfter);

            foreach (var lineHeight in lines)
            {
                height += lineHeight;
            }

            // The LAST paragraph's after-spacing stacks on the cell's bottom margin, same as
            // inter-paragraph spacing: Word sizes the row as margins + lines + full after
            // (measured on table_default_style: tblCellMar 3pt/3pt, Normal 8pt-after, one
            // 12pt line -> Word row 31pt = 3 + 16.97 + 8 + 3; the earlier max-overlap rule
            // predicted 28pt and rendered visibly short). That overlap rule dated from when
            // cell line heights ran too small and a 2pt default-padding fudge inflated rows
            // — each was calibrated against the other.
            height += (float) props.SpacingAfterPoints;
            previousAfter = (float) props.SpacingAfterPoints;
        }

        return height;
    }

    static bool IsOnlyEmptyParagraph(IReadOnlyList<DocumentElement> content)
    {
        if (content.Count != 1)
        {
            return false;
        }

        return content[0] is ParagraphElement {Runs.Count: 0};
    }

    /// <summary>
    /// Height contribution of a btLr / tbRl cell: the longest paragraph's natural single-line
    /// width becomes the cell's vertical extent. Multiple paragraphs stack horizontally
    /// (along the row direction) so they don't add to the cell's height contribution.
    /// </summary>
    public static float MeasureVerticalCellHeight(
        TableCell cell,
        CellSpacing padding,
        CellSpacing margin,
        IParagraphMeasurer measurer)
    {
        var widest = 0f;
        foreach (var element in cell.Content)
        {
            var para = element as ParagraphElement;
            if (para == null &&
                element is ContentControlElement {Runs.Count: > 0} cc)
            {
                para = new()
                {
                    Runs = cc.Runs,
                    Properties = new()
                };
            }

            if (para == null)
            {
                continue;
            }

            var natural = measurer.MeasureParagraphNaturalWidth(para, float.MaxValue / 4);
            if (natural > widest)
            {
                widest = natural;
            }
        }

        return (float) (padding.Vertical + margin.Vertical) + widest;
    }
}
