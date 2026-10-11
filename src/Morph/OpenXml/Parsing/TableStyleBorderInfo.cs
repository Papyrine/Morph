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
/// <see cref="CannotSplit"/> is the style's own <c>w:trPr/w:cantSplit</c>, which reaches every row
/// that does not declare its own. <see cref="CellMargin"/> is the style's whole-table
/// <c>w:tcPr/w:tcMar</c> — margins the style gives every cell, which stand above the table's own
/// <c>w:tblCellMar</c> and under a cell's own <c>w:tcMar</c> (<c>DocumentParser.ResolveStyleCellMargin</c>).
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
    double? IndentPoints = null,
    bool CannotSplit = false,
    DocumentFormat.OpenXml.Wordprocessing.TableCellMargin? CellMargin = null);
