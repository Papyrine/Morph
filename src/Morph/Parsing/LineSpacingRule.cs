/// <summary>
/// Specifies how line spacing is calculated.
/// </summary>
enum LineSpacingRule
{
    /// <summary>
    /// Automatic/Multiple: Line spacing is a multiple of the line height (e.g., 1.0, 1.5, 2.0).
    /// </summary>
    Auto,

    /// <summary>
    /// Exact: Line spacing is exactly the specified value in points.
    /// </summary>
    Exactly,

    /// <summary>
    /// At Least: Line spacing is at least the specified value in points.
    /// </summary>
    AtLeast
}