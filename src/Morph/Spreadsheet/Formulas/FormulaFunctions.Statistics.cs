static partial class FormulaFunctions
{
    static void RegisterStatistics(Action<FormulaFunction> add)
    {
        add(new("AVERAGE", 1, 255, Average));
        add(new("MIN", 1, 255, _ => Extreme(_, false)));
        add(new("MAX", 1, 255, _ => Extreme(_, true)));
        add(new("COUNT", 1, 255, Count));
        add(new("COUNTA", 1, 255, CountA));
        add(new("COUNTBLANK", 1, 1, CountBlank));
        add(new("COUNTIF", 2, 2, CountIf));
        add(new("COUNTIFS", 2, 254, _ => Conditional(_, 0, ConditionalKind.Count)));
        add(new("SUMIF", 2, 3, SumIf));
        add(new("SUMIFS", 3, 255, _ => Conditional(_, 1, ConditionalKind.Sum)));
        add(new("AVERAGEIF", 2, 3, AverageIf));
        add(new("AVERAGEIFS", 3, 255, _ => Conditional(_, 1, ConditionalKind.Average)));
        add(new("MINIFS", 3, 255, _ => Conditional(_, 1, ConditionalKind.Min)));
        add(new("MAXIFS", 3, 255, _ => Conditional(_, 1, ConditionalKind.Max)));
        add(new("MEDIAN", 1, 255, _ => Summary(_, Median)));
        add(new("MODE", 1, 255, _ => Summary(_, Mode)));
        add(new("MODE.SNGL", 1, 255, _ => Summary(_, Mode)));
        add(new("STDEV", 1, 255, _ => Summary(_, SampleDeviation)));
        add(new("STDEV.S", 1, 255, _ => Summary(_, SampleDeviation)));
        add(new("STDEVP", 1, 255, _ => Summary(_, PopulationDeviation)));
        add(new("STDEV.P", 1, 255, _ => Summary(_, PopulationDeviation)));
        add(new("VAR", 1, 255, _ => Summary(_, SampleVariance)));
        add(new("VAR.S", 1, 255, _ => Summary(_, SampleVariance)));
        add(new("VARP", 1, 255, _ => Summary(_, PopulationVariance)));
        add(new("VAR.P", 1, 255, _ => Summary(_, PopulationVariance)));
        add(new("LARGE", 2, 2, _ => Kth(_, true)));
        add(new("SMALL", 2, 2, _ => Kth(_, false)));
        add(new("RANK", 2, 3, _ => Rank(_, false)));
        add(new("RANK.EQ", 2, 3, _ => Rank(_, false)));
        add(new("RANK.AVG", 2, 3, _ => Rank(_, true)));
        add(new("SUBTOTAL", 2, 255, Subtotal));
        add(new("SUMPRODUCT", 1, 255, SumProduct)
        {
            TakesArrays = true
        });
    }

    static FormulaValue Average(FunctionArguments args)
    {
        var numbers = new List<double>();
        if (Numbers(args, 0, numbers) is { } error)
        {
            return error;
        }

        if (numbers.Count == 0)
        {
            return Error(FormulaError.DivideByZero);
        }

        return Number(numbers.Sum() / numbers.Count);
    }

    static FormulaValue Extreme(FunctionArguments args, bool largest)
    {
        var numbers = new List<double>();
        if (Numbers(args, 0, numbers) is { } error)
        {
            return error;
        }

        if (numbers.Count == 0)
        {
            return Number(0);
        }

        if (largest)
        {
            return Number(numbers.Max());
        }

        return Number(numbers.Min());
    }

    // COUNT counts the numbers in ranges; typed in, it counts the logicals and the text that reads as a
    // number too, and never fails.
    static FormulaValue Count(FunctionArguments args)
    {
        var count = 0;
        for (var index = 0; index < args.Count; index++)
        {
            if (args.IsMissing(index))
            {
                count++;
                continue;
            }

            var raw = args.Raw(index);
            if (raw.IsReference)
            {
                count += args.Evaluator.Cells(raw.Area).Count(_ => _.Value.IsNumber);
                continue;
            }

            if (raw.IsArray)
            {
                foreach (var value in raw.Array)
                {
                    if (value.IsNumber)
                    {
                        count++;
                    }
                }

                continue;
            }

            if (raw.IsNumber ||
                raw.IsBoolean ||
                (raw.IsText && FormulaCoercion.ParseNumber(raw.Text) != null))
            {
                count++;
            }
        }

        return Number(count);
    }

    // Every value that is not blank, an empty text a formula made included.
    static FormulaValue CountA(FunctionArguments args)
    {
        var count = 0;
        for (var index = 0; index < args.Count; index++)
        {
            var raw = args.Raw(index);
            if (raw.IsReference)
            {
                count += args.Evaluator.Cells(raw.Area).Count(_ => !_.Value.IsBlank);
                continue;
            }

            if (raw.IsArray)
            {
                foreach (var value in raw.Array)
                {
                    if (!value.IsBlank)
                    {
                        count++;
                    }
                }

                continue;
            }

            // An argument typed in counts whatever it is, a left-out one included.
            count++;
        }

        return Number(count);
    }

    static FormulaValue CountBlank(FunctionArguments args)
    {
        if (args.Area(0) is not { } area)
        {
            return Error(FormulaError.Value);
        }

        var filled = args.Evaluator.Cells(area).Count(_ => !Empty(_.Value));
        return Number((double) area.Rows * area.Columns - filled);
    }

    static bool Empty(FormulaValue value) =>
        value.IsBlank ||
        (value.IsText && value.Text.Length == 0);

    // An array of criteria counts for each: SUMPRODUCT(1/COUNTIF(r, r&"")) is how many different values
    // r holds, as inventory-list-accessibility-guide counts its bins.
    static FormulaValue CountIf(FunctionArguments args)
    {
        var criteria = args.Evaluator.Operand(args.Raw(1));
        if (criteria.IsArray)
        {
            return FormulaEvaluator.Map(criteria, _ => CountMatching(args, _));
        }

        return CountMatching(args, args.Evaluator.Scalar(criteria));
    }

    static FormulaValue CountMatching(FunctionArguments args, FormulaValue criterion)
    {
        var matches = FormulaCriteria.Parse(criterion);
        var raw = args.Raw(0);
        if (raw.IsArray)
        {
            var inArray = 0;
            foreach (var value in raw.Array)
            {
                if (matches(value))
                {
                    inArray++;
                }
            }

            return Number(inArray);
        }

        if (!raw.IsReference)
        {
            return Error(FormulaError.Value);
        }

        var area = raw.Area;
        var count = 0d;
        var present = 0;
        foreach (var (_, value) in args.Evaluator.Cells(area))
        {
            present++;
            if (matches(value))
            {
                count++;
            }
        }

        // The cells there are none of match a criterion that matches blank.
        if (matches(FormulaValue.Blank))
        {
            count += (double) area.Rows * area.Columns - present;
        }

        return Number(count);
    }

    enum ConditionalKind
    {
        Count,
        Sum,
        Average,
        Min,
        Max
    }

    // COUNTIFS, SUMIFS and the rest: pairs of a range and its criterion from `first`, every range the
    // same size, the value range (if any) first.
    static FormulaValue Conditional(FunctionArguments args, int first, ConditionalKind kind)
    {
        if ((args.Count - first) % 2 != 0)
        {
            return Error(FormulaError.Value);
        }

        var evaluator = args.Evaluator;
        var ranges = new List<FormulaValue>();
        var tests = new List<Func<FormulaValue, bool>>();
        for (var index = first; index < args.Count; index += 2)
        {
            var range = args.Raw(index);
            if (range is
                {
                    IsReference: false,
                    IsArray: false
                })
            {
                return Error(FormulaError.Value);
            }

            ranges.Add(range);
            var criterion = args.Scalar(index + 1);
            tests.Add(FormulaCriteria.Parse(criterion));
        }

        FormulaValue? values = null;
        if (first == 1)
        {
            values = args.Raw(0);
            if (values.Value is
                {
                    IsReference: false,
                    IsArray: false
                })
            {
                return Error(FormulaError.Value);
            }
        }

        var every = new List<FormulaValue>(ranges);
        if (values is { } valueRange)
        {
            every.Add(valueRange);
        }

        var (rows, columns) = Declared(ranges[0]);
        foreach (var range in every)
        {
            if (Declared(range) != (rows, columns))
            {
                return Error(FormulaError.Value);
            }
        }

        // Only as far as any of the ranges has cells: past that every range is blank.
        var reach = every.Select(_ => Size(evaluator, _)).ToList();
        var bounded = (Rows: Math.Min(rows, reach.Max(_ => _.Rows)), Columns: Math.Min(columns, reach.Max(_ => _.Columns)));

        var count = 0;
        var total = 0d;
        var extreme = double.NaN;
        for (var r = 0; r < bounded.Rows; r++)
        {
            for (var c = 0; c < bounded.Columns; c++)
            {
                var all = true;
                for (var index = 0; index < ranges.Count && all; index++)
                {
                    all = tests[index](At(evaluator, ranges[index], r, c));
                }

                if (!all)
                {
                    continue;
                }

                if (values is not { } source)
                {
                    count++;
                    continue;
                }

                var value = At(evaluator, source, r, c);
                if (value.IsError)
                {
                    return value;
                }

                if (!value.IsNumber)
                {
                    continue;
                }

                count++;
                total += value.Number;
                if (double.IsNaN(extreme) ||
                    (kind == ConditionalKind.Max && value.Number > extreme) ||
                    (kind == ConditionalKind.Min && value.Number < extreme))
                {
                    extreme = value.Number;
                }
            }
        }

        switch (kind)
        {
            case ConditionalKind.Count:
                return Number(count);
            case ConditionalKind.Sum:
                return Number(total);
            case ConditionalKind.Average:
                if (count == 0)
                {
                    return Error(FormulaError.DivideByZero);
                }

                return Number(total / count);
            default:
                if (double.IsNaN(extreme))
                {
                    return Number(0);
                }

                return Number(extreme);
        }
    }

    // A range's size as written, whole columns and all.
    static (int Rows, int Columns) Declared(FormulaValue range)
    {
        if (range.IsReference)
        {
            return (range.Area.Rows, range.Area.Columns);
        }

        return (range.Array.GetLength(0), range.Array.GetLength(1));
    }

    static FormulaValue SumIf(FunctionArguments args) =>
        IfWithValues(args, ConditionalKind.Sum);

    static FormulaValue AverageIf(FunctionArguments args) =>
        IfWithValues(args, ConditionalKind.Average);

    // SUMIF and AVERAGEIF: the values are the range itself, or a range of the same size starting where
    // the third argument starts, whatever size it is written as — as Excel reads it.
    static FormulaValue IfWithValues(FunctionArguments args, ConditionalKind kind)
    {
        var range = args.Raw(0);
        if (range is
            {
                IsReference: false,
                IsArray: false
            })
        {
            return Error(FormulaError.Value);
        }

        var matches = FormulaCriteria.Parse(args.Scalar(1));
        var values = range;
        if (!args.IsMissing(2))
        {
            values = args.Raw(2);
            if (values.IsReference &&
                range.IsReference)
            {
                var start = values.Area;
                values = FormulaValue.FromArea(new(start.Sheet, start.FirstRow, start.FirstColumn, start.FirstRow + range.Area.Rows - 1, start.FirstColumn + range.Area.Columns - 1));
            }
        }

        var evaluator = args.Evaluator;
        var (rows, columns) = Size(evaluator, range);
        var count = 0;
        var total = 0d;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                if (!matches(At(evaluator, range, r, c)))
                {
                    continue;
                }

                var value = At(evaluator, values, r, c);
                if (value.IsError)
                {
                    return value;
                }

                if (value.IsNumber)
                {
                    count++;
                    total += value.Number;
                }
            }
        }

        if (kind == ConditionalKind.Sum)
        {
            return Number(total);
        }

        if (count == 0)
        {
            return Error(FormulaError.DivideByZero);
        }

        return Number(total / count);
    }

    static FormulaValue Summary(FunctionArguments args, Func<List<double>, FormulaValue> summarise)
    {
        var numbers = new List<double>();
        if (Numbers(args, 0, numbers) is { } error)
        {
            return error;
        }

        return summarise(numbers);
    }

    static FormulaValue Median(List<double> numbers)
    {
        if (numbers.Count == 0)
        {
            return Error(FormulaError.Number);
        }

        numbers.Sort();
        var middle = numbers.Count / 2;
        if (numbers.Count % 2 == 1)
        {
            return Number(numbers[middle]);
        }

        return Number((numbers[middle - 1] + numbers[middle]) / 2);
    }

    // The value that comes most often, the first to come so often if several do; #N/A if none repeats.
    static FormulaValue Mode(List<double> numbers)
    {
        var best = double.NaN;
        var most = 1;
        var counts = new Dictionary<double, int>();
        foreach (var number in numbers)
        {
            var count = counts.GetValueOrDefault(number) + 1;
            counts[number] = count;
            if (count > most)
            {
                most = count;
                best = number;
            }
        }

        if (double.IsNaN(best))
        {
            return Error(FormulaError.NotAvailable);
        }

        return Number(best);
    }

    static FormulaValue SampleVariance(List<double> numbers) =>
        Variance(numbers, 1, false);

    static FormulaValue PopulationVariance(List<double> numbers) =>
        Variance(numbers, 0, false);

    static FormulaValue SampleDeviation(List<double> numbers) =>
        Variance(numbers, 1, true);

    static FormulaValue PopulationDeviation(List<double> numbers) =>
        Variance(numbers, 0, true);

    static FormulaValue Variance(List<double> numbers, int lost, bool root)
    {
        if (numbers.Count - lost <= 0)
        {
            return Error(FormulaError.DivideByZero);
        }

        var mean = numbers.Average();
        var sum = numbers.Sum(_ => (_ - mean) * (_ - mean));
        var variance = sum / (numbers.Count - lost);
        if (root)
        {
            return Number(Math.Sqrt(variance));
        }

        return Number(variance);
    }

    static FormulaValue Kth(FunctionArguments args, bool largest)
    {
        var numbers = new List<double>();
        foreach (var value in args.Array(0))
        {
            if (value.IsError)
            {
                return value;
            }

            if (value.IsNumber)
            {
                numbers.Add(value.Number);
            }
        }

        var k = args.Number(1);
        if (k.IsError)
        {
            return k;
        }

        var position = (int) Math.Ceiling(k.Number);
        if (position < 1 ||
            position > numbers.Count)
        {
            return Error(FormulaError.Number);
        }

        numbers.Sort();
        if (largest)
        {
            return Number(numbers[^position]);
        }

        return Number(numbers[position - 1]);
    }

    static FormulaValue Rank(FunctionArguments args, bool average)
    {
        var number = args.Number(0);
        if (number.IsError)
        {
            return number;
        }

        var order = args.Number(2, 0);
        if (order.IsError)
        {
            return order;
        }

        var raw = args.Raw(1);
        if (!raw.IsReference)
        {
            return Error(FormulaError.Value);
        }

        var ahead = 0;
        var same = 0;
        var ascending = order.Number != 0;
        foreach (var (_, value) in args.Evaluator.Cells(raw.Area))
        {
            if (!value.IsNumber)
            {
                continue;
            }

            var compared = FormulaCoercion.CompareNumbers(value.Number, number.Number);
            if (compared == 0)
            {
                same++;
            }
            else if ((compared > 0 && !ascending) ||
                     (compared < 0 && ascending))
            {
                ahead++;
            }
        }

        if (same == 0)
        {
            return Error(FormulaError.NotAvailable);
        }

        if (average)
        {
            return Number(ahead + (same + 1) / 2d);
        }

        return Number(ahead + 1);
    }

    // SUBTOTAL leaves out the cells of its ranges that are subtotals themselves, so a total of totals
    // is not counted twice, and with 101-111 the rows that are hidden.
    static FormulaValue Subtotal(FunctionArguments args)
    {
        var which = args.Number(0);
        if (which.IsError)
        {
            return which;
        }

        var code = (int) Math.Truncate(which.Number);
        var skipHidden = code > 100;
        code %= 100;
        if (code is < 1 or > 11)
        {
            return Error(FormulaError.Value);
        }

        var evaluator = args.Evaluator;
        var values = new List<FormulaValue>();
        for (var index = 1; index < args.Count; index++)
        {
            var raw = args.Raw(index);
            if (!raw.IsReference)
            {
                return Error(FormulaError.Value);
            }

            var area = raw.Area;
            foreach (var (cell, value) in evaluator.Cells(area))
            {
                if (cell.Subtotal ||
                    (skipHidden && evaluator.Book.RowHidden(area.Sheet, cell.Row)))
                {
                    continue;
                }

                values.Add(value);
            }
        }

        if (code == 3)
        {
            return Number(values.Count(_ => !_.IsBlank));
        }

        if (code == 2)
        {
            return Number(values.Count(_ => _.IsNumber));
        }

        if (values.FirstOrDefault(_ => _.IsError) is {IsError: true} error)
        {
            return error;
        }

        var numbers = values.Where(_ => _.IsNumber).Select(_ => _.Number).ToList();
        switch (code)
        {
            case 1:
                if (numbers.Count == 0)
                {
                    return Error(FormulaError.DivideByZero);
                }

                return Number(numbers.Average());
            case 4:
                return Number(numbers.Count == 0 ? 0 : numbers.Max());
            case 5:
                return Number(numbers.Count == 0 ? 0 : numbers.Min());
            case 6:
                return Number(numbers.Aggregate(1d, (total, each) => total * each));
            case 7:
                return SampleDeviation(numbers);
            case 8:
                return PopulationDeviation(numbers);
            case 9:
                return Number(numbers.Sum());
            case 10:
                return SampleVariance(numbers);
            default:
                return PopulationVariance(numbers);
        }
    }

    static FormulaValue SumProduct(FunctionArguments args)
    {
        FormulaValue[,]? product = null;
        for (var index = 0; index < args.Count; index++)
        {
            var array = args.Array(index);
            if (product == null)
            {
                product = new FormulaValue[array.GetLength(0), array.GetLength(1)];
                for (var r = 0; r < array.GetLength(0); r++)
                {
                    for (var c = 0; c < array.GetLength(1); c++)
                    {
                        if (array[r, c].IsError)
                        {
                            return array[r, c];
                        }

                        product[r, c] = Number(Factor(array[r, c]));
                    }
                }

                continue;
            }

            if (array.GetLength(0) != product.GetLength(0) ||
                array.GetLength(1) != product.GetLength(1))
            {
                return Error(FormulaError.Value);
            }

            for (var r = 0; r < array.GetLength(0); r++)
            {
                for (var c = 0; c < array.GetLength(1); c++)
                {
                    if (array[r, c].IsError)
                    {
                        return array[r, c];
                    }

                    product[r, c] = Number(product[r, c].Number * Factor(array[r, c]));
                }
            }
        }

        var total = 0d;
        foreach (var value in product!)
        {
            total += value.Number;
        }

        return Number(total);
    }

    // What SUMPRODUCT multiplies by: a number as it is, and anything else 0 — logicals too, which is why
    // a comparison is written --(A1:A9>0) or multiplied by another to count.
    static double Factor(FormulaValue value)
    {
        if (value.IsNumber)
        {
            return value.Number;
        }

        return 0;
    }
}
