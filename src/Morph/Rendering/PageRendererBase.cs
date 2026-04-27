/// <summary>
/// Backend-agnostic orchestration shared by SkiaPageRenderer and ImageSharpPageRenderer.
/// Owns table rendering, pagination, and cell-content fan-out. Backends supply only the
/// drawing primitives via the abstract members.
/// </summary>
abstract class PageRendererBase(RenderContextBase context)
{
    protected RenderContextBase Context => context;

    /// <summary>The text-layout engine used for measurement and (in derived backends) rendering.</summary>
    protected abstract IParagraphMeasurer Measurer { get; }

    /// <summary>True when there's a current canvas / page to draw on.</summary>
    protected abstract bool HasOutput { get; }

    /// <summary>Closes the current page and (typically) flushes it to the caller.</summary>
    protected abstract void FinishCurrentPage();

    /// <summary>Starts a fresh page, resetting per-page state.</summary>
    protected abstract void StartNewPage();

    /// <summary>Fills a rectangle with the parsed background color.</summary>
    protected abstract void DrawCellBackground(float pixelX, float pixelY, float pixelWidth, float pixelHeight, string hexColor);

    /// <summary>Strokes the visible edges of <paramref name="borders"/> around the cell rectangle.</summary>
    protected abstract void DrawCellBorders(float pixelX, float pixelY, float pixelWidth, float pixelHeight, CellBorders borders);

    /// <summary>Renders a paragraph constrained to a bounded x/width region.</summary>
    protected abstract void RenderParagraphInBounds(ParagraphElement paragraph, float x, float maxWidth);

    /// <summary>Renders an image scaled to fit the available width within a cell.</summary>
    protected abstract void RenderImageInCell(ImageElement image, float x, float maxWidth);

    /// <summary>Renders the contents of a btLr / tbRl rotated cell.</summary>
    protected abstract void RenderVerticalCellContent(TableCell cell, float cellX, float cellY, float cellWidth, float cellHeight, CellSpacing padding);

    /// <summary>
    /// Ensures there's space for <paramref name="height"/> on the current page; otherwise
    /// moves to the next column or page. Content taller than a full page renders at the
    /// current position rather than triggering a useless break.
    /// </summary>
    protected void EnsureSpaceFor(float height)
    {
        if (height > context.ContentHeight)
        {
            return;
        }

        if (!context.HasSpaceFor(height) &&
            context.CurrentY > context.ContentTop)
        {
            if (!context.MoveToNextColumn())
            {
                FinishCurrentPage();
                StartNewPage();
            }
        }
    }

    /// <summary>
    /// Renders a content control inside a table cell by reusing
    /// <see cref="RenderParagraphInBounds"/> with a synthetic paragraph.
    /// </summary>
    protected void RenderContentControlInCell(ContentControlElement control, float x, float maxWidth)
    {
        if (!HasOutput)
        {
            return;
        }

        // Use styled runs if available, otherwise fall back to plain text.
        ParagraphElement para;
        if (control.Runs is {Count: > 0})
        {
            para = new()
            {
                Runs = control.Runs,
                Properties = new()
            };
        }
        else if (!string.IsNullOrEmpty(control.Content))
        {
            para = new()
            {
                Runs =
                [
                    new()
                    {
                        Text = control.Content,
                        Properties = new()
                    }
                ],
                Properties = new()
            };
        }
        else
        {
            return;
        }

        RenderParagraphInBounds(para, x, maxWidth);
    }

    /// <summary>
    /// Top-level table render. Decides between single-page rendering and row-by-row
    /// pagination based on whether the table fits in the remaining content area.
    /// </summary>
    protected void RenderTable(TableElement table)
    {
        if (!HasOutput || table.Rows.Count == 0)
        {
            return;
        }

        var colCount = TableLayout.GetColumnCount(table);
        var colWidths = TableLayout.CalculateColumnWidths(table, colCount, context.ContentWidth);
        var hasVerticalMerge = TableLayout.HasVerticalMerge(table);

        var rowHeights = TableHeightCalculator.CalculateRowHeights(table, colWidths, Measurer, hasVerticalMerge);
        var totalHeight = rowHeights.Sum();

        // Allow a 10% tolerance on the page-overflow check; row-height measurement is conservative.
        var tableTolerance = context.ContentHeight * 0.10f;
        var needsRowByRowRendering = totalHeight > context.ContentHeight + tableTolerance;

        if (needsRowByRowRendering)
        {
            RenderTableRowByRow(table, colCount, colWidths, rowHeights);
        }
        else
        {
            // Word's layout often allows tables to slightly overflow (line-height rounding etc.).
            // Word 2013+ (mode 15) has more consistent table handling, so use a slightly higher tolerance.
            var tolerancePercent = context.Compatibility.CompatibilityMode >= 15 ? 0.02f : 0.01f;
            var tolerance = context.ContentHeight * tolerancePercent;
            var requiredHeight = totalHeight - tolerance;
            EnsureSpaceFor(requiredHeight);
            RenderTableRows(table, colCount, colWidths, rowHeights, hasVerticalMerge);
        }
    }

    protected float ComputeTableX(TableElement table, float[] colWidths)
    {
        var contentLeft = context.ContentLeft;
        var tableWidth = colWidths.Sum();
        var slack = context.ContentWidth - tableWidth;
        return table.Properties.Alignment switch
        {
            TextAlignment.Center => contentLeft + Math.Max(0, slack / 2),
            TextAlignment.Right => contentLeft + Math.Max(0, slack),
            _ => contentLeft
        };
    }

    /// <summary>
    /// Renders all table rows at the current position (used when the table fits on one page).
    /// Picks per-column Y tracking for vMerge tables and per-row tracking otherwise.
    /// </summary>
    void RenderTableRows(TableElement table, int colCount, float[] colWidths, float[] rowHeights, bool hasVerticalMerge)
    {
        var tableX = ComputeTableX(table, colWidths);
        var startY = context.CurrentY;

        if (hasVerticalMerge)
        {
            // Track Y per column so vertical merges line up properly.
            var columnYPositions = new float[colCount];
            for (var i = 0; i < colCount; i++)
            {
                columnYPositions[i] = startY;
            }

            RenderTableWithColumnTracking(table, colCount, colWidths, rowHeights, tableX, columnYPositions);

            // The cursor advances to the maximum column Y reached.
            context.CurrentY = columnYPositions.Max();
        }
        else
        {
            var currentY = startY;
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                RenderTableRow(table, rowIndex, colCount, colWidths, rowHeights, tableX, currentY);
                currentY += rowHeights[rowIndex];
            }

            context.CurrentY = currentY;
        }
    }

    void RenderTableWithColumnTracking(TableElement table, int colCount, float[] colWidths, float[] rowHeights, float tableX, float[] columnYPositions)
    {
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var currentX = tableX;
            var gridColIndex = 0;

            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var span = cell.Properties.GridSpan;

                // Sum column widths for horizontally merged cells.
                float cellWidth = 0;
                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    cellWidth += colWidths[gridColIndex + i];
                }

                // Skip cells continuing a vertical merge; the Restart cell drew over their area.
                if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                {
                    currentX += cellWidth;
                    gridColIndex += span;
                    continue;
                }

                var cellY = columnYPositions[gridColIndex];

                float cellHeight;
                if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
                {
                    // Use the full merged height so background fills the entire spanned area.
                    cellHeight = TableLayout.CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
                }
                else
                {
                    var padding = TableLayout.GetEffectivePadding(cell.Properties, table.Properties, row);
                    var contentWidth = cellWidth - (float) padding.Horizontal;
                    var contentHeight = TableHeightCalculator.MeasureCellHeight(cell, contentWidth, table.Properties, Measurer, row);
                    cellHeight = contentHeight + (float) padding.Vertical;
                }

                RenderTableCell(cell, currentX, cellY, cellWidth, cellHeight, table.Properties, row, rowIndex, gridColIndex, table.Rows.Count, colCount);

                for (var i = 0; i < span && gridColIndex + i < colCount; i++)
                {
                    columnYPositions[gridColIndex + i] = cellY + cellHeight;
                }

                currentX += cellWidth;
                gridColIndex += span;
            }
        }
    }

    /// <summary>
    /// Renders rows one at a time, triggering page breaks as needed and re-emitting any
    /// header rows after each break. Used when the table is taller than a single page.
    /// </summary>
    void RenderTableRowByRow(TableElement table, int colCount, float[] colWidths, float[] rowHeights)
    {
        // Count contiguous header rows from the top (w:tblHeader). They get re-rendered after each page break.
        var headerCount = 0;
        while (headerCount < table.Rows.Count && table.Rows[headerCount].IsHeader)
        {
            headerCount++;
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var rowHeight = rowHeights[rowIndex];

            var yBefore = context.CurrentY;
            EnsureSpaceFor(rowHeight);
            var pageBroke = context.CurrentY < yBefore;

            // After a page break, re-emit the header rows — but skip when the current row is itself
            // one of those headers (e.g. the very first row on the very first page).
            if (pageBroke && headerCount > 0 && rowIndex >= headerCount)
            {
                var tableXHeader = ComputeTableX(table, colWidths);
                for (var h = 0; h < headerCount; h++)
                {
                    var headerHeight = rowHeights[h];
                    var headerY = context.CurrentY;
                    RenderTableRow(table, h, colCount, colWidths, rowHeights, tableXHeader, headerY);
                    context.CurrentY += headerHeight;
                }
            }

            var tableX = ComputeTableX(table, colWidths);
            var currentY = context.CurrentY;

            RenderTableRow(table, rowIndex, colCount, colWidths, rowHeights, tableX, currentY);
            context.CurrentY += rowHeight;
        }
    }

    void RenderTableRow(TableElement table, int rowIndex, int colCount, float[] colWidths, float[] rowHeights, float tableX, float currentY)
    {
        var row = table.Rows[rowIndex];
        var rowHeight = rowHeights[rowIndex];
        var currentX = tableX;
        var gridColIndex = 0;

        for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
        {
            var cell = row.Cells[cellIndex];
            var span = cell.Properties.GridSpan;

            float cellWidth = 0;
            for (var i = 0; i < span && gridColIndex + i < colCount; i++)
            {
                cellWidth += colWidths[gridColIndex + i];
            }

            // Skip cells that continue a vertical merge above.
            if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
            {
                currentX += cellWidth;
                gridColIndex += span;
                continue;
            }

            var cellHeight = rowHeight;
            if (cell.Properties.VerticalMerge == VerticalMergeType.Restart)
            {
                // Use the full merged height so background fills the entire spanned area.
                cellHeight = TableLayout.CalculateVerticalMergeHeight(table, rowIndex, gridColIndex, rowHeights);
            }

            RenderTableCell(cell, currentX, currentY, cellWidth, cellHeight, table.Properties, row, rowIndex, gridColIndex, table.Rows.Count, colCount);

            currentX += cellWidth;
            gridColIndex += span;
        }
    }

    void RenderTableCell(TableCell cell, float x, float y, float width, float height, TableProperties tableProps, TableRow row, int rowIndex, int colIndex, int totalRows, int totalCols)
    {
        if (!HasOutput)
        {
            return;
        }

        var padding = TableLayout.GetEffectivePadding(cell.Properties, tableProps, row);
        var margin = TableLayout.GetEffectiveMargin(cell.Properties, tableProps);

        var cellX = x + (float) margin.Left;
        var cellY = y + (float) margin.Top;
        var cellWidth = width - (float) margin.Horizontal;
        var cellHeight = height - (float) margin.Vertical;

        var pixelX = context.PointsToPixels(cellX);
        var pixelY = context.PointsToPixels(cellY);
        var pixelWidth = context.PointsToPixels(cellWidth);
        var pixelHeight = context.PointsToPixels(cellHeight);

        if (cell.Properties.BackgroundColorHex != null)
        {
            DrawCellBackground(pixelX, pixelY, pixelWidth, pixelHeight, cell.Properties.BackgroundColorHex);
        }

        var borders = TableLayout.ResolveCellBorders(cell.Properties, tableProps, rowIndex, colIndex, totalRows, totalCols, row);
        if (borders != null)
        {
            DrawCellBorders(pixelX, pixelY, pixelWidth, pixelHeight, borders);
        }

        if (cell.Properties.TextDirection != CellTextDirection.LeftToRight)
        {
            RenderVerticalCellContent(cell, cellX, cellY, cellWidth, cellHeight, padding);
            return;
        }

        var savedY = context.CurrentY;

        var contentX = cellX + (float) padding.Left;
        var contentWidth = cellWidth - (float) padding.Horizontal;
        var availableHeight = cellHeight - (float) padding.Vertical;

        // Measure content height for vertical alignment.
        float contentHeight = 0;
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                // Account for bullet indent to match RenderParagraphInBounds behavior.
                float bulletIndent = para.Properties.Numbering != null ? 12 : 0;
                contentHeight += Measurer.MeasureParagraphHeightWithWidth(para, contentWidth - bulletIndent);
            }
            else if (element is ContentControlElement contentControl)
            {
                var measurePara = new ParagraphElement
                {
                    Runs = contentControl.Runs!,
                    Properties = new()
                };
                contentHeight += Measurer.MeasureParagraphHeightWithWidth(measurePara, contentWidth);
            }
            else if (element is ImageElement image)
            {
                var imageWidth = (float) image.WidthPoints;
                var imageHeight = (float) image.HeightPoints;
                if (imageWidth > contentWidth)
                {
                    var scale = contentWidth / imageWidth;
                    imageHeight *= scale;
                }

                contentHeight += imageHeight;
            }
        }

        var verticalOffset = cell.Properties.VerticalAlignment switch
        {
            CellVerticalAlignment.Center => Math.Max(0, (availableHeight - contentHeight) / 2),
            CellVerticalAlignment.Bottom => Math.Max(0, availableHeight - contentHeight),
            _ => 0 // Top alignment
        };

        // For cells that start a vertical merge (vMerge="restart"), Word uses reduced centering
        // — content sits closer to the top — so cap the offset at ~0.17 inches.
        if (cell.Properties is {VerticalMerge: VerticalMergeType.Restart, VerticalAlignment: CellVerticalAlignment.Center})
        {
            const float maxCenterOffset = 12f;
            verticalOffset = Math.Min(verticalOffset, maxCenterOffset);
        }

        context.CurrentY = cellY + (float) padding.Top + verticalOffset;

        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                RenderParagraphInBounds(para, contentX, contentWidth);
            }
            else if (element is ContentControlElement contentControl)
            {
                RenderContentControlInCell(contentControl, contentX, contentWidth);
            }
            else if (element is ImageElement image)
            {
                RenderImageInCell(image, contentX, contentWidth);
            }
        }

        context.CurrentY = savedY;
    }
}
