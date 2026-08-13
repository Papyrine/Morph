/// <summary>
/// Represents borders for all four edges of a cell.
/// Diagonals (<c>w:tl2br</c> / <c>w:tr2bl</c>) are tracked separately on
/// <see cref="TableCellProperties.Diagonals"/> so they don't interfere with the
/// cell→table 4-side border cascade.
/// </summary>
sealed record CellBorders
{
    public BorderEdge Top { get; init; } = BorderEdge.None;
    public BorderEdge Right { get; init; } = BorderEdge.None;
    public BorderEdge Bottom { get; init; } = BorderEdge.None;
    public BorderEdge Left { get; init; } = BorderEdge.None;

    /// <summary>Returns true if any border edge is visible.</summary>
    public bool HasAnyBorder => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;

    public static CellBorders All => new()
    {
        Top = BorderEdge.Default,
        Right = BorderEdge.Default,
        Bottom = BorderEdge.Default,
        Left = BorderEdge.Default
    };

    /// <summary>
    /// The same edge on all four sides — a run border (<c>w:bdr</c>), which OOXML declares once and
    /// Word draws as a box around the run. Lets the run path reuse the cell/paragraph edge painter
    /// rather than growing its own stroke code in each backend.
    /// </summary>
    public static CellBorders Uniform(BorderEdge edge) => new()
    {
        Top = edge,
        Right = edge,
        Bottom = edge,
        Left = edge
    };
}