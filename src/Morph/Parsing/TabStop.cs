/// <summary>
/// A single tab stop within a paragraph.
/// </summary>
sealed record TabStop
{
    /// <summary>Position in points from the left margin (converted from twips at parse time).</summary>
    public required double PositionPoints { get; init; }

    public TabAlignment Alignment { get; init; } = TabAlignment.Left;
    public TabLeader Leader { get; init; } = TabLeader.None;
}