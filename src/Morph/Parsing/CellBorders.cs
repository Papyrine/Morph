/// <summary>
/// Represents borders for all four edges of a cell.
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
}