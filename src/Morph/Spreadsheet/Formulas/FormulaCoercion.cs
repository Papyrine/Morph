/// <summary>
/// How Excel turns one kind of value into another: text into a number for arithmetic, a number into
/// text for <c>&amp;</c>, anything into a logical for IF; and how it orders values of different kinds
/// (numbers before text before logicals, text without regard to case).
/// </summary>
static class FormulaCoercion
{
    static readonly DateTime epoch = new(1899, 12, 30);

    static readonly string[] dateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy/MM/dd",
        "M/d/yyyy",
        "M/d/yy",
        "M/d/yyyy H:mm",
        "M/d/yyyy H:mm:ss",
        "M/d/yyyy h:mm tt",
        "d-MMM-yyyy",
        "d-MMM-yy",
        "d MMM yyyy",
        "MMM d, yyyy",
        "MMMM d, yyyy",
        "d MMMM yyyy",
        "MMM-yy",
        "MMMM yyyy"
    ];

    static readonly string[] timeFormats =
    [
        "H:mm",
        "H:mm:ss",
        "h:mm tt",
        "h:mm:ss tt",
        "h tt"
    ];

    /// <summary>
    /// The serial day Excel keeps a date as. Before 1 March 1900 it is a day short, because Excel counts
    /// the 29 February 1900 that never was, as Lotus 1-2-3 did.
    /// </summary>
    public static double ToSerial(DateTime date)
    {
        var serial = (date - epoch).TotalDays;
        if (serial < 61)
        {
            serial -= 1;
        }

        return serial;
    }

    public static DateTime FromSerial(double serial)
    {
        if (serial < 61)
        {
            serial += 1;
        }

        // Whole milliseconds, so a time typed as 10:30 reads back as 10:30 and not 10:29:59.999.
        var milliseconds = Math.Round(serial * 86_400_000);
        return epoch.AddMilliseconds(milliseconds);
    }

    /// <summary>A value as a number, for arithmetic: blank is 0, a logical 1 or 0, text read if it can be.</summary>
    public static FormulaValue ToNumber(FormulaValue value)
    {
        switch (value.Kind)
        {
            case FormulaValueKind.Number:
            case FormulaValueKind.Error:
                return value;
            case FormulaValueKind.Blank:
                return FormulaValue.FromNumber(0);
            case FormulaValueKind.Boolean:
                return FormulaValue.FromNumber(value.Number);
            case FormulaValueKind.Text:
                if (ParseNumber(value.Text) is { } number)
                {
                    return FormulaValue.FromNumber(number);
                }

                return FormulaValue.FromError(FormulaError.Value);
            default:
                return FormulaValue.FromError(FormulaError.Value);
        }
    }

    /// <summary>
    /// Text read as a number the way Excel reads it typed into a cell: <c>1,234.5</c>, <c>50%</c>,
    /// <c>$12</c>, <c>(5)</c>, <c>1E3</c>, a date or a time. Null when it is not one.
    /// </summary>
    public static double? ParseNumber(CharSpan text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var negative = false;
        if (trimmed.Length > 2 &&
            trimmed[0] == '(' &&
            trimmed[^1] == ')')
        {
            negative = true;
            trimmed = trimmed[1..^1].Trim();
        }

        var scale = 1d;
        if (trimmed.EndsWith('%'))
        {
            scale = 0.01;
            trimmed = trimmed[..^1].TrimEnd();
        }

        var unsigned = trimmed;
        var sign = 1d;
        if (unsigned.StartsWith('-') ||
            unsigned.StartsWith('+'))
        {
            sign = unsigned[0] == '-' ? -1 : 1;
            unsigned = unsigned[1..].TrimStart();
        }

        if (unsigned.StartsWith('$'))
        {
            unsigned = unsigned[1..].TrimStart();
        }

        if (unsigned.Length > 0 &&
            (char.IsAsciiDigit(unsigned[0]) || unsigned[0] == '.') &&
            double.TryParse(unsigned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number) &&
            ThousandsInPlace(unsigned))
        {
            var result = sign * number * scale;
            if (negative)
            {
                result = -result;
            }

            return result;
        }

        if (scale != 1 ||
            negative)
        {
            return null;
        }

        return ParseDate(trimmed);
    }

    // "1,234" is a thousand and more; "1,2" is not a number at all.
    static bool ThousandsInPlace(CharSpan text)
    {
        var point = text.IndexOfAny('.', 'e', 'E');
        var whole = point < 0 ? text : text[..point];
        var comma = whole.IndexOf(',');
        if (comma < 0)
        {
            return true;
        }

        // Every group after the first is three digits.
        var rest = whole[(comma + 1)..];
        foreach (var range in rest.Split(','))
        {
            if (rest[range].Length != 3)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A date or a time typed as text, as its serial; null when it is neither.</summary>
    public static double? ParseDate(CharSpan text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 3 ||
            !trimmed.ContainsAnyInRange('0', '9'))
        {
            return null;
        }

        if (DateTime.TryParseExact(trimmed, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AllowWhiteSpaces, out var time) &&
            time.Date == DateTime.MinValue.Date)
        {
            return time.TimeOfDay.TotalDays;
        }

        if (DateTime.TryParseExact(trimmed, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            return ToSerial(date);
        }

        return null;
    }

    /// <summary>A value as text, for <c>&amp;</c> and the text functions.</summary>
    public static FormulaValue ToText(FormulaValue value) =>
        value.Kind switch
        {
            FormulaValueKind.Text or FormulaValueKind.Error => value,
            FormulaValueKind.Blank => FormulaValue.EmptyText,
            FormulaValueKind.Boolean when value.Boolean => FormulaValue.FromText("TRUE"),
            FormulaValueKind.Boolean => FormulaValue.FromText("FALSE"),
            FormulaValueKind.Number => FormulaValue.FromText(NumberText(value.Number)),
            _ => FormulaValue.FromError(FormulaError.Value)
        };

    /// <summary>
    /// A number as Excel writes it into text: to fifteen significant digits, without a thousands
    /// separator, in E notation only past 1E+21 or for the very small.
    /// </summary>
    public static string NumberText(double number)
    {
        if (number == 0)
        {
            return "0";
        }

        var text = number.ToString("G15", CultureInfo.InvariantCulture);
        if (!text.Contains('E'))
        {
            return text;
        }

        var rounded = double.Parse(text, CultureInfo.InvariantCulture);
        var magnitude = Math.Abs(rounded);
        if (magnitude is >= 1e15 and < 1e21)
        {
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        }

        if (magnitude is < 1e15 and >= 1e-9)
        {
            return rounded.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        // .NET writes E-05, Excel E-5.
        return Regex.Replace(text, @"E([+-])0*(\d)", "E$1$2");
    }

    /// <summary>A value as a logical, for IF, AND and the like: text other than TRUE and FALSE is #VALUE!.</summary>
    public static FormulaValue ToBoolean(FormulaValue value)
    {
        switch (value.Kind)
        {
            case FormulaValueKind.Boolean:
            case FormulaValueKind.Error:
                return value;
            case FormulaValueKind.Blank:
                return FormulaValue.False;
            case FormulaValueKind.Number:
                return FormulaValue.FromBoolean(value.Number != 0);
            case FormulaValueKind.Text:
                if (value.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    return FormulaValue.True;
                }

                if (value.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    return FormulaValue.False;
                }

                return FormulaValue.FromError(FormulaError.Value);
            default:
                return FormulaValue.FromError(FormulaError.Value);
        }
    }

    // Numbers, then text, then logicals.
    static int Rank(FormulaValue value) =>
        value.Kind switch
        {
            FormulaValueKind.Number => 0,
            FormulaValueKind.Text => 1,
            FormulaValueKind.Boolean => 2,
            _ => 3
        };

    /// <summary>
    /// Orders two values as Excel's comparison operators do. A blank takes the other's kind — 0, "" or
    /// FALSE — and two numbers within fifteen significant digits of each other are equal, which is why
    /// <c>=0.1+0.2=0.3</c> is TRUE.
    /// </summary>
    public static int Compare(FormulaValue left, FormulaValue right)
    {
        left = Fill(left, right);
        right = Fill(right, left);
        if (left.IsBlank &&
            right.IsBlank)
        {
            return 0;
        }

        var leftRank = Rank(left);
        var rightRank = Rank(right);
        if (leftRank != rightRank)
        {
            return leftRank.CompareTo(rightRank);
        }

        switch (left.Kind)
        {
            case FormulaValueKind.Number:
                return CompareNumbers(left.Number, right.Number);
            case FormulaValueKind.Text:
                return Math.Sign(string.Compare(left.Text, right.Text, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase));
            default:
                return left.Number.CompareTo(right.Number);
        }
    }

    public static int CompareNumbers(double left, double right)
    {
        if (left == right ||
            Math.Abs(left - right) <= Math.Max(Math.Abs(left), Math.Abs(right)) * 1e-15)
        {
            return 0;
        }

        return left.CompareTo(right);
    }

    static FormulaValue Fill(FormulaValue value, FormulaValue other)
    {
        if (!value.IsBlank)
        {
            return value;
        }

        return other.Kind switch
        {
            FormulaValueKind.Number => FormulaValue.FromNumber(0),
            FormulaValueKind.Text => FormulaValue.EmptyText,
            FormulaValueKind.Boolean => FormulaValue.False,
            _ => value
        };
    }
}
