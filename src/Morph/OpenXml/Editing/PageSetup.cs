/// <summary>
/// A section's page: its size and margins, in twips, and whether it is turned on its side. The size is
/// the page as it stands — a landscape page is wider than it is high.
/// </summary>
sealed record PageSetup(int Width, int Height, int Top, int Right, int Bottom, int Left, bool Landscape)
{
    /// <summary>How many columns the text is set in.</summary>
    public int Columns { get; init; } = 1;

    /// <summary>Whether a line is drawn between the columns.</summary>
    public bool LineBetween { get; init; }
}
