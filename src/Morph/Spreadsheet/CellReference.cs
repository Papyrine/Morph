/// <summary>
/// Parsing for A1-style cell references, the only addressing a worksheet uses.
///
/// Columns are bijective base-26 — A..Z, then AA..AZ, BA.. — which is NOT ordinary base-26 because
/// there is no zero digit: AA is 27, not 26. Counting from 1 per letter is what makes it work.
/// </summary>
static class CellReference
{
    /// <summary>The 1-based column of a reference such as <c>BC7</c>, or 0 when it cannot be read.</summary>
    public static int ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        var column = 0;
        foreach (var ch in reference)
        {
            var upper = char.ToUpperInvariant(ch);
            if (upper is < 'A' or > 'Z')
            {
                break;
            }

            column = column * 26 + (upper - 'A') + 1;
        }

        return column;
    }

    /// <summary>The 1-based row of a reference such as <c>BC7</c>, or 0 when it cannot be read.</summary>
    public static int RowOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        var digits = new string(reference.SkipWhile(char.IsAsciiLetter).TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var row) ? row : 0;
    }

    /// <summary>Parses an <c>A1:F19</c> range, or a single <c>A1</c>, into inclusive bounds.</summary>
    public static SheetRange? ParseRange(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        // A range may be sheet-qualified ('My Sheet'!$B$2:$F$19) and is usually absolute.
        var bare = reference;
        var bang = bare.LastIndexOf('!');
        if (bang >= 0)
        {
            bare = bare[(bang + 1)..];
        }

        bare = bare.Replace("$", string.Empty, StringComparison.Ordinal);

        var parts = bare.Split(':');
        var firstColumn = ColumnOf(parts[0]);
        var firstRow = RowOf(parts[0]);
        if (firstColumn == 0 || firstRow == 0)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return new(firstRow, firstColumn, firstRow, firstColumn);
        }

        var lastColumn = ColumnOf(parts[1]);
        var lastRow = RowOf(parts[1]);
        return new(
            Math.Min(firstRow, lastRow),
            Math.Min(firstColumn, lastColumn),
            Math.Max(firstRow, lastRow),
            Math.Max(firstColumn, lastColumn));
    }
}

/// <summary>An inclusive rectangle of cells, 1-based on both axes.</summary>
readonly record struct SheetRange(int FirstRow, int FirstColumn, int LastRow, int LastColumn)
{
    public bool IsEmpty => LastRow < FirstRow || LastColumn < FirstColumn;

    public int RowCount => Math.Max(0, LastRow - FirstRow + 1);

    public int ColumnCount => Math.Max(0, LastColumn - FirstColumn + 1);

    public bool ContainsRow(int row) => row >= FirstRow && row <= LastRow;

    /// <summary>This range clipped to <paramref name="bounds"/>, for a print area inside a used range.</summary>
    public SheetRange Intersect(SheetRange bounds) =>
        new(
            Math.Max(FirstRow, bounds.FirstRow),
            Math.Max(FirstColumn, bounds.FirstColumn),
            Math.Min(LastRow, bounds.LastRow),
            Math.Min(LastColumn, bounds.LastColumn));
}
