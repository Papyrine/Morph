static partial class FormulaFunctions
{
    static void RegisterLogical(Action<FormulaFunction> add)
    {
        add(new("IF", 1, 3, If));
        add(new("IFS", 2, 254, Ifs));
        add(new("IFERROR", 2, 2, _ => Catch(_, null)));
        add(new("IFNA", 2, 2, _ => Catch(_, FormulaError.NotAvailable)));
        add(new("AND", 1, 255, _ => Logic(_, LogicKind.And)));
        add(new("OR", 1, 255, _ => Logic(_, LogicKind.Or)));
        add(new("XOR", 1, 255, _ => Logic(_, LogicKind.Xor)));
        add(new("NOT", 1, 1, Not));
        add(new("TRUE", 0, 0, _ => FormulaValue.True));
        add(new("FALSE", 0, 0, _ => FormulaValue.False));
        add(new("SWITCH", 3, 254, Switch));
    }

    static void RegisterInformation(Action<FormulaFunction> add)
    {
        add(new("ISBLANK", 1, 1, _ => Is(_, IsBlank)));
        add(new("ISNUMBER", 1, 1, _ => Is(_, IsNumber)));
        add(new("ISTEXT", 1, 1, _ => Is(_, IsText)));
        add(new("ISNONTEXT", 1, 1, _ => Is(_, IsNonText)));
        add(new("ISLOGICAL", 1, 1, _ => Is(_, IsLogical)));
        add(new("ISERROR", 1, 1, _ => Is(_, IsError)));
        add(new("ISERR", 1, 1, _ => Is(_, IsErr)));
        add(new("ISNA", 1, 1, _ => Is(_, IsNotAvailable)));
        add(new("ISEVEN", 1, 1, _ => Parity(_, 0)));
        add(new("ISODD", 1, 1, _ => Parity(_, 1)));
        add(new("ISREF", 1, 1, _ => FormulaValue.FromBoolean(_.Raw(0).IsReference)));
        add(new("ISFORMULA", 1, 1, IsFormula));
        add(new("NA", 0, 0, _ => Error(FormulaError.NotAvailable)));
        add(new("ERROR.TYPE", 1, 1, ErrorType));
        add(new("N", 1, 1, ToN));
        add(new("T", 1, 1, ToT));
        add(new("TYPE", 1, 1, TypeOf));
    }

    // IF's result is the branch as it evaluated, a reference staying one, so SUM(IF(x, A1:A3, B1:B3)) sums
    // the range it picked.
    static FormulaValue If(FunctionArguments args)
    {
        // Over an array of conditions, value by value: each picks from its own place in the branches.
        var conditions = args.Evaluator.Operand(args.Raw(0));
        if (conditions.IsArray)
        {
            return Choices(conditions.Array, Branch(args, 1), Branch(args, 2));
        }

        var condition = args.Boolean(0);
        if (condition.IsError)
        {
            return condition;
        }

        var branch = 2;
        if (condition.Boolean)
        {
            branch = 1;
        }

        if (branch >= args.Count)
        {
            return FormulaValue.FromBoolean(condition.Boolean);
        }

        // IF(TRUE,,1): an argument written but left empty is 0.
        if (args.IsMissing(branch))
        {
            return Number(0);
        }

        return args.Raw(branch);
    }

    // A branch of IF as an array or a value: a left-out else is FALSE, a written-but-empty branch 0.
    static FormulaValue Branch(FunctionArguments args, int index)
    {
        if (index >= args.Count)
        {
            return FormulaValue.False;
        }

        if (args.IsMissing(index))
        {
            return Number(0);
        }

        return args.Evaluator.Operand(args.Raw(index));
    }

    // IF over an array of conditions: the branches stretched to the conditions' size as an operation
    // stretches its operands, a single row, column or value standing for every place.
    static FormulaValue Choices(FormulaValue[,] conditions, FormulaValue then, FormulaValue otherwise)
    {
        var rows = conditions.GetLength(0);
        var columns = conditions.GetLength(1);
        var result = new FormulaValue[rows, columns];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var truth = FormulaCoercion.ToBoolean(conditions[r, c]);
                if (truth.IsError)
                {
                    result[r, c] = truth;
                    continue;
                }

                var branch = otherwise;
                if (truth.Boolean)
                {
                    branch = then;
                }

                result[r, c] = Stretched(branch, r, c);
            }
        }

        return FormulaValue.FromArray(result);
    }

    static FormulaValue Stretched(FormulaValue value, int r, int c)
    {
        if (!value.IsArray)
        {
            return value;
        }

        var values = value.Array;
        var row = values.GetLength(0) == 1 ? 0 : r;
        var column = values.GetLength(1) == 1 ? 0 : c;
        if (row >= values.GetLength(0) ||
            column >= values.GetLength(1))
        {
            return FormulaValue.FromError(FormulaError.NotAvailable);
        }

        return values[row, column];
    }

    static FormulaValue Ifs(FunctionArguments args)
    {
        if (args.Count % 2 != 0)
        {
            return Error(FormulaError.Value);
        }

        for (var index = 0; index < args.Count; index += 2)
        {
            var condition = args.Boolean(index);
            if (condition.IsError)
            {
                return condition;
            }

            if (condition.Boolean)
            {
                return args.Raw(index + 1);
            }
        }

        return Error(FormulaError.NotAvailable);
    }

    // IFERROR and IFNA, over an array value by value.
    static FormulaValue Catch(FunctionArguments args, FormulaError? only)
    {
        var value = args.Evaluator.Operand(args.Raw(0));
        return FormulaEvaluator.Map(value, _ =>
        {
            if (_.IsError &&
                (only == null || _.Error == only))
            {
                return args.Scalar(1);
            }

            return _;
        });
    }

    enum LogicKind
    {
        And,
        Or,
        Xor
    }

    // In ranges only the logicals and the numbers count; typed in, text that is not TRUE or FALSE is
    // #VALUE!; with nothing logical at all the result is #VALUE! too.
    static FormulaValue Logic(FunctionArguments args, LogicKind kind)
    {
        var seen = false;
        var all = true;
        var any = false;
        var odd = false;
        for (var index = 0; index < args.Count; index++)
        {
            var raw = args.Raw(index);
            IEnumerable<FormulaValue> values;
            if (raw.IsReference)
            {
                values = args.Evaluator.Cells(raw.Area).Select(_ => _.Value).Where(_ => _.IsBoolean || _.IsNumber || _.IsError);
            }
            else if (raw.IsArray)
            {
                values = raw.Array.Cast<FormulaValue>().Where(_ => _.IsBoolean || _.IsNumber || _.IsError);
            }
            else
            {
                values = [FormulaCoercion.ToBoolean(raw)];
            }

            foreach (var value in values)
            {
                if (value.IsError)
                {
                    return value;
                }

                seen = true;
                var truth = value.Number != 0;
                all &= truth;
                any |= truth;
                odd ^= truth;
            }
        }

        if (!seen)
        {
            return Error(FormulaError.Value);
        }

        return kind switch
        {
            LogicKind.And => FormulaValue.FromBoolean(all),
            LogicKind.Or => FormulaValue.FromBoolean(any),
            _ => FormulaValue.FromBoolean(odd)
        };
    }

    static FormulaValue Not(FunctionArguments args) =>
        FormulaEvaluator.Map(args.Evaluator.Operand(args.Raw(0)), _ =>
        {
            var truth = FormulaCoercion.ToBoolean(_);
            if (truth.IsError)
            {
                return truth;
            }

            return FormulaValue.FromBoolean(!truth.Boolean);
        });

    static FormulaValue Switch(FunctionArguments args)
    {
        var value = args.Scalar(0);
        if (value.IsError)
        {
            return value;
        }

        var index = 1;
        for (; index + 1 < args.Count; index += 2)
        {
            var candidate = args.Scalar(index);
            if (candidate.IsError)
            {
                return candidate;
            }

            if (candidate.Kind == value.Kind &&
                FormulaCoercion.Compare(candidate, value) == 0)
            {
                return args.Raw(index + 1);
            }
        }

        if (index < args.Count)
        {
            return args.Raw(index);
        }

        return Error(FormulaError.NotAvailable);
    }

    // The IS functions read a cell as it is: ISBLANK of a formula's "" is FALSE.
    static FormulaValue Is(FunctionArguments args, Func<FormulaValue, bool> test) =>
        FormulaEvaluator.Map(args.Evaluator.Operand(args.Raw(0)), _ => FormulaValue.FromBoolean(test(_)));

    static bool IsBlank(FormulaValue value) => value.IsBlank;

    static bool IsNumber(FormulaValue value) => value.IsNumber;

    static bool IsText(FormulaValue value) => value.IsText;

    static bool IsNonText(FormulaValue value) => !value.IsText;

    static bool IsLogical(FormulaValue value) => value.IsBoolean;

    static bool IsError(FormulaValue value) => value.IsError;

    static bool IsErr(FormulaValue value) =>
        value.IsError &&
        value.Error != FormulaError.NotAvailable;

    static bool IsNotAvailable(FormulaValue value) =>
        value is {IsError: true, Error: FormulaError.NotAvailable};

    static FormulaValue Parity(FunctionArguments args, int remainder)
    {
        var number = args.Number(0);
        if (number.IsError)
        {
            return number;
        }

        var whole = Math.Abs(Math.Truncate(number.Number));
        return FormulaValue.FromBoolean(whole % 2 == remainder);
    }

    static FormulaValue IsFormula(FunctionArguments args)
    {
        if (args.Area(0) is not { } area)
        {
            return Error(FormulaError.Value);
        }

        var cell = args.Evaluator.Book.Cell(area.Sheet, area.FirstRow, area.FirstColumn);
        return FormulaValue.FromBoolean(cell?.HasFormula == true);
    }

    static FormulaValue ErrorType(FunctionArguments args)
    {
        var value = args.Scalar(0);
        if (!value.IsError)
        {
            return Error(FormulaError.NotAvailable);
        }

        return Number(FormulaErrors.Number(value.Error));
    }

    static FormulaValue ToN(FunctionArguments args)
    {
        var value = args.Scalar(0);
        if (value.IsNumber ||
            value.IsError)
        {
            return value;
        }

        if (value.IsBoolean)
        {
            return Number(value.Number);
        }

        return Number(0);
    }

    static FormulaValue ToT(FunctionArguments args)
    {
        var value = args.Scalar(0);
        if (value.IsText ||
            value.IsError)
        {
            return value;
        }

        return FormulaValue.EmptyText;
    }

    static FormulaValue TypeOf(FunctionArguments args)
    {
        var value = args.Evaluator.Operand(args.Raw(0));
        return Number(value.Kind switch
        {
            FormulaValueKind.Text => 2,
            FormulaValueKind.Boolean => 4,
            FormulaValueKind.Error => 16,
            FormulaValueKind.Array => 64,
            _ => 1
        });
    }
}
