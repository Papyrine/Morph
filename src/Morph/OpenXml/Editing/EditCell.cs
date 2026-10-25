/// <summary>
/// A table's cell, as a paragraph in it knows it: the table, by its ordinal among the document's tables in
/// the order they start; the row, by its index in the table; the cell, by its index in the row.
/// </summary>
readonly record struct EditCell(int Table, int Row, int Column);
