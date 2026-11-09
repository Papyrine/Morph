using OoxmlTableCellProperties = DocumentFormat.OpenXml.Wordprocessing.TableCellProperties;
using OoxmlTableProperties = DocumentFormat.OpenXml.Wordprocessing.TableProperties;

/// <summary>
/// A table: its grid, rows, cells and the borders they declare.
/// </summary>
sealed partial class DocumentParser
{
    // A cell's w:gridSpan is an unbounded ST_DecimalNumber in the file. Left raw, a crafted value
    // (Word itself caps a table at 63 columns) flows through GetColumnCount into float[colCount]
    // allocations and per-column layout loops, so a single giant span can exhaust memory before a
    // page is even measured. Clamp to a ceiling far above any real table and floor at 1 so a
    // zero/negative span still advances the grid.
    const int maxGridSpan = 1000;

    static int ClampGridSpan(int value) => Math.Clamp(value, 1, maxGridSpan);

    // w:trPr/w:gridBefore and w:gridAfter — grid columns the row leaves empty at either end (TableRow.GridBefore).
    static (int Before, int After) RowGridSkips(OpenXmlElement row)
    {
        var properties = row.GetFirstChild<TableRowProperties>();
        var before = properties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
        var after = properties?.GetFirstChild<GridAfter>()?.Val?.Value ?? 0;
        return (Math.Clamp(before, 0, maxGridSpan), Math.Clamp(after, 0, maxGridSpan));
    }

    /// <summary>
    /// Parses a table, publishing its style id for the duration so paragraphs in its cells can
    /// pick up the table-style step of the paragraph cascade.
    /// </summary>
    TableElement? ParseTable(Table table, MainDocumentPart mainPart)
    {
        // Saved and restored rather than cleared: a nested table resolves its OWN style (or the
        // document default when it declares none — it does not inherit the outer table's), and
        // the outer table's later rows must still see the outer style.
        var previousTableStyleId = currentTableStyleId;
        currentTableStyleId = table.GetFirstChild<OoxmlTableProperties>()?
                                  .GetFirstChild<TableStyle>()?.Val?.Value
                              ?? defaultTableStyleId;
        tableDepth++;
        try
        {
            return ParseTableCore(table, mainPart);
        }
        finally
        {
            tableDepth--;
            currentTableStyleId = previousTableStyleId;
        }
    }

    // How deep tables nest before the parser stops building tables. Parsing, measuring and laying a table
    // out each recurse into the tables in its cells, so a document of tables nested hundreds deep
    // overflowed the stack — 300 of them killed the process from the parser, beyond any catch
    // (MalformedPackageTests). Past the bound a nested table's paragraphs are kept, in order, as the
    // cell's own: the text survives without the boxes around it. No corpus document nests past 3.
    const int maxTableDepth = 64;

    int tableDepth;

    IEnumerable<DocumentElement> FlattenTable(Table table, MainDocumentPart mainPart) =>
        table.Descendants<Paragraph>().SelectMany(_ => ParseParagraph(_, mainPart));

    /// <summary>
    /// A row's cells in document order, unwrapping any cell-level content control. Word wraps a
    /// templated cell in <c>w:sdt</c> (an <see cref="SdtCell"/> whose <see cref="SdtContentCell"/>
    /// holds the real <c>w:tc</c>): menus/03's "EVENT TITLE", resumes/19's Skills list and
    /// wedding/11's venue block live in one, and newsletters/09 has a row of 4 direct cells against
    /// an 11-column grid. Order is preserved so a row that mixes plain and wrapped cells keeps its
    /// left-to-right layout.
    /// </summary>
    static IEnumerable<DocumentFormat.OpenXml.Wordprocessing.TableCell> RowCells(
        DocumentFormat.OpenXml.Wordprocessing.TableRow row)
    {
        foreach (var child in row.ChildElements)
        {
            if (child is DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
            {
                yield return cell;
            }
            else if (child is SdtCell {SdtContentCell: { } content})
            {
                foreach (var wrapped in content.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
                {
                    yield return wrapped;
                }
            }
        }
    }

    TableElement? ParseTableCore(Table table, MainDocumentPart mainPart)
    {
        var rows = new List<TableRow>();
        var tableProps = table.GetFirstChild<OoxmlTableProperties>();
        var rowList = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().ToList();
        var totalRows = rowList.Count;

        // Look up table style border info (including conditional formatting) early,
        // so we can apply per-cell conditional borders during cell parsing.
        // When the table has no explicit w:tblStyle reference, fall back to the styles part's
        // default table style (the one with w:default="true") — Word implicitly applies it.
        TableStyleBorderInfo? styleInfo = null;
        if (tableStyleBorders != null)
        {
            var styleId = tableProps?.GetFirstChild<TableStyle>()?.Val?.Value
                          ?? defaultTableStyleId;
            if (styleId != null)
            {
                tableStyleBorders.TryGetValue(styleId, out styleInfo);
            }
        }

        List<double>? gridColumnWidths = null;
        var tableGrid = table.GetFirstChild<TableGrid>();
        if (tableGrid != null)
        {
            gridColumnWidths = [];
            foreach (var gridCol in tableGrid.Elements<GridColumn>())
            {
                if (gridCol.Width?.HasValue == true &&
                    double.TryParse(gridCol.Width.Value, out var widthTwips))
                {
                    gridColumnWidths.Add(widthTwips / OoxmlUnits.TwipsPerPoint);
                }
            }

            if (gridColumnWidths.Count == 0)
            {
                gridColumnWidths = null;
            }
        }

        // w:tblW — explicit table preferred width. dxa is a fixed point value; pct says
        // "fill <pct>% of container" (pct units are 50ths of a percent, 5000 = 100%);
        // auto/missing means "fit to content".
        double? preferredWidthPoints = null;
        double? preferredWidthFraction = null;
        var fillContainer = false;
        var tblWidthEl = tableProps?.GetFirstChild<TableWidth>();
        if (tblWidthEl?.Type?.Value == TableWidthUnitValues.Dxa &&
            tblWidthEl.Width?.HasValue == true &&
            double.TryParse(tblWidthEl.Width.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tblWTwips) &&
            tblWTwips > 0)
        {
            preferredWidthPoints = tblWTwips / OoxmlUnits.TwipsPerPoint;
        }
        else if (tblWidthEl?.Type?.Value == TableWidthUnitValues.Pct &&
                 tblWidthEl.Width?.HasValue == true &&
                 double.TryParse(tblWidthEl.Width.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tblWPct) &&
                 tblWPct > 0)
        {
            fillContainer = true;
            preferredWidthFraction = tblWPct / 5000.0;
        }

        // Parse table-level default cell margins and floating table positioning
        CellSpacing? defaultCellMargin = null;
        CellSpacing? defaultCellPadding = null;
        var isFloating = false;
        double floatingYOffsetPoints = 0;
        double floatingXOffsetPoints = 0;
        var floatingVAnchor = FloatingTableVerticalAnchor.Text;
        var floatingHAnchor = FloatingTableHorizontalAnchor.Text;
        if (tableProps != null)
        {
            var tblCellMar = tableProps.GetFirstChild<TableCellMarginDefault>();
            if (tblCellMar != null)
            {
                // Merges with the table style's padding PER SIDE (Word probe, docs/word-features.md,
                // Cell Padding): a w:tblCellMar of only top/bottom kept the style chain's 108-twip
                // horizontal padding, while an explicit left/right of 0 rendered flush.
                defaultCellPadding = TableStyleParser.MergeTableCellMargin(tblCellMar, styleInfo?.DefaultCellPadding);
            }

            // Check for floating table positioning (tblpPr).
            var tblpPr = tableProps.GetFirstChild<TablePositionProperties>();
            isFloating = tblpPr != null;
            if (tblpPr?.TablePositionY?.HasValue == true)
            {
                floatingYOffsetPoints = tblpPr.TablePositionY.Value / OoxmlUnits.TwipsPerPoint;
            }

            if (tblpPr?.TablePositionX?.HasValue == true)
            {
                floatingXOffsetPoints = tblpPr.TablePositionX.Value / OoxmlUnits.TwipsPerPoint;
            }

            if (tblpPr?.VerticalAnchor?.Value is { } vAnch)
            {
                floatingVAnchor = vAnch == VerticalAnchorValues.Page
                    ? FloatingTableVerticalAnchor.Page
                    : vAnch == VerticalAnchorValues.Margin
                        ? FloatingTableVerticalAnchor.Margin
                        : FloatingTableVerticalAnchor.Text;
            }

            if (tblpPr?.HorizontalAnchor?.Value is { } hAnch)
            {
                floatingHAnchor = hAnch == HorizontalAnchorValues.Page
                    ? FloatingTableHorizontalAnchor.Page
                    : hAnch == HorizontalAnchorValues.Margin
                        ? FloatingTableHorizontalAnchor.Margin
                        : FloatingTableHorizontalAnchor.Text;
            }
        }

        // The table's resolved cell padding: the style chain merged per side, then the table's own
        // w:tblCellMar merged over it. Everything further down the cascade — a row's w:tblPrEx and
        // a cell's w:tcMar — inherits its unstated sides from THIS, so it is computed once here
        // rather than re-derived at each site.
        var effectiveCellPadding = defaultCellPadding ?? styleInfo?.DefaultCellPadding;

        // The style's whole-table w:tcPr/w:tcMar stands over both, side by side
        // (ResolveStyleCellMargin).
        if (styleInfo?.CellMargin is { } styleCellMargin)
        {
            effectiveCellPadding = TableStyleParser.ParseCellMargin(styleCellMargin, effectiveCellPadding);
        }

        // tblLook gates which conditional flags can be derived from cell position when the
        // document doesn't carry explicit w:cnfStyle.
        var tableLookMask = TableStyleParser.ParseTableLookMask(tableProps?.GetFirstChild<TableLook>());

        // Total grid columns — used for resolving lastColumn cnfStyle. Falls back to the
        // first row's gridSpan-aware cell count when no explicit grid is supplied.
        var totalCols = gridColumnWidths?.Count ?? 0;
        if (totalCols == 0 &&
            rowList.Count > 0)
        {
            var (firstBefore, firstAfter) = RowGridSkips(rowList[0]);
            totalCols = firstBefore + firstAfter;
            foreach (var cell in RowCells(rowList[0]))
            {
                var span = ClampGridSpan(cell.GetFirstChild<OoxmlTableCellProperties>()?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1);
                totalCols += span;
            }
        }

        for (var rowIndex = 0; rowIndex < rowList.Count; rowIndex++)
        {
            var row = rowList[rowIndex];
            var cells = new List<TableCell>();
            var (gridBefore, gridAfter) = RowGridSkips(row);
            var gridColIndex = gridBefore;
            var rowFlags = TableStyleParser.ParseConditionalFormatFlags(row.GetFirstChild<TableRowProperties>()?.GetFirstChild<ConditionalFormatStyle>());

            foreach (var cell in RowCells(row))
            {
                var cellContent = new List<DocumentElement>();
                var cellProps = cell.GetFirstChild<OoxmlTableCellProperties>();

                // The table style's run properties for THIS cell, resolved BEFORE its content is parsed so
                // run resolution can layer them at their proper rung — between the document defaults and
                // any paragraph style. Same condition set and precedence order the border/shading cascade
                // below uses, so the two can never disagree about which region applies.
                var outerTableStyleRun = tableStyleRunDefaults;
                if (styleInfo != null)
                {
                    var layeredRun = styleInfo.RunProperties;
                    if (styleInfo.Conditionals != null)
                    {
                        var runFlags = TableStyleParser.ParseConditionalFormatFlags(cellProps?.GetFirstChild<ConditionalFormatStyle>()) | rowFlags;
                        foreach (var condType in TableStyleParser.ResolveActiveConditions(
                                     runFlags, rowIndex, gridColIndex, totalRows, totalCols,
                                     styleInfo.RowBandSize, styleInfo.ColBandSize, tableLookMask))
                        {
                            if (styleInfo.Conditionals.TryGetValue(condType, out var cond) &&
                                cond.RunProperties != null)
                            {
                                layeredRun = layeredRun == null ? cond.RunProperties : layeredRun.Layer(cond.RunProperties);
                            }
                        }
                    }

                    tableStyleRunDefaults = layeredRun;
                }

                // Parse cell content (paragraphs, nested tables, SdtBlocks, etc.)
                foreach (var cellChild in cell.ChildElements)
                {
                    switch (cellChild)
                    {
                        case Paragraph para:
                            cellContent.AddRange(ParseParagraph(para, mainPart));
                            break;
                        case Table tooDeep when tableDepth >= maxTableDepth:
                            cellContent.AddRange(FlattenTable(tooDeep, mainPart));
                            break;
                        case Table nestedTable:
                            // A floating table stays in its cell, which places it at its offset
                            // from there (Fragmenter.LayoutCellFragment; labels/09's ticket numbers).
                            if (ParseTable(nestedTable, mainPart) is { } nested)
                            {
                                cellContent.Add(nested);
                            }

                            break;
                        case SdtBlock sdtBlock:
                            // Block-level content control - extract and parse paragraphs from its content
                            foreach (var sdtChild in sdtBlock.SdtContentBlock?.ChildElements ?? [])
                            {
                                if (sdtChild is Paragraph sdtPara)
                                {
                                    cellContent.AddRange(ParseParagraph(sdtPara, mainPart));
                                }
                                else if (sdtChild is Table sdtTable)
                                {
                                    if (tableDepth >= maxTableDepth)
                                    {
                                        cellContent.AddRange(FlattenTable(sdtTable, mainPart));
                                    }
                                    else if (ParseTable(sdtTable, mainPart) is { } nestedSdt)
                                    {
                                        cellContent.Add(nestedSdt);
                                    }
                                }
                            }

                            break;
                    }
                }

                // The cell's content is parsed, so its table-style context ends here — restoring rather
                // than clearing keeps a nested table's cells from stripping the outer cell's context.
                tableStyleRunDefaults = outerTableStyleRun;

                double? width = null;
                double? widthFraction = null;
                string? bgColor = null;
                CellSpacing? cellPadding = null;
                CellSpacing? cellMargin = null;
                var clearsShading = false;

                if (cellProps != null)
                {
                    var cellWidth = cellProps.GetFirstChild<TableCellWidth>();
                    if (cellWidth?.Width?.HasValue == true &&
                        cellWidth.Type?.Value == TableWidthUnitValues.Dxa)
                    {
                        width = double.Parse(cellWidth.Width.Value!) / OoxmlUnits.TwipsPerPoint;
                    }
                    else if (cellWidth?.Width?.HasValue == true &&
                             cellWidth.Type?.Value == TableWidthUnitValues.Pct &&
                             double.TryParse(cellWidth.Width.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tcPct) &&
                             tcPct > 0)
                    {
                        // w:tcW pct is in fiftieths of a percent (5000 = 100% of table width).
                        widthFraction = tcPct / 5000.0;
                    }

                    var shading = cellProps.GetFirstChild<Shading>();
                    if (shading?.Fill?.HasValue == true &&
                        shading.Fill.Value != "auto")
                    {
                        bgColor = shading.Fill.Value;
                    }

                    // A cell that declares its shading as no colour at all (w:val="clear" with
                    // w:fill="auto", what Word writes for Shading > No Color) has said so in
                    // direct formatting, which outranks the table style: its banding and its
                    // header fill stop at this cell. business-plans/15 stands two tables side by
                    // side as one, and the spacer column between them is cleared this way.
                    clearsShading = shading is {Fill.Value: "auto"} &&
                                    (shading.Val?.HasValue != true ||
                                     shading.Val.Value == ShadingPatternValues.Clear ||
                                     shading.Val.Value == ShadingPatternValues.Nil);

                    // Parse cell-level margins (which act as padding in Word). A partial w:tcMar
                    // inherits its unspecified sides from the table's w:tblCellMar (doc, then style)
                    // rather than zeroing them — see ParseCellMargin.
                    var tcMar = cellProps.GetFirstChild<TableCellMargin>();
                    if (tcMar != null)
                    {
                        cellPadding = TableStyleParser.ParseCellMargin(tcMar, effectiveCellPadding);
                    }
                }

                // Parse cell-level borders. Side-borders only get materialised when at
                // least one of the four side children appears, so a cell that specifies
                // only diagonals doesn't lose its table-level side cascade.
                CellBorders? cellBorders = null;
                CellDiagonals? cellDiagonals = null;
                var tcBorders = cellProps?.GetFirstChild<TableCellBorders>();
                if (tcBorders != null)
                {
                    cellBorders = ParseDeclaredCellBorders(tcBorders);

                    var tl2br = tcBorders.GetFirstChild<TopLeftToBottomRightCellBorder>();
                    var tr2bl = tcBorders.GetFirstChild<TopRightToBottomLeftCellBorder>();
                    if (tl2br != null || tr2bl != null)
                    {
                        cellDiagonals = new()
                        {
                            Down = ParseBorderEdge(tl2br),
                            Up = ParseBorderEdge(tr2bl)
                        };
                    }
                }

                var gridSpan = 1;
                var gridSpanElement = cellProps?.GetFirstChild<GridSpan>();
                if (gridSpanElement?.Val?.HasValue == true)
                {
                    gridSpan = ClampGridSpan(gridSpanElement.Val.Value);
                }

                // Direct cell w:vAlign wins; otherwise the table style's whole-table tcPr
                // (e.g. FormTable's vAlign="center").
                var verticalAlign = styleInfo?.VerticalAlignment ?? CellVerticalAlignment.Top;
                var vAlignElement = cellProps?.GetFirstChild<TableCellVerticalAlignment>();
                if (vAlignElement?.Val?.HasValue == true)
                {
                    var vAlignVal = vAlignElement.Val.Value;
                    if (vAlignVal == TableVerticalAlignmentValues.Center)
                    {
                        verticalAlign = CellVerticalAlignment.Center;
                    }
                    else if (vAlignVal == TableVerticalAlignmentValues.Bottom)
                    {
                        verticalAlign = CellVerticalAlignment.Bottom;
                    }
                    else
                    {
                        verticalAlign = CellVerticalAlignment.Top;
                    }
                }

                var textDirection = CellTextDirection.LeftToRight;
                var textDirElement = cellProps?.GetFirstChild<TextDirection>();
                if (textDirElement?.Val?.HasValue == true)
                {
                    var dirVal = textDirElement.Val.Value;
                    if (dirVal == TextDirectionValues.BottomToTopLeftToRight)
                    {
                        textDirection = CellTextDirection.BottomToTop;
                    }
                    else if (dirVal == TextDirectionValues.TopToBottomRightToLeft)
                    {
                        textDirection = CellTextDirection.TopToBottom;
                    }
                }

                var verticalMerge = VerticalMergeType.None;
                var vMergeElement = cellProps?.GetFirstChild<VerticalMerge>();
                if (vMergeElement != null)
                {
                    // If val="restart", this cell starts a vertical merge
                    // If val is missing or val="continue", this cell continues a merge from above
                    if (vMergeElement.Val?.Value == MergedCellValues.Restart)
                    {
                        verticalMerge = VerticalMergeType.Restart;
                    }
                    else
                    {
                        verticalMerge = VerticalMergeType.Continue;
                    }
                }

                // Apply conditional table-style formatting (w:cnfStyle / w:tblStylePr cascade).
                // Cell- and row-level explicit overrides (w:tcBorders / w:shd on the cell) still
                // win — they're applied above by leaving cellBorders / bgColor non-null.
                //
                // Word distributes cnfStyle across both row and cell: the row carries row-spanning
                // flags (firstRow / lastRow / oddHBand / evenHBand) and the cell carries
                // cell-spanning flags (firstColumn / lastColumn / corner cells). ORing the two is
                // exactly what Word's renderer does — the cell at (0,0) of a styled table ends up
                // with firstRow ∪ firstColumn ∪ nwCell, all from a single OR.
                if (styleInfo != null)
                {
                    var cellFlags = TableStyleParser.ParseConditionalFormatFlags(cellProps?.GetFirstChild<ConditionalFormatStyle>());
                    var effectiveFlags = cellFlags | rowFlags;

                    CellBorders? conditionalBorders = null;
                    var conditionalShading = styleInfo.BackgroundColorHex;
                    string? conditionalRunColor = null;

                    if (styleInfo.Conditionals != null)
                    {
                        foreach (var condType in TableStyleParser.ResolveActiveConditions(
                                     effectiveFlags, rowIndex, gridColIndex, totalRows, totalCols,
                                     styleInfo.RowBandSize, styleInfo.ColBandSize, tableLookMask))
                        {
                            if (!styleInfo.Conditionals.TryGetValue(condType, out var cond))
                            {
                                continue;
                            }

                            if (cond.Borders != null)
                            {
                                // A region declaring only some sides (firstRow's bottom rule, say)
                                // layers over the regions before it rather than replacing them.
                                conditionalBorders = cond.Borders.Over(conditionalBorders);
                            }

                            if (cond.BackgroundColorHex != null)
                            {
                                conditionalShading = cond.BackgroundColorHex;
                            }

                            if (cond.RunColorHex != null)
                            {
                                conditionalRunColor = cond.RunColorHex;
                            }
                        }
                    }

                    // The cell's own sides win; what it leaves undeclared comes from the style's
                    // conditional regions, and what THEY leave undeclared stays open for the
                    // table-level cascade at layout (TableLayout.ResolveCellBorders).
                    cellBorders = cellBorders?.Over(conditionalBorders) ?? conditionalBorders;
                    if (!clearsShading)
                    {
                        bgColor ??= conditionalShading;
                    }

                    // Cascade tblStylePr's run colour onto runs in this cell that don't carry
                    // an explicit w:color. Explicit run colour wins (RunProperties.ColorHex
                    // is non-null only when the run carried a w:color element).
                    if (conditionalRunColor != null)
                    {
                        ApplyDefaultRunColor(cellContent, conditionalRunColor);
                    }
                }

                // "Automatic" text colour is contrast-aware against the CELL's shading, not only the
                // page background: on a dark fill Word draws it white. Runs still lacking a colour at
                // this point are the automatic ones — an explicit w:color, or a colour cascaded from
                // the table style just above, has already filled ColorHex in and wins. Placed outside
                // the styleInfo block so a direct w:shd on the cell counts the same as a conditional
                // one, which is what Word does (probed both ways).
                if (bgColor != null &&
                    IsDarkForAutomaticText(bgColor))
                {
                    ApplyDefaultRunColor(cellContent, "FFFFFF");
                }

                // Detach floating drawings from the cell's flow content: Word anchors them to
                // the cell's frame, and the table renderer draws them when the cell rectangle is
                // known (TableCell.Floats). Sorted by relativeHeight — Word's z-space.
                List<DocumentElement>? cellFloats = null;
                List<int>? cellFloatOrdinals = null;
                if (cellContent.Any(static _ => _ is FloatingImageElement or FloatingShapeElement or FloatingTextBoxElement or FloatingWordArtElement))
                {
                    // Each float's anchor paragraph is the next paragraph after it in the cell's
                    // flow (ParseParagraph emits a drawing's floats before the paragraph's runs);
                    // record its ordinal so paragraph-relative anchors can resolve against the
                    // paragraph's laid-out position (brochures/07 hangs its balloons photo 260pt
                    // ABOVE the cell's bottom paragraph). The z-sort keeps float+ordinal aligned.
                    var floatsInOrder = new List<(DocumentElement Element, int Ordinal)>();
                    var paragraphOrdinal = 0;
                    foreach (var cellElement in cellContent)
                    {
                        if (cellElement is FloatingImageElement or FloatingShapeElement or FloatingTextBoxElement or FloatingWordArtElement)
                        {
                            floatsInOrder.Add((cellElement, paragraphOrdinal));
                        }
                        else if (cellElement is ParagraphElement)
                        {
                            paragraphOrdinal++;
                        }
                    }

                    var sorted = floatsInOrder.OrderBy(static _ => FloatingZOrder(_.Element)).ToList();
                    cellFloats = [.. sorted.Select(static _ => _.Element)];
                    cellFloatOrdinals = [.. sorted.Select(static _ => _.Ordinal)];
                    cellContent.RemoveAll(static _ => _ is FloatingImageElement or FloatingShapeElement or FloatingTextBoxElement or FloatingWordArtElement);
                }

                // Word collapses the unavoidable empty end-of-cell paragraph mark that directly
                // follows a nested table to zero height.
                if (cellContent is [.., TableElement, ParagraphElement {Runs.Count: 0, IsAnchorOnlyMark: false} endMark])
                {
                    cellContent[^1] = new ParagraphElement
                    {
                        Runs = endMark.Runs,
                        Properties = endMark.Properties,
                        IsCollapsedCellMark = true
                    };
                }

                cells.Add(
                    new()
                    {
                        Content = cellContent,
                        Floats = cellFloats ?? [],
                        FloatAnchorParagraphOrdinals = cellFloatOrdinals ?? [],
                        Properties = new()
                        {
                            WidthPoints = width,
                            WidthFraction = widthFraction,
                            BackgroundColorHex = bgColor,
                            Padding = cellPadding,
                            Margin = cellMargin,
                            Borders = cellBorders,
                            Diagonals = cellDiagonals,
                            HideMark = cellProps?.GetFirstChild<HideMark>() != null,
                            NoWrap = cellProps?.GetFirstChild<NoWrap>() != null,
                            GridSpan = gridSpan,
                            VerticalAlignment = verticalAlign,
                            VerticalMerge = verticalMerge,
                            TextDirection = textDirection
                        }
                    });

                gridColIndex += gridSpan;
            }

            double? rowHeight = null;
            var isExactHeight = false;
            var isHeader = false;
            // A row's own w:cantSplit wins; without one it takes the table style's.
            var cannotSplit = styleInfo?.CannotSplit ?? false;
            var rowProps = row.GetFirstChild<TableRowProperties>();
            if (rowProps != null)
            {
                var cantSplitElement = rowProps.GetFirstChild<CantSplit>();
                if (cantSplitElement != null)
                {
                    cannotSplit = TableStyleParser.OnOffOnlyIsOn(cantSplitElement.Val);
                }

                var trHeight = rowProps.GetFirstChild<TableRowHeight>();
                if (trHeight?.Val?.HasValue == true)
                {
                    rowHeight = trHeight.Val.Value / OoxmlUnits.TwipsPerPoint;
                    // hRule="exact" means exact height, otherwise it's minimum height
                    isExactHeight = trHeight.HeightType?.Value == HeightRuleValues.Exact;
                }

                var headerElement = rowProps.GetFirstChild<TableHeader>();
                if (headerElement != null)
                {
                    isHeader = TableStyleParser.OnOffOnlyIsOn(headerElement.Val);
                }
            }

            // Parse w:tblPrEx — row-level overrides for table-wide properties (borders, cell margins).
            CellBorders? rowOverrideBorders = null;
            BorderEdge? rowOverrideInsideH = null;
            BorderEdge? rowOverrideInsideV = null;
            CellSpacing? rowOverrideCellPadding = null;

            var tblPrEx = row.GetFirstChild<TablePropertyExceptions>();
            if (tblPrEx != null)
            {
                var exBorders = tblPrEx.GetFirstChild<TableBorders>();
                if (exBorders != null)
                {
                    rowOverrideBorders = new()
                    {
                        Top = ParseBorderEdge(exBorders.GetFirstChild<TopBorder>()),
                        Right = ParseBorderEdge(exBorders.GetFirstChild<RightBorder>()),
                        Bottom = ParseBorderEdge(exBorders.GetFirstChild<BottomBorder>()),
                        Left = ParseBorderEdge(exBorders.GetFirstChild<LeftBorder>())
                    };

                    // Present = the row spoke, whatever it said: an invisible w:insideH/w:insideV
                    // (val="none") switches the table style's off. newsletters/04's layout tables
                    // nil every row's borders through w:tblPrEx.
                    if (exBorders.GetFirstChild<InsideHorizontalBorder>() != null)
                    {
                        rowOverrideInsideH = ParseBorderEdge(exBorders.GetFirstChild<InsideHorizontalBorder>());
                    }

                    if (exBorders.GetFirstChild<InsideVerticalBorder>() != null)
                    {
                        rowOverrideInsideV = ParseBorderEdge(exBorders.GetFirstChild<InsideVerticalBorder>());
                    }
                }

                var exCellMar = tblPrEx.GetFirstChild<TableCellMarginDefault>();
                if (exCellMar != null)
                {
                    // Merges with the table's resolved padding PER SIDE (Word probe,
                    // docs/word-features.md, Cell Padding): over a table default of 360 twips and a
                    // style chain of 108, a row stating only top/bottom rendered 360 horizontally.
                    rowOverrideCellPadding = TableStyleParser.MergeTableCellMargin(exCellMar, effectiveCellPadding);
                }
            }

            rows.Add(
                new()
                {
                    Cells = cells,
                    GridBefore = gridBefore,
                    GridAfter = gridAfter,
                    HeightPoints = rowHeight,
                    IsExactHeight = isExactHeight,
                    IsHeader = isHeader,
                    CannotSplit = cannotSplit,
                    OverrideBorders = rowOverrideBorders,
                    OverrideInsideHBorder = rowOverrideInsideH,
                    OverrideInsideVBorder = rowOverrideInsideV,
                    OverrideCellPadding = rowOverrideCellPadding
                });
        }

        if (rows.Count == 0)
        {
            return null;
        }

        CellBorders? defaultBorders = null;
        BorderEdge? insideHBorder = null;
        BorderEdge? insideVBorder = null;
        var declaredIndent = styleInfo?.IndentPoints;

        if (tableProps != null)
        {
            var borders = tableProps.GetFirstChild<TableBorders>();
            if (borders != null)
            {
                defaultBorders = new()
                {
                    Top = ParseBorderEdge(borders.GetFirstChild<TopBorder>()),
                    Right = ParseBorderEdge(borders.GetFirstChild<RightBorder>()),
                    Bottom = ParseBorderEdge(borders.GetFirstChild<BottomBorder>()),
                    Left = ParseBorderEdge(borders.GetFirstChild<LeftBorder>())
                };

                var insideH = ParseBorderEdge(borders.GetFirstChild<InsideHorizontalBorder>());
                var insideV = ParseBorderEdge(borders.GetFirstChild<InsideVerticalBorder>());

                if (insideH.IsVisible)
                {
                    insideHBorder = insideH;
                }

                if (insideV.IsVisible)
                {
                    insideVBorder = insideV;
                }
            }

            // If no inline borders, try to get borders from the table style
            if (defaultBorders == null &&
                insideHBorder == null &&
                insideVBorder == null &&
                styleInfo != null)
            {
                defaultBorders = styleInfo.Outer;
                if (styleInfo.InsideH.IsVisible)
                {
                    insideHBorder = styleInfo.InsideH;
                }

                if (styleInfo.InsideV.IsVisible)
                {
                    insideVBorder = styleInfo.InsideV;
                }
            }

            // The table's own w:tblInd over the style chain's.
            var tblInd = tableProps.GetFirstChild<TableIndentation>();
            if (tblInd?.Width?.HasValue == true)
            {
                declaredIndent = tblInd.Width.Value / OoxmlUnits.TwipsPerPoint;
            }
        }

        // The left BORDER offset the layout uses — see ResolveTableIndent for the compatibility rule
        // that turns the declared indent into it. The first cell's margin is its resolved left
        // padding: w:tcMar, else the row's w:tblPrEx, else the table/style default.
        var firstCell = rows[0].Cells.Count > 0 ? rows[0].Cells[0].Properties : null;
        var firstCellLeftMargin = firstCell?.Padding?.Left ?? rows[0].OverrideCellPadding?.Left ?? effectiveCellPadding?.Left ?? 0;
        var indentPoints = TableStyleParser.ResolveTableIndent(compatibilityMode, declaredIndent, defaultCellPadding != null, firstCellLeftMargin);
        var lastCell = rows[0].Cells.Count > 0 ? rows[0].Cells[^1].Properties : null;
        var lastCellRightMargin = lastCell?.Padding?.Right ?? rows[0].OverrideCellPadding?.Right ?? effectiveCellPadding?.Right ?? 0;
        var widthOverhang = TableStyleParser.ResolveTableWidthOverhang(compatibilityMode, declaredIndent, defaultCellPadding != null, firstCellLeftMargin, lastCellRightMargin);

        // A non-zero w:tblCellSpacing switches the table to the detached-border model.
        // Falls back to the table style's tblCellSpacing when the document doesn't override.
        var cellSpacingPoints = ReadTableCellSpacing(tableProps);
        if (cellSpacingPoints == 0)
        {
            cellSpacingPoints = styleInfo?.CellSpacingPoints ?? 0;
        }

        var isAutoFit = true;
        var tableLayoutEl = tableProps?.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableLayout>();
        if (tableLayoutEl?.Type?.Value is { } layoutType)
        {
            isAutoFit = layoutType != TableLayoutValues.Fixed;
        }

        var alignment = TextAlignment.Left;
        var tableJustification = tableProps?.GetFirstChild<TableJustification>();
        if (tableJustification?.Val?.HasValue == true)
        {
            var jcVal = tableJustification.Val.Value;
            if (jcVal == TableRowAlignmentValues.Center)
            {
                alignment = TextAlignment.Center;
            }
            else if (jcVal == TableRowAlignmentValues.Right)
            {
                alignment = TextAlignment.Right;
            }
        }
        // Floating tables can carry their horizontal alignment in tblpXSpec instead of w:jc.
        // We don't honour the absolute X coordinate (tblpX) — the inline fallback re-uses
        // the alignment so the table at least lands in the right column.
        else if (tableProps?.GetFirstChild<TablePositionProperties>()?.TablePositionXAlignment?.Value is { } xAlign)
        {
            if (xAlign == HorizontalAlignmentValues.Center) alignment = TextAlignment.Center;
            else if (xAlign == HorizontalAlignmentValues.Right) alignment = TextAlignment.Right;
        }

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                IsFloating = isFloating,
                FloatingYOffsetPoints = floatingYOffsetPoints,
                FloatingXOffsetPoints = floatingXOffsetPoints,
                FloatingVerticalAnchor = floatingVAnchor,
                FloatingHorizontalAnchor = floatingHAnchor,
                DefaultBorders = defaultBorders,
                InsideHorizontalBorder = insideHBorder,
                InsideVerticalBorder = insideVBorder,
                // For tables without an explicit w:tblCellMar and no inherited style padding,
                // vertical padding is 0, per the OOXML spec AND per Word's real render: an
                // unstyled row measures exactly font-line + spacing-after (simple_table row
                // pitch 52.02px at 150 DPI = 12pt Aptos line 16.97pt + 8pt after).
                DefaultCellPadding = effectiveCellPadding ?? new CellSpacing(0),
                DefaultCellPaddingDeclared = effectiveCellPadding != null,
                DefaultCellMargin = defaultCellMargin ?? new CellSpacing(0),
                IndentPoints = indentPoints,
                WidthOverhangPoints = widthOverhang,
                GridColumnWidths = gridColumnWidths,
                PreferredWidthPoints = preferredWidthPoints,
                FillContainer = fillContainer,
                PreferredWidthFraction = preferredWidthFraction,
                Alignment = alignment,
                IsAutoFit = isAutoFit,
                CellSpacingPoints = cellSpacingPoints
            }
        };
    }

    /// <summary>
    /// Reads <c>w:tblCellSpacing</c> from the given properties container (either
    /// <c>w:tblPr</c> or its style equivalent). Returns 0 when absent.
    /// </summary>
    static double ReadTableCellSpacing(OpenXmlElement? container)
    {
        var spacing = container?.GetFirstChild<TableCellSpacing>();
        if (spacing?.Width?.Value is not { } widthStr)
        {
            return 0;
        }

        if (!double.TryParse(widthStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips))
        {
            return 0;
        }

        // Only honour explicit dxa values. pct/auto/nil aren't modelled — treating them as 0
        // matches Word's behaviour for non-positive cell spacing (no detached-border switch).
        var type = spacing.Type?.Value;
        if (type != null &&
            type != TableWidthUnitValues.Dxa)
        {
            return 0;
        }

        return twips / OoxmlUnits.TwipsPerPoint;
    }

    static double ParseBorderSpace(BorderType? border)
    {
        if (border?.Space?.HasValue == true)
        {
            return border.Space.Value;
        }

        return 0;
    }

    /// <summary>
    /// The four sides of a <c>w:tcBorders</c> with <see cref="CellBorders.Declared"/> recording which
    /// were actually written — an omitted side is NOT <c>nil</c>, it inherits (see
    /// <see cref="CellBorders.Declared"/>). Null when none of the four side children is present, so
    /// a cell that specifies only diagonals keeps its table-level cascade untouched.
    /// </summary>
    CellBorders? ParseDeclaredCellBorders(TableCellBorders? tcBorders)
    {
        if (tcBorders == null)
        {
            return null;
        }

        var topChild = tcBorders.GetFirstChild<TopBorder>();
        var rightChild = tcBorders.GetFirstChild<RightBorder>();
        var bottomChild = tcBorders.GetFirstChild<BottomBorder>();
        var leftChild = tcBorders.GetFirstChild<LeftBorder>();
        var declared = (topChild != null ? BorderSides.Top : BorderSides.None)
                       | (rightChild != null ? BorderSides.Right : BorderSides.None)
                       | (bottomChild != null ? BorderSides.Bottom : BorderSides.None)
                       | (leftChild != null ? BorderSides.Left : BorderSides.None);
        if (declared == BorderSides.None)
        {
            return null;
        }

        return new()
        {
            Top = ParseBorderEdge(topChild),
            Right = ParseBorderEdge(rightChild),
            Bottom = ParseBorderEdge(bottomChild),
            Left = ParseBorderEdge(leftChild),
            Declared = declared
        };
    }
}
