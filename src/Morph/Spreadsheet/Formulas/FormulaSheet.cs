/// <summary>One sheet's cells, found by position and walked in order.</summary>
sealed class FormulaSheet(string name, int index)
{
    readonly Dictionary<long, FormulaCell> cells = [];
    readonly SortedDictionary<int, SortedDictionary<int, FormulaCell>> rows = [];

    public string Name { get; } = name;

    public int Index { get; } = index;

    public HashSet<int> HiddenRows { get; } = [];

    /// <summary>The last row and column holding a cell, which a whole-column reference is read to.</summary>
    public int MaxRow { get; private set; }

    public int MaxColumn { get; private set; }

    public IEnumerable<FormulaCell> Cells => cells.Values;

    static long Key(int row, int column) =>
        (long) row * (CellReference.MaxColumn + 1) + column;

    public FormulaCell? Find(int row, int column) =>
        cells.GetValueOrDefault(Key(row, column));

    public FormulaCell Add(int row, int column)
    {
        var key = Key(row, column);
        if (cells.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var cell = new FormulaCell(row, column);
        cells[key] = cell;
        if (!rows.TryGetValue(row, out var line))
        {
            line = [];
            rows[row] = line;
        }

        line[column] = cell;
        MaxRow = Math.Max(MaxRow, row);
        MaxColumn = Math.Max(MaxColumn, column);
        return cell;
    }

    /// <summary>The cells there are within an area, row by row.</summary>
    public IEnumerable<FormulaCell> In(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        if (lastRow - firstRow < rows.Count)
        {
            for (var row = firstRow; row <= Math.Min(lastRow, MaxRow); row++)
            {
                if (!rows.TryGetValue(row, out var line))
                {
                    continue;
                }

                foreach (var cell in Line(line, firstColumn, lastColumn))
                {
                    yield return cell;
                }
            }

            yield break;
        }

        foreach (var (row, line) in rows)
        {
            if (row < firstRow)
            {
                continue;
            }

            if (row > lastRow)
            {
                yield break;
            }

            foreach (var cell in Line(line, firstColumn, lastColumn))
            {
                yield return cell;
            }
        }
    }

    static IEnumerable<FormulaCell> Line(SortedDictionary<int, FormulaCell> line, int firstColumn, int lastColumn)
    {
        if (lastColumn - firstColumn < line.Count)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                if (line.TryGetValue(column, out var cell))
                {
                    yield return cell;
                }
            }

            yield break;
        }

        foreach (var (column, cell) in line)
        {
            if (column < firstColumn)
            {
                continue;
            }

            if (column > lastColumn)
            {
                yield break;
            }

            yield return cell;
        }
    }
}
