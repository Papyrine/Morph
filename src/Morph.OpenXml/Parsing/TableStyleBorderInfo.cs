/// <summary>
/// Stores border information extracted from a table style, including conditional formatting overrides.
/// </summary>
sealed record TableStyleBorderInfo(
    CellBorders Outer,
    BorderEdge InsideH,
    BorderEdge InsideV,
    int ColBandSize,
    Dictionary<TableStyleOverrideValues, CellBorders>? ConditionalBorders);