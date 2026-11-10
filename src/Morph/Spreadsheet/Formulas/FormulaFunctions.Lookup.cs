static partial class FormulaFunctions
{
    static void RegisterLookup(Action<FormulaFunction> add)
    {
        add(new("VLOOKUP", 3, 4, _ => TableLookup(_, true)));
        add(new("HLOOKUP", 3, 4, _ => TableLookup(_, false)));
        add(new("MATCH", 2, 3, Match));
        add(new("INDEX", 2, 4, Index));
        add(new("XLOOKUP", 3, 6, XLookup));
        add(new("CHOOSE", 2, 255, Choose));
        add(new("ROW", 0, 1, _ => Position(_, true)));
        add(new("COLUMN", 0, 1, _ => Position(_, false)));
        add(new("ROWS", 1, 1, _ => Extent(_, true)));
        add(new("COLUMNS", 1, 1, _ => Extent(_, false)));
        add(new("OFFSET", 3, 5, Offset)
        {
            Volatile = true
        });
        add(new("INDIRECT", 1, 2, Indirect)
        {
            Volatile = true
        });
        add(new("ADDRESS", 2, 5, Address));
    }

    // The same kind and equal, or for text with wildcards a match of the pattern: what an exact lookup
    // looks for.
    static Func<FormulaValue, bool> Exactly(FormulaValue wanted, bool wildcards)
    {
        if (wildcards &&
            wanted.IsText &&
            (wanted.Text.Contains('*') || wanted.Text.Contains('?') || wanted.Text.Contains('~')))
        {
            var pattern = FormulaCriteria.Wildcard(wanted.Text);
            return _ => _.IsText && pattern.IsMatch(_.Text);
        }

        return _ => _.Kind == wanted.Kind &&
                    FormulaCoercion.Compare(_, wanted) == 0;
    }

    // An approximate lookup over sorted values: the last that is not past the one wanted, of the same
    // kind, stopping at the first that is past it.
    static int Approximate(int count, Func<int, FormulaValue> at, FormulaValue wanted, bool descending)
    {
        var found = -1;
        for (var index = 0; index < count; index++)
        {
            var value = at(index);
            if (value.Kind != wanted.Kind)
            {
                continue;
            }

            var order = FormulaCoercion.Compare(value, wanted);
            if (descending)
            {
                order = -order;
            }

            if (order > 0)
            {
                break;
            }

            found = index;
            if (order == 0 &&
                !descending)
            {
                // The last of equal values, as a binary search over sorted data finds.
                continue;
            }
        }

        return found;
    }

    static FormulaValue TableLookup(FunctionArguments args, bool vertical)
    {
        var wanted = args.Scalar(0);
        if (wanted.IsError)
        {
            return wanted;
        }

        var table = args.Raw(1);
        if (table.IsError)
        {
            return table;
        }

        if (table is
            {
                IsReference: false,
                IsArray: false
            })
        {
            return Error(FormulaError.Value);
        }

        var which = Integer(args, 2, 0);
        if (which.IsError)
        {
            return which;
        }

        var approximate = args.Boolean(3, true);
        if (approximate.IsError)
        {
            return approximate;
        }

        var evaluator = args.Evaluator;
        var (rows, columns) = Size(evaluator, table);
        var across = vertical ? columns : rows;
        var along = vertical ? rows : columns;
        var offset = (int) which.Number - 1;
        if (offset < 0)
        {
            return Error(FormulaError.Value);
        }

        if (offset >= across)
        {
            return Error(FormulaError.Reference);
        }

        FormulaValue Key(int index)
        {
            if (vertical)
            {
                return At(evaluator, table, index, 0);
            }

            return At(evaluator, table, 0, index);
        }

        int found;
        if (approximate.Boolean)
        {
            found = Approximate(along, Key, wanted, false);
        }
        else
        {
            var matches = Exactly(wanted, true);
            found = -1;
            for (var index = 0; index < along; index++)
            {
                if (matches(Key(index)))
                {
                    found = index;
                    break;
                }
            }
        }

        if (found < 0)
        {
            return Error(FormulaError.NotAvailable);
        }

        if (vertical)
        {
            return At(evaluator, table, found, offset);
        }

        return At(evaluator, table, offset, found);
    }

    // A one-row or one-column range or array as a list.
    static (int Count, Func<int, FormulaValue> At)? Vector(FormulaEvaluator evaluator, FormulaValue source)
    {
        var (rows, columns) = Size(evaluator, source);
        if (rows == 1)
        {
            return (columns, _ => At(evaluator, source, 0, _));
        }

        if (columns == 1)
        {
            return (rows, _ => At(evaluator, source, _, 0));
        }

        return null;
    }

    static FormulaValue Match(FunctionArguments args)
    {
        var wanted = args.Scalar(0);
        if (wanted.IsError)
        {
            return wanted;
        }

        var type = Integer(args, 2, 1);
        if (type.IsError)
        {
            return type;
        }

        if (Vector(args.Evaluator, args.Raw(1)) is not var (count, at))
        {
            return Error(FormulaError.NotAvailable);
        }

        int found;
        switch (Math.Sign(type.Number))
        {
            case 0:
                var matches = Exactly(wanted, true);
                found = -1;
                for (var index = 0; index < count; index++)
                {
                    if (matches(at(index)))
                    {
                        found = index;
                        break;
                    }
                }

                break;
            case > 0:
                found = Approximate(count, at, wanted, false);
                break;
            default:
                found = Approximate(count, at, wanted, true);
                break;
        }

        if (found < 0)
        {
            return Error(FormulaError.NotAvailable);
        }

        return Number(found + 1);
    }

    // INDEX of a range is a reference, so INDEX(A1:C9, 2, 3):C9 is a range, and ROW(INDEX(...)) a row.
    static FormulaValue Index(FunctionArguments args)
    {
        var source = args.Raw(0);
        if (source.IsError)
        {
            return source;
        }

        var row = Integer(args, 1, 0);
        if (row.IsError)
        {
            return row;
        }

        var column = Integer(args, 2, 0);
        if (column.IsError)
        {
            return column;
        }

        var evaluator = args.Evaluator;
        var (rows, columns) = Size(evaluator, source);
        var r = (int) row.Number;
        var c = (int) column.Number;

        // A single row or column takes its one number as the position along it.
        if (args.IsMissing(2) &&
            rows == 1 &&
            columns > 1)
        {
            c = r;
            r = 1;
        }

        if (r < 0 ||
            c < 0 ||
            r > rows ||
            c > columns)
        {
            return Error(FormulaError.Reference);
        }

        if (source.IsReference)
        {
            var area = source.Area;
            var firstRow = area.FirstRow;
            var lastRow = area.FirstRow + rows - 1;
            if (r > 0)
            {
                firstRow = area.FirstRow + r - 1;
                lastRow = firstRow;
            }

            var firstColumn = area.FirstColumn;
            var lastColumn = area.FirstColumn + columns - 1;
            if (c > 0)
            {
                firstColumn = area.FirstColumn + c - 1;
                lastColumn = firstColumn;
            }
            else if (columns == 1)
            {
                lastColumn = firstColumn;
            }

            return FormulaValue.FromArea(new(area.Sheet, firstRow, firstColumn, lastRow, lastColumn));
        }

        var values = evaluator.ToArray(source);
        if (r == 0 &&
            c == 0)
        {
            return source;
        }

        if (r == 0)
        {
            var columnValues = new FormulaValue[rows, 1];
            for (var index = 0; index < rows; index++)
            {
                columnValues[index, 0] = values[index, c - 1];
            }

            return FormulaValue.FromArray(columnValues);
        }

        if (c == 0)
        {
            if (columns == 1)
            {
                return values[r - 1, 0];
            }

            var rowValues = new FormulaValue[1, columns];
            for (var index = 0; index < columns; index++)
            {
                rowValues[0, index] = values[r - 1, index];
            }

            return FormulaValue.FromArray(rowValues);
        }

        return values[r - 1, c - 1];
    }

    static FormulaValue XLookup(FunctionArguments args)
    {
        var wanted = args.Scalar(0);
        if (wanted.IsError)
        {
            return wanted;
        }

        var evaluator = args.Evaluator;
        var lookIn = args.Raw(1);
        var results = args.Raw(2);
        if (Vector(evaluator, lookIn) is not var (count, at))
        {
            return Error(FormulaError.Value);
        }

        var mode = Integer(args, 4, 0);
        if (mode.IsError)
        {
            return mode;
        }

        var search = Integer(args, 5, 1);
        if (search.IsError)
        {
            return search;
        }

        var backwards = search.Number < 0;
        var found = -1;
        var best = FormulaValue.Blank;
        var matches = Exactly(wanted, mode.Number == 2);
        for (var step = 0; step < count; step++)
        {
            var index = step;
            if (backwards)
            {
                index = count - 1 - step;
            }

            var value = at(index);
            if (matches(value))
            {
                found = index;
                break;
            }

            // -1 takes the next smaller when nothing is equal, 1 the next larger.
            if (mode.Number is -1 or 1 &&
                value.Kind == wanted.Kind)
            {
                var order = FormulaCoercion.Compare(value, wanted);
                var side = (int) mode.Number;
                if (Math.Sign(order) == side &&
                    (found < 0 || Math.Sign(FormulaCoercion.Compare(value, best)) == -side))
                {
                    found = index;
                    best = value;
                }
            }
        }

        if (found < 0)
        {
            if (!args.IsMissing(3))
            {
                return args.Raw(3);
            }

            return Error(FormulaError.NotAvailable);
        }

        // The row or column of the results that stands where the match does.
        var (rows, columns) = Size(evaluator, results);
        var (lookRows, _) = Size(evaluator, lookIn);
        var vertical = lookRows > 1 || count == 1 && rows > 1;
        if (results.IsReference)
        {
            var area = results.Area;
            if (vertical)
            {
                return FormulaValue.FromArea(area with
                {
                    FirstRow = area.FirstRow + found,
                    LastRow = area.FirstRow + found,
                    LastColumn = area.FirstColumn + columns - 1
                });
            }

            return FormulaValue.FromArea(area with
            {
                FirstColumn = area.FirstColumn + found,
                LastColumn = area.FirstColumn + found,
                LastRow = area.FirstRow + rows - 1
            });
        }

        if (vertical)
        {
            return At(evaluator, results, found, 0);
        }

        return At(evaluator, results, 0, found);
    }

    static FormulaValue Choose(FunctionArguments args)
    {
        var which = Integer(args, 0, 0);
        if (which.IsError)
        {
            return which;
        }

        var index = (int) which.Number;
        if (index < 1 ||
            index >= args.Count)
        {
            return Error(FormulaError.Value);
        }

        return args.Raw(index);
    }

    static FormulaValue Position(FunctionArguments args, bool row)
    {
        var evaluator = args.Evaluator;
        if (args.IsMissing(0))
        {
            if (row)
            {
                return Number(evaluator.Row);
            }

            return Number(evaluator.Column);
        }

        if (args.Area(0) is not { } area)
        {
            return Error(FormulaError.Value);
        }

        if (row)
        {
            return Number(area.FirstRow);
        }

        return Number(area.FirstColumn);
    }

    static FormulaValue Extent(FunctionArguments args, bool rows)
    {
        var raw = args.Raw(0);
        if (raw.IsError)
        {
            return raw;
        }

        int count;
        if (raw.IsReference)
        {
            count = rows ? raw.Area.Rows : raw.Area.Columns;
        }
        else if (raw.IsArray)
        {
            count = raw.Array.GetLength(rows ? 0 : 1);
        }
        else
        {
            count = 1;
        }

        return Number(count);
    }

    static FormulaValue Offset(FunctionArguments args)
    {
        if (args.Area(0) is not { } area)
        {
            return Error(FormulaError.Value);
        }

        var down = Integer(args, 1, 0);
        if (down.IsError)
        {
            return down;
        }

        var across = Integer(args, 2, 0);
        if (across.IsError)
        {
            return across;
        }

        var height = Integer(args, 3, area.Rows);
        if (height.IsError)
        {
            return height;
        }

        var width = Integer(args, 4, area.Columns);
        if (width.IsError)
        {
            return width;
        }

        if (height.Number < 1 ||
            width.Number < 1)
        {
            return Error(FormulaError.Reference);
        }

        var firstRow = area.FirstRow + (int) down.Number;
        var firstColumn = area.FirstColumn + (int) across.Number;
        var lastRow = firstRow + (int) height.Number - 1;
        var lastColumn = firstColumn + (int) width.Number - 1;
        if (firstRow < 1 ||
            firstColumn < 1 ||
            lastRow > CellReference.MaxRow ||
            lastColumn > CellReference.MaxColumn)
        {
            return Error(FormulaError.Reference);
        }

        return FormulaValue.FromArea(new(area.Sheet, firstRow, firstColumn, lastRow, lastColumn));
    }

    // INDIRECT reads its text as a formula would read it: a reference, perhaps on a named sheet, or a
    // defined name. R1C1 text is not read.
    static FormulaValue Indirect(FunctionArguments args)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        var a1 = args.Boolean(1, true);
        if (a1.IsError)
        {
            return a1;
        }

        if (!a1.Boolean)
        {
            args.Evaluator.Unsupported = true;
            return Error(FormulaError.Reference);
        }

        FormulaNode node;
        try
        {
            node = FormulaParser.Parse(text.Text.Trim());
        }
        catch (FormulaSyntaxException)
        {
            return Error(FormulaError.Reference);
        }

        if (node is not (ReferenceNode or NameNode or StructuredNode or BinaryNode {Operator: ":"}))
        {
            return Error(FormulaError.Reference);
        }

        var value = args.Evaluator.Evaluate(node);
        if (!value.IsReference)
        {
            return Error(FormulaError.Reference);
        }

        return value;
    }

    static FormulaValue Address(FunctionArguments args)
    {
        var row = Integer(args, 0, 0);
        if (row.IsError)
        {
            return row;
        }

        var column = Integer(args, 1, 0);
        if (column.IsError)
        {
            return column;
        }

        var kind = Integer(args, 2, 1);
        if (kind.IsError)
        {
            return kind;
        }

        var a1 = args.Boolean(3, true);
        if (a1.IsError)
        {
            return a1;
        }

        var r = (int) row.Number;
        var c = (int) column.Number;
        if (r is < 1 or > CellReference.MaxRow ||
            c is < 1 or > CellReference.MaxColumn ||
            kind.Number is < 1 or > 4)
        {
            return Error(FormulaError.Value);
        }

        var rowAbsolute = kind.Number is 1 or 2;
        var columnAbsolute = kind.Number is 1 or 3;
        string address;
        if (a1.Boolean)
        {
            address = new FormulaReference(null, r, c, r, c, rowAbsolute, columnAbsolute, rowAbsolute, columnAbsolute, ReferenceShape.Cell).Write();
        }
        else
        {
            var rowPart = rowAbsolute ? $"R{r}" : $"R[{r}]";
            var columnPart = columnAbsolute ? $"C{c}" : $"C[{c}]";
            address = rowPart + columnPart;
        }

        if (!args.IsMissing(4))
        {
            var sheet = args.Text(4);
            if (sheet.IsError)
            {
                return sheet;
            }

            var name = sheet.Text;
            if (name.Any(_ => !char.IsLetterOrDigit(_) && _ != '_'))
            {
                name = $"'{name.Replace("'", "''", StringComparison.Ordinal)}'";
            }

            address = $"{name}!{address}";
        }

        return FormulaValue.FromText(address);
    }
}
