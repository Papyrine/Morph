/// <summary>What a reference names: one cell, a rectangle, whole columns or whole rows.</summary>
enum ReferenceShape
{
    Cell,
    Area,
    Columns,
    Rows,

    /// <summary><c>#REF!</c>: a reference to cells that are gone.</summary>
    Invalid
}

/// <summary>
/// An A1 reference as a formula spells it — <c>$B$2</c>, <c>A1:C9</c>, <c>A:A</c>, <c>3:5</c>, each
/// perhaps on a named sheet — with which of its rows and columns are absolute, which is what a formula
/// copied to another cell moves by (<see cref="Shift"/>).
/// </summary>
sealed record FormulaReference(
    string? Sheet,
    int FirstRow,
    int FirstColumn,
    int LastRow,
    int LastColumn,
    bool FirstRowAbsolute,
    bool FirstColumnAbsolute,
    bool LastRowAbsolute,
    bool LastColumnAbsolute,
    ReferenceShape Shape)
{
    static readonly Regex cell = new(@"^(\$?)([A-Za-z]{1,3})(\$?)(\d{1,7})$", RegexOptions.CultureInvariant);
    static readonly Regex column = new(@"^(\$?)([A-Za-z]{1,3})$", RegexOptions.CultureInvariant);
    static readonly Regex row = new(@"^(\$?)(\d{1,7})$", RegexOptions.CultureInvariant);

    /// <summary>Reads the part after any <c>Sheet!</c>; null when it is not a reference.</summary>
    public static FormulaReference? Parse(string? sheet, string text)
    {
        if (string.Equals(text, "#REF!", StringComparison.OrdinalIgnoreCase))
        {
            return new(sheet, 0, 0, 0, 0, false, false, false, false, ReferenceShape.Invalid);
        }

        var parts = text.Split(':');
        if (parts.Length == 1)
        {
            if (Cell(parts[0]) is not var (r, c, ra, ca))
            {
                return null;
            }

            return new(sheet, r, c, r, c, ra, ca, ra, ca, ReferenceShape.Cell);
        }

        if (parts.Length != 2)
        {
            return null;
        }

        if (Cell(parts[0]) is var (r1, c1, ra1, ca1) &&
            Cell(parts[1]) is var (r2, c2, ra2, ca2))
        {
            return new(sheet, r1, c1, r2, c2, ra1, ca1, ra2, ca2, ReferenceShape.Area);
        }

        if (column.Match(parts[0]) is {Success: true} left &&
            column.Match(parts[1]) is {Success: true} right &&
            CellReference.ColumnOf(left.Groups[2].Value) is > 0 and var first &&
            CellReference.ColumnOf(right.Groups[2].Value) is > 0 and var last)
        {
            return new(sheet, 1, first, CellReference.MaxRow, last, true, left.Groups[1].Length > 0, true, right.Groups[1].Length > 0, ReferenceShape.Columns);
        }

        if (row.Match(parts[0]) is {Success: true} top &&
            row.Match(parts[1]) is {Success: true} bottom &&
            int.TryParse(top.Groups[2].Value, CultureInfo.InvariantCulture, out var firstRow) &&
            int.TryParse(bottom.Groups[2].Value, CultureInfo.InvariantCulture, out var lastRow) &&
            firstRow is >= 1 and <= CellReference.MaxRow &&
            lastRow is >= 1 and <= CellReference.MaxRow)
        {
            return new(sheet, firstRow, 1, lastRow, CellReference.MaxColumn, top.Groups[1].Length > 0, true, bottom.Groups[1].Length > 0, true, ReferenceShape.Rows);
        }

        return null;
    }

    static (int Row, int Column, bool RowAbsolute, bool ColumnAbsolute)? Cell(string text)
    {
        var match = cell.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var columnNumber = CellReference.ColumnOf(match.Groups[2].Value);
        if (columnNumber == 0 ||
            !int.TryParse(match.Groups[4].Value, CultureInfo.InvariantCulture, out var rowNumber) ||
            rowNumber is < 1 or > CellReference.MaxRow)
        {
            return null;
        }

        return (rowNumber, columnNumber, match.Groups[3].Length > 0, match.Groups[1].Length > 0);
    }

    /// <summary>The rows and columns named, in order whichever way round they were written.</summary>
    public (int FirstRow, int FirstColumn, int LastRow, int LastColumn) Bounds =>
        (Math.Min(FirstRow, LastRow), Math.Min(FirstColumn, LastColumn), Math.Max(FirstRow, LastRow), Math.Max(FirstColumn, LastColumn));

    /// <summary>
    /// The reference a formula copied by rows and columns has: its relative parts move, its absolute
    /// parts stay. Moved off the grid it is <c>#REF!</c>, as Excel makes it.
    /// </summary>
    public FormulaReference Shift(int rows, int columns)
    {
        if (Shape == ReferenceShape.Invalid)
        {
            return this;
        }

        var firstRow = FirstRow;
        var lastRow = LastRow;
        var firstColumn = FirstColumn;
        var lastColumn = LastColumn;
        if (Shape != ReferenceShape.Columns)
        {
            firstRow = Move(FirstRow, FirstRowAbsolute, rows);
            lastRow = Move(LastRow, LastRowAbsolute, rows);
        }

        if (Shape != ReferenceShape.Rows)
        {
            firstColumn = Move(FirstColumn, FirstColumnAbsolute, columns);
            lastColumn = Move(LastColumn, LastColumnAbsolute, columns);
        }

        if (firstRow is < 1 or > CellReference.MaxRow ||
            lastRow is < 1 or > CellReference.MaxRow ||
            firstColumn is < 1 or > CellReference.MaxColumn ||
            lastColumn is < 1 or > CellReference.MaxColumn)
        {
            return this with
            {
                Shape = ReferenceShape.Invalid
            };
        }

        return this with
        {
            FirstRow = firstRow,
            LastRow = lastRow,
            FirstColumn = firstColumn,
            LastColumn = lastColumn
        };
    }

    static int Move(int value, bool absolute, int by)
    {
        if (absolute)
        {
            return value;
        }

        return value + by;
    }

    /// <summary>The reference as a formula spells it, without the sheet.</summary>
    public string Write()
    {
        switch (Shape)
        {
            case ReferenceShape.Invalid:
                return "#REF!";
            case ReferenceShape.Cell:
                return WriteCell(FirstRow, FirstColumn, FirstRowAbsolute, FirstColumnAbsolute);
            case ReferenceShape.Area:
                return $"{WriteCell(FirstRow, FirstColumn, FirstRowAbsolute, FirstColumnAbsolute)}:{WriteCell(LastRow, LastColumn, LastRowAbsolute, LastColumnAbsolute)}";
            case ReferenceShape.Columns:
                return $"{Dollar(FirstColumnAbsolute)}{Letters(FirstColumn)}:{Dollar(LastColumnAbsolute)}{Letters(LastColumn)}";
            default:
                return $"{Dollar(FirstRowAbsolute)}{FirstRow.ToString(CultureInfo.InvariantCulture)}:{Dollar(LastRowAbsolute)}{LastRow.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    static string WriteCell(int rowNumber, int columnNumber, bool rowAbsolute, bool columnAbsolute) =>
        $"{Dollar(columnAbsolute)}{Letters(columnNumber)}{Dollar(rowAbsolute)}{rowNumber.ToString(CultureInfo.InvariantCulture)}";

    static string Letters(int columnNumber)
    {
        var reference = CellReference.Format(columnNumber, 1);
        return reference[..^1];
    }

    static string Dollar(bool absolute)
    {
        if (absolute)
        {
            return "$";
        }

        return string.Empty;
    }
}
