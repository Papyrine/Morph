/// <summary>
/// Flows a document's block content into pages once, backend-independently — the heart of the layout
/// engine (<c>docs/layout-engine-proposal.md</c>, step 3). Handles single-column block flow with
/// **line-level** page breaks (a paragraph too tall for the space left splits at a line boundary and
/// continues on the next page, which the raster backends cannot do today) and **row-level** table
/// breaks (a table taller than a page flows row by row, re-emitting <c>w:tblHeader</c> rows after each
/// break). It applies the measured height-model rules from <c>src/page_counts.md</c> — max-collapse
/// paragraph spacing, space-before dropped at an automatically broken page top, and the empty-paragraph
/// mark line.
///
/// <para>Paragraph flow fits lines exactly (the canonical measurer does not over-measure, so no slack is
/// needed); table pagination mirrors the raster backend's tolerances (row height measurement carries the
/// shared <see cref="TableHeightCalculator"/>'s padding conventions), so the two paginate a table
/// identically until a canonical row measurement retires the slack.</para>
///
/// <para>Deferred to later slices, and noted so a document using them is not yet expected to paginate:
/// multi-column sections and column breaks, widow/orphan and keep-next/keep-lines, floats and their wrap
/// exclusions, floating tables, header/footer band height, and even/odd section-break parity. Other
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
        readonly float contentLeft = (float) page.MarginLeft;
        readonly float contentWidth = (float) page.ContentWidth;
        readonly float contentHeight = (float) (page.HeightPoints - page.MarginTop - page.MarginBottom);
        readonly List<LaidOutPage> pages = [];

        List<PlacedItem> items = [];
        float y;
        bool atPageTop = true;
        float lastAfter;
        bool currentPageExplicit;

        public LaidOutDocument Run(IReadOnlyList<DocumentElement> elements)
        {
            y = contentTop;

            foreach (var element in elements)
            {
                switch (element)
                {
                    case PageBreakElement:
                        // A page break starts a page even at the top of one, so N consecutive breaks
                        // yield N blank pages (ledger experiment 18); the page is marked explicit so it
                        // survives FinishPage's keep test.
                        FinishPage(nextPageExplicit: true);
                        break;

                    case SectionBreakElement { BreakType: not SectionBreakType.Continuous }:
                        // Even/odd parity is a later slice; treated as a plain page break here.
                        if (!atPageTop)
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

                    // Floats, images, column breaks and continuous sections are later slices.
                }
            }

            // Emit the trailing page. FinishPage keeps it only when it carries content, is an
            // explicit-break blank, or is the first page — so a natural trailing-overflow blank is
            // dropped while a deliberate one survives.
            FinishPage(false);

            return new(pages);
        }

        // Emits the in-progress page and starts a fresh one. A page is kept when it has content, when it
        // is a deliberate blank left by an explicit break (Word does not absorb those), or when it is the
        // only page; a natural trailing-overflow blank is dropped.
        void FinishPage(bool nextPageExplicit)
        {
            if (items.Count > 0 || currentPageExplicit || pages.Count == 0)
            {
                pages.Add(new(pages.Count + 1, page, items));
            }

            items = [];
            y = contentTop;
            atPageTop = true;
            lastAfter = 0;
            currentPageExplicit = nextPageExplicit;
        }

        void PlaceParagraph(ParagraphElement paragraph)
        {
            var properties = paragraph.Properties;
            if (properties.PageBreakBefore && !atPageTop)
            {
                FinishPage(false);
            }

            var paragraphLines = measurer.LayoutLines(paragraph, contentWidth);
            var isEmpty = paragraphLines.Count == 1 && paragraphLines[0].Width <= 0;
            var lineLeft = contentLeft + (float) properties.LeftIndentPoints;

            // Space-before, collapsed with the previous paragraph's after (max, not sum) and dropped at
            // the top of an automatically broken page. If the collapsed gap plus the first line overflows,
            // the line-level break below resets the cursor — the same space-before drop for the moved
            // paragraph.
            if (!atPageTop)
            {
                y += Math.Max(lastAfter, (float) properties.SpacingBeforePoints);
            }

            for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
            {
                var line = paragraphLines[lineIndex];
                if (!atPageTop && y + line.Height > contentBottom)
                {
                    FinishPage(false);
                }

                items.Add(new PlacedLine(lineLeft, y, line.Width, line.Height, paragraph, lineIndex));
                y += line.Height;
                atPageTop = false;
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

            var colWidths = TableLayout.CalculateColumnWidths(table, colCount, contentWidth, measurer);
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

            // The previous paragraph's after-spacing precedes the table (a table has no before-spacing to
            // collapse it with), and never applies at a fresh page top.
            if (!atPageTop)
            {
                y += lastAfter;
            }

            lastAfter = 0;

            // A table over 110% of a page's content height flows row by row (the raster backend's
            // needsRowByRowRendering); otherwise it stays whole and moves down as a unit if needed.
            if (totalHeight > contentHeight * 1.10f)
            {
                PlaceTableRowByRow(table, rowHeights, tableWidth);
                return;
            }

            // Narrow pre-advance for the fixed-height-row letter layouts: lift the whole table onto the
            // next page when it would otherwise clip its exact row, ahead of the softer whole-table move.
            if (!atPageTop && totalHeight > 0)
            {
                var remaining = Math.Max(0f, contentBottom - y);
                if (totalHeight > remaining + 5f && HasExactRow(table))
                {
                    FinishPage(false);
                }
            }

            // Whole-table move: mirrors EnsureSpaceFor(totalHeight − 2%) — a flow table may over-spill the
            // bottom margin by the shared rounding slack before it is pushed to the next page.
            EnsureSpaceFor(totalHeight - contentHeight * 0.02f);

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                items.Add(new PlacedTableRow(contentLeft, y, tableWidth, rowHeights[rowIndex], table, rowIndex, false));
                y += rowHeights[rowIndex];
                atPageTop = false;
            }
        }

        // A table taller than a page, placed row by row. Rows do not split: one that will not fit moves
        // whole to the next page. w:tblHeader rows are re-emitted after each break, and a trailing run of
        // empty rows is absorbed into the bottom margin rather than starting a page for them.
        void PlaceTableRowByRow(TableElement table, float[] rowHeights, float tableWidth)
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
                        items.Add(new PlacedTableRow(contentLeft, y, tableWidth, rowHeights[headerIndex], table, headerIndex, true));
                        y += rowHeights[headerIndex];
                        atPageTop = false;
                    }
                }

                items.Add(new PlacedTableRow(contentLeft, y, tableWidth, rowHeight, table, rowIndex, false));
                y += rowHeight;
                atPageTop = false;
            }
        }

        // Mirrors PageRendererBase.EnsureSpaceFor: breaks to a new page when the height will not fit in
        // the space left (a 2% rounding slack via HasSpaceFor), unless already at the page top or the
        // height exceeds a whole page. Returns whether it broke.
        bool EnsureSpaceFor(float height)
        {
            if (height > contentHeight || atPageTop || HasSpaceFor(height))
            {
                return false;
            }

            FinishPage(false);
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
    }
}
