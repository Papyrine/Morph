/// <summary>
/// Represents a cell in a table row.
/// </summary>
sealed class TableCell
{
    public required IReadOnlyList<DocumentElement> Content { get; init; }

    /// <summary>
    /// Floating drawings anchored inside this cell. Word anchors them to the cell's frame
    /// (<c>layoutInCell</c>, the default), so the table renderer draws them when the cell's
    /// rectangle is known — behind-text ones under the cell's content, the rest above it.
    /// Sorted by <c>relativeHeight</c> (Word's z-space) at parse.
    /// </summary>
    public IReadOnlyList<DocumentElement> Floats { get; init; } = [];
    public TableCellProperties Properties { get; init; } = new();
}