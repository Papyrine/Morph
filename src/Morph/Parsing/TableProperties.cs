/// <summary>
/// Table-level properties.
/// </summary>
sealed record TableProperties
{
    /// <summary>Whether this table is a floating table with absolute positioning (w:tblpPr).</summary>
    public bool IsFloating { get; init; }

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