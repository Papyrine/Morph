/// <summary>
/// One table row placed on a page at an absolute position, in points from the page's top-left, carrying
/// its <see cref="Cells"/> (each with its box, shading, borders and laid-out content). The row is also
/// anchored back to its source <see cref="Table"/> and zero-based <see cref="RowIndex"/>. Rows do not
/// split: a row that will not fit in the space left moves whole to the next page. A row flagged
/// <see cref="IsRepeatedHeader"/> is a <c>w:tblHeader</c> row re-emitted at the top of a continuation
/// page, so it appears on more than one <see cref="LaidOutPage"/> for the same <see cref="RowIndex"/>.
/// </summary>
sealed record PlacedTableRow(
    float X,
    float Y,
    float Width,
    float Height,
    TableElement Table,
    int RowIndex,
    bool IsRepeatedHeader,
    IReadOnlyList<PlacedCell> Cells) : PlacedItem(X, Y, Width, Height);
