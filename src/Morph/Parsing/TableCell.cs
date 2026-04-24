/// <summary>
/// Represents a cell in a table row.
/// </summary>
sealed class TableCell
{
    public required IReadOnlyList<DocumentElement> Content { get; init; }
    public TableCellProperties Properties { get; init; } = new();
}