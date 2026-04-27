/// <summary>
/// Cell-level diagonal borders (<c>w:tl2br</c> top-left→bottom-right and
/// <c>w:tr2bl</c> top-right→bottom-left). Kept separate from <see cref="CellBorders"/>
/// because diagonals never inherit from table-level borders the way the four sides do —
/// a cell that specifies only diagonals must still pick up the table's outer/inside
/// borders for its sides.
/// </summary>
sealed record CellDiagonals
{
    public BorderEdge Down { get; init; } = BorderEdge.None;
    public BorderEdge Up { get; init; } = BorderEdge.None;

    public bool HasAny => Down.IsVisible || Up.IsVisible;
}
