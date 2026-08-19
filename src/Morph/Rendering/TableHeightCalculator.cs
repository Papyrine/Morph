/// <summary>
/// Shared table row/cell height computation. Backend-agnostic: paragraph layout
/// is delegated to <see cref="IParagraphMeasurer"/>.
/// </summary>
static class TableHeightCalculator
{
    /// <summary>
    /// The width a line is laid out against when it must not wrap. Far past any real page, but well
    /// short of <see cref="float.MaxValue"/> so the measurer's own arithmetic on it stays finite.
    /// </summary>
    internal const float UnboundedWidth = float.MaxValue / 4;

    /// <summary>
    /// Computes the final height of every row in <paramref name="table"/>, accounting for
    /// non-merged cells, explicit row heights (atLeast vs exact, vMerge-strict tables),
    /// and vertically merged content overflow.
    /// </summary>
    public static float[] CalculateRowHeights(
        TableElement table,
        float[] colWidths,
        IParagraphMeasurer measurer,
        bool hasVerticalMerge,
        bool addInteriorBorders = false)
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

                // Detached-border geometry loses the horizontal insets from the content measure —
                // see TableLayout.CellSpacingInsets for the Word-probed gap law.
                cellWidth -= (float) TableLayout.CellSpacingInsets(
                    table.Properties, gridColIndex, span, colCount).Horizontal;

                var cellHeight = MeasureCellHeight(cell, cellWidth, table.Properties, measurer, row);
                maxHeight = Math.Max(maxHeight, cellHeight);

                gridColIndex += span;
            }

            // w:tblCellSpacing expands the row slot by 2 × spacing — the cell box keeps its content
            // size while the gaps land above and below it. The first and last rows carry one extra
            // spacing each: the table FRAME sits a further 2 × spacing outside the outermost cell
            // rules (_probe_cellspacing at 2/6/12pt: frame-to-cell reads 11/27/52px at 150 DPI,
            // exactly 2 × spacing plus the rules' half-widths, while cell-to-cell is 2 × spacing
            // from the two adjacent insets).
            var slotSpacing = 2 * (float) table.Properties.CellSpacingPoints;
            heights[rowIndex] = maxHeight + slotSpacing;
            if (rowIndex == 0)
            {
                heights[rowIndex] += slotSpacing / 2;
            }

            if (rowIndex == table.Rows.Count - 1)
            {
                heights[rowIndex] += slotSpacing / 2;
            }
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

        // Fourth pass: border-collapse height. Word grows the table box by every collapsed horizontal
        // edge — the outer top of the first row and the outer bottom of the last row always, and (when
        // <paramref name="addInteriorBorders"/> is set) each interior edge between two rows. Word draws
        // each edge on the boundary and insets the content below it, so a table of N rows accrues its
        // outer top + outer bottom + the N-1 interior edges. Measured on header_row_repeat/01 (Word
        // XPS): a 0.5pt insideH adds ~0.5pt of row-to-row pitch, which across 60 single-line rows is a
        // whole extra row per page the engine would otherwise fit that Word does not (25 vs 24),
        // shifting every continuation page. Each interior edge equals the row-below's resolved top
        // border (the collapsed insideH), so HorizontalBorderWidth(top: true) reads it directly.
        //
        // The FULL edge width is Word's law, probed directly (2026-08-04, single-column tables of
        // identical 12pt rows varying only the border): insideH of 0.5 / 2.25 / 4.5 / 6pt each grew the
        // row-to-row pitch by the full width, per-cell authored top/bottom borders behaved identically to
        // insideH, and the content ink starts a full border-width lower in bordered rows — Word draws the
        // edge at the boundary and insets the row-below's content under it. The same probe settled the
        // resumes/01 spacer rows (2.25pt rules over empty atLeast rows): Word's spacer row is the empty
        // mark line + the full edge (13.97 + 2.25 = 16.22pt), which is what this pass computes — the
        // production PDF path, which does not opt in, runs those rows 2.25pt short.
        //
        // Interior edges are OPT-IN because the two consumers position content differently. The layout
        // engine's PdfPainter tops content in the taller cell and its pagination then matches Word, so
        // it asks for them. The deleted production render did NOT inset content by the top
        // border, so growing the row there drops the extra height as blank space at the cell bottom and
        // shifts page breaks the wrong way (measured: resumes/10 −0.205, cover-letters/05 −0.05); it
        // keeps the outer-only behaviour until it owns the content inset too. An earlier note claimed
        // interior edges collapse and add nothing, but that was asserted on a 1-row table (no interior
        // edge) and the XPS disproves it.
        // An exact row (w:hRule="exact") is exempt from all of it. ECMA-376 §17.4.81 makes that height
        // the row's height verbatim, and Word honours it against borders as it does against overflowing
        // content — it draws the edge and clips rather than growing the row. business/05's letterhead is
        // three exact rows (22.3 + 25.9 + 66.2pt) under a bordered layout table: Word starts the
        // "Memorandum" heading at exactly margin + 114.4pt, while growing those rows by their outer top,
        // outer bottom and two interior edges put every line on the page ~3pt low.
        if (heights.Length > 0)
        {
            var lastRowIndex = table.Rows.Count - 1;
            if (!IsPinnedExact(table.Rows[0]))
            {
                heights[0] += HorizontalBorderWidth(table, colCount, rowIndex: 0, top: true);
            }

            if (!IsPinnedExact(table.Rows[lastRowIndex]))
            {
                heights[^1] += HorizontalBorderWidth(table, colCount, lastRowIndex, top: false);
            }

            if (addInteriorBorders)
            {
                for (var rowIndex = 1; rowIndex < table.Rows.Count; rowIndex++)
                {
                    if (IsPinnedExact(table.Rows[rowIndex]))
                    {
                        continue;
                    }

                    heights[rowIndex] += HorizontalBorderWidth(table, colCount, rowIndex, top: true);
                }
            }
        }

        return heights;
    }

    // A row whose w:trHeight carries w:hRule="exact": its height is that value verbatim, so neither
    // content nor a collapsed border edge may grow it.
    internal static bool IsPinnedExact(TableRow row) =>
        row is
        {
            HeightPoints: not null,
            IsExactHeight: true
        };

    /// <summary>
    /// Widest visible top (or bottom) border across the cells of one row, in points. For the first
    /// row's top and the last row's bottom this is the table's outer horizontal edge; for any interior
    /// row's top it is the collapsed edge shared with the row above (the resolved insideH). Used to
    /// grow rows by the border width their content is inset by.
    /// </summary>
    internal static float HorizontalBorderWidth(TableElement table, int colCount, int rowIndex, bool top)
    {
        var row = table.Rows[rowIndex];
        var width = 0f;
        var gridColIndex = 0;
        for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
        {
            var cell = row.Cells[cellIndex];
            var borders = TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row, table.Rows);
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

        // A single-line cell is measured unbounded, so the row is sized to one line whatever the
        // text's natural width — matching the placement, which lays the same line out past the
        // cell's edge rather than breaking it.
        var contentWidth = cell.Properties.SingleLine
            ? UnboundedWidth
            : cellWidth - (float) (padding.Horizontal + margin.Horizontal);
        var height = (float) (padding.Vertical + margin.Vertical);

        // w:hideMark: when the cell's only content is an empty end-of-cell paragraph mark,
        // suppress its height contribution so the cell can collapse below one line of text.
        if (cell.Properties.HideMark && IsOnlyEmptyParagraph(cell.Content))
        {
            return height;
        }

        // Collect paragraphs (and content-control wrappers) so we know first/last for spacing collapse.
        // Non-paragraph content between two paragraphs SEPARATES them: w:contextualSpacing suppresses
        // spacing only against the immediately preceding/following PARAGRAPH, so a paragraph that
        // follows a drawing is not adjacent to the one before it however the two styles compare.
        // Collapsing across the gap sank wedding/08's "GROOM'S FULL NAME" — its cell is Title,
        // inline oval (a WordArtElement), Title, and the two contextual Titles are three list entries
        // apart in the document but were neighbours once the drawing was filtered out.
        var paragraphs = new List<ParagraphElement>();
        var separatedFromPrevious = new HashSet<int>();
        var sawNonParagraph = false;
        foreach (var element in cell.Content)
        {
            var cellParagraph = element as ParagraphElement ?? (element as ContentControlElement)?.CellParagraph;
            if (cellParagraph is not null)
            {
                // The shared ContentControlElement wrapper keeps the layout-cache key identical
                // across the measure/render pipeline stages.
                if (sawNonParagraph)
                {
                    separatedFromPrevious.Add(paragraphs.Count);
                    sawNonParagraph = false;
                }

                paragraphs.Add(cellParagraph);
                continue;
            }

            if (element is TableElement {Properties.IsFloating: false})
            {
                height += 50;
                sawNonParagraph = true;
            }
            else if (element is WordArtElement wordArt)
            {
                // Inline WordArt occupies its own band in the cell exactly as it does in the body
                // flow, where RenderWordArt advances CurrentY by the same height. Leaving it out
                // sized the row without it while the render still consumed the space, so the cell
                // overflowed and pushed content onto a further page (brochures/08 went 2 pages to
                // 3). Measure and render have to agree.
                height += (float) wordArt.HeightPoints;
                sawNonParagraph = true;
            }
        }

        float previousAfter = 0;
        var previousContextual = false;
        string? previousStyleId = null;
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
            // before over the previous paragraph's after adds height. w:contextualSpacing
            // removes that gap ENTIRELY between two same-style contextual paragraphs, exactly
            // as the placement path does (Fragmenter's cell arm). Measuring without the
            // collapse sized the row as if every suppressed gap were paid while the render drew
            // the paragraphs tight, so the row carried the difference as dead space below its
            // content: letters/04's four 18pt-after RecipientAddress lines left 54pt of it, and
            // the salutation and everything under it — down to the footer contact strip, which
            // landed in the navy band — shifted down the page by that much.
            var contextualCollapse = i > 0 &&
                                     !separatedFromPrevious.Contains(i) &&
                                     props.ContextualSpacing &&
                                     previousContextual &&
                                     props.StyleId == previousStyleId;
            if (contextualCollapse)
            {
                // The boundary contributes nothing at all, so take back the previous
                // paragraph's after rather than adding a before on top of it.
                height -= previousAfter;
            }
            else
            {
                height += Math.Max(0, (float) props.SpacingBeforePoints - previousAfter);
            }

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
            previousContextual = props.ContextualSpacing;
            previousStyleId = props.StyleId;
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
