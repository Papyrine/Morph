/// <summary>
/// The numeric half of <see cref="NumberFormat"/>: turning a value and a section body such as
/// <c>_("$"* #,##0.00_)</c> into text.
///
/// The code is walked twice, because digits cannot be produced until the whole code is known — the
/// number of placeholders, the rounding, and the scaling all come from the code as a whole.
///
/// The subtle part is that integer placeholders are filled from the RIGHT, and a literal between
/// them splits them into separate runs that each take their own slice. That is what makes
/// <c>###\-####</c> render 5551234 as <c>555-1234</c> rather than dumping every digit at the first
/// placeholder, and it is the same mechanism behind the corpus's phone-number format.
/// </summary>
static partial class NumberFormat
{
    static string FormatNumber(double value, string body)
    {
        var shape = MeasureNumeric(body);

        var scaled = value * Math.Pow(100, shape.PercentCount) / Math.Pow(1000, shape.ScaleCommas);
        var negative = scaled < 0;
        var magnitude = Math.Abs(scaled);
        var rounded = Math.Round(magnitude, shape.MaxDecimals, MidpointRounding.AwayFromZero);

        var integerDigits = ((long) Math.Truncate(rounded)).ToString(CultureInfo.InvariantCulture);
        if (integerDigits == "0" && shape.MinIntegerDigits == 0)
        {
            // "#.##" shows .5 rather than 0.5 — a '#' before the point suppresses a lone zero.
            integerDigits = string.Empty;
        }
        else if (integerDigits.Length < shape.MinIntegerDigits)
        {
            integerDigits = integerDigits.PadLeft(shape.MinIntegerDigits, '0');
        }

        var fractionDigits = Fraction(rounded, shape);
        var text = EmitNumeric(body, shape, integerDigits, fractionDigits);

        // The sign goes in front of everything the code emitted, including a leading currency
        // symbol: Excel renders -1234.5 through "$"#,##0.00 as -$1,234.50, not $-1,234.50. A code
        // that writes its own sign or parentheses suppresses this entirely.
        return negative && !HasExplicitSign(body) ? "-" + text : text;
    }

    /// <summary>
    /// The fraction digits to emit: rounded to the most the code allows, then trailing zeros trimmed
    /// back to the fewest it demands, so <c>0.##</c> shows 0.5 while <c>0.00</c> shows 0.50.
    /// </summary>
    static string Fraction(double rounded, NumericShape shape)
    {
        if (shape.MaxDecimals == 0)
        {
            return string.Empty;
        }

        var digits = rounded.ToString("F" + shape.MaxDecimals, CultureInfo.InvariantCulture);
        var fraction = digits[(digits.IndexOf('.') + 1)..];

        var length = fraction.Length;
        while (length > shape.MinDecimals && fraction[length - 1] == '0')
        {
            length--;
        }

        return fraction[..length];
    }

    /// <summary>What a numeric code demands, gathered before any digit is produced.</summary>
    readonly record struct NumericShape(
        IReadOnlyList<int> IntegerRunLengths,
        int MinIntegerDigits,
        int MinDecimals,
        int MaxDecimals,
        bool Grouped,
        int ScaleCommas,
        int PercentCount);

    static NumericShape MeasureNumeric(string body)
    {
        var runs = new List<int>();
        int minInteger = 0, minDecimals = 0, maxDecimals = 0, percents = 0;
        var grouped = false;
        var afterPoint = false;
        var inRun = false;
        var lastPlaceholder = -1;
        var commasSinceLastPlaceholder = 0;

        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];

            if (ch == '"')
            {
                var close = body.IndexOf('"', i + 1);
                i = close < 0 ? body.Length : close;
                inRun = false;
                continue;
            }

            if (ch == '[')
            {
                var close = body.IndexOf(']', i);
                i = close < 0 ? body.Length : close;
                inRun = false;
                continue;
            }

            if ((ch == '\\' || ch == '_' || ch == '*') && i + 1 < body.Length)
            {
                i++;
                inRun = false;
                continue;
            }

            switch (ch)
            {
                case '#' or '?' or '0':
                    if (afterPoint)
                    {
                        maxDecimals++;
                        if (ch != '#')
                        {
                            minDecimals++;
                        }
                    }
                    else
                    {
                        if (inRun)
                        {
                            runs[^1]++;
                        }
                        else
                        {
                            runs.Add(1);
                            inRun = true;
                        }

                        if (ch == '0')
                        {
                            minInteger++;
                        }

                        lastPlaceholder = i;
                        commasSinceLastPlaceholder = 0;
                    }

                    break;

                case '.':
                    afterPoint = true;
                    inRun = false;
                    break;

                case ',' when !afterPoint:
                    // A comma still inside the placeholder run groups; one trailing the final
                    // placeholder scales by a thousand. Deciding by "is another placeholder coming"
                    // is what keeps #,##0 a grouped format rather than a scaled one.
                    if (lastPlaceholder >= 0 && HasPlaceholderAfter(body, i))
                    {
                        grouped = true;
                    }
                    else if (lastPlaceholder >= 0)
                    {
                        commasSinceLastPlaceholder++;
                    }

                    break;

                case '%':
                    percents++;
                    inRun = false;
                    break;

                default:
                    inRun = false;
                    break;
            }
        }

        return new(runs, minInteger, minDecimals, maxDecimals, grouped, commasSinceLastPlaceholder, percents);
    }

    static bool HasPlaceholderAfter(string body, int index)
    {
        for (var i = index + 1; i < body.Length; i++)
        {
            var ch = body[i];
            if (ch is '#' or '0' or '?')
            {
                return true;
            }

            if (ch == '.')
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits the digit string across the code's integer runs, filling from the right so the last
    /// run takes the least-significant digits. The FIRST run absorbs everything left over, which is
    /// what stops a value wider than the code from losing its leading digits.
    /// </summary>
    static string[] DistributeIntegerDigits(string digits, IReadOnlyList<int> runLengths, bool grouped)
    {
        if (runLengths.Count == 0)
        {
            return [];
        }

        if (runLengths.Count == 1)
        {
            return [grouped ? Group(digits) : digits];
        }

        var slices = new string[runLengths.Count];
        var remaining = digits;

        for (var run = runLengths.Count - 1; run > 0; run--)
        {
            var take = Math.Min(runLengths[run], remaining.Length);
            slices[run] = remaining[^take..];
            remaining = remaining[..^take];
        }

        slices[0] = grouped ? Group(remaining) : remaining;
        return slices;
    }

    static string EmitNumeric(string body, NumericShape shape, string integerDigits, string fractionDigits)
    {
        var slices = DistributeIntegerDigits(integerDigits, shape.IntegerRunLengths, shape.Grouped);
        var result = new StringBuilder();
        var runIndex = 0;
        var inRun = false;
        var fractionIndex = 0;
        var afterPoint = false;

        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            switch (ch)
            {
                case '"':
                    var close = body.IndexOf('"', i + 1);
                    if (close < 0)
                    {
                        i = body.Length;
                        break;
                    }

                    result.Append(body[(i + 1)..close]);
                    i = close;
                    inRun = false;
                    break;

                case '[':
                    var end = body.IndexOf(']', i);
                    if (end < 0)
                    {
                        i = body.Length;
                        break;
                    }

                    // A locale group carries the currency symbol: [$$-409] emits '$', [$€-2] emits '€'.
                    var inner = body[(i + 1)..end];
                    if (inner.StartsWith('$'))
                    {
                        var symbol = inner[1..];
                        var dash = symbol.IndexOf('-');
                        result.Append(dash >= 0 ? symbol[..dash] : symbol);
                    }

                    i = end;
                    inRun = false;
                    break;

                case '\\' when i + 1 < body.Length:
                    result.Append(body[++i]);
                    inRun = false;
                    break;

                // "_x" reserves the width of x. A single space is the standard approximation: a
                // proportional renderer cannot reserve a glyph's exact advance, and dropping it
                // misaligns accounting columns entirely.
                case '_' when i + 1 < body.Length:
                    result.Append(' ');
                    i++;
                    inRun = false;
                    break;

                // "*x" repeats x to fill the column width, which is not known at format time.
                case '*' when i + 1 < body.Length:
                    i++;
                    inRun = false;
                    break;

                case '#' or '0' or '?':
                    if (afterPoint)
                    {
                        if (fractionIndex < fractionDigits.Length)
                        {
                            result.Append(fractionDigits[fractionIndex++]);
                        }
                        else if (ch == '?')
                        {
                            result.Append(' ');
                        }
                    }
                    else if (!inRun)
                    {
                        // One slice per run, emitted whole at the run's first placeholder; the rest
                        // of the run's placeholders are its width and produce nothing more.
                        if (runIndex < slices.Length)
                        {
                            result.Append(slices[runIndex++]);
                        }

                        inRun = true;
                    }

                    break;

                case '.':
                    afterPoint = true;
                    inRun = false;
                    if (fractionDigits.Length > 0 || shape.MinDecimals > 0)
                    {
                        result.Append('.');
                    }

                    break;

                case ',':
                    inRun = false;
                    break;

                case '%':
                    result.Append('%');
                    inRun = false;
                    break;

                default:
                    result.Append(ch);
                    inRun = false;
                    break;
            }
        }

        return result.ToString();
    }

    // A code that writes its own sign or wraps in parentheses must not also get an automatic minus.
    static bool HasExplicitSign(string body)
    {
        var quoted = false;
        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            // An escaped or width-reserved character is a literal, not a sign: the "_)" that pads an
            // accounting positive must not be read as the parenthesis of a negative.
            if (!quoted && (ch == '\\' || ch == '_') && i + 1 < body.Length)
            {
                i++;
                continue;
            }

            if (!quoted && ch is '-' or '(')
            {
                return true;
            }
        }

        return false;
    }

    static string Group(string digits)
    {
        if (digits.Length <= 3)
        {
            return digits;
        }

        var result = new StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                result.Append(',');
            }

            result.Append(digits[i]);
        }

        return result.ToString();
    }
}
