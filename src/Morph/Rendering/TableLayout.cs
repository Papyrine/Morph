namespace WordRender.Rendering;

/// <summary>
/// Shared table layout calculations used by both rendering backends.
/// </summary>
static class TableLayout
{
    internal static CellSpacing GetEffectivePadding(TableCellProperties cellProps, TableProperties tableProps) =>
        cellProps.Padding ?? tableProps.DefaultCellPadding;

    internal static CellSpacing GetEffectiveMargin(TableCellProperties cellProps, TableProperties tableProps) =>
        cellProps.Margin ?? tableProps.DefaultCellMargin;

    internal static CellBorders? ResolveCellBorders(TableCellProperties cellProps, TableProperties tableProps, int rowIndex, int colIndex, int totalRows, int totalCols)
    {
        if (cellProps.Borders != null)
        {
            return cellProps.Borders;
        }

        var outer = tableProps.DefaultBorders;
        var insideH = tableProps.InsideHorizontalBorder;
        var insideV = tableProps.InsideVerticalBorder;

        if (outer == null && insideH == null && insideV == null)
        {
            return null;
        }

        var isFirstRow = rowIndex == 0;
        var isLastRow = rowIndex == totalRows - 1;
        var isFirstCol = colIndex == 0;
        var isLastCol = colIndex == totalCols - 1;

        return new()
        {
            Top = isFirstRow ? outer?.Top ?? BorderEdge.None : insideH ?? BorderEdge.None,
            Bottom = isLastRow ? outer?.Bottom ?? BorderEdge.None : insideH ?? BorderEdge.None,
            Left = isFirstCol ? outer?.Left ?? BorderEdge.None : insideV ?? BorderEdge.None,
            Right = isLastCol ? outer?.Right ?? BorderEdge.None : insideV ?? BorderEdge.None
        };
    }

    internal static float[] CalculateColumnWidths(TableElement table, int colCount, float availableWidth)
    {
        var widths = new float[colCount];
        var gridWidths = table.Properties.GridColumnWidths;

        var hasExplicitWidths = false;

        foreach (var row in table.Rows)
        {
            var gridColIndex = 0;
            for (var cellIndex = 0; cellIndex < row.Cells.Count && gridColIndex < colCount; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var props = cell.Properties;
                var span = props.GridSpan;

                if (span == 1 && props.WidthPoints.HasValue)
                {
                    widths[gridColIndex] = Math.Max(widths[gridColIndex], (float) props.WidthPoints.Value);
                    hasExplicitWidths = true;
                }

                gridColIndex += span;
            }
        }

        if (hasExplicitWidths)
        {
            var totalExplicitWidth = widths.Sum();
            var columnsWithoutWidth = widths.Count(w => w == 0);

            if (columnsWithoutWidth > 0 && totalExplicitWidth < availableWidth)
            {
                var remainingWidth = availableWidth - totalExplicitWidth;
                var perColumnWidth = remainingWidth / columnsWithoutWidth;
                for (var i = 0; i < colCount; i++)
                {
                    if (widths[i] == 0)
                    {
                        widths[i] = perColumnWidth;
                    }
                }
            }

            var totalWidth = widths.Sum();
            if (totalWidth > availableWidth)
            {
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
        }
        else if (gridWidths is {Count: > 0})
        {
            for (var i = 0; i < colCount && i < gridWidths.Count; i++)
            {
                widths[i] = (float) gridWidths[i];
            }

            if (gridWidths.Count < colCount)
            {
                var avgWidth = (float) gridWidths.Average();
                for (var i = gridWidths.Count; i < colCount; i++)
                {
                    widths[i] = avgWidth;
                }
            }

            var totalWidth = widths.Sum();
            if (totalWidth > availableWidth && totalWidth > 0)
            {
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
        }
        else
        {
            var cellWidth = availableWidth / colCount;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = cellWidth;
            }
        }

        return widths;
    }

    internal static float CalculateVerticalMergeHeight(TableElement table, int startRowIndex, int gridColIndex, float[] rowHeights)
    {
        var height = rowHeights[startRowIndex];
        for (var r = startRowIndex + 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var col = 0;
            var found = false;
            foreach (var cell in row.Cells)
            {
                if (col == gridColIndex)
                {
                    if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                    {
                        height += rowHeights[r];
                        found = true;
                    }

                    break;
                }

                col += cell.Properties.GridSpan;
            }

            if (!found)
            {
                break;
            }
        }

        return height;
    }

    internal static int CalculateVerticalMergeRowSpan(TableElement table, int startRowIndex, int gridColIndex)
    {
        var rowSpan = 1;
        for (var r = startRowIndex + 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var col = 0;
            var found = false;
            foreach (var cell in row.Cells)
            {
                if (col == gridColIndex)
                {
                    if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                    {
                        rowSpan++;
                        found = true;
                    }

                    break;
                }

                col += cell.Properties.GridSpan;
            }

            if (!found)
            {
                break;
            }
        }

        return rowSpan;
    }

    /// <summary>
    /// Calculates the effective line height for table cell measurement (compact, no boost).
    /// </summary>
    internal static float CalculateCompactLineHeight(float naturalHeight, ParagraphProperties props)
    {
        var lineHeight = props.LineSpacingRule switch
        {
            LineSpacingRule.Exactly => (float) props.LineSpacingPoints,
            LineSpacingRule.AtLeast => Math.Max(naturalHeight, (float) props.LineSpacingPoints),
            _ => naturalHeight * (float) props.LineSpacingMultiplier
        };

        return lineHeight;
    }
}
