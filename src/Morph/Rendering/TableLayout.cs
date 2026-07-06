/// <summary>
/// Shared table layout calculations used by both rendering backends.
/// </summary>
static class TableLayout
{
    internal static int GetColumnCount(TableElement table)
    {
        if (table.Properties.GridColumnWidths?.Count > 0)
        {
            return table.Properties.GridColumnWidths.Count;
        }

        var maxSpan = 0;
        foreach (var row in table.Rows)
        {
            var rowSpan = 0;
            foreach (var cell in row.Cells)
            {
                rowSpan += cell.Properties.GridSpan;
            }

            if (rowSpan > maxSpan)
            {
                maxSpan = rowSpan;
            }
        }

        return maxSpan;
    }

    internal static bool HasVerticalMerge(TableElement table)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (cell.Properties.VerticalMerge is VerticalMergeType.Restart or VerticalMergeType.Continue)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the effective cell padding ("cell margin" in Word's UI). OOXML's
    /// <c>w:tblCellMar</c> appears at three scopes: table-level default
    /// (<see cref="TableProperties.DefaultCellPadding"/>), row-level override via
    /// <c>w:tblPrEx</c> (<see cref="TableRow.OverrideCellPadding"/>), and per-cell
    /// <c>w:tcMar</c> (<see cref="TableCellProperties.Padding"/>). Cell wins, then row, then table.
    /// </summary>
    internal static CellSpacing GetEffectivePadding(TableCellProperties cellProps, TableProperties tableProps, TableRow? row = null) =>
        cellProps.Padding ?? row?.OverrideCellPadding ?? tableProps.DefaultCellPadding;

    /// <summary>
    /// Cell margin (the gap *outside* the border). OOXML doesn't expose a row-level override
    /// for this — <c>w:tblPrEx</c> only carries <c>w:tblCellMar</c>, which Morph maps to
    /// padding. The <see cref="TableCellProperties.Margin"/> field is reserved for HTML
    /// inputs; DOCX inputs always leave it null.
    /// </summary>
    internal static CellSpacing GetEffectiveMargin(TableCellProperties cellProps, TableProperties tableProps) =>
        cellProps.Margin ?? tableProps.DefaultCellMargin;

    internal static CellBorders? ResolveCellBorders(TableCellProperties cellProps, TableProperties tableProps, int rowIndex, int colIndex, int totalRows, int totalCols, TableRow? row = null)
    {
        if (cellProps.Borders != null)
        {
            return cellProps.Borders;
        }

        // w:tblPrEx row-level overrides take precedence over the table's defaults.
        var outer = row?.OverrideBorders ?? tableProps.DefaultBorders;
        var insideH = row?.OverrideInsideHBorder ?? tableProps.InsideHorizontalBorder;
        var insideV = row?.OverrideInsideVBorder ?? tableProps.InsideVerticalBorder;

        if (outer == null &&
            insideH == null &&
            insideV == null)
        {
            return null;
        }

        // Detached-border model (w:tblCellSpacing > 0): every cell renders as an isolated
        // box with the table's *outer* border applied to all four edges. The inside
        // borders never appear because adjacent cells don't share an edge — there's a gap.
        if (tableProps.CellSpacingPoints > 0 && outer != null)
        {
            return new()
            {
                Top = outer.Top,
                Bottom = outer.Bottom,
                Left = outer.Left,
                Right = outer.Right
            };
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

    internal static float[] CalculateColumnWidths(TableElement table, int colCount, float availableWidth, IParagraphMeasurer? measurer = null)
    {
        var widths = new float[colCount];
        var gridWidths = table.Properties.GridColumnWidths;
        var isAutoFit = table.Properties.IsAutoFit;

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
                else if (span == 1 && props.WidthFraction.HasValue)
                {
                    // Percent-preferred cell (w:tcW type="pct"): resolve against the table's
                    // available width so a 45% layout cell gets 45%, not an equal share.
                    widths[gridColIndex] = Math.Max(widths[gridColIndex], (float) (props.WidthFraction.Value * availableWidth));
                    hasExplicitWidths = true;
                }

                gridColIndex += span;
            }
        }

        if (hasExplicitWidths)
        {
            var totalExplicitWidth = 0f;
            var columnsWithoutWidth = 0;
            foreach (var width in widths)
            {
                totalExplicitWidth += width;
                if (width == 0)
                {
                    columnsWithoutWidth++;
                }
            }

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

                // Recompute after filling in zero-width columns
                totalExplicitWidth = availableWidth;
            }

            if (totalExplicitWidth > availableWidth)
            {
                var scale = availableWidth / totalExplicitWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
            else if (isAutoFit && totalExplicitWidth > 0 && totalExplicitWidth < availableWidth &&
                     table.Properties.FillContainer)
            {
                // Autofit: only grow columns to fill the container when the table explicitly
                // asked to (w:tblW w:type="pct"). When w:tblW is dxa, the table is a fixed
                // size; when it's missing or auto, Word fits to content and leaves whitespace
                // on the right — growing here would make narrow tables (e.g. a vertical-text
                // sidebar) span the page.
                var scale = availableWidth / totalExplicitWidth;
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
                var avgWidth = 0f;
                foreach (var gw in gridWidths)
                {
                    avgWidth += (float) gw;
                }

                avgWidth /= gridWidths.Count;
                for (var i = gridWidths.Count; i < colCount; i++)
                {
                    widths[i] = avgWidth;
                }
            }

            var totalWidth = 0f;
            foreach (var width in widths)
            {
                totalWidth += width;
            }

            if (totalWidth > availableWidth && totalWidth > 0)
            {
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
            else if (isAutoFit && totalWidth > 0 && totalWidth < availableWidth &&
                     table.Properties.PreferredWidthPoints == null)
            {
                // Same autofit-grow rule for grid-only widths. Skip when the table set an
                // explicit w:tblW dxa width — that's a fixed size, not a "fill to container"
                // hint, so growing the columns would override the user's intent.
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
        }
        else
        {
            // No explicit widths anywhere. With autofit + a measurer, distribute by content
            // (Word's default behaviour when w:tblGrid carries bare w:gridCol entries with no
            // w:w). Without a measurer (or with fixed layout), fall back to equal columns.
            if (isAutoFit && measurer != null)
            {
                return CalculateContentBasedColumnWidths(table, colCount, availableWidth, measurer);
            }

            var cellWidth = availableWidth / colCount;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = cellWidth;
            }
        }

        return widths;
    }

    /// <summary>
    /// Content-based autofit: per column, take the max preferred (single-line natural)
    /// width and the max minimum (longest unbreakable token) width over its cells, then
    /// distribute available width:
    ///   - sum(pref) ≤ avail: scale pref up to fill,
    ///   - sum(min)  ≤ avail &lt; sum(pref): interpolate between min and pref,
    ///   - sum(min) &gt; avail: scale min down to fit (mirrors Word's autofit fallback when
    ///     even the longest unbreakable token can't fit the page).
    /// Multi-span cells contribute their content width split evenly across the columns
    /// they span; vertically merged continuation cells are skipped.
    /// </summary>
    static float[] CalculateContentBasedColumnWidths(TableElement table, int colCount, float availableWidth, IParagraphMeasurer measurer)
    {
        var prefs = new float[colCount];
        var mins = new float[colCount];
        var tableProps = table.Properties;

        foreach (var row in table.Rows)
        {
            var gridColIndex = 0;
            foreach (var cell in row.Cells)
            {
                if (gridColIndex >= colCount)
                {
                    break;
                }

                var props = cell.Properties;
                var span = Math.Max(1, props.GridSpan);

                if (props.VerticalMerge == VerticalMergeType.Continue)
                {
                    gridColIndex += span;
                    continue;
                }

                var padding = GetEffectivePadding(props, tableProps, row);
                var margin = GetEffectiveMargin(props, tableProps);
                var horizontalChrome = (float) (padding.Horizontal + margin.Horizontal);

                var (cellPref, cellMin) = MeasureCellContentWidth(cell, measurer);

                cellPref += horizontalChrome;
                cellMin += horizontalChrome;

                var perColPref = cellPref / span;
                var perColMin = cellMin / span;

                for (var s = 0; s < span && gridColIndex + s < colCount; s++)
                {
                    if (perColPref > prefs[gridColIndex + s])
                    {
                        prefs[gridColIndex + s] = perColPref;
                    }

                    if (perColMin > mins[gridColIndex + s])
                    {
                        mins[gridColIndex + s] = perColMin;
                    }
                }

                gridColIndex += span;
            }
        }

        var widths = new float[colCount];
        var sumPref = 0f;
        var sumMin = 0f;
        for (var i = 0; i < colCount; i++)
        {
            sumPref += prefs[i];
            sumMin += mins[i];
        }

        if (sumPref <= 0)
        {
            var equal = availableWidth / colCount;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = equal;
            }

            return widths;
        }

        if (sumPref <= availableWidth)
        {
            // Two flavours:
            //  * w:tblW w:type="pct" said the table fills its container — distribute the
            //    available width proportional to content prefs so col1=col2=… add up to
            //    the page width even when no explicit cell widths are present.
            //  * No w:tblW (or w:type="auto") — autofit hugs the content, so a small
            //    "Col 1 / R1C1" grid doesn't span the whole page.
            if (tableProps.FillContainer)
            {
                var scale = availableWidth / sumPref;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] = prefs[i] * scale;
                }
            }
            else
            {
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] = prefs[i];
                }
            }
        }
        else if (sumMin < availableWidth)
        {
            var ratio = (availableWidth - sumMin) / (sumPref - sumMin);
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = mins[i] + (prefs[i] - mins[i]) * ratio;
            }
        }
        else if (sumMin > 0)
        {
            var scale = availableWidth / sumMin;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = mins[i] * scale;
            }
        }
        else
        {
            var equal = availableWidth / colCount;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = equal;
            }
        }

        return widths;
    }

    static (float Preferred, float Minimum) MeasureCellContentWidth(TableCell cell, IParagraphMeasurer measurer)
    {
        var pref = 0f;
        var min = 0f;

        foreach (var element in cell.Content)
        {
            ParagraphElement? para = null;
            if (element is ParagraphElement direct)
            {
                para = direct;
            }
            else if (element is ContentControlElement {Runs.Count: > 0} contentControl)
            {
                // Autofit deliberately measures only runs-backed controls (text-only controls
                // never participated in width measurement); the shared wrapper keeps the
                // layout-cache key identical across the measure/render pipeline stages.
                para = contentControl.CellParagraph;
            }

            if (para == null)
            {
                continue;
            }

            // Natural single-line width: pass an effectively unbounded width so nothing wraps.
            var natural = measurer.MeasureParagraphNaturalWidth(para, float.MaxValue / 4);
            // Minimum width: pass 1pt so the layout breaks at every word boundary; the widest
            // remaining line is the longest unbreakable token (e.g. "john@company.com").
            var minimum = measurer.MeasureParagraphNaturalWidth(para, 1f);

            if (natural > pref)
            {
                pref = natural;
            }

            if (minimum > min)
            {
                min = minimum;
            }
        }

        return (pref, min);
    }

    // Vertical-merge occupancy, one pass per table: row index → the starting grid columns of
    // that row's vMerge-Continue cells. The span/height lookups walk rows through this map
    // instead of rescanning each row's cells from column zero for every Restart cell, which
    // was O(rows² × cells) on merge-heavy tables. A row accumulates into a merge run exactly
    // when a cell STARTS at the merge's grid column and is a Continue — a row whose cells jump
    // past the column ends the run, same as the old scan. Weakly keyed per parsed table, so
    // concurrent conversions and repeated measure/render passes share one map.
    static readonly ConditionalWeakTable<TableElement, HashSet<int>[]> verticalMergeContinueStarts = new();

    static HashSet<int>[] GetVerticalMergeContinueStarts(TableElement table) =>
        verticalMergeContinueStarts.GetValue(table, static keyTable =>
        {
            var map = new HashSet<int>[keyTable.Rows.Count];
            for (var r = 0; r < keyTable.Rows.Count; r++)
            {
                var starts = new HashSet<int>();
                var col = 0;
                foreach (var cell in keyTable.Rows[r].Cells)
                {
                    if (cell.Properties.VerticalMerge == VerticalMergeType.Continue)
                    {
                        starts.Add(col);
                    }

                    col += cell.Properties.GridSpan;
                }

                map[r] = starts;
            }

            return map;
        });

    internal static float CalculateVerticalMergeHeight(TableElement table, int startRowIndex, int gridColIndex, float[] rowHeights)
    {
        var continueStarts = GetVerticalMergeContinueStarts(table);
        var height = rowHeights[startRowIndex];
        for (var r = startRowIndex + 1; r < table.Rows.Count; r++)
        {
            if (!continueStarts[r].Contains(gridColIndex))
            {
                break;
            }

            height += rowHeights[r];
        }

        return height;
    }

    internal static int CalculateVerticalMergeRowSpan(TableElement table, int startRowIndex, int gridColIndex)
    {
        var continueStarts = GetVerticalMergeContinueStarts(table);
        var rowSpan = 1;
        for (var r = startRowIndex + 1; r < table.Rows.Count; r++)
        {
            if (!continueStarts[r].Contains(gridColIndex))
            {
                break;
            }

            rowSpan++;
        }

        return rowSpan;
    }

    /// <summary>
    /// Calculates the effective line height for table cell measurement (compact, no boost).
    /// </summary>
    internal static float CalculateCompactLineHeight(float naturalHeight, ParagraphProperties properties) =>
        properties.LineSpacingRule switch
        {
            LineSpacingRule.Exactly => (float) properties.LineSpacingPoints,
            LineSpacingRule.AtLeast => Math.Max(naturalHeight, (float) properties.LineSpacingPoints),
            _ => naturalHeight * (float) properties.LineSpacingMultiplier
        };
}
