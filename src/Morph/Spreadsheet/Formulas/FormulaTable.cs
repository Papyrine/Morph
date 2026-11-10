/// <summary>
/// A table (<c>xl/tables/table1.xml</c>) as its structured references need it: where it stands, how many
/// header and totals rows it has, and its columns' names in order.
/// </summary>
sealed record FormulaTable(string Name, int Sheet, SheetRange Range, int HeaderRows, int TotalsRows, IReadOnlyList<string> Columns)
{
    public int FirstDataRow => Range.FirstRow + HeaderRows;

    public int LastDataRow => Range.LastRow - TotalsRows;

    /// <summary>The column a name is, from 0; -1 when the table has none of that name.</summary>
    public int ColumnIndex(string name)
    {
        var wanted = Normalize(name);
        for (var index = 0; index < Columns.Count; index++)
        {
            if (string.Equals(Normalize(Columns[index]), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        // A name that differs only in the spaces around it, which a hand-typed reference may drop.
        var trimmed = wanted.AsSpan().Trim();
        for (var index = 0; index < Columns.Count; index++)
        {
            if (Normalize(Columns[index]).AsSpan().Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    // A column's name may hold a line break, written CRLF in a formula and LF (or _x000a_) in the table.
    static string Normalize(string name) =>
        name.Replace("\r", string.Empty, StringComparison.Ordinal);
}
