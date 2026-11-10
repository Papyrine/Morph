/// <summary>
/// A rectangle of cells on one sheet: what a reference names once it has been resolved. The sheet is its
/// index in the workbook's sheet list; rows and columns count from 1, inclusive at both ends.
/// </summary>
readonly record struct FormulaArea(int Sheet, int FirstRow, int FirstColumn, int LastRow, int LastColumn)
{
    public int Rows => LastRow - FirstRow + 1;

    public int Columns => LastColumn - FirstColumn + 1;

    public bool IsSingleCell => FirstRow == LastRow &&
                                FirstColumn == LastColumn;

    public bool Contains(int sheet, int row, int column) =>
        sheet == Sheet &&
        row >= FirstRow &&
        row <= LastRow &&
        column >= FirstColumn &&
        column <= LastColumn;

    public static FormulaArea Cell(int sheet, int row, int column) =>
        new(sheet, row, column, row, column);

    public override string ToString()
    {
        var first = CellReference.Format(FirstColumn, FirstRow);
        if (IsSingleCell)
        {
            return $"{Sheet}!{first}";
        }

        return $"{Sheet}!{first}:{CellReference.Format(LastColumn, LastRow)}";
    }
}
