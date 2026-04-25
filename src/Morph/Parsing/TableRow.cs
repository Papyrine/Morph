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

    /// <summary>
    /// Whether this row is marked as a header row (w:trPr/w:tblHeader).
    /// Header rows are intended to repeat at the top of each page when a table spans pages.
    /// Captured in the model; the renderer does not yet repeat them.
    /// </summary>
    public bool IsHeader { get; init; }
}