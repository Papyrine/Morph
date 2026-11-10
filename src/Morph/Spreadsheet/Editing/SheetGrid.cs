/// <summary>
/// One sheet as the grid shows it: its cells, its columns' widths and rows' heights in pixels, what is
/// merged, frozen and hidden. A sheet that is a chart has no cells (<see cref="IsChart"/>).
/// </summary>
sealed class SheetGrid(string name, int index)
{
    readonly Dictionary<long, GridCell> cells = [];
    readonly Dictionary<long, GridLook> looks = [];

    public string Name { get; } = name;

    /// <summary>The sheet's place in the workbook's list of sheets, which the editor's edits name it by.</summary>
    public int Index { get; } = index;

    public bool Hidden { get; init; }

    public bool IsChart { get; init; }

    public double DefaultColumnWidth { get; set; } = 64;

    public double DefaultRowHeight { get; set; } = 20;

    public Dictionary<int, double> ColumnWidths { get; } = [];

    public Dictionary<int, double> RowHeights { get; } = [];

    /// <summary>The column widths as the file states them, in characters, for a column the reader sizes.</summary>
    public double MaxDigitWidth { get; set; } = 7;

    public int FrozenRows { get; set; }

    public int FrozenColumns { get; set; }

    public bool ShowGridLines { get; set; } = true;

    public List<SheetRange> Merges { get; } = [];

    /// <summary>The cell the sheet was last left at, from its view.</summary>
    public (int Row, int Column) Active { get; set; } = (1, 1);

    /// <summary>The last row and column holding anything — a value or a look of its own.</summary>
    public int MaxRow { get; private set; }

    public int MaxColumn { get; private set; }

    public IEnumerable<GridCell> Cells => cells.Values;

    static long Key(int row, int column) =>
        (long) row * (CellReference.MaxColumn + 1) + column;

    public GridCell? Cell(int row, int column) =>
        cells.GetValueOrDefault(Key(row, column));

    /// <summary>How a cell looks, a cell with nothing in it included.</summary>
    public GridLook Look(int row, int column)
    {
        if (cells.TryGetValue(Key(row, column), out var cell))
        {
            return cell.Look;
        }

        return looks.GetValueOrDefault(Key(row, column)) ?? GridLook.Plain;
    }

    public void Add(GridCell cell)
    {
        cells[Key(cell.Row, cell.Column)] = cell;
        Reach(cell.Row, cell.Column);
    }

    public void AddLook(int row, int column, GridLook look)
    {
        looks[Key(row, column)] = look;
        Reach(row, column);
    }

    void Reach(int row, int column)
    {
        MaxRow = Math.Max(MaxRow, row);
        MaxColumn = Math.Max(MaxColumn, column);
    }

    public double ColumnWidth(int column) =>
        ColumnWidths.GetValueOrDefault(column, DefaultColumnWidth);

    public double RowHeight(int row) =>
        RowHeights.GetValueOrDefault(row, DefaultRowHeight);

    /// <summary>The merged area a cell is in, if it is in one.</summary>
    public SheetRange? MergeAt(int row, int column)
    {
        foreach (var merge in Merges)
        {
            if (merge.ContainsRow(row) &&
                column >= merge.FirstColumn &&
                column <= merge.LastColumn)
            {
                return merge;
            }
        }

        return null;
    }
}
