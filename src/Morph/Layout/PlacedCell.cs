/// <summary>
/// One table cell placed on a page: its box in points from the page's top-left, its optional shading
/// (<see cref="BackgroundColorHex"/>) and resolved <see cref="Borders"/>, and its laid-out
/// <see cref="Content"/> (the cell's paragraphs, already wrapped and positioned inside the padded
/// interior). A painter fills the box, draws the content, then strokes the borders. A vertically merged
/// cell's box spans the merged rows' heights; a merge-continuation cell contributes no
/// <see cref="PlacedCell"/> (the originating cell covers it). Cells live on their
/// <see cref="PlacedTableRow"/>, not directly on the page.
///
/// <see cref="ClipContent"/> asks the painter to bound the content — and only the content, not the
/// shading or the borders — to the box, which is Excel's rule for a cell whose text outgrows its
/// column or its pinned row height (<see cref="TableCellProperties.ClipOverflow"/>). It is false for
/// every DOCX and HTML cell, which keep drawing their overflow as Word does. The clip is the box
/// widened by <see cref="ClipSpillLeft"/> / <see cref="ClipSpillRight"/>, which are the empty
/// neighbours the ink is allowed to run over (see <see cref="TableCellProperties.ClipSpillLeftPoints"/>);
/// both are zero unless the content spills in a direction the box itself cannot cover.
/// </summary>
sealed record PlacedCell(
    float X,
    float Y,
    float Width,
    float Height,
    string? BackgroundColorHex,
    CellBorders? Borders,
    IReadOnlyList<PlacedItem> Content,
    bool ClipContent = false,
    float ClipSpillLeft = 0,
    float ClipSpillRight = 0);
