/// <summary>
/// One table row placed on a page at an absolute position, in points from the page's top-left. The row
/// is anchored back to its source <see cref="Table"/> and its zero-based <see cref="RowIndex"/>, so a
/// painter can resolve the cells, borders and shading to draw (the per-cell line breakdown is a later
/// slice). Rows do not split: a row that will not fit in the space left moves whole to the next page. A
/// row flagged <see cref="IsRepeatedHeader"/> is a <c>w:tblHeader</c> row re-emitted at the top of a
/// continuation page, so it appears on more than one <see cref="LaidOutPage"/> for the same
/// <see cref="RowIndex"/>.
/// </summary>
sealed record PlacedTableRow(
    float X,
    float Y,
    float Width,
    float Height,
    TableElement Table,
    int RowIndex,
    bool IsRepeatedHeader) : PlacedItem(X, Y, Width, Height);
