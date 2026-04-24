/// <summary>
/// Represents a row in a table.
/// </summary>
sealed class TableRow
{
    public required IReadOnlyList<TableCell> Cells { get; init; }

    /// <summary>
    /// Explicit row height in points, if specified in the document.
    /// Null means the height should be calculated from content.
    /// </summary>
    public double? HeightPoints { get; init; }

    /// <summary>
    /// Whether the row height is exact (true) or minimum (false).
    /// When exact, the row will be exactly HeightPoints tall.
    /// When minimum, the row will be at least HeightPoints tall.
    /// </summary>
    public bool IsExactHeight { get; init; }
}