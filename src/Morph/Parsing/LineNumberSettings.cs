/// <summary>
/// Settings for line numbering in a document section.
/// </summary>
sealed record LineNumberSettings
{
    /// <summary>
    /// The value before the first counted line (w:start; Word's UI "start at 1" writes 0), so
    /// the first displayed number is <c>Start + 1</c>. Default is 0.
    /// </summary>
    public int Start { get; init; }

    /// <summary>
    /// Line number increment (1 = every line, 5 = every 5th line, etc.). Default is 1.
    /// </summary>
    public int CountBy { get; init; } = 1;

    /// <summary>
    /// Distance from text to line numbers in points. Default is 18 points (0.25 inch).
    /// </summary>
    public double DistancePoints { get; init; } = 18;

    /// <summary>
    /// When to restart line numbering.
    /// </summary>
    public LineNumberRestart Restart { get; init; } = LineNumberRestart.NewPage;
}