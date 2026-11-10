/// <summary>
/// The functions the engine works out, by name. A formula calling one it has not got keeps the value
/// Excel cached for it, and the workbook asks Excel to work it out on opening
/// (<see cref="FormulaWorkbook.NeedsExcel"/>), so the gaps here cost accuracy only in what the grid
/// shows until the file is next opened in Excel — never what Excel shows.
///
/// The set began from what the corpus calls — IFERROR, IF, ISBLANK, SUBTOTAL, TODAY, VLOOKUP, SUM,
/// TEXT, COUNTIFS, ROW, WEEKDAY, COUNTIF, SUMIFS, COUNTA, MIN, SUMPRODUCT, RANK.EQ — and adds the
/// everyday functions around them.
/// </summary>
static partial class FormulaFunctions
{
    static readonly Dictionary<string, FormulaFunction> functions = Register();

    public static IEnumerable<string> Names => functions.Keys;

    public static bool TryGet(string name, [NotNullWhen(true)] out FormulaFunction? function) =>
        functions.TryGetValue(name, out function);

    static Dictionary<string, FormulaFunction> Register()
    {
        var all = new Dictionary<string, FormulaFunction>(StringComparer.OrdinalIgnoreCase);
        void Add(FormulaFunction function) => all[function.Name] = function;
        RegisterMath(Add);
        RegisterStatistics(Add);
        RegisterLogical(Add);
        RegisterInformation(Add);
        RegisterText(Add);
        RegisterDates(Add);
        RegisterLookup(Add);
        return all;
    }

    static FormulaValue Error(FormulaError error) =>
        FormulaValue.FromError(error);

    static FormulaValue Number(double value) =>
        FormulaValue.FromNumber(value);

    /// <summary>
    /// The numbers of the arguments as SUM reads them: in ranges and arrays only the numbers, typed as
    /// arguments the logicals and the text that reads as a number too. An error anywhere is the result.
    /// </summary>
    static FormulaValue? Numbers(FunctionArguments args, int first, List<double> into)
    {
        for (var index = first; index < args.Count; index++)
        {
            if (args.IsMissing(index))
            {
                into.Add(0);
                continue;
            }

            var raw = args.Raw(index);
            if (raw.IsReference)
            {
                foreach (var (_, value) in args.Evaluator.Cells(raw.Area))
                {
                    if (value.IsError)
                    {
                        return value;
                    }

                    if (value.IsNumber)
                    {
                        into.Add(value.Number);
                    }
                }

                continue;
            }

            if (raw.IsArray)
            {
                foreach (var value in raw.Array)
                {
                    if (value.IsError)
                    {
                        return value;
                    }

                    if (value.IsNumber)
                    {
                        into.Add(value.Number);
                    }
                }

                continue;
            }

            var number = FormulaCoercion.ToNumber(raw);
            if (number.IsError)
            {
                return number;
            }

            into.Add(number.Number);
        }

        return null;
    }

    /// <summary>Every value of the arguments, ranges read cell by cell, and blank cells left out.</summary>
    static IEnumerable<FormulaValue> Values(FunctionArguments args, int first)
    {
        for (var index = first; index < args.Count; index++)
        {
            if (args.IsMissing(index))
            {
                yield return FormulaValue.Blank;
                continue;
            }

            var raw = args.Raw(index);
            if (raw.IsReference)
            {
                foreach (var (_, value) in args.Evaluator.Cells(raw.Area))
                {
                    if (!value.IsBlank)
                    {
                        yield return value;
                    }
                }

                continue;
            }

            if (raw.IsArray)
            {
                foreach (var value in raw.Array)
                {
                    yield return value;
                }

                continue;
            }

            yield return raw;
        }
    }

    // A whole number argument, as Excel truncates them: 2.9 columns is 2.
    static FormulaValue Integer(FunctionArguments args, int index, double fallback)
    {
        var number = args.Number(index, fallback);
        if (number.IsError)
        {
            return number;
        }

        return Number(Math.Truncate(number.Number));
    }

    // The value at a position of a range or an array, by rows and columns from 0.
    static FormulaValue At(FormulaEvaluator evaluator, FormulaValue source, int row, int column)
    {
        if (source.IsReference)
        {
            var area = source.Area;
            return evaluator.CellValue(area.Sheet, area.FirstRow + row, area.FirstColumn + column);
        }

        if (source.IsArray)
        {
            return source.Array[row, column];
        }

        return source;
    }

    static (int Rows, int Columns) Size(FormulaEvaluator evaluator, FormulaValue source)
    {
        if (source.IsReference)
        {
            var area = evaluator.Bounded(source.Area);
            if (source.Area.LastRow == CellReference.MaxRow ||
                source.Area.LastColumn == CellReference.MaxColumn)
            {
                return (area.Rows, area.Columns);
            }

            return (source.Area.Rows, source.Area.Columns);
        }

        if (source.IsArray)
        {
            return (source.Array.GetLength(0), source.Array.GetLength(1));
        }

        return (1, 1);
    }
}
