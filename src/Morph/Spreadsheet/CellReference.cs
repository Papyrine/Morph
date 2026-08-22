/// <summary>
/// Parsing for A1-style cell references, the only addressing a worksheet uses.
///
/// Columns are bijective base-26 — A..Z, then AA..AZ, BA.. — which is NOT ordinary base-26 because
/// there is no zero digit: AA is 27, not 26. Counting from 1 per letter is what makes it work.
/// </summary>
static class CellReference
{
    // Excel's worksheet limits.
    public const int MaxRow = 1_048_576;
    public const int MaxColumn = 16_384;
    /// <summary>The 1-based column of a reference such as <c>BC7</c>, or 0 when it cannot be read.</summary>
    /// <exception cref="InvalidOperationException">
    /// The reference names a column beyond <see cref="MaxColumn"/>. A schema-valid reference can carry
    /// far more letters than a worksheet has columns, and such value becomes a grid bound that overflows
    /// its loop counter -- so it is rejected rather than clamped.</exception>
    public static int ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        long column = 0;
        foreach (var ch in reference)
        {
            var upper = char.ToUpperInvariant(ch);
            if (upper is < 'A' or > 'Z')
            {
                break;
            }

            column = column * 26 + (upper - 'A') + 1;
            if (column > MaxColumn)
            {
                throw new InvalidOperationException($"Cell reference '{reference}' names a column beyond the maximum of {MaxColumn}.");
            }
        }

        return (int)column;
    }

    /// <summary>
    /// The reference for a 1-based column and row — the inverse of <see cref="ColumnOf"/> and
    /// <see cref="RowOf"/>. Bijective base-26 again, so each letter is taken from the column biased
    /// down by one, most significant last.
    /// </summary>
    public static string Format(int column, int row)
    {
        var letters = new StringBuilder();
        for (var remaining = column; remaining > 0; remaining = (remaining - 1) / 26)
        {
            letters.Insert(0, (char) ('A' + (remaining - 1) % 26));
        }

        return letters.Append(row).ToString();
    }

    /// <summary>The 1-based row of a reference such as <c>BC7</c>, or 0 when it cannot be read.</summary>
    /// <exception cref="InvalidOperationException">
    /// The reference names a row beyond <see cref="MaxRow"/>. A schema-valid reference can carry a
    /// longer digit run than a worksheet has rows -- <c>A2147483647</c> is int.MaxValue, and a bound of
    /// exactly that overflows the grid loop counter back to int.MinValue -- so such a value is rejected
    /// rather than clamped. A run too long for <c>long</c> is rejected on the same grounds: it cannot
    /// name a row that exists.</exception>
    public static int RowOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        var digits = new string(reference.SkipWhile(char.IsAsciiLetter).TakeWhile(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return 0;
        }
        // The run is non-empty and all ASCII digits, so the only way this fails is overflow.
        if (!long.TryParse(digits, out var row) || row > MaxRow)
        {
            throw new InvalidOperationException($"Cell reference '{reference}' names a row beyond the maximum of {MaxRow}.");
        }

        return (int)row;
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
