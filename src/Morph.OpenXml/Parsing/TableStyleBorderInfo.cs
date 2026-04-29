/// <summary>
/// Cell-level formatting from a single conditional region of a table style
/// (a <c>w:tblStylePr</c> block). Captures the cascaded fields that Morph
/// can render today — borders, cell shading, and run-level text colour.
/// </summary>
sealed record ConditionalFormat(
    CellBorders? Borders,
    string? BackgroundColorHex,
    string? RunColorHex);

/// <summary>
/// Captures the cell-level fields Morph cascades through a table style
/// (whole-table defaults plus each <c>w:tblStylePr</c> conditional region).
/// Run-property and paragraph-property cascading aren't modelled yet.
/// </summary>
sealed record TableStyleBorderInfo(
    CellBorders Outer,
    BorderEdge InsideH,
    BorderEdge InsideV,
    string? BackgroundColorHex,
    int RowBandSize,
    int ColBandSize,
    double CellSpacingPoints,
    Dictionary<TableStyleOverrideValues, ConditionalFormat>? Conditionals);
