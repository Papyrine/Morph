/// <summary>Reference frame for a floating table's <c>tblpY</c> offset.</summary>
enum FloatingTableVerticalAnchor
{
    /// <summary>Anchored to the surrounding text-flow position (default for unspecified).</summary>
    Text,

    /// <summary>Anchored to the top of the page's content area (margin top).</summary>
    Margin,

    /// <summary>Anchored to the top edge of the page itself.</summary>
    Page
}

/// <summary>Reference frame for a floating table's <c>tblpX</c> offset.</summary>
enum FloatingTableHorizontalAnchor
{
    /// <summary>Anchored to the column / text-flow column (default for unspecified).</summary>
    Text,

    /// <summary>Anchored to the page margin edge.</summary>
    Margin,

    /// <summary>Anchored to the page edge.</summary>
    Page
}

/// <summary>
/// Table-level properties.
/// </summary>
sealed record TableProperties
{
    /// <summary>Whether this table is a floating table with absolute positioning (w:tblpPr).</summary>
    public bool IsFloating { get; init; }

    /// <summary>
    /// Vertical offset in points from the floating-table anchor (w:tblpPr/@w:tblpY).
    /// Only meaningful when <see cref="IsFloating"/> is true. Morph treats floating tables
    /// inline today, so this is added as a y-offset before the table's first row to
    /// approximate Word's positioning.
    /// </summary>
    public double FloatingYOffsetPoints { get; init; }

    /// <summary>
    /// Horizontal offset in points from the floating-table anchor (w:tblpPr/@w:tblpX).
    /// Only meaningful when <see cref="IsFloating"/> is true. Used as the table's left edge
    /// relative to the page's text column, ignoring <see cref="Alignment"/>.
    /// </summary>
    public double FloatingXOffsetPoints { get; init; }

    /// <summary>What <see cref="FloatingYOffsetPoints"/> is measured from (w:tblpPr/@w:vertAnchor).</summary>
    public FloatingTableVerticalAnchor FloatingVerticalAnchor { get; init; } = FloatingTableVerticalAnchor.Text;

    /// <summary>What <see cref="FloatingXOffsetPoints"/> is measured from (w:tblpPr/@w:horzAnchor).</summary>
    public FloatingTableHorizontalAnchor FloatingHorizontalAnchor { get; init; } = FloatingTableHorizontalAnchor.Text;

    /// <summary>Default borders for cells (from w:tblBorders). Null means no borders.</summary>
    public CellBorders? DefaultBorders { get; init; }

    /// <summary>Inside horizontal border (between rows). Null means none.</summary>
    public BorderEdge? InsideHorizontalBorder { get; init; }

    /// <summary>Inside vertical border (between columns). Null means none.</summary>
    public BorderEdge? InsideVerticalBorder { get; init; }

    /// <summary>Default cell padding (used when cell doesn't specify its own).</summary>
    public CellSpacing DefaultCellPadding { get; init; } = new();

    /// <summary>Default cell margins (used when cell doesn't specify its own).</summary>
    public CellSpacing DefaultCellMargin { get; init; } = new();

    /// <summary>Table indent from left margin (can be negative).</summary>
    public double IndentPoints { get; init; }

    /// <summary>Column widths from the table grid (w:tblGrid), in points. Null if not specified.</summary>
    public IReadOnlyList<double>? GridColumnWidths { get; init; }

    /// <summary>Explicit table width from <c>w:tblW</c> with <c>w:type="dxa"</c>, in points.
    /// Null when the table omits <c>w:tblW</c> or specifies it as auto/pct (the autofit
    /// path will resolve those forms from the grid / available width instead).</summary>
    public double? PreferredWidthPoints { get; init; }

    /// <summary>True when the table sets <c>w:tblW w:type="pct"</c> with a positive value —
    /// the table is intended to fill its container, even when columns/cells don't carry
    /// explicit widths. Drives the content-based autofit pass to scale natural widths up
    /// to the available width instead of hugging content.</summary>
    public bool FillContainer { get; init; }

    /// <summary>
    /// Table-level horizontal alignment within the page content area (from w:tblPr/w:jc).
    /// Justify is not valid for tables and is treated as Left.
    /// </summary>
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>
    /// Whether the table uses automatic column-width fitting (w:tblLayout/@type="autofit").
    /// Default true matches Word's behaviour for tables without an explicit layout type.
    /// When false, column widths are taken verbatim from <see cref="GridColumnWidths"/>.
    /// </summary>
    public bool IsAutoFit { get; init; } = true;

    /// <summary>
    /// Cell spacing in points (from <c>w:tblCellSpacing</c>). When non-zero, switches the
    /// table to the "detached border" model — each cell renders inside a box shrunk by
    /// <see cref="CellSpacingPoints"/> on every edge, producing visible gaps of
    /// <c>2 * CellSpacingPoints</c> between adjacent cells. Per ECMA-376 §17.4.44 the value
    /// applies as additional padding on each side of every cell, so the visible gap
    /// between two cells is twice this value.
    /// </summary>
    public double CellSpacingPoints { get; init; }
}