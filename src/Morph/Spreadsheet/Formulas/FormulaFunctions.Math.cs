static partial class FormulaFunctions
{
    static void RegisterMath(Action<FormulaFunction> add)
    {
        add(new("SUM", 1, 255, _ => Fold(_, 0, 0d, (total, value) => total + value)));
        add(new("PRODUCT", 1, 255, _ => Fold(_, 0, 1d, (total, value) => total * value)));
        add(new("SUMSQ", 1, 255, _ => Fold(_, 0, 0d, (total, value) => total + value * value)));
        add(new("ABS", 1, 1, _ => Unary(_, Math.Abs)));
        add(new("INT", 1, 1, _ => Unary(_, Math.Floor)));
        add(new("SIGN", 1, 1, _ => Unary(_, Sign)));
        add(new("SQRT", 1, 1, _ => Unary(_, Root)));
        add(new("EXP", 1, 1, _ => Unary(_, Math.Exp)));
        add(new("LN", 1, 1, _ => Unary(_, Ln)));
        add(new("LOG10", 1, 1, _ => Unary(_, Log10)));
        add(new("LOG", 1, 2, Log));
        add(new("PI", 0, 0, _ => Number(Math.PI)));
        add(new("POWER", 2, 2, Power));
        add(new("MOD", 2, 2, Mod));
        add(new("QUOTIENT", 2, 2, Quotient));
        add(new("TRUNC", 1, 2, _ => Rounding(_, 0, RoundingKind.Down)));
        add(new("ROUND", 1, 2, _ => Rounding(_, double.NaN, RoundingKind.Nearest)));
        add(new("ROUNDUP", 1, 2, _ => Rounding(_, double.NaN, RoundingKind.Up)));
        add(new("ROUNDDOWN", 1, 2, _ => Rounding(_, double.NaN, RoundingKind.Down)));
        add(new("MROUND", 2, 2, MRound));
        add(new("CEILING", 2, 2, _ => Ceiling(_, false)));
        add(new("FLOOR", 2, 2, _ => Ceiling(_, true)));
        add(new("CEILING.MATH", 1, 3, _ => MathRounding(_, false)));
        add(new("FLOOR.MATH", 1, 3, _ => MathRounding(_, true)));
        add(new("EVEN", 1, 1, _ => Unary(_, Even)));
        add(new("ODD", 1, 1, _ => Unary(_, Odd)));
        add(new("FACT", 1, 1, Fact));
        add(new("RAND", 0, 0, _ => Number(Random.Shared.NextDouble()))
        {
            Volatile = true
        });
        add(new("RANDBETWEEN", 2, 2, RandBetween)
        {
            Volatile = true
        });
    }

    static FormulaValue Fold(FunctionArguments args, int first, double seed, Func<double, double, double> step)
    {
        var numbers = new List<double>();
        if (Numbers(args, first, numbers) is { } error)
        {
            return error;
        }

        var total = seed;
        foreach (var number in numbers)
        {
            total = step(total, number);
        }

        return Number(total);
    }

    static FormulaValue Unary(FunctionArguments args, Func<double, double> map) =>
        FormulaEvaluator.Map(args.Evaluator.Operand(args.Raw(0)), _ =>
        {
            var number = FormulaCoercion.ToNumber(_);
            if (number.IsError)
            {
                return number;
            }

            return Number(map(number.Number));
        });

    static double Sign(double value) =>
        Math.Sign(value);

    // An argument out of the function's domain is NaN, which FromNumber makes #NUM!.
    static double Root(double value)
    {
        if (value < 0)
        {
            return double.NaN;
        }

        return Math.Sqrt(value);
    }

    static double Ln(double value)
    {
        if (value <= 0)
        {
            return double.NaN;
        }

        return Math.Log(value);
    }

    static double Log10(double value)
    {
        if (value <= 0)
        {
            return double.NaN;
        }

        return Math.Log10(value);
    }

    static double Even(double value) =>
        AwayToMultiple(value, 2, 0);

    static double Odd(double value) =>
        AwayToMultiple(value, 2, 1);

    static FormulaValue Log(FunctionArguments args)
    {
        var value = args.Number(0);
        if (value.IsError)
        {
            return value;
        }

        var radix = args.Number(1, 10);
        if (radix.IsError)
        {
            return radix;
        }

        if (value.Number <= 0 ||
            radix.Number <= 0)
        {
            return Error(FormulaError.Number);
        }

        if (radix.Number == 1)
        {
            return Error(FormulaError.DivideByZero);
        }

        return Number(Math.Log(value.Number, radix.Number));
    }

    static FormulaValue Power(FunctionArguments args)
    {
        var x = args.Number(0);
        if (x.IsError)
        {
            return x;
        }

        var y = args.Number(1);
        if (y.IsError)
        {
            return y;
        }

        if (x.Number == 0 &&
            y.Number == 0)
        {
            return Error(FormulaError.Number);
        }

        if (x.Number == 0 &&
            y.Number < 0)
        {
            return Error(FormulaError.DivideByZero);
        }

        return Number(Math.Pow(x.Number, y.Number));
    }

    static FormulaValue Mod(FunctionArguments args)
    {
        var n = args.Number(0);
        if (n.IsError)
        {
            return n;
        }

        var d = args.Number(1);
        if (d.IsError)
        {
            return d;
        }

        if (d.Number == 0)
        {
            return Error(FormulaError.DivideByZero);
        }

        // The result takes the divisor's sign: MOD(-3, 2) is 1.
        return Number(n.Number - d.Number * Math.Floor(n.Number / d.Number));
    }

    static FormulaValue Quotient(FunctionArguments args)
    {
        var n = args.Number(0);
        if (n.IsError)
        {
            return n;
        }

        var d = args.Number(1);
        if (d.IsError)
        {
            return d;
        }

        if (d.Number == 0)
        {
            return Error(FormulaError.DivideByZero);
        }

        return Number(Math.Truncate(n.Number / d.Number));
    }

    enum RoundingKind
    {
        Nearest,
        Up,
        Down
    }

    // ROUND's digits are required; TRUNC's default to 0, which the fallback says.
    static FormulaValue Rounding(FunctionArguments args, double fallback, RoundingKind kind)
    {
        var value = args.Number(0);
        if (value.IsError)
        {
            return value;
        }

        FormulaValue digits;
        if (double.IsNaN(fallback))
        {
            if (args.Count < 2)
            {
                return Error(FormulaError.Value);
            }

            digits = args.Number(1, 0);
        }
        else
        {
            digits = args.Number(1, fallback);
        }

        if (digits.IsError)
        {
            return digits;
        }

        return Number(Round(value.Number, (int) Math.Truncate(digits.Number), kind));
    }

    /// <summary>
    /// Rounds as Excel does: on the number as it reads to fifteen digits, so ROUND(2.675, 2) is 2.68
    /// though the double nearest 2.675 is a little under it; halves away from zero.
    /// </summary>
    static double Round(double value, int digits, RoundingKind kind)
    {
        if (value == 0 ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            return value;
        }

        if (Math.Abs(value) < 7.9e27 &&
            digits is >= -27 and <= 27)
        {
            // The number as it reads to fifteen digits, which is what Excel rounds: the double nearest
            // 2.675 is 2.67499999999999982..., and rounded as it is that is 2.67.
            var exact = decimal.Parse(value.ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            var scale = 1m;
            for (var index = 0; index < Math.Abs(digits); index++)
            {
                scale *= 10;
            }

            var scaled = exact;
            if (digits >= 0)
            {
                scaled *= scale;
            }
            else
            {
                scaled /= scale;
            }

            scaled = kind switch
            {
                RoundingKind.Nearest => Math.Round(scaled, MidpointRounding.AwayFromZero),
                RoundingKind.Up => Math.Sign(scaled) * Math.Ceiling(Math.Abs(scaled)),
                _ => Math.Truncate(scaled)
            };

            if (digits >= 0)
            {
                return (double) (scaled / scale);
            }

            return (double) (scaled * scale);
        }

        var factor = Math.Pow(10, digits);
        var shifted = value * factor;
        shifted = kind switch
        {
            RoundingKind.Nearest => Math.Round(shifted, MidpointRounding.AwayFromZero),
            RoundingKind.Up => Math.Sign(shifted) * Math.Ceiling(Math.Abs(shifted)),
            _ => Math.Truncate(shifted)
        };
        return shifted / factor;
    }

    static FormulaValue MRound(FunctionArguments args)
    {
        var value = args.Number(0);
        if (value.IsError)
        {
            return value;
        }

        var multiple = args.Number(1);
        if (multiple.IsError)
        {
            return multiple;
        }

        if (multiple.Number == 0)
        {
            return Number(0);
        }

        if (Math.Sign(value.Number) * Math.Sign(multiple.Number) < 0)
        {
            return Error(FormulaError.Number);
        }

        return Number(Round(value.Number / multiple.Number, 0, RoundingKind.Nearest) * multiple.Number);
    }

    // CEILING and FLOOR: to a multiple of the significance, which must share the number's sign unless
    // the number is negative and the significance positive.
    static FormulaValue Ceiling(FunctionArguments args, bool floor)
    {
        var value = args.Number(0);
        if (value.IsError)
        {
            return value;
        }

        var significance = args.Number(1);
        if (significance.IsError)
        {
            return significance;
        }

        var x = value.Number;
        var s = significance.Number;
        if (x == 0)
        {
            return Number(0);
        }

        if (s == 0)
        {
            if (floor)
            {
                return Error(FormulaError.DivideByZero);
            }

            return Number(0);
        }

        if (x > 0 &&
            s < 0)
        {
            return Error(FormulaError.Number);
        }

        var quotient = Round(x / s, 15 - Digits(x / s), RoundingKind.Nearest);
        if (floor)
        {
            return Number(Math.Floor(quotient) * s);
        }

        return Number(Math.Ceiling(quotient) * s);
    }

    static FormulaValue MathRounding(FunctionArguments args, bool floor)
    {
        var value = args.Number(0);
        if (value.IsError)
        {
            return value;
        }

        var significance = args.Number(1, 1);
        if (significance.IsError)
        {
            return significance;
        }

        var mode = args.Number(2, 0);
        if (mode.IsError)
        {
            return mode;
        }

        var x = value.Number;
        var s = Math.Abs(significance.Number);
        if (x == 0 ||
            s == 0)
        {
            return Number(0);
        }

        var quotient = Round(x / s, 15 - Digits(x / s), RoundingKind.Nearest);
        var awayFromZero = mode.Number != 0 && x < 0;
        double result;
        if (floor ^ awayFromZero)
        {
            result = Math.Floor(quotient);
        }
        else
        {
            result = Math.Ceiling(quotient);
        }

        return Number(result * s);
    }

    // The digits before the point, for rounding a quotient to fifteen significant digits.
    static int Digits(double value)
    {
        if (value == 0)
        {
            return 0;
        }

        return Math.Max(0, (int) Math.Floor(Math.Log10(Math.Abs(value))) + 1);
    }

    static double AwayToMultiple(double value, double multiple, double offset)
    {
        var sign = Math.Sign(value);
        if (sign == 0)
        {
            if (offset == 0)
            {
                return 0;
            }

            return 1;
        }

        var magnitude = Math.Abs(value);
        var result = Math.Ceiling((magnitude - offset) / multiple) * multiple + offset;
        if (result < magnitude)
        {
            result += multiple;
        }

        return sign * result;
    }

    static FormulaValue Fact(FunctionArguments args)
    {
        var value = args.Number(0);
        if (value.IsError)
        {
            return value;
        }

        var n = Math.Truncate(value.Number);
        if (n < 0)
        {
            return Error(FormulaError.Number);
        }

        var result = 1d;
        for (var index = 2; index <= n; index++)
        {
            result *= index;
        }

        return Number(result);
    }

    static FormulaValue RandBetween(FunctionArguments args)
    {
        var bottom = args.Number(0);
        if (bottom.IsError)
        {
            return bottom;
        }

        var top = args.Number(1);
        if (top.IsError)
        {
            return top;
        }

        var low = Math.Ceiling(bottom.Number);
        var high = Math.Floor(top.Number);
        if (high < low)
        {
            return Error(FormulaError.Number);
        }

        return Number(low + Math.Floor(Random.Shared.NextDouble() * (high - low + 1)));
    }
}
