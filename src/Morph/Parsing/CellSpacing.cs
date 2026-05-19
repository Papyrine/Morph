/// <summary>
/// Represents spacing (padding or margin) with individual values for each side.
/// </summary>
sealed record CellSpacing
{
    // Word's default cell margins are 0
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }
    public double Left { get; init; }

    public CellSpacing() { }

    public CellSpacing(double all) =>
        Top = Right = Bottom = Left = all;

    public CellSpacing(double vertical, double horizontal)
    {
        Top = Bottom = vertical;
        Left = Right = horizontal;
    }

    public CellSpacing(double top, double right, double bottom, double left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    /// <summary>Total horizontal spacing (left + right).</summary>
    public double Horizontal => Left + Right;

    /// <summary>Total vertical spacing (top + bottom).</summary>
    public double Vertical => Top + Bottom;
}