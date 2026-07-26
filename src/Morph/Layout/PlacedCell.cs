/// <summary>
/// One table cell placed on a page: its box in points from the page's top-left, its optional shading
/// (<see cref="BackgroundColorHex"/>) and resolved <see cref="Borders"/>, and its laid-out
/// <see cref="Content"/> (the cell's paragraphs, already wrapped and positioned inside the padded
/// interior). A painter fills the box, draws the content, then strokes the borders. A vertically merged
/// cell's box spans the merged rows' heights; a merge-continuation cell contributes no
/// <see cref="PlacedCell"/> (the originating cell covers it). Cells live on their
/// <see cref="PlacedTableRow"/>, not directly on the page.
/// </summary>
sealed record PlacedCell(
    float X,
    float Y,
    float Width,
    float Height,
    string? BackgroundColorHex,
    CellBorders? Borders,
    IReadOnlyList<PlacedItem> Content);
