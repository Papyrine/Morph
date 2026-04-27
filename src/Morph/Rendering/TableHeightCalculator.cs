/// <summary>
/// Shared table row/cell height computation. Backend-agnostic: paragraph layout
/// is delegated to <see cref="IParagraphMeasurer"/>.
/// </summary>
static class TableHeightCalculator
{
    static bool AllRowsHaveExplicitHeight(TableElement table)
    {
        foreach (var row in table.Rows)
        {
            if (!row.HeightPoints.HasValue)
            {
                return false;
            }
        }

        return true;
    }

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
            // Minimum row height
            float maxHeight = 20;

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

        // Second pass: Apply explicit row heights (w:trHeight).
        // Tables with vMerge AND every row carrying an explicit height use those heights
        // verbatim — common in letterhead-style layouts. Otherwise atLeast lets content expand.
        var allRowsHaveExplicitHeight = AllRowsHaveExplicitHeight(table);
        var useStrictHeights = hasVerticalMerge && allRowsHaveExplicitHeight;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (row.HeightPoints.HasValue)
            {
                var explicitHeight = (float) row.HeightPoints.Value;
                if (row.IsExactHeight || useStrictHeights)
                {
                    heights[rowIndex] = explicitHeight;
                }
                else
                {
                    heights[rowIndex] = Math.Max(heights[rowIndex], explicitHeight);
                }
            }
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

        return heights;
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

        // Collect paragraphs (and content-control wrappers) so we know first/last for spacing collapse.
        var paragraphs = new List<(ParagraphElement para, float bulletIndent)>();
        foreach (var element in cell.Content)
        {
            if (element is ParagraphElement para)
            {
                float bulletIndent = para.Properties.Numbering != null ? 12 : 0;
                paragraphs.Add((para, bulletIndent));
            }
            else if (element is ContentControlElement contentControl)
            {
                ParagraphElement? measurePara = null;
                if (contentControl.Runs is {Count: > 0})
                {
                    measurePara = new()
                    {
                        Runs = contentControl.Runs,
                        Properties = new()
                    };
                }
                else if (!string.IsNullOrEmpty(contentControl.Content))
                {
                    measurePara = new()
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = contentControl.Content,
                                Properties = new()
                            }
                        ],
                        Properties = new()
                    };
                }

                if (measurePara != null)
                {
                    paragraphs.Add((measurePara, 0));
                }
            }
            else if (element is TableElement {Properties.IsFloating: false})
            {
                height += 50;
            }
        }

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var (para, bulletIndent) = paragraphs[i];
            var lines = measurer.LayoutParagraphForMeasurement(para, contentWidth - bulletIndent);
            var props = para.Properties;

            if (i == 0)
            {
                var extra = (float) props.SpacingBeforePoints - (float) padding.Top;
                if (extra > 0)
                {
                    height += extra;
                }
            }
            else
            {
                height += (float) props.SpacingBeforePoints;
            }

            foreach (var lineHeight in lines)
            {
                height += lineHeight;
            }

            if (i == paragraphs.Count - 1)
            {
                var extra = (float) props.SpacingAfterPoints - (float) padding.Bottom;
                if (extra > 0)
                {
                    height += extra;
                }
            }
            else
            {
                height += (float) props.SpacingAfterPoints;
            }
        }

        return height;
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
