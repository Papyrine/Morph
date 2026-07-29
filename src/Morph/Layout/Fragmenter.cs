using System.Globalization;

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
/// <para>Paragraph flow places a line while its baseline clears the bottom margin, letting the last
/// line's descent and trailing line gap encroach the margin as Word does; table pagination mirrors the
/// raster backend's percentage tolerances so the two paginate a table identically until a canonical row
/// measurement retires the slack.</para>
///
/// <para>Columns are equal-width from a single <see cref="PageSettings"/> (the common case). Deferred to
/// later slices, and noted so a document using them is not yet expected to paginate: per-section geometry
/// changes (a section break switching column count or page size, including the continuous mid-page kind);
/// widow/orphan and keep-next/keep-lines; floats and their wrap exclusions; floating tables; images and
/// nested tables inside a cell; a tall header/footer band pushing the body's margin; and even/odd
/// section-break parity. Other non-paragraph, non-table elements are skipped for now.</para>
/// </summary>
sealed class Fragmenter(CanonicalParagraphMeasurer measurer)
{
    public LaidOutDocument Layout(
        IReadOnlyList<DocumentElement> elements,
        PageSettings page,
        HeaderFooterContent? header = null,
        HeaderFooterContent? footer = null,
        HeaderFooterContent? firstPageHeader = null,
        HeaderFooterContent? firstPageFooter = null) =>
        new Flow(measurer, page, header, footer, firstPageHeader, firstPageFooter).Run(elements);

    // One document's flow state. A fresh instance per Layout call keeps the cursor, the in-progress page
    // and the emitted pages together without leaking between runs (the Fragmenter itself is reusable).
    sealed class Flow(
        CanonicalParagraphMeasurer measurer,
        PageSettings page,
        HeaderFooterContent? header,
        HeaderFooterContent? footer,
        HeaderFooterContent? firstPageHeader,
        HeaderFooterContent? firstPageFooter)
    {
        readonly float contentTop = (float) page.MarginTop;
        readonly float contentBottom = (float) (page.HeightPoints - page.MarginBottom);
        readonly float contentHeight = (float) (page.HeightPoints - page.MarginTop - page.MarginBottom);
        readonly float fullContentLeft = (float) page.MarginLeft;
        readonly float fullContentWidth = (float) (page.WidthPoints - page.MarginLeft - page.MarginRight);
        readonly int columnCount = Math.Max(1, page.ColumnCount);
        readonly float columnWidth = (float) page.ColumnWidth;
        readonly float columnSpacing = (float) page.ColumnSpacing;
        readonly List<LaidOutPage> pages = [];

        // The header's behind-text floating images, resolved once to page positions and painted behind
        // every page's body (the decorative full-page frames of letter/label templates live here). Same
        // header on every page for now — first-page / even-page variants and footers are later slices.
        readonly IReadOnlyList<PlacedImage> backgroundImages = ResolveHeaderImages(header, page);


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
                // Header background images paint first (behind the body), then the header's text band, then
                // the body, then the footer band at the page bottom. None counts toward the page's keep test —
                // a page is kept for its body, not its decoration, repeated header or footer.
                var pageNumber = pages.Count + 1;
                var headerBand = HeaderBand(pageNumber);
                var footerBand = FooterBand(pageNumber);
                IReadOnlyList<PlacedItem> pageItems =
                    backgroundImages.Count == 0 && headerBand.Count == 0 && footerBand.Count == 0
                        ? items
                        : [.. backgroundImages, .. headerBand, .. items, .. footerBand];
                pages.Add(new(pageNumber, page, pageItems));
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

            // Paragraph-border box (w:pBdr): track the first line's top and last line's bottom so a single
            // border can be stroked around the whole paragraph. If it breaks across a column or page the
            // box would span the gap, so a break disables it (per-fragment borders are a later slice).
            float? borderTop = null;
            var borderBroke = false;

            for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
            {
                var line = paragraphLines[lineIndex];
                // A line fits if its baseline clears the bottom margin — Word lets the last line's descent
                // (and trailing line gap) encroach the margin rather than pushing it to the next page. This
                // is self-limiting: once a line's descent spills, y passes contentBottom and the next line
                // breaks. Without it, correct empty-paragraph spacing tips borderline pages one line early.
                if (!atRegionTop && y + line.Ascent > contentBottom)
                {
                    AdvanceColumnOrPage();
                    if (borderTop != null)
                    {
                        borderBroke = true;
                    }
                }

                var indentLeft = ColumnLeft + (float) properties.LeftIndentPoints;
                var lineLeft = indentLeft + AlignmentOffset(properties.Alignment, availableWidth, line.Width);
                var baseline = y + line.Ascent;

                // Paragraph shading (w:shd) fills the paragraph's column box behind the text, regardless of
                // the text's own width or alignment — a centred title's band still spans the full column.
                // Emitted before the line so it paints behind; one per line tiles into a continuous band.
                if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
                {
                    items.Add(new PlacedShading(indentLeft, y, availableWidth, line.Height, properties.BackgroundColorHex));
                }

                items.Add(new PlacedLine(lineLeft, y, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                borderTop ??= y;
                y += line.Height;
                atRegionTop = false;
            }

            // Stroke the border around the paragraph's box, expanded by each edge's space (the gap Word
            // leaves between the text and the line). Emitted after the lines so it paints over any shading.
            if (!borderBroke && borderTop is { } boxTop && properties.Borders is { HasAnyBorder: true })
            {
                var left = ColumnLeft + (float) properties.LeftIndentPoints - (float) properties.BorderLeftSpacePoints;
                var top = boxTop - (float) properties.BorderTopSpacePoints;
                var width = availableWidth + (float) properties.BorderLeftSpacePoints + (float) properties.BorderRightSpacePoints;
                var height = y - boxTop + (float) properties.BorderTopSpacePoints + (float) properties.BorderBottomSpacePoints;
                items.Add(new PlacedBorder(left, top, width, height, properties.Borders!));
            }

            // An empty paragraph is a full-height spacer line AND carries its after-spacing into the
            // collapse with the next paragraph (measured against Word: two_columns' title/blank/body gap is
            // line + after + line + after, not line + after + line). It behaves like any other paragraph.
            lastAfter = (float) properties.SpacingAfterPoints;
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
                var content = LayoutCellContent(cell, cellX + (float) padding.Left, y + (float) padding.Top, cellWidth - (float) padding.Horizontal, cellHeight - (float) padding.Vertical, cell.Properties.VerticalAlignment);
                var borders = TableLayout.ResolveCellBorders(cell.Properties, table.Properties, rowIndex, gridColIndex, table.Rows.Count, colCount, row);

                // Behind-text floats (a label template's coloured cell background and freeform blobs) paint
                // before the cell's paragraphs, so prepend them to the content.
                var floatShapes = ResolveCellFloatShapes(cell, cellX, y);
                if (floatShapes.Count > 0)
                {
                    content = [.. floatShapes, .. content];
                }

                cells.Add(new PlacedCell(cellX, y, cellWidth, cellHeight, cell.Properties.BackgroundColorHex, borders, content));

                cellX += cellWidth;
                gridColIndex += span;
            }

            return new PlacedTableRow(tableX, y, tableWidth, rowHeight, table, rowIndex, isRepeatedHeader, cells);
        }

        // Cell-anchored behind-text shapes resolved to absolute boxes: the offset is measured from the
        // cell's top-left (Word anchors these to the cell frame). Only solid-fill / outline shapes render
        // for now; gradient and image fills, in-front-of-text floats, and the paragraph-anchor walk that
        // positions non-cell-top floats are later slices (PdfPainter.PaintShape skips what it can't draw).
        static IReadOnlyList<PlacedItem> ResolveCellFloatShapes(TableCell cell, float cellX, float cellY)
        {
            if (cell.Floats.Count == 0)
            {
                return [];
            }

            var shapes = new List<PlacedItem>();
            foreach (var element in cell.Floats)
            {
                if (element is not FloatingShapeElement shape || !shape.BehindText)
                {
                    continue;
                }

                var shapeX = cellX + (float) shape.HorizontalPositionPoints;
                var shapeY = cellY + (float) shape.VerticalPositionPoints;
                shapes.Add(new PlacedShape(shapeX, shapeY, (float) shape.WidthPoints, (float) shape.HeightPoints, shape));
            }

            return shapes;
        }

        // Stacks a cell's paragraphs from the top of its padded interior, wrapping each to the cell width,
        // then shifts them down for centre/bottom vertical alignment within the available height. No page
        // breaks — the row height already accommodates the content. Nested tables are a later slice.
        IReadOnlyList<PlacedItem> LayoutCellContent(TableCell cell, float contentLeft, float contentTop, float contentWidth, float availableHeight, CellVerticalAlignment verticalAlignment)
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
                // Space-before, collapsed with the previous paragraph's after (max, not sum). Unlike page
                // flow, a cell's FIRST paragraph keeps its space-before — TableHeightCalculator sizes the
                // cell with it, so the content must be positioned with it too or it floats to the top and
                // leaves the gap at the bottom.
                cellY += first
                    ? (float) properties.SpacingBeforePoints
                    : Math.Max(lastCellAfter, (float) properties.SpacingBeforePoints);
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

            // Centre/bottom alignment shifts the whole content down within the cell's available height
            // (top alignment leaves it at the padded top). Mirrors PageRendererBase's cell content offset.
            var offset = verticalAlignment switch
            {
                CellVerticalAlignment.Center => Math.Max(0f, (availableHeight - (cellY - contentTop)) / 2),
                CellVerticalAlignment.Bottom => Math.Max(0f, availableHeight - (cellY - contentTop)),
                _ => 0f
            };

            if (offset > 0.01f)
            {
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    lines[lineIndex] = ShiftDown(lines[lineIndex], offset);
                }
            }

            return lines;
        }

        // Moves a placed line (and its inline images) down the page by an offset, for cell vertical
        // alignment. Runs carry no Y of their own — the painter draws them at the line's baseline — so
        // shifting Y and Baseline moves the text with the box.
        static PlacedItem ShiftDown(PlacedItem item, float offset)
        {
            if (item is not PlacedLine line)
            {
                return item;
            }

            var images = line.Images.Count == 0 ? line.Images : ShiftImages(line.Images, offset);
            return line with { Y = line.Y + offset, Baseline = line.Baseline + offset, Images = images };
        }

        static IReadOnlyList<PlacedImage> ShiftImages(IReadOnlyList<PlacedImage> images, float offset)
        {
            var shifted = new PlacedImage[images.Count];
            for (var imageIndex = 0; imageIndex < images.Count; imageIndex++)
            {
                shifted[imageIndex] = images[imageIndex] with { Y = images[imageIndex].Y + offset };
            }

            return shifted;
        }

        // Resolves a header's behind-text floating images to absolute page positions. The full-page
        // decorative frames of letter/label templates are anchored here — page/margin/column horizontally,
        // at the header paragraph vertically — and a header-band-top estimate suffices since they span the
        // whole page. Front-text header art, header text/tables and footers are later slices.
        // Lays out a header or footer's paragraphs as a self-contained band from (left, top), wrapping each
        // to the band width and stacking it with its own spacing — no page breaks (a band fits its margin
        // area). Reuses the body's line mapping, alignment and shading. Tables in a band, borders, and
        // per-page page-number fields are later slices; an empty paragraph adds only its invisible mark line.
        IReadOnlyList<PlacedItem> LayoutBand(IReadOnlyList<DocumentElement> elements, float left, float top, float width)
        {
            var result = new List<PlacedItem>();
            var bandY = top;
            foreach (var element in elements)
            {
                if (element is not ParagraphElement paragraph)
                {
                    continue;
                }

                var properties = paragraph.Properties;
                bandY += (float) properties.SpacingBeforePoints;
                var paragraphLines = measurer.LayoutLineContents(paragraph, width);
                var availableWidth = width - (float) properties.LeftIndentPoints - (float) properties.RightIndentPoints;
                for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
                {
                    var line = paragraphLines[lineIndex];
                    var indentLeft = left + (float) properties.LeftIndentPoints;
                    var lineLeft = indentLeft + AlignmentOffset(properties.Alignment, availableWidth, line.Width);
                    var baseline = bandY + line.Ascent;
                    if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
                    {
                        result.Add(new PlacedShading(indentLeft, bandY, availableWidth, line.Height, properties.BackgroundColorHex));
                    }

                    result.Add(new PlacedLine(lineLeft, bandY, line.Width, line.Height, baseline, paragraph, lineIndex, LineRuns(paragraph, line, lineIndex, lineLeft), MapImages(line, lineLeft, baseline)));
                    bandY += line.Height;
                }

                bandY += (float) properties.SpacingAfterPoints;
            }

            return result;
        }

        // The header's text band for one page, at the header distance across the full content width. Page 1
        // takes the first-page header when the document has one (a "different first page" title header, which
        // may be empty); other pages take the default. Even-page headers are a later slice.
        IReadOnlyList<PlacedItem> HeaderBand(int pageNumber)
        {
            // With a title page, page 1 takes the first-page header — which may be null, meaning no header
            // (Word shows none on a title page), so it does not fall back to the default.
            var content = pageNumber == 1 && page.DifferentFirstPage ? firstPageHeader : header;
            return content == null
                ? []
                : LayoutBand(content.Elements, fullContentLeft, (float) page.HeaderDistance, fullContentWidth);
        }

        // The footer's text band for one page, anchored so its bottom sits the footer distance above the
        // page's bottom edge, with PAGE fields resolved to this page's number. Page 1 takes the first-page
        // footer when present (often empty — Word shows no footer on a title page). NUMPAGES (needs the final
        // total), even-page footers, footer tables and 3-way tab alignment are later slices.
        IReadOnlyList<PlacedItem> FooterBand(int pageNumber)
        {
            // With a title page, page 1 takes the first-page footer — often null, meaning no footer on the
            // title page (agendas-minutes/01), so it does not fall back to the default "Page N".
            var content = pageNumber == 1 && page.DifferentFirstPage ? firstPageFooter : footer;
            if (content == null)
            {
                return [];
            }

            var elements = SubstitutePageFields(content.Elements, pageNumber);
            var height = 0f;
            foreach (var element in elements)
            {
                if (element is ParagraphElement paragraph)
                {
                    height += measurer.MeasureParagraphHeightWithWidth(paragraph, fullContentWidth);
                }
            }

            var footerTop = (float) page.HeightPoints - (float) page.FooterDistance - height;
            return LayoutBand(elements, fullContentLeft, footerTop, fullContentWidth);
        }

        // Replaces each PAGE field run's cached text with this page's number, cloning only the paragraphs
        // that carry one (the common "Page N" footer). NUMPAGES / SECTIONPAGES keep their cached text until
        // a total-aware pass lands.
        static IReadOnlyList<DocumentElement> SubstitutePageFields(IReadOnlyList<DocumentElement> elements, int pageNumber)
        {
            var result = new List<DocumentElement>(elements.Count);
            foreach (var element in elements)
            {
                if (element is ParagraphElement paragraph && paragraph.Runs.Any(_ => _.PageField == PageFieldKind.Page))
                {
                    var runs = paragraph.Runs
                        .Select(_ => _.PageField == PageFieldKind.Page ? _.WithText(pageNumber.ToString(CultureInfo.InvariantCulture)) : _)
                        .ToList();
                    result.Add(new ParagraphElement
                    {
                        Runs = runs,
                        Properties = paragraph.Properties,
                        IsAnchorOnlyMark = paragraph.IsAnchorOnlyMark,
                        IsCollapsedCellMark = paragraph.IsCollapsedCellMark
                    });
                }
                else
                {
                    result.Add(element);
                }
            }

            return result;
        }

        static IReadOnlyList<PlacedImage> ResolveHeaderImages(HeaderFooterContent? header, PageSettings page)
        {
            if (header == null)
            {
                return [];
            }

            var images = new List<PlacedImage>();
            var marginLeft = (float) page.MarginLeft;
            var headerTop = (float) page.HeaderDistance;

            foreach (var element in header.Elements)
            {
                if (element is not FloatingImageElement image || image.ImageData is not { Length: > 0 } data || !image.BehindText)
                {
                    continue;
                }

                var imageX = image.HorizontalAnchor == HorizontalAnchor.Page
                    ? (float) image.HorizontalPositionPoints
                    : marginLeft + (float) image.HorizontalPositionPoints;
                var imageY = image.VerticalAnchor switch
                {
                    VerticalAnchor.Page => (float) image.VerticalPositionPoints,
                    VerticalAnchor.Margin => (float) page.MarginTop + (float) image.VerticalPositionPoints,
                    _ => headerTop + (float) image.VerticalPositionPoints
                };

                images.Add(new PlacedImage(imageX, imageY, (float) image.WidthPoints, (float) image.HeightPoints, data));
            }

            return images;
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
