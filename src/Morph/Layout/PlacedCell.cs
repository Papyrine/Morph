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
///
/// <see cref="BottomEdgeInset"/> is how far above the box's bottom face the bottom border's stack
/// starts, in points: the declared stack the row reserved inside itself when it is the table's last
/// row or a split fragment (<c>TableHeightCalculator</c> charges those), zero for an interior row
/// whose bottom edge hangs below its face into the row under it — Word hangs every horizontal cell
/// edge DOWN from its grid line (<c>BorderStroke.CellEdgeLines</c>).
///
/// <see cref="Diagonals"/> are the cell's <c>w:tl2br</c> / <c>w:tr2bl</c> rules, drawn corner to
/// corner across the box at their grid-floored width after the edges. They were an engine-flip
/// orphan: parsed onto <see cref="TableCellProperties.Diagonals"/> and drawn by the deleted
/// production renderers, but never carried here, so <c>table_diagonal_borders/01</c> rendered none.
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
    float ClipSpillRight = 0,
    float BottomEdgeInset = 0,
    CellDiagonals? Diagonals = null);
