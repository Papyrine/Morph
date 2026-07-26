/// <summary>
/// Flows a document's block content into pages once, backend-independently — the heart of the layout
/// engine (<c>docs/layout-engine-proposal.md</c>, step 3). Handles multi-column block flow with
/// **line-level** page/column breaks (a paragraph too tall for the space left splits at a line boundary
/// and continues in the next column or page, which the raster backends cannot do today) and
/// **row-level** table breaks (a table taller than a column flows row by row, re-emitting
/// <c>w:tblHeader</c> rows after each break). Content fills column 0 to the bottom, then column 1, and so
/// on; the last column overflowing starts a new page. It applies the measured height-model rules from
/// <c>src/page_counts.md</c> — max-collapse paragraph spacing, space-before dropped at an automatically
/// broken region (column or page) top, and the empty-paragraph mark line.
///
/// <para>Paragraph flow fits lines exactly (the canonical measurer does not over-measure, so no slack is
/// needed); table pagination mirrors the raster backend's tolerances so the two paginate a table
/// identically until a canonical row measurement retires the slack.</para>
///
/// <para>Columns are equal-width from a single <see cref="PageSettings"/> (the common case). Deferred to
/// later slices, and noted so a document using them is not yet expected to paginate: per-section geometry
/// changes (a section break switching column count or page size, including the continuous mid-page kind);
/// widow/orphan and keep-next/keep-lines; floats and their wrap exclusions; floating tables; images and
/// nested tables inside a cell; header/footer band height; and even/odd section-break parity. Other
/// non-paragraph, non-table elements are skipped for now.</para>
/// </summary>
sealed class Fragmenter(CanonicalParagraphMeasurer measurer)
{
    public LaidOutDocument Layout(IReadOnlyList<DocumentElement> elements, PageSettings page) =>
        new Flow(measurer, page).Run(elements);

    // One document's flow state. A fresh instance per Layout call keeps the cursor, the in-progress page
    // and the emitted pages together without leaking between runs (the Fragmenter itself is reusable).
    sealed class Flow(CanonicalParagraphMeasurer measurer, PageSettings page)
    {
        readonly float contentTop = (float) page.MarginTop;
        readonly float contentBottom = (float) (page.HeightPoints - page.MarginBottom);
        readonly float contentHeight = (float) (page.HeightPoints - page.MarginTop - page.MarginBottom);
        readonly float fullContentLeft = (float) page.MarginLeft;
        readonly int columnCount = Math.Max(1, page.ColumnCount);
        readonly float columnWidth = (float) page.ColumnWidth;
        readonly float columnSpacing = (float) page.ColumnSpacing;
        readonly List<LaidOutPage> pages = [];

        List<PlacedItem> items = [];
        int currentColumn;
        float y;
        // Top of the current *region* — a fresh column or a fresh page. Space-before is dropped here and a
        // line/row is never pushed off it (nothing better to do), the same rule at a column or page top.
        bool atRegionTop = true;
        float lastAfter;
        bool currentPageExplicit;

        // Left edge of the current column, in points from the page's left.
        float ColumnLeft => fullContentLeft + currentColumn * (columnWidth + columnSpacing);

        // Top of a fresh *page* — the first column's region top. Page-level breaks skip when already here.
        bool AtPageTop => atRegionTop && currentColumn == 0;

        public LaidOutDocument Run(IReadOnlyList<DocumentElement> elements)
        {
            y = contentTop;

            foreach (var element in elements)
            {
                switch (element)
                {
                    case PageBreakElement:
                        // A page break starts a page even at the top of one, so N consecutive breaks yield
                        // N blank pages (ledger experiment 18); the page is marked explicit so it survives
                        // FinishPage's keep test. It resets to the first column.
                        FinishPage(nextPageExplicit: true);
                        break;

                    case ColumnBreakElement:
                        // Move to the next column, or a new page from the last column.
                        AdvanceColumnOrPage();
                        break;

                    case SectionBreakElement { BreakType: not SectionBreakType.Continuous }:
                        // Even/odd parity and per-section geometry are later slices; treated as a plain
                        // page break here.
                        if (!AtPageTop)
                        {
                            FinishPage(false);
                        }

                        break;

                    case ParagraphElement paragraph:
                        PlaceParagraph(paragraph);
                        break;

                    case TableElement table:
                        PlaceTable(table);
                        break;

                    // Floats, images and continuous sections are later slices.
                }
            }

            // Emit the trailing page. FinishPage keeps it only when it carries content, is an
            // explicit-break blank, or is the first page — so a natural trailing-overflow blank is dropped
            // while a deliberate one survives.
            FinishPage(false);

            return new(pages);
        }

        // Emits the in-progress page and starts a fresh one at its first column. A page is kept when it
        // has content, when it is a deliberate blank left by an explicit break (Word does not absorb
        // those), or when it is the only page; a natural trailing-overflow blank is dropped.
        void FinishPage(bool nextPageExplicit)
        {
            if (items.Count > 0 || currentPageExplicit || pages.Count == 0)
            {
                pages.Add(new(pages.Count + 1, page, items));
            }

            items = [];
            currentColumn = 0;
            y = contentTop;
            atRegionTop = true;
            lastAfter = 0;
            currentPageExplicit = nextPageExplicit;
        }

        // Content overflow or a column break: move to the next column, keeping the current page's items,
        // or start a new page when the last column is full.
        void AdvanceColumnOrPage()
        {
            if (currentColumn < columnCount - 1)
            {
                currentColumn++;
                y = contentTop;
                atRegionTop = true;
                lastAfter = 0;
                return;
            }

            FinishPage(false);
        }

        void PlaceParagraph(ParagraphElement paragraph)
        {
            var properties = paragraph.Properties;
            if (properties.PageBreakBefore && !AtPageTop)
            {
                FinishPage(false);
            }

            var paragraphLines = measurer.LayoutLineContents(paragraph, columnWidth);
            var isEmpty = paragraphLines.Count == 1 && paragraphLines[0].Width <= 0;

            // Columns are equal width, so the available width for alignment is constant even as a line
            // spills to the next column; the left edge (ColumnLeft) is read per line, after any advance.
            var availableWidth = columnWidth - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;

            // Space-before, collapsed with the previous paragraph's after (max, not sum) and dropped at a
            // region top. If the collapsed gap plus the first line overflows, the line-level break below
            // resets the cursor — the same space-before drop for the moved paragraph.
            if (!atRegionTop)
            {
                y += Math.Max(lastAfter, (float) properties.SpacingBeforePoints);
            }

            for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
            {
                var line = paragraphLines[lineIndex];
                if (!atRegionTop && y + line.Height > contentBottom)
                {
                    AdvanceColumnOrPage();
                }

                var lineLeft = ColumnLeft + (float) properties.LeftIndentPoints + AlignmentOffset(properties.Alignment, availableWidth, line.Width);
                var baseline = y + line.Ascent;
                items.Add(new PlacedLine(lineLeft, y, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                y += line.Height;
                atRegionTop = false;
            }

            lastAfter = isEmpty ? 0 : (float) properties.SpacingAfterPoints;
        }

        void PlaceTable(TableElement table)
        {
            // Floating tables take no flow space (their own slice); an empty table places nothing.
            if (table.Properties.IsFloating || table.Rows.Count == 0)
            {
                return;
            }

            var colCount = TableLayout.GetColumnCount(table);
            if (colCount == 0)
            {
                return;
            }

            var colWidths = TableLayout.CalculateColumnWidths(table, colCount, columnWidth, measurer);
            var hasVerticalMerge = TableLayout.HasVerticalMerge(table);
            var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, measurer, hasVerticalMerge);

            var tableWidth = 0f;
            foreach (var width in colWidths)
            {
                tableWidth += width;
            }

            var totalHeight = 0f;
            foreach (var height in rowHeights)
            {
                totalHeight += height;
            }

            var tableX = ComputeTableX(table, tableWidth);

            // The previous paragraph's after-spacing precedes the table (a table has no before-spacing to
            // collapse it with), and never applies at a region top.
            if (!atRegionTop)
            {
                y += lastAfter;
            }

            lastAfter = 0;

            // A table over 110% of a column's content height flows row by row (the raster backend's
            // needsRowByRowRendering); otherwise it stays whole and moves as a unit if needed.
            if (totalHeight > contentHeight * 1.10f)
            {
                PlaceTableRowByRow(table, colWidths, rowHeights, colCount, tableX, tableWidth);
                return;
            }

            // Narrow pre-advance for the fixed-height-row letter layouts: lift the whole table onto the
            // next region when it would otherwise clip its exact row, ahead of the softer whole-table move.
            if (!atRegionTop && totalHeight > 0)
            {
                var remaining = Math.Max(0f, contentBottom - y);
                if (totalHeight > remaining + 5f && HasExactRow(table))
                {
                    AdvanceColumnOrPage();
                }
            }

            // Whole-table move: mirrors EnsureSpaceFor(totalHeight − 2%) — a flow table may over-spill the
            // bottom by the shared rounding slack before it is pushed to the next region.
            EnsureSpaceFor(totalHeight - contentHeight * 0.02f);

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                items.Add(BuildRow(table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, false));
                y += rowHeights[rowIndex];
                atRegionTop = false;
            }
        }

        // A table taller than a column, placed row by row. Rows do not split: one that will not fit moves
        // whole to the next region. w:tblHeader rows are re-emitted after each break, and a trailing run of
        // empty rows is absorbed rather than starting a region for them.
        void PlaceTableRowByRow(TableElement table, float[] colWidths, float[] rowHeights, int colCount, float tableX, float tableWidth)
        {
            var headerCount = 0;
            while (headerCount < table.Rows.Count && table.Rows[headerCount].IsHeader)
            {
                headerCount++;
            }

            var lastVisibleRow = table.Rows.Count - 1;
            while (lastVisibleRow >= 0 && !TableLayout.RowHasVisibleContent(table.Rows[lastVisibleRow]))
            {
                lastVisibleRow--;
            }

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var rowHeight = rowHeights[rowIndex];
                var broke = rowIndex <= lastVisibleRow && EnsureSpaceFor(rowHeight);

                if (broke && headerCount > 0 && rowIndex >= headerCount)
                {
                    for (var headerIndex = 0; headerIndex < headerCount; headerIndex++)
                    {
                        items.Add(BuildRow(table, headerIndex, colWidths, rowHeights, colCount, tableX, tableWidth, true));
                        y += rowHeights[headerIndex];
                        atRegionTop = false;
                    }
                }

                items.Add(BuildRow(table, rowIndex, colWidths, rowHeights, colCount, tableX, tableWidth, false));
                y += rowHeight;
                atRegionTop = false;
            }
        }

        // Builds a placed row at the current cursor: the row box plus each cell's box, shading, borders and
        // laid-out content. Cell geometry mirrors PageRendererBase.RenderTableRow so the tree matches the
        // raster backend's grid — merge-continuation cells contribute nothing (the originating cell covers
        // them), and a merge-restart cell's box spans the merged rows' heights.
        PlacedTableRow BuildRow(TableElement table, int rowIndex, float[] colWidths, float[] rowHeights, int colCount, float tableX, float tableWidth, bool isRepeatedHeader)
        {
            var row = table.Rows[rowIndex];
            var rowHeight = rowHeights[rowIndex];
            var cells = new List<PlacedCell>();
            var cellX = tableX;
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                var cellWidth = 0f;
                for (var offset = 0; offset < span && gridColIndex + offset < colCount; offset++)
                {
                    cellWidth += colWidths[gridColIndex + offset];
                }

                if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                {
                    cellX += cellWidth;
                    gridColIndex += span;
                    continue;
                }

                var cellHeight = cell.Properties.VerticalMerge == VerticalMergeType.Restart
                    ? TableLayout.CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights)
                    : rowHeight;

                var padding = TableLayout.GetEffectivePadding(cell.Properties, table.Properties, row);
                var content = LayoutCellContent(cell, cellX + (float) padding.Left, y + (float) padding.Top, cellWidth - (float) padding.Horizontal);
                var borders = TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row);

                cells.Add(new PlacedCell(cellX, y, cellWidth, cellHeight, cell.Properties.BackgroundColorHex, borders, content));

                cellX += cellWidth;
                gridColIndex += span;
            }

            return new PlacedTableRow(tableX, y, tableWidth, rowHeight, table, rowIndex, isRepeatedHeader, cells);
        }

        // Stacks a cell's paragraphs from the top of its padded interior, wrapping each to the cell width.
        // No page breaks — the row height already accommodates the content. Top-aligned for now; vertical
        // alignment, nested tables and in-cell images are later slices.
        IReadOnlyList<PlacedItem> LayoutCellContent(TableCell cell, float contentLeft, float contentTop, float contentWidth)
        {
            var lines = new List<PlacedItem>();
            var cellY = contentTop;
            var lastCellAfter = 0f;
            var first = true;

            foreach (var element in cell.Content)
            {
                if (element is not ParagraphElement paragraph)
                {
                    continue;
                }

                var properties = paragraph.Properties;
                if (!first)
                {
                    cellY += Math.Max(lastCellAfter, (float) properties.SpacingBeforePoints);
                }

                first = false;

                var paragraphLines = measurer.LayoutLineContents(paragraph, contentWidth);
                var isEmpty = paragraphLines.Count == 1 && paragraphLines[0].Width <= 0;
                var textLeft = contentLeft + (float) properties.LeftIndentPoints;
                var availableWidth = contentWidth - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;

                for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
                {
                    var line = paragraphLines[lineIndex];
                    var lineLeft = textLeft + AlignmentOffset(properties.Alignment, availableWidth, line.Width);
                    var baseline = cellY + line.Ascent;
                    lines.Add(new PlacedLine(lineLeft, cellY, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                    cellY += line.Height;
                }

                lastCellAfter = isEmpty ? 0 : (float) properties.SpacingAfterPoints;
            }

            return lines;
        }

        // Table X within the current column, by w:jc alignment: centred and right collapse the indent into
        // the slack, left applies w:tblInd — matching PageRendererBase.ComputeTableX (non-floating).
        float ComputeTableX(TableElement table, float tableWidth)
        {
            var slack = columnWidth - tableWidth;
            return table.Properties.Alignment switch
            {
                TextAlignment.Center => ColumnLeft + Math.Max(0, slack / 2),
                TextAlignment.Right => ColumnLeft + Math.Max(0, slack),
                _ => ColumnLeft + (float) table.Properties.IndentPoints
            };
        }

        // Mirrors PageRendererBase.EnsureSpaceFor: advances to the next column or page when the height will
        // not fit in the space left (a 2% rounding slack via HasSpaceFor), unless already at a region top
        // or the height exceeds a whole column. Returns whether it broke.
        bool EnsureSpaceFor(float height)
        {
            if (height > contentHeight || atRegionTop || HasSpaceFor(height))
            {
                return false;
            }

            AdvanceColumnOrPage();
            return true;
        }

        bool HasSpaceFor(float height) => y + height <= contentBottom + contentHeight * 0.02f;

        static bool HasExactRow(TableElement table)
        {
            foreach (var row in table.Rows)
            {
                if (row.IsExactHeight)
                {
                    return true;
                }
            }

            return false;
        }

        // The X offset that aligns a line of the given width within the available width. Centre and right
        // shift the whole line; left and justify sit at the left edge (justify's inter-word slack is a
        // later slice). A line wider than the available width (rare — an unbreakable word) is not shifted.
        static float AlignmentOffset(TextAlignment alignment, float availableWidth, float lineWidth)
        {
            var slack = availableWidth - lineWidth;
            if (slack <= 0)
            {
                return 0;
            }

            return alignment switch
            {
                TextAlignment.Center => slack / 2,
                TextAlignment.Right => slack,
                _ => 0
            };
        }

        // The runs to paint for a line: its text run segments, plus — on the first line of a list
        // paragraph — the list marker positioned in the hanging-indent gutter to the left of the text.
        IReadOnlyList<PlacedRun> LineRuns(ParagraphElement paragraph, LaidOutLine line, int lineIndex, float lineLeft)
        {
            var runs = MapRuns(line, lineLeft);
            if (lineIndex == 0 && paragraph.Properties.Numbering is { Text.Length: > 0 } numbering)
            {
                return [MarkerRun(paragraph, numbering, lineLeft), .. runs];
            }

            return runs;
        }

        // The list marker as a placed run: its text in the bullet or number font, a hanging indent to the
        // left of the text (or right-aligned just before it when there is no hanging indent). Font, colour
        // and position mirror PdfTextEngine's marker placement; the paragraph's LeftIndent already sets the
        // text edge, so the marker offsets back from there.
        PlacedRun MarkerRun(ParagraphElement paragraph, NumberingInfo numbering, float lineLeft)
        {
            var firstProperties = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new RunProperties();
            var useBulletFont = FontHelpers.UseBulletFont(numbering.Text, numbering.FontFamily);
            var markerProperties = new RunProperties
            {
                FontFamily = useBulletFont ? "Morph Bullets" : firstProperties.FontFamily,
                FontSizePoints = firstProperties.FontSizePoints,
                Bold = !useBulletFont && firstProperties.Bold,
                ColorHex = numbering.ColorHex ?? firstProperties.ColorHex
            };

            var markerWidth = measurer.MeasureRunWidth(numbering.Text, markerProperties);
            var hanging = (float) paragraph.Properties.HangingIndentPoints;
            var markerX = hanging > 0.01f
                ? lineLeft - hanging
                : lineLeft - markerWidth - 3f;

            return new PlacedRun(markerX, markerWidth, numbering.Text, markerProperties);
        }

        // Projects a laid-out line's run segments to placed runs at absolute X (the line's left edge plus
        // each segment's canonical offset). Shared by page-flow and cell-flow line placement.
        static PlacedRun[] MapRuns(LaidOutLine line, float lineLeft)
        {
            var runs = new PlacedRun[line.Runs.Count];
            for (var runIndex = 0; runIndex < line.Runs.Count; runIndex++)
            {
                var run = line.Runs[runIndex];
                runs[runIndex] = new PlacedRun(lineLeft + run.X, run.Width, run.Text, run.Properties);
            }

            return runs;
        }

        // Projects a laid-out line's inline images to placed images: the line's left edge plus each
        // image's offset, with its bottom sitting on the text baseline.
        static IReadOnlyList<PlacedImage> MapImages(LaidOutLine line, float lineLeft, float baseline)
        {
            if (line.Images.Count == 0)
            {
                return [];
            }

            var images = new PlacedImage[line.Images.Count];
            for (var imageIndex = 0; imageIndex < line.Images.Count; imageIndex++)
            {
                var image = line.Images[imageIndex];
                images[imageIndex] = new PlacedImage(lineLeft + image.X, baseline - image.Height, image.Width, image.Height, image.Data);
            }

            return images;
        }
    }
}
