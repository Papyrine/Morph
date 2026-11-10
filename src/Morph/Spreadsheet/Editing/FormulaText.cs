/// <summary>
/// A formula as the file stores it and as the reader types and reads it. Excel stores the functions
/// added since 2007 with a prefix — <c>_xlfn.IFS</c>, <c>_xlfn.XLOOKUP</c> — and an Excel that finds
/// one without it shows <c>#NAME?</c>; it shows them without. It writes a function's name and a cell's
/// reference in capitals, however they were typed.
/// </summary>
static class FormulaText
{
    // The functions Excel writes with _xlfn., among those the engine has.
    static readonly HashSet<string> prefixed =
    [
        "IFS", "IFNA", "SWITCH", "XOR", "CONCAT", "TEXTJOIN", "MAXIFS", "MINIFS", "XLOOKUP", "XMATCH",
        "RANK.EQ", "RANK.AVG", "STDEV.S", "STDEV.P", "VAR.S", "VAR.P", "MODE.SNGL", "CEILING.MATH",
        "FLOOR.MATH", "ISOWEEKNUM", "DAYS", "UNICHAR", "UNICODE", "ISFORMULA", "AGGREGATE", "NUMBERVALUE"
    ];

    /// <summary>The formula as the file is to hold it, from what was typed (without its <c>=</c>).</summary>
    public static string ForFile(string typed)
    {
        // Read first, so a formula that cannot be read is refused rather than saved — and one whose
        // function has too few or too many arguments, which Excel refuses as it is typed (=SUM()).
        Arguments(FormulaParser.Parse(typed));
        return Rewrite(typed, _ =>
        {
            if (prefixed.Contains(_))
            {
                return "_xlfn." + _;
            }

            return _;
        });
    }

    // Every call in the tree, walked with a stack rather than by recursion, as the engine walks it.
    static void Arguments(FormulaNode root)
    {
        var pending = new Stack<FormulaNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case CallNode call:
                    if (FormulaFunctions.TryGet(call.Name, out var function))
                    {
                        if (call.Arguments.Count < function.Least)
                        {
                            throw new FormulaSyntaxException($"{call.Name} needs more arguments than that.", 0);
                        }

                        if (call.Arguments.Count > function.Most)
                        {
                            throw new FormulaSyntaxException($"{call.Name} takes fewer arguments than that.", 0);
                        }
                    }

                    foreach (var argument in call.Arguments)
                    {
                        pending.Push(argument);
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
            }
        }
    }

    /// <summary>The formula as the reader sees it in the formula bar, with its <c>=</c>.</summary>
    public static string ForDisplay(string stored)
    {
        try
        {
            return "=" + Rewrite(stored, _ => _);
        }
        catch (FormulaSyntaxException)
        {
            return "=" + stored;
        }
    }

    static string Rewrite(string formula, Func<string, string> function)
    {
        var builder = new StringBuilder(formula.Length + 8);
        var copied = 0;
        foreach (var token in FormulaLexer.Tokenize(formula))
        {
            string? replacement = null;
            var from = token.Start;
            if (token.Kind == FormulaTokenKind.Function)
            {
                replacement = function(FormulaParser.FunctionName(token.Text));
            }
            else if (token is {Kind: FormulaTokenKind.Reference, Reference: { } reference})
            {
                from = token.ReferenceStart;
                replacement = reference.Write();
            }
            else if (token.Kind == FormulaTokenKind.Boolean)
            {
                replacement = token.Text.ToUpperInvariant();
            }

            if (replacement == null)
            {
                continue;
            }

            builder.Append(formula, copied, from - copied);
            builder.Append(replacement);
            copied = token.Start + token.Length;
        }

        builder.Append(formula, copied, formula.Length - copied);
        return builder.ToString();
    }
}
