/// <summary>
/// Works out one formula, in the cell it is in: references resolve against that cell's sheet, <c>[#This
/// Row]</c> against its row, and a range where one value is wanted is narrowed to the cell's own row or
/// column (implicit intersection) — unless the formula is an array formula, or the range is an argument
/// of a function that takes arrays (SUMPRODUCT), where an operation over ranges is worked cell by cell.
/// </summary>
sealed class FormulaEvaluator(FormulaWorkbook book, int sheet, int row, int column)
{
    int arrays;

    public FormulaWorkbook Book { get; } = book;

    public int Sheet { get; } = sheet;

    public int Row { get; } = row;

    public int Column { get; } = column;

    /// <summary>Set for an array formula (<c>t="array"</c>), where every operation works cell by cell.</summary>
    public bool ArrayFormula { get; init; }

    /// <summary>Whether a function was met that the engine has not got.</summary>
    public bool Unsupported { get; set; }

    /// <summary>Whether the result rests on TODAY, NOW, RAND or the like.</summary>
    public bool Volatile { get; set; }

    bool ArrayMode => ArrayFormula || arrays > 0;

    public FormulaValue Evaluate(FormulaNode node)
    {
        switch (node)
        {
            case NumberNode number:
                return FormulaValue.FromNumber(number.Value);
            case TextNode text:
                return FormulaValue.FromText(text.Value);
            case BooleanNode boolean:
                return FormulaValue.FromBoolean(boolean.Value);
            case ErrorNode error:
                return FormulaValue.FromError(error.Error);
            case ArrayNode array:
                return FormulaValue.FromArray(array.Values);
            case MissingNode:
                return FormulaValue.Blank;
            case ReferenceNode reference:
                return Resolve(reference.Reference);
            case NameNode name:
                return Name(name);
            case StructuredNode structured:
                return Structured(structured);
            case UnaryNode unary:
                return Unary(unary);
            case PercentNode percent:
                return Map(Operand(Evaluate(percent.Operand)), _ => Arithmetic(_, FormulaValue.FromNumber(100), "/"));
            case BinaryNode binary:
                return Binary(binary);
            case CallNode call:
                return Call(call);
            default:
                return FormulaValue.FromError(FormulaError.Value);
        }
    }

    /// <summary>What a formula's result puts in its cell: one value, a blank one being 0.</summary>
    public FormulaValue Final(FormulaValue value)
    {
        var scalar = Scalar(value);
        if (scalar.IsBlank)
        {
            return FormulaValue.FromNumber(0);
        }

        return scalar;
    }

    /// <summary>An array formula's result as an array: a range read whole.</summary>
    public FormulaValue Arrayed(FormulaValue value)
    {
        if (value.IsReference)
        {
            return FormulaValue.FromArray(Values(value.Area));
        }

        return value;
    }

    // References

    FormulaValue Resolve(FormulaReference reference)
    {
        if (reference.Shape == ReferenceShape.Invalid)
        {
            return FormulaValue.FromError(FormulaError.Reference);
        }

        var index = Sheet;
        if (reference.Sheet is { } name)
        {
            index = Book.SheetIndex(name);
            if (index < 0)
            {
                return FormulaValue.FromError(FormulaError.Reference);
            }
        }

        var (firstRow, firstColumn, lastRow, lastColumn) = reference.Bounds;
        return FormulaValue.FromArea(new(index, firstRow, firstColumn, lastRow, lastColumn));
    }

    FormulaValue Name(NameNode node)
    {
        var scope = Sheet;
        if (node.Sheet is { } sheetName)
        {
            scope = Book.SheetIndex(sheetName);
            if (scope < 0)
            {
                return FormulaValue.FromError(FormulaError.Reference);
            }
        }

        if (Book.Name(scope, node.Name) is { } formula)
        {
            if (Book.Parse(formula) is not { } parsed ||
                !Book.EnterName())
            {
                return FormulaValue.FromError(FormulaError.Name);
            }

            // A defined name is worked as an array formula is, whatever cell reads it: wedding-invitation-
            // tracker's TotalAttending, SUM(IF(tblInvites[RSVP]="Yes", tblInvites['# IN PARTY])), sums the
            // rows that say Yes (17), where narrowed to the reading cell's row it summed them all (20).
            arrays++;
            try
            {
                return Evaluate(parsed);
            }
            finally
            {
                arrays--;
                Book.LeaveName();
            }
        }

        // A table's name alone is its data, as Table1 reads Table1[].
        if (node.Sheet == null &&
            Book.Table(node.Name) is { } table)
        {
            return Structured(new(table.Name, new(StructuredItems.Data, null, null)));
        }

        return FormulaValue.FromError(FormulaError.Name);
    }

    FormulaValue Structured(StructuredNode node)
    {
        if (Area(node) is { } area)
        {
            return FormulaValue.FromArea(area);
        }

        if ((node.Specifier.Items & StructuredItems.ThisRow) != 0)
        {
            return FormulaValue.FromError(FormulaError.Value);
        }

        return FormulaValue.FromError(FormulaError.Reference);
    }

    FormulaArea? Area(StructuredNode node)
    {
        var table = node.Table.Length == 0 ? Book.TableAt(Sheet, Row, Column) : Book.Table(node.Table);
        if (table == null)
        {
            return null;
        }

        var spec = node.Specifier;
        var range = table.Range;
        int firstRow;
        int lastRow;
        var items = spec.Items;
        if ((items & StructuredItems.ThisRow) != 0)
        {
            if (Row < table.FirstDataRow ||
                Row > table.LastDataRow ||
                Sheet != table.Sheet)
            {
                return null;
            }

            firstRow = Row;
            lastRow = Row;
        }
        else if ((items & StructuredItems.All) != 0)
        {
            firstRow = range.FirstRow;
            lastRow = range.LastRow;
        }
        else
        {
            firstRow = int.MaxValue;
            lastRow = int.MinValue;
            if ((items & StructuredItems.Headers) != 0)
            {
                if (table.HeaderRows == 0)
                {
                    return null;
                }

                firstRow = Math.Min(firstRow, range.FirstRow);
                lastRow = Math.Max(lastRow, range.FirstRow + table.HeaderRows - 1);
            }

            if ((items & StructuredItems.Data) != 0)
            {
                firstRow = Math.Min(firstRow, table.FirstDataRow);
                lastRow = Math.Max(lastRow, table.LastDataRow);
            }

            if ((items & StructuredItems.Totals) != 0)
            {
                if (table.TotalsRows == 0)
                {
                    return null;
                }

                firstRow = Math.Min(firstRow, range.LastRow - table.TotalsRows + 1);
                lastRow = Math.Max(lastRow, range.LastRow);
            }

            if (lastRow < firstRow)
            {
                return null;
            }
        }

        var firstColumn = range.FirstColumn;
        var lastColumn = range.LastColumn;
        if (spec.FirstColumn is { } first)
        {
            var from = table.ColumnIndex(first);
            var to = table.ColumnIndex(spec.LastColumn ?? first);
            if (from < 0 ||
                to < 0)
            {
                return null;
            }

            firstColumn = range.FirstColumn + Math.Min(from, to);
            lastColumn = range.FirstColumn + Math.Max(from, to);
        }

        return new(table.Sheet, firstRow, firstColumn, lastRow, lastColumn);
    }

    /// <summary>
    /// The areas a formula reads, as far as they can be known without working it out, which is what
    /// orders the workbook's formulas (<see cref="FormulaWorkbook.Recalculate"/>).
    /// </summary>
    public IEnumerable<FormulaArea> Areas(FormulaNode node)
    {
        var pending = new Stack<FormulaNode>();
        pending.Push(node);
        var names = 0;
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case ReferenceNode reference:
                    if (Resolve(reference.Reference) is {IsReference: true} resolved)
                    {
                        yield return resolved.Area;
                    }

                    break;
                case StructuredNode structured:
                    if (Area(structured) is { } area)
                    {
                        yield return area;
                    }

                    break;
                case NameNode name:
                    var scope = name.Sheet is { } sheetName ? Book.SheetIndex(sheetName) : Sheet;
                    if (names++ < 64 &&
                        Book.Name(scope, name.Name) is { } formula &&
                        Book.Parse(formula) is { } parsed)
                    {
                        pending.Push(parsed);
                    }
                    else if (name.Sheet == null &&
                             Book.Table(name.Name) is { } table)
                    {
                        pending.Push(new StructuredNode(table.Name, new(StructuredItems.Data, null, null)));
                    }

                    break;
                case UnaryNode unary:
                    pending.Push(unary.Operand);
                    break;
                case PercentNode percent:
                    pending.Push(percent.Operand);
                    break;
                case BinaryNode binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case CallNode call:
                    foreach (var argument in call.Arguments)
                    {
                        pending.Push(argument);
                    }

                    break;
            }
        }
    }

    // Values

    /// <summary>A cell's value, read through the workbook so its formula is worked out first.</summary>
    public FormulaValue CellValue(int sheetIndex, int rowNumber, int columnNumber) =>
        Book.Value(sheetIndex, rowNumber, columnNumber, this);

    /// <summary>
    /// What a value is where one value or an array is wanted: a single cell's content, or a range read
    /// whole (in array mode) or narrowed to this cell's row or column.
    /// </summary>
    public FormulaValue Operand(FormulaValue value)
    {
        if (!value.IsReference)
        {
            return value;
        }

        var area = value.Area;
        if (area.IsSingleCell)
        {
            return CellValue(area.Sheet, area.FirstRow, area.FirstColumn);
        }

        if (ArrayMode)
        {
            return FormulaValue.FromArray(Values(area));
        }

        return Intersect(area);
    }

    /// <summary>One value: a range narrowed to this cell, an array's first.</summary>
    public FormulaValue Scalar(FormulaValue value)
    {
        if (value.IsReference)
        {
            var area = value.Area;
            if (area.IsSingleCell)
            {
                return CellValue(area.Sheet, area.FirstRow, area.FirstColumn);
            }

            return Intersect(area);
        }

        if (value.IsArray)
        {
            var array = value.Array;
            if (array.Length == 0)
            {
                return FormulaValue.FromError(FormulaError.Value);
            }

            return array[0, 0];
        }

        return value;
    }

    // Implicit intersection: the range's cell in this cell's row, if it is one column, or column.
    FormulaValue Intersect(FormulaArea area)
    {
        if (area.FirstColumn == area.LastColumn &&
            Row >= area.FirstRow &&
            Row <= area.LastRow)
        {
            return CellValue(area.Sheet, Row, area.FirstColumn);
        }

        if (area.FirstRow == area.LastRow &&
            Column >= area.FirstColumn &&
            Column <= area.LastColumn)
        {
            return CellValue(area.Sheet, area.FirstRow, Column);
        }

        return FormulaValue.FromError(FormulaError.Value);
    }

    /// <summary>
    /// A range's values as an array. A whole column or row is read only as far as the sheet has cells,
    /// as Excel itself reads it.
    /// </summary>
    public FormulaValue[,] Values(FormulaArea area)
    {
        var bounded = Bounded(area);
        var values = new FormulaValue[bounded.Rows, bounded.Columns];
        for (var r = 0; r < bounded.Rows; r++)
        {
            for (var c = 0; c < bounded.Columns; c++)
            {
                values[r, c] = CellValue(bounded.Sheet, bounded.FirstRow + r, bounded.FirstColumn + c);
            }
        }

        return values;
    }

    /// <summary>An area cut to the cells its sheet has, keeping at least its first row and column.</summary>
    public FormulaArea Bounded(FormulaArea area)
    {
        if (area.Sheet < 0 ||
            area.Sheet >= Book.Sheets.Count)
        {
            return area;
        }

        var sheetCells = Book.Sheets[area.Sheet];
        return area with
        {
            LastRow = Math.Max(area.FirstRow, Math.Min(area.LastRow, sheetCells.MaxRow)),
            LastColumn = Math.Max(area.FirstColumn, Math.Min(area.LastColumn, sheetCells.MaxColumn))
        };
    }

    /// <summary>A value as an array: a range read whole, a single value one by one.</summary>
    public FormulaValue[,] ToArray(FormulaValue value)
    {
        if (value.IsReference)
        {
            return Values(value.Area);
        }

        if (value.IsArray)
        {
            return value.Array;
        }

        return new[,] { { value } };
    }

    /// <summary>The cells a range has, with what is in them, for the functions that skip blanks.</summary>
    public IEnumerable<(FormulaCell Cell, FormulaValue Value)> Cells(FormulaArea area)
    {
        if (area.Sheet < 0 ||
            area.Sheet >= Book.Sheets.Count)
        {
            yield break;
        }

        var cells = Book.Sheets[area.Sheet].In(area.FirstRow, area.FirstColumn, area.LastRow, area.LastColumn).ToList();
        foreach (var cell in cells)
        {
            yield return (cell, CellValue(area.Sheet, cell.Row, cell.Column));
        }
    }

    // Operators

    FormulaValue Unary(UnaryNode node)
    {
        var value = Evaluate(node.Operand);
        switch (node.Operator)
        {
            case "@":
                return Scalar(value);
            case "+":
                return Operand(value);
            default:
                return Map(Operand(value), _ =>
                {
                    var number = FormulaCoercion.ToNumber(_);
                    if (number.IsError)
                    {
                        return number;
                    }

                    return FormulaValue.FromNumber(-number.Number);
                });
        }
    }

    FormulaValue Binary(BinaryNode node)
    {
        var left = Evaluate(node.Left);
        var right = Evaluate(node.Right);
        if (node.Operator == ":")
        {
            return Span(left, right);
        }

        var leftValue = Operand(left);
        var rightValue = Operand(right);
        if (leftValue.IsArray ||
            rightValue.IsArray)
        {
            return Pairwise(leftValue, rightValue, (l, r) => Scalar(node.Operator, l, r));
        }

        return Scalar(node.Operator, leftValue, rightValue);
    }

    // A1:INDEX(...) — the rectangle that holds both references.
    static FormulaValue Span(FormulaValue left, FormulaValue right)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (!left.IsReference ||
            !right.IsReference ||
            left.Area.Sheet != right.Area.Sheet)
        {
            return FormulaValue.FromError(FormulaError.Value);
        }

        var a = left.Area;
        var b = right.Area;
        return FormulaValue.FromArea(new(
            a.Sheet,
            Math.Min(a.FirstRow, b.FirstRow),
            Math.Min(a.FirstColumn, b.FirstColumn),
            Math.Max(a.LastRow, b.LastRow),
            Math.Max(a.LastColumn, b.LastColumn)));
    }

    static FormulaValue Scalar(string op, FormulaValue left, FormulaValue right)
    {
        switch (op)
        {
            case "&":
                var leftText = FormulaCoercion.ToText(left);
                if (leftText.IsError)
                {
                    return leftText;
                }

                var rightText = FormulaCoercion.ToText(right);
                if (rightText.IsError)
                {
                    return rightText;
                }

                return FormulaValue.FromText(leftText.Text + rightText.Text);
            case "=" or "<>" or "<" or ">" or "<=" or ">=":
                if (left.IsError)
                {
                    return left;
                }

                if (right.IsError)
                {
                    return right;
                }

                var order = FormulaCoercion.Compare(left, right);
                return FormulaValue.FromBoolean(op switch
                {
                    "=" => order == 0,
                    "<>" => order != 0,
                    "<" => order < 0,
                    ">" => order > 0,
                    "<=" => order <= 0,
                    _ => order >= 0
                });
            default:
                return Arithmetic(left, right, op);
        }
    }

    static FormulaValue Arithmetic(FormulaValue left, FormulaValue right, string op)
    {
        var a = FormulaCoercion.ToNumber(left);
        if (a.IsError)
        {
            return a;
        }

        var b = FormulaCoercion.ToNumber(right);
        if (b.IsError)
        {
            return b;
        }

        var x = a.Number;
        var y = b.Number;
        switch (op)
        {
            case "+":
                return FormulaValue.FromNumber(x + y);
            case "-":
                return FormulaValue.FromNumber(x - y);
            case "*":
                return FormulaValue.FromNumber(x * y);
            case "/":
                if (y == 0)
                {
                    return FormulaValue.FromError(FormulaError.DivideByZero);
                }

                return FormulaValue.FromNumber(x / y);
            case "^":
                if (x == 0 &&
                    y == 0)
                {
                    return FormulaValue.FromError(FormulaError.Number);
                }

                if (x == 0 &&
                    y < 0)
                {
                    return FormulaValue.FromError(FormulaError.DivideByZero);
                }

                return FormulaValue.FromNumber(Math.Pow(x, y));
            default:
                return FormulaValue.FromError(FormulaError.Value);
        }
    }

    /// <summary>A function applied to each value of an array, or to the one value.</summary>
    public static FormulaValue Map(FormulaValue value, Func<FormulaValue, FormulaValue> map)
    {
        if (!value.IsArray)
        {
            return map(value);
        }

        var source = value.Array;
        var result = new FormulaValue[source.GetLength(0), source.GetLength(1)];
        for (var r = 0; r < source.GetLength(0); r++)
        {
            for (var c = 0; c < source.GetLength(1); c++)
            {
                result[r, c] = map(source[r, c]);
            }
        }

        return FormulaValue.FromArray(result);
    }

    /// <summary>
    /// Two arrays combined value by value. A single value, a single row or a single column is
    /// stretched to the other's size, as Excel stretches it; past the smaller array's end is #N/A.
    /// </summary>
    public static FormulaValue Pairwise(FormulaValue left, FormulaValue right, Func<FormulaValue, FormulaValue, FormulaValue> combine)
    {
        var a = left.IsArray ? left.Array : new[,] { { left } };
        var b = right.IsArray ? right.Array : new[,] { { right } };
        var rows = Math.Max(a.GetLength(0), b.GetLength(0));
        var columns = Math.Max(a.GetLength(1), b.GetLength(1));
        var result = new FormulaValue[rows, columns];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                if (At(a, r, c) is not { } x ||
                    At(b, r, c) is not { } y)
                {
                    result[r, c] = FormulaValue.FromError(FormulaError.NotAvailable);
                    continue;
                }

                result[r, c] = combine(x, y);
            }
        }

        return FormulaValue.FromArray(result);
    }

    static FormulaValue? At(FormulaValue[,] values, int r, int c)
    {
        var rows = values.GetLength(0);
        var columns = values.GetLength(1);
        if (rows == 1)
        {
            r = 0;
        }

        if (columns == 1)
        {
            c = 0;
        }

        if (r >= rows ||
            c >= columns)
        {
            return null;
        }

        return values[r, c];
    }

    // Functions

    FormulaValue Call(CallNode node)
    {
        if (!FormulaFunctions.TryGet(node.Name, out var function))
        {
            Unsupported = true;
            return FormulaValue.FromError(FormulaError.Name);
        }

        if (function.Volatile)
        {
            Volatile = true;
        }

        if (node.Arguments.Count < function.Least ||
            node.Arguments.Count > function.Most)
        {
            return FormulaValue.FromError(FormulaError.Value);
        }

        if (function.TakesArrays)
        {
            arrays++;
        }

        try
        {
            return function.Invoke(new(this, node.Arguments));
        }
        finally
        {
            if (function.TakesArrays)
            {
                arrays--;
            }
        }
    }

    /// <summary>Evaluates an argument with ranges in it read whole, as SUMPRODUCT's are.</summary>
    public FormulaValue EvaluateArray(FormulaNode node)
    {
        arrays++;
        try
        {
            return Evaluate(node);
        }
        finally
        {
            arrays--;
        }
    }
}
