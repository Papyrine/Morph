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
}