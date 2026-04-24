/// <summary>
/// Represents a table in the document.
/// </summary>
sealed class TableElement : DocumentElement
{
    public required IReadOnlyList<TableRow> Rows { get; init; }
    public TableProperties Properties { get; init; } = new();
}