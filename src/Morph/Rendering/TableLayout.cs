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

    /// <summary>
    /// Whether a row draws anything: a shaded cell, a run with text or an inline image, or any
    /// non-paragraph cell content. A trailing run of rows for which this is false is absorbed into the
    /// bottom margin rather than forcing a continuation page (a letter template's empty spacer row).
    /// </summary>
    internal static bool RowHasVisibleContent(TableRow row)
    {
        foreach (var cell in row.Cells)
        {
            if (!string.IsNullOrEmpty(cell.Properties.BackgroundColorHex))
            {
                return true;
            }

            foreach (var element in cell.Content)
            {
                if (element is ParagraphElement paragraph)
                {
                    foreach (var run in paragraph.Runs)
                    {
                        if (run.InlineImageData != null || !string.IsNullOrWhiteSpace(run.Text))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    // Nested tables, images, content controls, form fields — all draw something.
                    return true;
                }
            }
        }

        return false;
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

    /// <summary>
    /// Resolves the table's column widths against the width available to it.
    ///
    /// <para><b>OverflowsTextColumn.</b> An AUTOFIT table is squeezed to fit
    /// <paramref name="availableWidth"/>; a FIXED-layout one (<c>w:tblLayout w:type="fixed"</c>)
    /// keeps its declared grid verbatim even when that is wider, because Word lets it bleed past
    /// the margins. That is how a template's banner table spans the full page:
    /// <c>nonstandard_main_part_name</c>'s header declares a 625.4pt grid at a -79.65pt indent
    /// inside a 487.35pt column, and Word draws it edge to edge — squeezing it to the column left
    /// the bar stopping 78% across the page. It matches the contract
    /// <see cref="TableProperties.IsAutoFit"/> already documented.</para>
    /// </summary>
    internal static float[] CalculateColumnWidths(TableElement table, int colCount, float availableWidth, IParagraphMeasurer? measurer = null)
    {
        var widths = new float[colCount];
        var gridWidths = table.Properties.GridColumnWidths;
        var isAutoFit = table.Properties.IsAutoFit;

        // w:tblGrid defines the columns and the per-cell w:tcW is advisory, so the grid wins when they
        // disagree. labels/13 is the proof: its grid is 11376 twips, exactly the text column, while its
        // cells declare 11724 (+17.4pt). Word lays the sheet out at the grid. Reading the tcW sum
        // instead only looked right while over-wide tables were squeezed back to the column — the
        // squeeze was cancelling the wrong width, and removing it exposed the label columns running off
        // the page.
        //
        // This held only for FIXED-layout tables until 2026-08-06, which left an autofit table's grid on
        // the floor: the loop below takes a width only from a single-span cell carrying an explicit
        // w:tcW, so a column covered exclusively by spanned cells stayed at zero and then split the
        // leftover evenly with its neighbours. newsletters/06's newspaper tables declare a grid of
        // [42.4, 141.6, 14.0, 9.0, 157.5, 13.5, 4.5, 7.3, 150.2]pt and came out
        // [42.4, 35.3, 35.3, 35.3, 157.5, 13.5, 35.3, 35.3, 150.2] — five columns flattened to their
        // 176.4pt total divided five ways. The middle newspaper column rendered 114pt wide against
        // Word's 156, so its text wrapped far longer, inflating one row by 88-210pt until two of the
        // four page-tables overflowed the 745.75pt content height and spilled a row onto an extra page
        // (6 pages against Word's 4). An authored grid is Word's starting point under autofit too:
        // autofit adjusts columns to their content, it does not discard the grid.
        var gridIsAuthoritative = gridWidths is {Count: > 0};

        var hasExplicitWidths = false;
        var columnHasExplicitWidth = new bool[colCount];

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
                    columnHasExplicitWidth[gridColIndex] = true;
                    hasExplicitWidths = true;
                }
                else if (span == 1 && props.WidthFraction.HasValue)
                {
                    // Percent-preferred cell (w:tcW type="pct"): resolve against the table's
                    // available width so a 45% layout cell gets 45%, not an equal share.
                    widths[gridColIndex] = Math.Max(widths[gridColIndex], (float) (props.WidthFraction.Value * availableWidth));
                    columnHasExplicitWidth[gridColIndex] = true;
                    hasExplicitWidths = true;
                }

                gridColIndex += span;
            }
        }

        // w:tblGrid is a CACHE of the layout Word last performed, not an input: the authoritative
        // preferred widths are w:tblW and the per-cell w:tcW. Because Word writes the grid out of
        // its own fitted layout, the grid is normally the BETTER number of the two — it already has
        // Word's content fitting baked in, and re-deriving it here can only drift. Measured over
        // the 285 corpus tables that carry a full single-span dxa w:tcW cover, the two never
        // disagree in SHAPE: normalise each to fractions of its own total and the largest
        // per-column difference anywhere is 0.10pp (agendas-minutes/13), median 0.00pp. labels/13's
        // 348-twip gap is 0.01pp of shape — a pure uniform scale, not a different layout.
        //
        // A grid that disagrees in shape is therefore not a variant reading, it is STALE: a
        // generator wrote w:tcW without asking Word to re-lay the table out. "Stocktake export
        // template v2.docx" declares a Name column of 6374tw against the grid's 1849tw — 32.7pp of
        // shape, two orders of magnitude past anything authored — and Word lays it out at the
        // w:tcW. Rendering the grid gave that column 88.6pt against Word's 258 and wrapped its
        // heading to five lines instead of two.
        //
        // So only a provably stale grid hands over to the seeded autofit below; a grid that agrees
        // with the cells keeps its long-standing verbatim treatment, which is what newsletters/05
        // needs (grid and w:tcW identical at [131.5, 52.8, 367.8]pt — re-fitting it moved the body
        // column 8pt off Word). Tables with no grid at all are left alone too: the explicit-widths
        // branch below already handles them and nothing shows it is wrong. Fixed-layout tables keep
        // the grid because they are sized verbatim rather than fitted, and
        // header_full_bleed_banner's 625.4pt banner grid depends on it.
        var explicitWidthsCoverAllColumns = hasExplicitWidths && Array.TrueForAll(columnHasExplicitWidth, _ => _);

        if (explicitWidthsCoverAllColumns &&
            isAutoFit &&
            measurer != null &&
            GridShapeIsStale(gridWidths, widths))
        {
            return CalculateContentBasedColumnWidths(table, colCount, availableWidth, measurer, widths);
        }

        if (hasExplicitWidths && !gridIsAuthoritative)
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

            // Zero-width (flexible) columns share the space the table has left over. When the table
            // declares its own width (w:tblW dxa / CSS px width → PreferredWidthPoints) that leftover
            // is measured against the DECLARED width, not the whole text column: Word sizes
            // html_table_styled's `width:400px` table's Flexible column to 400px − 100px − 200px, not
            // to the page. Only when the table has no declared width does the flexible column fill
            // the available column.
            var fillTarget = table.Properties.PreferredWidthPoints is { } declaredWidth
                ? Math.Min((float) declaredWidth, availableWidth)
                : availableWidth;
            if (columnsWithoutWidth > 0 && totalExplicitWidth < fillTarget)
            {
                var remainingWidth = fillTarget - totalExplicitWidth;
                var perColumnWidth = remainingWidth / columnsWithoutWidth;
                for (var i = 0; i < colCount; i++)
                {
                    if (widths[i] == 0)
                    {
                        widths[i] = perColumnWidth;
                    }
                }

                // Recompute after filling in zero-width columns
                totalExplicitWidth = fillTarget;
            }

            if (totalExplicitWidth > availableWidth && isAutoFit)
            {
                // Autofit only: a FIXED-layout table keeps its declared widths even when they
                // overflow the text column — see OverflowsTextColumn below.
                var scale = availableWidth / totalExplicitWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
            else if (totalExplicitWidth > 0 && table.Properties.FillContainer)
            {
                // Only grow columns when the table explicitly asked to (w:tblW w:type="pct"),
                // to the PCT TARGET — fraction × container — and then regardless of
                // tblLayout: Word scales a FIXED-layout pct table's columns to the target too
                // (cards/15's card table declares tcW 10800 under tblW 5000pct on an 11520
                // grid — Word lays the column out at 11520, and the 36pt shortfall squeezed
                // the card-back placeholder into 6 lines vs Word's 5). The fraction matters:
                // labels/15 is 4880 pct (97.6%) and its widths already sum to exactly that —
                // scaling it to 100% shifted all eight label columns. When w:tblW is dxa, the
                // table is a fixed size; when it's missing or auto, Word fits to content and
                // leaves whitespace on the right.
                var target = (float) (availableWidth * (table.Properties.PreferredWidthFraction ?? 1.0));
                if (totalExplicitWidth < target)
                {
                    var scale = target / totalExplicitWidth;
                    for (var i = 0; i < colCount; i++)
                    {
                        widths[i] *= scale;
                    }
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

            if (totalWidth > availableWidth && totalWidth > 0 && isAutoFit)
            {
                // Autofit only, as in the explicit-widths branch above.
                var scale = availableWidth / totalWidth;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] *= scale;
                }
            }
            else if ((isAutoFit || table.Properties.FillContainer) && totalWidth > 0 &&
                     table.Properties.PreferredWidthPoints == null)
            {
                // Same grow rule for grid-only widths — autofit tables grow to the container
                // (the long-standing behaviour), pct tables of ANY layout grow to their pct
                // target (see the explicit-widths branch). Skip when the table set an
                // explicit w:tblW dxa width — that's a fixed size, not a "fill to container"
                // hint, so growing the columns would override the user's intent.
                var target = table.Properties.FillContainer
                    ? (float) (availableWidth * (table.Properties.PreferredWidthFraction ?? 1.0))
                    : availableWidth;
                if (totalWidth < target)
                {
                    var scale = target / totalWidth;
                    for (var i = 0; i < colCount; i++)
                    {
                        widths[i] *= scale;
                    }
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
    /// Whether <c>w:tblGrid</c> describes a different column SHAPE than the cells' own
    /// <c>w:tcW</c> — the signature of a grid left stale by a generator that wrote cell widths
    /// without re-laying the table out. Each side is normalised to fractions of its own total, so
    /// a table whose grid and cells differ only by a uniform scale (labels/13, cards/*, wedding/03
    /// — all a w:tblW pct or dxa target applied to one side) reads as agreeing, which it does.
    ///
    /// The 2pp threshold sits two orders of magnitude clear of both populations: no authored
    /// corpus table exceeds 0.10pp, and the stale case that motivated this is 32.7pp.
    /// </summary>
    static bool GridShapeIsStale(IReadOnlyList<double>? gridWidths, float[] declaredWidths)
    {
        if (gridWidths == null ||
            gridWidths.Count != declaredWidths.Length)
        {
            return false;
        }

        var gridTotal = 0d;
        foreach (var width in gridWidths)
        {
            gridTotal += width;
        }

        var declaredTotal = 0f;
        foreach (var width in declaredWidths)
        {
            declaredTotal += width;
        }

        if (gridTotal <= 0 ||
            declaredTotal <= 0)
        {
            return false;
        }

        for (var i = 0; i < declaredWidths.Length; i++)
        {
            if (Math.Abs(gridWidths[i] / gridTotal - declaredWidths[i] / declaredTotal) > 0.02)
            {
                return true;
            }
        }

        return false;
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
    ///
    /// <para><b>declaredPrefs.</b> When every column carries an explicit <c>w:tcW</c>, those
    /// widths are the preferred ones and content is only allowed to WIDEN a column, never to
    /// narrow it below its longest unbreakable token. That is measurably Word's rule: in
    /// "Stocktake export template v2.docx" the Challenges column declares 60.5pt but holds the
    /// unbreakable <c>Legislative/regulatory/constitutional</c>, and Word lays the column out at
    /// 149.3pt — exactly that token plus padding — then takes the 88.8pt back from the other six
    /// in proportion to their slack above their own minimums, which is what the interpolation
    /// below already does. The table total stays pinned to <c>w:tblW</c> (701.45pt there).</para>
    /// </summary>
    static float[] CalculateContentBasedColumnWidths(TableElement table, int colCount, float availableWidth, IParagraphMeasurer measurer, float[]? declaredPrefs = null)
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

        if (declaredPrefs != null)
        {
            for (var i = 0; i < colCount; i++)
            {
                prefs[i] = Math.Max(declaredPrefs[i], mins[i]);
            }
        }

        // A table that declared w:tblW dxa is that size, so the columns are fitted to it rather
        // than to the whole text column — but never past the column, which is where an autofit
        // table gets squeezed. Only the seeded path reads it: without declared widths the target
        // has always been the available width, and the FillContainer scaling below still owns the
        // pct case in both.
        var fitTarget = availableWidth;
        if (declaredPrefs != null &&
            tableProps.PreferredWidthPoints is { } declaredTableWidth)
        {
            fitTarget = Math.Min((float) declaredTableWidth, availableWidth);
        }

        var sumPref = 0f;
        var sumMin = 0f;
        for (var i = 0; i < colCount; i++)
        {
            sumPref += prefs[i];
            sumMin += mins[i];
        }

        if (sumPref <= 0)
        {
            var equal = fitTarget / colCount;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = equal;
            }

            return widths;
        }

        if (sumPref <= fitTarget)
        {
            // Two flavours:
            //  * w:tblW w:type="pct" said the table fills its container — distribute the
            //    available width proportional to content prefs so col1=col2=… add up to
            //    the page width even when no explicit cell widths are present.
            //  * No w:tblW (or w:type="auto") — autofit hugs the content, so a small
            //    "Col 1 / R1C1" grid doesn't span the whole page.
            //  * Seeded from w:tcW under a declared w:tblW dxa — the table is that size, so the
            //    shortfall is shared out rather than left as trailing whitespace.
            if (tableProps.FillContainer)
            {
                // Scale to the pct target, not blanket 100% (see the explicit-widths branch).
                var target = (float) (availableWidth * (tableProps.PreferredWidthFraction ?? 1.0));
                var scale = target / sumPref;
                for (var i = 0; i < colCount; i++)
                {
                    widths[i] = prefs[i] * scale;
                }
            }
            else if (declaredPrefs != null && tableProps.PreferredWidthPoints.HasValue)
            {
                var scale = fitTarget / sumPref;
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
        else if (sumMin < fitTarget)
        {
            var ratio = (fitTarget - sumMin) / (sumPref - sumMin);
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = mins[i] + (prefs[i] - mins[i]) * ratio;
            }
        }
        else if (sumMin > 0)
        {
            var scale = fitTarget / sumMin;
            for (var i = 0; i < colCount; i++)
            {
                widths[i] = mins[i] * scale;
            }
        }
        else
        {
            var equal = fitTarget / colCount;
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
    // autoHeight is the Auto-rule height for this line, supplied by the caller because it depends
    // on the line's fragments: an inline image contributes its height unscaled while only the text
    // line box is multiplied (see each backend's AutoLineHeight). Passing it in keeps this
    // measurement path identical to the render path — they diverged before, so a table row holding
    // an image grew with the line multiplier here even though the rendered line did not.
    internal static float CalculateCompactLineHeight(float naturalHeight, float autoHeight, ParagraphProperties properties) =>
        properties.LineSpacingRule switch
        {
            LineSpacingRule.Exactly => (float) properties.LineSpacingPoints,
            LineSpacingRule.AtLeast => Math.Max(naturalHeight, (float) properties.LineSpacingPoints),
            _ => autoHeight
        };
}
