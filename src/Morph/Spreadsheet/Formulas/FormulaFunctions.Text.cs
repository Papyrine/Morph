static partial class FormulaFunctions
{
    static void RegisterText(Action<FormulaFunction> add)
    {
        add(new("CONCATENATE", 1, 255, Concatenate));
        add(new("CONCAT", 1, 255, Concat));
        add(new("TEXTJOIN", 3, 255, TextJoin));
        add(new("LEN", 1, 1, _ => TextMap(_, Length)));
        add(new("UPPER", 1, 1, _ => TextMap(_, Upper)));
        add(new("LOWER", 1, 1, _ => TextMap(_, Lower)));
        add(new("PROPER", 1, 1, _ => TextMap(_, Proper)));
        add(new("TRIM", 1, 1, _ => TextMap(_, Trim)));
        add(new("CLEAN", 1, 1, _ => TextMap(_, Clean)));
        add(new("LEFT", 1, 2, _ => Slice(_, SliceKind.Left)));
        add(new("RIGHT", 1, 2, _ => Slice(_, SliceKind.Right)));
        add(new("MID", 3, 3, _ => Slice(_, SliceKind.Middle)));
        add(new("TEXT", 2, 2, Text));
        add(new("VALUE", 1, 1, Value));
        add(new("FIND", 2, 3, _ => Find(_, false)));
        add(new("SEARCH", 2, 3, _ => Find(_, true)));
        add(new("SUBSTITUTE", 3, 4, Substitute));
        add(new("REPLACE", 4, 4, Replace));
        add(new("REPT", 2, 2, Repeat));
        add(new("EXACT", 2, 2, Exact));
        add(new("CHAR", 1, 1, Char));
        add(new("UNICHAR", 1, 1, Char));
        add(new("CODE", 1, 1, Code));
        add(new("UNICODE", 1, 1, Code));
        add(new("FIXED", 1, 3, Fixed));
        add(new("DOLLAR", 1, 2, Dollar));
    }

    static FormulaValue Concatenate(FunctionArguments args)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < args.Count; index++)
        {
            var text = args.Text(index);
            if (text.IsError)
            {
                return text;
            }

            builder.Append(text.Text);
        }

        return FormulaValue.FromText(builder.ToString());
    }

    // CONCAT, unlike CONCATENATE, reads a range whole.
    static FormulaValue Concat(FunctionArguments args)
    {
        var builder = new StringBuilder();
        foreach (var value in Values(args, 0))
        {
            var text = FormulaCoercion.ToText(value);
            if (text.IsError)
            {
                return text;
            }

            builder.Append(text.Text);
        }

        return FormulaValue.FromText(builder.ToString());
    }

    static FormulaValue TextJoin(FunctionArguments args)
    {
        var delimiter = args.Text(0);
        if (delimiter.IsError)
        {
            return delimiter;
        }

        var ignoreEmpty = args.Boolean(1);
        if (ignoreEmpty.IsError)
        {
            return ignoreEmpty;
        }

        var parts = new List<string>();
        foreach (var value in Values(args, 2))
        {
            var text = FormulaCoercion.ToText(value);
            if (text.IsError)
            {
                return text;
            }

            if (ignoreEmpty.Boolean &&
                text.Text.Length == 0)
            {
                continue;
            }

            parts.Add(text.Text);
        }

        return FormulaValue.FromText(string.Join(delimiter.Text, parts));
    }

    static FormulaValue TextMap(FunctionArguments args, Func<string, FormulaValue> map) =>
        FormulaEvaluator.Map(args.Evaluator.Operand(args.Raw(0)), _ =>
        {
            var text = FormulaCoercion.ToText(_);
            if (text.IsError)
            {
                return text;
            }

            return map(text.Text);
        });

    static FormulaValue Length(string text) => Number(text.Length);

    static FormulaValue Upper(string text) => FormulaValue.FromText(text.ToUpperInvariant());

    static FormulaValue Lower(string text) => FormulaValue.FromText(text.ToLowerInvariant());

    // A capital after anything that is not a letter, as Excel's PROPER: "o'neil" is "O'Neil".
    static FormulaValue Proper(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousLetter = false;
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                if (previousLetter)
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
                else
                {
                    builder.Append(char.ToUpperInvariant(ch));
                }

                previousLetter = true;
                continue;
            }

            builder.Append(ch);
            previousLetter = false;
        }

        return FormulaValue.FromText(builder.ToString());
    }

    // Spaces at the ends taken off, and every run of them inside made one.
    static FormulaValue Trim(string text) =>
        FormulaValue.FromText(Regex.Replace(text.Trim(' '), " {2,}", " "));

    static FormulaValue Clean(string text) =>
        FormulaValue.FromText(new([.. text.Where(_ => _ >= 32)]));

    enum SliceKind
    {
        Left,
        Right,
        Middle
    }

    static FormulaValue Slice(FunctionArguments args, SliceKind kind)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        var value = text.Text;
        if (kind == SliceKind.Middle)
        {
            var start = args.Number(1);
            if (start.IsError)
            {
                return start;
            }

            var count = args.Number(2);
            if (count.IsError)
            {
                return count;
            }

            var from = (int) Math.Truncate(start.Number);
            var length = (int) Math.Truncate(Math.Min(count.Number, int.MaxValue));
            if (from < 1 ||
                length < 0)
            {
                return Error(FormulaError.Value);
            }

            if (from > value.Length)
            {
                return FormulaValue.EmptyText;
            }

            return FormulaValue.FromText(value.Substring(from - 1, Math.Min(length, value.Length - from + 1)));
        }

        var characters = args.Number(1, 1);
        if (characters.IsError)
        {
            return characters;
        }

        var taken = Math.Truncate(characters.Number);
        if (taken < 0)
        {
            return Error(FormulaError.Value);
        }

        var length2 = (int) Math.Min(taken, value.Length);
        if (kind == SliceKind.Left)
        {
            return FormulaValue.FromText(value[..length2]);
        }

        return FormulaValue.FromText(value[^length2..]);
    }

    // TEXT formats with the number format codes a cell takes, so TEXT(A1, "mmm d") reads as the cell
    // formatted so would.
    static FormulaValue Text(FunctionArguments args)
    {
        var value = args.Scalar(0);
        if (value.IsError)
        {
            return value;
        }

        var format = args.Text(1);
        if (format.IsError)
        {
            return format;
        }

        var code = format.Text;
        if (value.IsText)
        {
            if (FormulaCoercion.ParseNumber(value.Text) is { } parsed)
            {
                value = Number(parsed);
            }
            else
            {
                return FormulaValue.FromText(NumberFormat.FormatText(value.Text, code).Text);
            }
        }

        var number = FormulaCoercion.ToNumber(value);
        if (number.IsError)
        {
            return number;
        }

        if (code.Length == 0)
        {
            return FormulaValue.EmptyText;
        }

        var culture = args.Evaluator.Book.Culture;
        return FormulaValue.FromText(NumberFormat.Format(number.Number, NumberFormat.Localize(code, culture)).Text);
    }

    static FormulaValue Value(FunctionArguments args)
    {
        var value = args.Scalar(0);
        if (value.IsError ||
            value.IsNumber)
        {
            return value;
        }

        if (value.IsBlank)
        {
            return Number(0);
        }

        if (value.IsText &&
            FormulaCoercion.ParseNumber(value.Text) is { } number)
        {
            return Number(number);
        }

        return Error(FormulaError.Value);
    }

    // FIND is exact and has no wildcards; SEARCH ignores case and has them.
    static FormulaValue Find(FunctionArguments args, bool search)
    {
        var wanted = args.Text(0);
        if (wanted.IsError)
        {
            return wanted;
        }

        var within = args.Text(1);
        if (within.IsError)
        {
            return within;
        }

        var start = args.Number(2, 1);
        if (start.IsError)
        {
            return start;
        }

        var from = (int) Math.Truncate(start.Number);
        if (from < 1 ||
            from > within.Text.Length + 1)
        {
            return Error(FormulaError.Value);
        }

        if (!search)
        {
            var found = within.Text.IndexOf(wanted.Text, from - 1, StringComparison.Ordinal);
            if (found < 0)
            {
                return Error(FormulaError.Value);
            }

            return Number(found + 1);
        }

        var pattern = FormulaCriteria.Wildcard(wanted.Text).ToString();
        // Not anchored: SEARCH finds the pattern anywhere.
        var loose = new Regex(pattern[1..^1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var match = loose.Match(within.Text, from - 1);
        if (!match.Success)
        {
            return Error(FormulaError.Value);
        }

        return Number(match.Index + 1);
    }

    static FormulaValue Substitute(FunctionArguments args)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        var old = args.Text(1);
        if (old.IsError)
        {
            return old;
        }

        var replacement = args.Text(2);
        if (replacement.IsError)
        {
            return replacement;
        }

        if (old.Text.Length == 0)
        {
            return text;
        }

        if (args.IsMissing(3))
        {
            return FormulaValue.FromText(text.Text.Replace(old.Text, replacement.Text, StringComparison.Ordinal));
        }

        var instance = args.Number(3);
        if (instance.IsError)
        {
            return instance;
        }

        var which = (int) Math.Truncate(instance.Number);
        if (which < 1)
        {
            return Error(FormulaError.Value);
        }

        var index = -1;
        for (var seen = 0; seen < which; seen++)
        {
            index = text.Text.IndexOf(old.Text, index + 1, StringComparison.Ordinal);
            if (index < 0)
            {
                return text;
            }
        }

        return FormulaValue.FromText(string.Concat(text.Text.AsSpan(0, index), replacement.Text, text.Text.AsSpan(index + old.Text.Length)));
    }

    static FormulaValue Replace(FunctionArguments args)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        var start = args.Number(1);
        if (start.IsError)
        {
            return start;
        }

        var count = args.Number(2);
        if (count.IsError)
        {
            return count;
        }

        var replacement = args.Text(3);
        if (replacement.IsError)
        {
            return replacement;
        }

        var from = (int) Math.Truncate(start.Number);
        var length = (int) Math.Truncate(count.Number);
        if (from < 1 ||
            length < 0)
        {
            return Error(FormulaError.Value);
        }

        var value = text.Text;
        var left = value[..Math.Min(from - 1, value.Length)];
        var rightStart = Math.Min(from - 1 + length, value.Length);
        return FormulaValue.FromText(left + replacement.Text + value[rightStart..]);
    }

    static FormulaValue Repeat(FunctionArguments args)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        var times = args.Number(1);
        if (times.IsError)
        {
            return times;
        }

        var count = (int) Math.Truncate(times.Number);
        if (count < 0 ||
            (long) count * text.Text.Length > 32767)
        {
            return Error(FormulaError.Value);
        }

        return FormulaValue.FromText(string.Concat(Enumerable.Repeat(text.Text, count)));
    }

    static FormulaValue Exact(FunctionArguments args)
    {
        var left = args.Text(0);
        if (left.IsError)
        {
            return left;
        }

        var right = args.Text(1);
        if (right.IsError)
        {
            return right;
        }

        return FormulaValue.FromBoolean(string.Equals(left.Text, right.Text, StringComparison.Ordinal));
    }

    static FormulaValue Char(FunctionArguments args)
    {
        var code = args.Number(0);
        if (code.IsError)
        {
            return code;
        }

        var value = (int) Math.Truncate(code.Number);
        if (value is < 1 or > 0x10FFFF)
        {
            return Error(FormulaError.Value);
        }

        return FormulaValue.FromText(char.ConvertFromUtf32(value));
    }

    static FormulaValue Code(FunctionArguments args)
    {
        var text = args.Text(0);
        if (text.IsError)
        {
            return text;
        }

        if (text.Text.Length == 0)
        {
            return Error(FormulaError.Value);
        }

        return Number(char.ConvertToUtf32(text.Text, 0));
    }

    static FormulaValue Fixed(FunctionArguments args)
    {
        var number = args.Number(0);
        if (number.IsError)
        {
            return number;
        }

        var decimals = args.Number(1, 2);
        if (decimals.IsError)
        {
            return decimals;
        }

        var noCommas = args.Boolean(2, false);
        if (noCommas.IsError)
        {
            return noCommas;
        }

        var digits = (int) Math.Truncate(decimals.Number);
        var rounded = Round(number.Number, digits, RoundingKind.Nearest);
        var format = noCommas.Boolean ? "0" : "#,##0";
        if (digits > 0)
        {
            format += "." + new string('0', digits);
        }

        return FormulaValue.FromText(rounded.ToString(format, CultureInfo.InvariantCulture));
    }

    static FormulaValue Dollar(FunctionArguments args)
    {
        var number = args.Number(0);
        if (number.IsError)
        {
            return number;
        }

        var decimals = args.Number(1, 2);
        if (decimals.IsError)
        {
            return decimals;
        }

        var digits = Math.Max(0, (int) Math.Truncate(decimals.Number));
        var places = digits > 0 ? "." + new string('0', digits) : string.Empty;
        var code = $"\"$\"#,##0{places};(\"$\"#,##0{places})";
        return FormulaValue.FromText(NumberFormat.Format(Round(number.Number, digits, RoundingKind.Nearest), code).Text);
    }
}
