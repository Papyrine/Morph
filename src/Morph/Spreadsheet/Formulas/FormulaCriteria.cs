/// <summary>
/// A criterion of COUNTIF, SUMIFS and the like: <c>5</c>, <c>"&gt;0"</c>, <c>"&lt;&gt;"</c>, <c>"apple*"</c>,
/// <c>"=" &amp; A1</c>. A comparison against a number matches only numbers (and text that reads as the
/// number, for equality); text matches without regard to case, with <c>*</c> and <c>?</c> as wildcards
/// and <c>~</c> escaping them; an empty criterion matches blank cells.
/// </summary>
static class FormulaCriteria
{
    public static Func<FormulaValue, bool> Parse(FormulaValue criterion)
    {
        switch (criterion.Kind)
        {
            case FormulaValueKind.Number:
                var number = criterion.Number;
                return _ => Equal(_, number);
            case FormulaValueKind.Boolean:
                var boolean = criterion.Boolean;
                return _ => _.IsBoolean && _.Boolean == boolean;
            case FormulaValueKind.Error:
                var error = criterion.Error;
                return _ => _.IsError && _.Error == error;
            case FormulaValueKind.Blank:
                return _ => _.IsBlank || (_.IsText && _.Text.Length == 0);
        }

        var text = criterion.Text;
        var op = "=";
        foreach (var each in (string[]) ["<>", ">=", "<=", "=", ">", "<"])
        {
            if (text.StartsWith(each, StringComparison.Ordinal))
            {
                op = each;
                text = text[each.Length..];
                break;
            }
        }

        if (text.Length == 0)
        {
            if (op == "<>")
            {
                return _ => !(_.IsBlank || (_.IsText && _.Text.Length == 0));
            }

            if (op == "=")
            {
                // "=" alone matches only cells with nothing in them; "" matches empty text as well.
                if (criterion.Text.Length == 0)
                {
                    return _ => _.IsBlank || (_.IsText && _.Text.Length == 0);
                }

                return _ => _.IsBlank;
            }
        }

        if (FormulaCoercion.ParseNumber(text) is { } operand &&
            !text.Contains('*') &&
            !text.Contains('?'))
        {
            return op switch
            {
                "=" => _ => Equal(_, operand),
                "<>" => _ => !Equal(_, operand),
                _ => _ => _.IsNumber && Holds(op, FormulaCoercion.CompareNumbers(_.Number, operand))
            };
        }

        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            var wanted = text.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                "=" => _ => _.IsBoolean && _.Boolean == wanted,
                "<>" => _ => !(_.IsBoolean && _.Boolean == wanted),
                _ => _ => _.IsBoolean && Holds(op, _.Number.CompareTo(wanted ? 1d : 0d))
            };
        }

        if (FormulaErrors.TryParse(text, out var errorText))
        {
            return op switch
            {
                "<>" => _ => !(_.IsError && _.Error == errorText),
                _ => _ => _.IsError && _.Error == errorText
            };
        }

        switch (op)
        {
            case "=":
                var pattern = Wildcard(text);
                return _ => _.IsText && pattern.IsMatch(_.Text);
            case "<>":
                var excluded = Wildcard(text);
                return _ => !(_.IsText && excluded.IsMatch(_.Text));
            default:
                return _ => _.IsText &&
                            Holds(op, Math.Sign(string.Compare(_.Text, text, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase)));
        }
    }

    static bool Equal(FormulaValue value, double number)
    {
        if (value.IsNumber)
        {
            return FormulaCoercion.CompareNumbers(value.Number, number) == 0;
        }

        return value.IsText &&
               FormulaCoercion.ParseNumber(value.Text) is { } parsed &&
               FormulaCoercion.CompareNumbers(parsed, number) == 0;
    }

    static bool Holds(string op, int order) =>
        op switch
        {
            ">" => order > 0,
            "<" => order < 0,
            ">=" => order >= 0,
            "<=" => order <= 0,
            "<>" => order != 0,
            _ => order == 0
        };

    /// <summary>Excel's wildcards as a regular expression: * any run, ? any one, ~ escaping either.</summary>
    public static Regex Wildcard(string text)
    {
        var pattern = new StringBuilder("^");
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '~' &&
                index + 1 < text.Length &&
                text[index + 1] is '*' or '?' or '~')
            {
                pattern.Append(Regex.Escape(text[index + 1].ToString()));
                index++;
                continue;
            }

            pattern.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(ch.ToString())
            });
        }

        pattern.Append('$');
        return new(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }
}
