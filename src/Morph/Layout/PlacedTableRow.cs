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
    IReadOnlyList<PlacedCell> Cells) :
    PlacedItem(X, Y, Width, Height)
{
    /// <summary>
    /// Whether the painters lay down every cell's shading before any cell's content. A cell whose
    /// text spills over its neighbours (<see cref="PlacedCell.ClipSpillLeft"/> /
    /// <see cref="PlacedCell.ClipSpillRight"/>) is drawn over THEIR shading, as Excel draws
    /// overflowing text over the fill of the cells it crosses: back-to-school-planner's titles run
    /// over cells filled white by the workbook's Normal style, and painted cell by cell the next
    /// cell's white covered "Budget breakdown" from column C on. Elsewhere a row keeps the cell-by-
    /// cell order (shading, content, borders), which is Word's.
    /// </summary>
    public bool ShadesFirst => Cells.Any(_ => _.ClipSpillLeft > 0 || _.ClipSpillRight > 0);
}
