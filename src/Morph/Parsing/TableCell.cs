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

    /// <summary>
    /// For each entry in <see cref="Floats"/>: the ordinal (paragraph count) of its anchor
    /// paragraph within <see cref="Content"/>. Paragraph-relative vertical anchors resolve
    /// against that paragraph's laid-out position; −1 falls back to the cell top.
    /// </summary>
    public IReadOnlyList<int> FloatAnchorParagraphOrdinals { get; init; } = [];
    public TableCellProperties Properties { get; init; } = new();
}