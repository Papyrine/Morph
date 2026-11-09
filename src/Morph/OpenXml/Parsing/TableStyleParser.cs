using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Resolves what a table style declares up its <c>w:basedOn</c> chain — cell margins, borders,
/// conditional regions, run properties, indent — and the conditional-format flags a cell
/// takes from its position (ECMA-376 §17.7.6). Pure functions of the style definitions, shared
/// by the parser and by <see cref="TableProperties"/>.
/// </summary>
static class TableStyleParser
{
    // A table style and its w:basedOn ancestors, nearest first. A cycle or a basedOn that names no
    // style ends the walk.
    static IEnumerable<Style> Chain(Style style, Dictionary<string, Style> tableStylesById)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = style;
        while (current != null)
        {
            if (current.StyleId?.Value is { } id &&
                !visited.Add(id))
            {
                yield break;
            }

            yield return current;

            var basedOnId = current.BasedOn?.Val?.Value;
            if (basedOnId == null || !tableStylesById.TryGetValue(basedOnId, out var baseStyle))
            {
                yield break;
            }

            current = baseStyle;
        }
    }

    /// <summary>
    /// The cell margins a table style gives every cell through its whole-table <c>w:tcPr/w:tcMar</c>,
    /// the nearest declaration on the <c>w:basedOn</c> chain. They are not the style's
    /// <c>w:tblCellMar</c> and do not rank with it. Word-probed 2026-10-11 (<c>_probe_styletcmar</c>:
    /// a Table Grid style fattened to 15pt above and below and 30pt on the left, on one-row tables
    /// of 10pt text): a table with nothing of its own took all three (27.6pt from the line above to
    /// the cell's baseline, text 30pt in from the edge); a table declaring its OWN
    /// <c>w:tblCellMar</c> of 30pt, 30pt and 5pt still took the style's 15, 15 and 30; and a cell
    /// with its own zero <c>w:tcMar</c> stood flush left. So a cell's own margins come first, then
    /// these, then the table's, then the style's <c>w:tblCellMar</c>. agendas-minutes/19's contact
    /// table is the corpus case (3.6pt above and below).
    /// </summary>
    public static TableCellMargin? ResolveStyleCellMargin(Style style, Dictionary<string, Style> tableStylesById)
    {
        foreach (var current in Chain(style, tableStylesById))
        {
            if (current.StyleTableCellProperties?.GetFirstChild<TableCellMargin>() is { } margin)
            {
                return margin;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a table style stops its rows splitting across pages — its own <c>w:trPr/w:cantSplit</c>,
    /// the nearest declaration on the <c>w:basedOn</c> chain winning, so a derived style can switch a
    /// base's off with <c>w:val="0"</c>. The conditional regions' row properties are not read here.
    /// Word honours it (A/B on a report, 2026-09-30): a straddling three-paragraph row whose cantSplit
    /// came only from its style went whole to the next page, and without it split at the engine's line.
    /// </summary>
    public static bool ResolveStyleCannotSplit(Style style, Dictionary<string, Style> tableStylesById)
    {
        foreach (var current in Chain(style, tableStylesById))
        {
            if (current.GetFirstChild<TableStyleConditionalFormattingTableRowProperties>()?.GetFirstChild<CantSplit>() is { } cantSplit)
            {
                return OnOffOnlyIsOn(cantSplit.Val);
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a table style's <c>w:tblStylePr</c> conditional regions through the <c>w:basedOn</c>
    /// chain, a region's <c>w:tcPr</c> and <c>w:rPr</c> each taken WHOLE from the nearest style whose
    /// block for that region declares it. Word-probed 2026-10-30 (<c>_probe_tblinherit</c>: a base
    /// first row of 20pt text on a red fill over a 6pt blue rule): a derived first row declaring only
    /// bold keeps the base's fill and rule but sets its bold at the default size, and one declaring only
    /// a green fill keeps the 20pt text but loses the blue rule — neither the region taken whole nor
    /// each property merged on its own.
    /// </summary>
    public static Dictionary<TableStyleOverrideValues, ConditionalFormat>? ResolveStyleConditionals(Style style, Dictionary<string, Style> tableStylesById, ThemeColors? themeColors)
    {
        // Leaf-first: the first declaration seen for a region/property is the nearest one.
        var blocks = new Dictionary<TableStyleOverrideValues, List<TableStyleProperties>>();
        foreach (var current in Chain(style, tableStylesById))
        {
            foreach (var tblStylePr in current.Elements<TableStyleProperties>())
            {
                if (tblStylePr.Type?.Value is not { } type)
                {
                    continue;
                }

                if (!blocks.TryGetValue(type, out var list))
                {
                    list = [];
                    blocks[type] = list;
                }

                list.Add(tblStylePr);
            }
        }

        Dictionary<TableStyleOverrideValues, ConditionalFormat>? conditionals = null;
        foreach (var (type, chain) in blocks)
        {
            BorderType? top = null, right = null, bottom = null, left = null;
            var tcPr = chain
                .Select(_ => _.TableStyleConditionalFormattingTableCellProperties)
                .FirstOrDefault(_ => _ != null);
            if (tcPr?.GetFirstChild<TableCellBorders>() is { } tcBorders)
            {
                top = tcBorders.GetFirstChild<TopBorder>();
                right = tcBorders.GetFirstChild<RightBorder>();
                bottom = tcBorders.GetFirstChild<BottomBorder>();
                left = tcBorders.GetFirstChild<LeftBorder>();
            }

            var shading = BorderParser.ReadShadingFill(tcPr?.GetFirstChild<Shading>());

            // Run-level rPr inside the tblStylePr: font, size, colour and the toggles. Captured as
            // DECLARED values rather than resolved ones, because this rung sits above the document
            // defaults but below any paragraph style (ECMA-376 §17.7.2) and only what it actually
            // says may be layered in.
            var run = DeclaredRunProperties.Read(chain
                .Select(_ => _.GetFirstChild<RunPropertiesBaseStyle>())
                .FirstOrDefault(_ => _ != null));

            var declaredSides = (top != null ? BorderSides.Top : BorderSides.None)
                                | (right != null ? BorderSides.Right : BorderSides.None)
                                | (bottom != null ? BorderSides.Bottom : BorderSides.None)
                                | (left != null ? BorderSides.Left : BorderSides.None);
            CellBorders? borders = declaredSides == BorderSides.None
                ? null
                : new()
                {
                    Top = BorderParser.ParseBorderEdge(top, themeColors),
                    Right = BorderParser.ParseBorderEdge(right, themeColors),
                    Bottom = BorderParser.ParseBorderEdge(bottom, themeColors),
                    Left = BorderParser.ParseBorderEdge(left, themeColors),
                    Declared = declaredSides
                };

            if (borders == null &&
                shading == null &&
                run == null)
            {
                continue;
            }

            conditionals ??= new();
            conditionals[type] = new(borders, shading, run?.ColorHex, run);
        }

        return conditionals;
    }

    /// <summary>
    /// Resolves a table style's <c>w:tblBorders</c> by walking the <c>w:basedOn</c> chain and taking each
    /// side from the NEAREST ancestor that declares it, exactly as <see cref="ResolveStyleCellPadding"/>
    /// does for the cell margins.
    ///
    /// A style's own <c>w:tblPr</c> is frequently empty: the common template shape is a base style that
    /// carries the grid (top/bottom/insideH, say) and a per-variant style based on it that adds nothing
    /// but a <c>w:tblStylePr</c> header colour.
    ///
    /// A side declared as <c>w:val="none"</c> STOPS the walk for that side rather than falling through:
    /// that is a derived style switching a base's rule off, and Word honours it.
    /// </summary>
    public static (CellBorders Outer, BorderEdge InsideH, BorderEdge InsideV) ResolveStyleBorders(Style style, Dictionary<string, Style> tableStylesById, ThemeColors? themeColors)
    {
        BorderType? top = null, right = null, bottom = null, left = null, insideH = null, insideV = null;
        foreach (var current in Chain(style, tableStylesById))
        {
            var borders = current.StyleTableProperties?.GetFirstChild<TableBorders>();
            if (borders != null)
            {
                top ??= borders.GetFirstChild<TopBorder>();
                right ??= borders.GetFirstChild<RightBorder>();
                bottom ??= borders.GetFirstChild<BottomBorder>();
                left ??= borders.GetFirstChild<LeftBorder>();
                insideH ??= borders.GetFirstChild<InsideHorizontalBorder>();
                insideV ??= borders.GetFirstChild<InsideVerticalBorder>();

                if (top != null &&
                    right != null &&
                    bottom != null &&
                    left != null &&
                    insideH != null &&
                    insideV != null)
                {
                    break;
                }
            }
        }

        var outer = new CellBorders
        {
            Top = BorderParser.ParseBorderEdge(top, themeColors),
            Right = BorderParser.ParseBorderEdge(right, themeColors),
            Bottom = BorderParser.ParseBorderEdge(bottom, themeColors),
            Left = BorderParser.ParseBorderEdge(left, themeColors)
        };

        return (outer, BorderParser.ParseBorderEdge(insideH, themeColors), BorderParser.ParseBorderEdge(insideV, themeColors));
    }

    /// <summary>
    /// The table style's whole-table <c>w:rPr</c>, resolved through <c>w:basedOn</c> — each property
    /// taken from the NEAREST ancestor that declares it, exactly as <see cref="ResolveStyleCellPadding"/>
    /// does for the margins and <see cref="ResolveStyleBorders"/> for the sides, and for the same
    /// template shape: a base style carries the formatting (a summary table's 9pt, which feeds the
    /// autofit measurement) and a per-variant style based on it adds only its <c>w:tblStylePr</c> blocks.
    /// </summary>
    public static DeclaredRunProperties? ResolveStyleRunProperties(Style style, Dictionary<string, Style> tableStylesById)
    {
        // Collect leaf-first, then layer base-first so the nearest declaration wins.
        var chain = new List<DeclaredRunProperties>();
        foreach (var current in Chain(style, tableStylesById))
        {
            if (DeclaredRunProperties.Read(current.StyleRunProperties) is { } declared)
            {
                chain.Add(declared);
            }
        }

        if (chain.Count == 0)
        {
            return null;
        }

        var resolved = chain[^1];
        for (var i = chain.Count - 2; i >= 0; i--)
        {
            resolved = resolved.Layer(chain[i]);
        }

        return resolved;
    }

    /// <summary>
    /// The table style chain's <c>w:tblInd</c>, from the nearest style that declares one. Word's
    /// built-in Normal Table declares 0, and every table inherits it — which is what makes the
    /// indent DECLARED for the compatibility rule in <see cref="ResolveTableIndent"/>.
    /// </summary>
    public static double? ResolveStyleIndent(Style style, Dictionary<string, Style> tableStylesById)
    {
        foreach (var current in Chain(style, tableStylesById))
        {
            var tblInd = current.StyleTableProperties?.GetFirstChild<TableIndentation>();
            if (tblInd?.Width?.HasValue == true)
            {
                return tblInd.Width.Value / OoxmlUnits.TwipsPerPoint;
            }
        }

        return null;
    }

    /// <summary>
    /// Where the table's left BORDER sits relative to the margin, from <c>w:tblInd</c> and the
    /// document's compatibility mode. XPS-read on <c>_probe_compat12</c>/<c>14</c>/<c>15</c>,
    /// <c>_probe_compats12</c>/<c>15</c> and <c>_probe_compatt12</c>/<c>15</c> (2026-09-05; 24
    /// tables sweeping the indent, the table-level margin, per-cell margins and style-declared
    /// values at 1in magnitudes):
    ///
    /// <para><b>Mode 15 (Word 2013+):</b> the border is at margin + tblInd. Cell margins do not
    /// move it (a 1in first-cell margin only pushes the text).</para>
    ///
    /// <para><b>Modes 12 and 14:</b> tblInd positions the TEXT of the first cell — the border hangs
    /// left of it by the first cell's own resolved left margin (a per-cell <c>w:tcMar</c> of 0 or
    /// 1in moved the border by exactly that; the second row's margin did not). So the border is
    /// at margin + tblInd − firstCellLeftMargin. The indent counts as declared when any rung
    /// states it, and Word's built-in Normal Table states 0: a corpus table under Table Grid
    /// therefore hangs 108 twips (5.4pt) left of the margin with its text on the margin, which is
    /// where <c>table_grid_styling_padding</c>'s Word reference draws it (x137 against a 149px
    /// margin). Only a package with no styles part has an undeclared indent; there Word puts the
    /// border on the margin when the table declares its own <c>w:tblCellMar</c> (text at margin +
    /// margin) and 108 twips left of it otherwise (the built-in margin, text on the margin).</para>
    ///
    /// <para>Centred and right-aligned tables ignore the indent on either mode
    /// (<c>Fragmenter.ComputeTableX</c>).</para>
    /// </summary>
    public static double ResolveTableIndent(int compatibilityMode, double? declaredIndent, bool tableDeclaresCellMargin, double firstCellLeftMargin)
    {
        if (compatibilityMode >= 15)
        {
            return declaredIndent ?? 0;
        }

        if (declaredIndent is { } indent)
        {
            return indent - firstCellLeftMargin;
        }

        if (tableDeclaresCellMargin)
        {
            return 0;
        }

        return -firstCellLeftMargin;
    }

    /// <summary>
    /// How far a table's border box may run past the text column, by compatibility mode — the
    /// companion of <see cref="ResolveTableIndent"/>, XPS-read on <c>_probe_pct12</c> / <c>_probe_pct15</c>
    /// (2026-09-05; 100% / 50% / dxa / auto tables under 108-twip, 720-twip and per-cell 1in margins).
    /// In modes 12 and 14 a <c>w:tblW</c> percentage is taken of the column PLUS the first cell's left
    /// margin and the last cell's right margin, and the border box hangs outside the column by those
    /// margins: 100% reads 479.25 (468 + 5.4 + 5.4) with the default margins, 540.5 with 36pt ones,
    /// 545.4 with a 1in first-cell margin against the default last; 50% is half of that box (270.3),
    /// so the percentage scales the whole outdented box; an autofit table grows to the same box. A
    /// dxa width is the border box itself on either mode (200 for 4000 twips). In mode 15 the column
    /// bounds the border box: 468 at 100% whatever the margins, 234 at 50%.
    ///
    /// <para>The left margin joins the overhang only when the border hangs left of the margin — the
    /// declared-indent case of <see cref="ResolveTableIndent"/>. An undeclared indent over the table's
    /// own <c>w:tblCellMar</c> puts the border ON the margin, and the box then runs only the right
    /// margin past the column: <c>table_autofit_no_widths</c> (no styles part, direct 108-twip
    /// margins) reads 474pt against a 468pt column in Word, not 479.</para>
    /// </summary>
    public static double ResolveTableWidthOverhang(int compatibilityMode, double? declaredIndent, bool tableDeclaresCellMargin, double firstCellLeftMargin, double lastCellRightMargin)
    {
        if (compatibilityMode >= 15)
        {
            return 0;
        }

        var leftHangs = declaredIndent != null || !tableDeclaresCellMargin;
        return lastCellRightMargin + (leftHangs ? firstCellLeftMargin : 0);
    }

    /// <summary>
    /// Resolves a table style's default cell padding by walking the <c>w:basedOn</c> chain and
    /// taking each side from the NEAREST ancestor that specifies it. Word merges
    /// <c>w:tblCellMar</c> per side rather than letting the first one found replace the whole box:
    /// a house style that sets only top/bottom (57 twips) on top of TableNormal's left/right
    /// (108 twips) is a common shape.
    /// </summary>
    public static CellSpacing? ResolveStyleCellPadding(Style style, Dictionary<string, Style> tableStylesById)
    {
        double? top = null, right = null, bottom = null, left = null;
        foreach (var current in Chain(style, tableStylesById))
        {
            var tblCellMar = current.StyleTableProperties?.GetFirstChild<TableCellMarginDefault>();
            if (tblCellMar != null)
            {
                var sides = ReadTableCellMarginSides(tblCellMar);
                top ??= sides.Top;
                right ??= sides.Right;
                bottom ??= sides.Bottom;
                left ??= sides.Left;

                if (top != null &&
                    right != null &&
                    bottom != null &&
                    left != null)
                {
                    break;
                }
            }
        }

        if (top == null &&
            right == null &&
            bottom == null &&
            left == null)
        {
            return null;
        }

        return new(top ?? 0, right ?? 0, bottom ?? 0, left ?? 0);
    }

    /// <summary>
    /// Reads <c>w:tblLook</c> into the subset of conditional flags that the table allows
    /// to be derived from cell position. Defaults to all conditions when no <c>w:tblLook</c>
    /// is present (matches Word's behaviour for tables saved without an explicit look).
    /// </summary>
    public static ConditionalFormatFlags ParseTableLookMask(TableLook? tableLook)
    {
        if (tableLook == null)
        {
            return ConditionalFormatFlagsExtensions.AllConditions;
        }

        var mask = ConditionalFormatFlagsExtensions.AllConditions;

        // First/last-row and first/last-column derivation only when explicitly enabled.
        if (tableLook.FirstRow?.Value != true) mask &= ~ConditionalFormatFlags.FirstRow;
        if (tableLook.LastRow?.Value != true) mask &= ~ConditionalFormatFlags.LastRow;
        if (tableLook.FirstColumn?.Value != true) mask &= ~ConditionalFormatFlags.FirstColumn;
        if (tableLook.LastColumn?.Value != true) mask &= ~ConditionalFormatFlags.LastColumn;

        // Banding is enabled by default; tblLook's noHBand/noVBand flags suppress it.
        if (tableLook.NoHorizontalBand?.Value == true)
        {
            mask &= ~(ConditionalFormatFlags.OddHBand | ConditionalFormatFlags.EvenHBand);
        }

        if (tableLook.NoVerticalBand?.Value == true)
        {
            mask &= ~(ConditionalFormatFlags.OddVBand | ConditionalFormatFlags.EvenVBand);
        }

        return mask;
    }

    public static ConditionalFormatFlags ParseConditionalFormatFlags(ConditionalFormatStyle? cnfStyle)
    {
        if (cnfStyle == null)
        {
            return ConditionalFormatFlags.None;
        }

        var flags = ConditionalFormatFlags.None;
        if (cnfStyle.FirstRow?.Value == true) flags |= ConditionalFormatFlags.FirstRow;
        if (cnfStyle.LastRow?.Value == true) flags |= ConditionalFormatFlags.LastRow;
        if (cnfStyle.FirstColumn?.Value == true) flags |= ConditionalFormatFlags.FirstColumn;
        if (cnfStyle.LastColumn?.Value == true) flags |= ConditionalFormatFlags.LastColumn;
        if (cnfStyle.OddVerticalBand?.Value == true) flags |= ConditionalFormatFlags.OddVBand;
        if (cnfStyle.EvenVerticalBand?.Value == true) flags |= ConditionalFormatFlags.EvenVBand;
        if (cnfStyle.OddHorizontalBand?.Value == true) flags |= ConditionalFormatFlags.OddHBand;
        if (cnfStyle.EvenHorizontalBand?.Value == true) flags |= ConditionalFormatFlags.EvenHBand;
        if (cnfStyle.FirstRowFirstColumn?.Value == true) flags |= ConditionalFormatFlags.FirstRowFirstColumn;
        if (cnfStyle.FirstRowLastColumn?.Value == true) flags |= ConditionalFormatFlags.FirstRowLastColumn;
        if (cnfStyle.LastRowFirstColumn?.Value == true) flags |= ConditionalFormatFlags.LastRowFirstColumn;
        if (cnfStyle.LastRowLastColumn?.Value == true) flags |= ConditionalFormatFlags.LastRowLastColumn;

        return flags;
    }

    /// <summary>
    /// Returns the conditional regions that apply to a given cell, in cascade order
    /// (lowest priority first). Caller cascades borders / shading by overlaying each
    /// region's value onto the running result.
    /// <para>
    /// ECMA-376 priority (low → high), which this method emits in order:
    /// wholeTable (caller's base) → bandHoriz → bandVert → lastCol → firstCol →
    /// lastRow → firstRow → seCell → swCell → neCell → nwCell.
    /// </para>
    /// </summary>
    /// <param name="flags">Explicit <c>w:cnfStyle</c> flags from the row, cell, or paragraph. When
    /// <see cref="ConditionalFormatFlags.None"/>, flags are derived from the cell's grid position.</param>
    /// <param name="rowIndex">Zero-based row index of the cell within the table.</param>
    /// <param name="colIndex">Zero-based column index of the cell within the row.</param>
    /// <param name="totalRows">Total number of rows in the table.</param>
    /// <param name="totalCols">Total number of columns in the row.</param>
    /// <param name="rowBandSize">Row band size from <c>w:tblStylePr</c> (rows per horizontal band).</param>
    /// <param name="colBandSize">Column band size from <c>w:tblStylePr</c> (columns per vertical band).</param>
    /// <param name="tableLookMask">Subset of conditions that the table's <c>w:tblLook</c>
    /// allows to be auto-derived from cell position. Explicit <c>w:cnfStyle</c> is always
    /// honoured regardless of this mask. Word writes the <c>w:tblLook</c> bits to indicate
    /// "treat this row as the header" / "no banding" — without honouring it Morph applies
    /// banding shading to tables Word renders un-banded (see <c>cards/09</c>).</param>
    public static IEnumerable<TableStyleOverrideValues> ResolveActiveConditions(
        ConditionalFormatFlags flags,
        int rowIndex,
        int colIndex,
        int totalRows,
        int totalCols,
        int rowBandSize,
        int colBandSize,
        ConditionalFormatFlags tableLookMask = ConditionalFormatFlagsExtensions.AllConditions)
    {
        // When the source provides an explicit cnfStyle (row, cell, or paragraph),
        // trust those flags. Otherwise derive them from the cell's grid position,
        // but only for conditions the table's tblLook permits.
        if (flags == ConditionalFormatFlags.None)
        {
            flags = DerivePositionalFlags(rowIndex, colIndex, totalRows, totalCols, rowBandSize, colBandSize) & tableLookMask;
        }

        // ECMA-376 priority: lowest precedence first; later overrides earlier.
        if ((flags & ConditionalFormatFlags.OddHBand) != 0)
        {
            yield return TableStyleOverrideValues.Band1Horizontal;
        }
        else if ((flags & ConditionalFormatFlags.EvenHBand) != 0)
        {
            yield return TableStyleOverrideValues.Band2Horizontal;
        }

        if ((flags & ConditionalFormatFlags.OddVBand) != 0)
        {
            yield return TableStyleOverrideValues.Band1Vertical;
        }
        else if ((flags & ConditionalFormatFlags.EvenVBand) != 0)
        {
            yield return TableStyleOverrideValues.Band2Vertical;
        }

        if ((flags & ConditionalFormatFlags.LastColumn) != 0)
        {
            yield return TableStyleOverrideValues.LastColumn;
        }

        if ((flags & ConditionalFormatFlags.FirstColumn) != 0)
        {
            yield return TableStyleOverrideValues.FirstColumn;
        }

        if ((flags & ConditionalFormatFlags.LastRow) != 0)
        {
            yield return TableStyleOverrideValues.LastRow;
        }

        if ((flags & ConditionalFormatFlags.FirstRow) != 0)
        {
            yield return TableStyleOverrideValues.FirstRow;
        }

        if ((flags & ConditionalFormatFlags.LastRowLastColumn) != 0)
        {
            yield return TableStyleOverrideValues.SouthEastCell;
        }

        if ((flags & ConditionalFormatFlags.LastRowFirstColumn) != 0)
        {
            yield return TableStyleOverrideValues.SouthWestCell;
        }

        if ((flags & ConditionalFormatFlags.FirstRowLastColumn) != 0)
        {
            yield return TableStyleOverrideValues.NorthEastCell;
        }

        if ((flags & ConditionalFormatFlags.FirstRowFirstColumn) != 0)
        {
            yield return TableStyleOverrideValues.NorthWestCell;
        }
    }

    static ConditionalFormatFlags DerivePositionalFlags(
        int rowIndex,
        int colIndex,
        int totalRows,
        int totalCols,
        int rowBandSize,
        int colBandSize)
    {
        var flags = ConditionalFormatFlags.None;

        if (rowIndex == 0)
        {
            flags |= ConditionalFormatFlags.FirstRow;
        }
        else if (rowIndex == totalRows - 1)
        {
            flags |= ConditionalFormatFlags.LastRow;
        }
        else if (rowBandSize > 0)
        {
            // Body rows alternate after the header row.
            flags |= (rowIndex - 1) / rowBandSize % 2 == 0
                ? ConditionalFormatFlags.OddHBand
                : ConditionalFormatFlags.EvenHBand;
        }

        if (colIndex == 0)
        {
            flags |= ConditionalFormatFlags.FirstColumn;
        }
        else if (colIndex == totalCols - 1)
        {
            flags |= ConditionalFormatFlags.LastColumn;
        }
        else if (colBandSize > 0)
        {
            flags |= (colIndex - 1) / colBandSize % 2 == 0
                ? ConditionalFormatFlags.OddVBand
                : ConditionalFormatFlags.EvenVBand;
        }

        return flags;
    }

    /// <summary>
    /// Reads the four sides of a <c>w:tblCellMar</c>, leaving a side null when the element omits
    /// it. Keeping "absent" distinct from "zero" is what lets a style's partial cell margin merge
    /// with its <c>w:basedOn</c> ancestor's per side — see <see cref="ResolveStyleCellPadding"/>.
    /// </summary>
    static (double? Top, double? Right, double? Bottom, double? Left) ReadTableCellMarginSides(TableCellMarginDefault margin)
    {
        double? top = null, right = null, bottom = null, left = null;

        var topMargin = margin.TopMargin;
        if (topMargin?.Width?.HasValue == true)
        {
            top = TableWidthToPoints(topMargin.Width.Value!);
        }

        if (margin.EndMargin?.Width?.HasValue == true)
        {
            right = TableWidthToPoints(margin.EndMargin.Width.Value!);
        }
        else if (margin.TableCellRightMargin?.Width?.HasValue == true)
        {
            right = margin.TableCellRightMargin.Width.Value / OoxmlUnits.TwipsPerPoint;
        }

        var bottomMargin = margin.BottomMargin;
        if (bottomMargin?.Width?.HasValue == true)
        {
            bottom = TableWidthToPoints(bottomMargin.Width.Value!);
        }

        if (margin.StartMargin?.Width?.HasValue == true)
        {
            left = TableWidthToPoints(margin.StartMargin.Width.Value!);
        }
        else if (margin.TableCellLeftMargin?.Width?.HasValue == true)
        {
            left = margin.TableCellLeftMargin.Width.Value / OoxmlUnits.TwipsPerPoint;
        }

        return (top, right, bottom, left);
    }

    public static CellSpacing? ParseTableCellMargin(TableCellMarginDefault margin) =>
        MergeTableCellMargin(margin, fallback: null);

    /// <summary>
    /// Parses a <c>w:tblCellMar</c>, taking each side the element omits from
    /// <paramref name="fallback"/> instead of zeroing it. Returns null only when the element
    /// carries no sides at all, letting the caller fall back wholesale.
    /// </summary>
    public static CellSpacing? MergeTableCellMargin(TableCellMarginDefault margin, CellSpacing? fallback)
    {
        var (top, right, bottom, left) = ReadTableCellMarginSides(margin);
        if (top == null &&
            right == null &&
            bottom == null &&
            left == null)
        {
            return null;
        }

        return new(top ?? fallback?.Top ?? 0,
            right ?? fallback?.Right ?? 0,
            bottom ?? fallback?.Bottom ?? 0,
            left ?? fallback?.Left ?? 0);
    }

    // Parses an OOXML table-width measure (CT_TblWidth/@w:w, type ST_MeasurementOrPercent) to points.
    // Accepts a bare number in twips (the dxa form Word normally writes) or an ST_UniversalMeasure
    // value carrying an explicit unit (e.g. "0pt", "1.5cm") — Aspose emits the latter for cell
    // margins. Percent and unparseable values yield 0.
    static double TableWidthToPoints(string value)
    {
        var span = value.AsSpan().Trim();
        if (span.IsEmpty || span[^1] == '%')
        {
            return 0;
        }

        if (span.Length > 2 &&
            char.IsAsciiLetter(span[^1]) &&
            char.IsAsciiLetter(span[^2]))
        {
            if (!double.TryParse(span[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var measure))
            {
                return 0;
            }

            return span[^2..].ToString().ToLowerInvariant() switch
            {
                "pt" => measure,
                "pc" or "pi" => measure * 12,
                "in" => measure * 72,
                "cm" => measure * 72 / 2.54,
                "mm" => measure * 72 / 25.4,
                _ => 0
            };
        }

        if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips))
        {
            return twips / OoxmlUnits.TwipsPerPoint;
        }

        return 0;
    }

    /// <summary>
    /// Parses a cell-level <c>w:tcMar</c> override. Per OOXML each side present in the override
    /// replaces that side of the table's <c>w:tblCellMar</c>; sides that are ABSENT inherit the
    /// table default rather than collapsing to zero. <paramref name="tableDefault"/> supplies that
    /// per-side fallback (the document's <c>w:tblCellMar</c>, then the table style's). Returns null
    /// only when the override carries no sides at all, letting the caller fall back to the table
    /// default wholesale. business-plans/04's cover heading (vAlign=bottom,
    /// <c>&lt;w:tcMar&gt;&lt;w:bottom w:w="0"/&gt;&lt;/w:tcMar&gt;</c>) keeps the inherited top margin.
    /// </summary>
    public static CellSpacing? ParseCellMargin(TableCellMargin margin, CellSpacing? tableDefault = null)
    {
        var top = tableDefault?.Top ?? 0;
        var right = tableDefault?.Right ?? 0;
        var bottom = tableDefault?.Bottom ?? 0;
        var left = tableDefault?.Left ?? 0;
        var hasAny = false;

        var topMargin = margin.TopMargin;
        if (topMargin?.Width?.HasValue == true)
        {
            top = TableWidthToPoints(topMargin.Width.Value!);
            hasAny = true;
        }

        if (margin.EndMargin?.Width?.HasValue == true)
        {
            right = TableWidthToPoints(margin.EndMargin.Width.Value!);
            hasAny = true;
        }
        else if (margin.RightMargin?.Width?.HasValue == true)
        {
            right = TableWidthToPoints(margin.RightMargin.Width.Value!);
            hasAny = true;
        }

        var bottomMargin = margin.BottomMargin;
        if (bottomMargin?.Width?.HasValue == true)
        {
            bottom = TableWidthToPoints(bottomMargin.Width.Value!);
            hasAny = true;
        }

        if (margin.StartMargin?.Width?.HasValue == true)
        {
            left = TableWidthToPoints(margin.StartMargin.Width.Value!);
            hasAny = true;
        }
        else if (margin.LeftMargin?.Width?.HasValue == true)
        {
            left = TableWidthToPoints(margin.LeftMargin.Width.Value!);
            hasAny = true;
        }

        if (hasAny)
        {
            return new CellSpacing(top, right, bottom, left);
        }

        return null;
    }

    // An ST_OnOffOnly w:val (w:cantSplit, w:tblHeader): absent is on, and off is "off" — or the "0" and
    // "false" writers put there though the schema allows only on/off. The SDK's typed value throws on
    // those rather than parsing them.
    public static bool OnOffOnlyIsOn(EnumValue<OnOffOnlyValues>? value) =>
        value?.InnerText?.ToLowerInvariant() is not ("off" or "0" or "false");
}
