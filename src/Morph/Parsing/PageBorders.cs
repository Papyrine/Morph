/// <summary>
/// Decorative borders drawn around each page (from w:pgBorders).
/// </summary>
sealed record PageBorders
{
    public BorderEdge Top { get; init; } = BorderEdge.None;
    public BorderEdge Right { get; init; } = BorderEdge.None;
    public BorderEdge Bottom { get; init; } = BorderEdge.None;
    public BorderEdge Left { get; init; } = BorderEdge.None;

    /// <summary>Inset from the page edge for the top border, in points.</summary>
    public double TopSpacePoints { get; init; } = 24;

    /// <summary>Inset from the page edge for the right border, in points.</summary>
    public double RightSpacePoints { get; init; } = 24;

    /// <summary>Inset from the page edge for the bottom border, in points.</summary>
    public double BottomSpacePoints { get; init; } = 24;

    /// <summary>Inset from the page edge for the left border, in points.</summary>
    public double LeftSpacePoints { get; init; } = 24;

    /// <summary>True when at least one edge is rendered.</summary>
    public bool HasAnyBorder => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;
}
