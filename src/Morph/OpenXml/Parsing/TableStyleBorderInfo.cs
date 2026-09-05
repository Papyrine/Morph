/// <summary>
/// Cell-level formatting from a single conditional region of a table style
/// (a <c>w:tblStylePr</c> block): borders, cell shading, and the region's run properties.
/// <see cref="RunColorHex"/> is the colour out of <see cref="RunProperties"/>, kept as its own
/// member because the colour cascade runs as a post-pass over already-parsed cell content while
/// the rest of the run properties are layered during run resolution.
/// </summary>
sealed record ConditionalFormat(
    CellBorders? Borders,
    string? BackgroundColorHex,
    string? RunColorHex,
    DeclaredRunProperties? RunProperties = null);

/// <summary>
/// Captures the fields Morph cascades through a table style — whole-table defaults plus each
/// <c>w:tblStylePr</c> conditional region. Paragraph-property cascading isn't modelled yet.
/// </summary>
sealed record TableStyleBorderInfo(
    CellBorders Outer,
    BorderEdge InsideH,
    BorderEdge InsideV,
    string? BackgroundColorHex,
    int RowBandSize,
    int ColBandSize,
    double CellSpacingPoints,
    Dictionary<TableStyleOverrideValues, ConditionalFormat>? Conditionals,
    CellVerticalAlignment? VerticalAlignment = null,
    CellSpacing? DefaultCellPadding = null,
    DeclaredRunProperties? RunProperties = null,
    double? IndentPoints = null);
