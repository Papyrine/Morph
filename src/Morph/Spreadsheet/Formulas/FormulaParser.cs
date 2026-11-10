/// <summary>
/// Reads a formula's tokens into a tree, with Excel's precedence, which is not arithmetic's in two
/// places: negation binds tighter than <c>^</c> (<c>-2^2</c> is 4), and <c>^</c> associates to the left
/// (<c>2^3^2</c> is 64). From loosest to tightest: comparison, <c>&amp;</c>, <c>+ -</c>, <c>* /</c>,
/// <c>^</c>, <c>%</c>, negation, <c>:</c>.
/// </summary>
sealed class FormulaParser
{
    const int comparison = 1;
    const int concatenation = 2;
    const int additive = 3;
    const int multiplicative = 4;
    const int power = 5;
    const int percent = 6;
    const int prefix = 7;
    const int range = 8;

    readonly List<FormulaToken> tokens;
    int position;

    FormulaParser(List<FormulaToken> tokens) =>
        this.tokens = tokens;

    /// <summary>Reads a formula's text, without its leading <c>=</c>.</summary>
    public static FormulaNode Parse(string formula)
    {
        var parser = new FormulaParser(FormulaLexer.Tokenize(formula));
        if (parser.tokens.Count == 0)
        {
            throw new FormulaSyntaxException("The formula is empty.", 0);
        }

        var node = parser.Expression(0);
        if (parser.position < parser.tokens.Count)
        {
            var extra = parser.tokens[parser.position];
            throw new FormulaSyntaxException($"'{extra.Text}' is not expected here.", extra.Start);
        }

        return node;
    }

    FormulaToken? Peek()
    {
        if (position < tokens.Count)
        {
            return tokens[position];
        }

        return null;
    }

    FormulaToken Take()
    {
        if (position >= tokens.Count)
        {
            throw new FormulaSyntaxException("The formula ends too soon.", End);
        }

        return tokens[position++];
    }

    int End
    {
        get
        {
            if (tokens.Count == 0)
            {
                return 0;
            }

            return tokens[^1].Start + tokens[^1].Length;
        }
    }

    FormulaNode Expression(int least)
    {
        var left = Prefix();
        while (Peek() is {Kind: FormulaTokenKind.Operator} next)
        {
            if (next.Text == "%")
            {
                if (percent < least)
                {
                    break;
                }

                position++;
                left = new PercentNode(left);
                continue;
            }

            var precedence = Precedence(next.Text);
            if (precedence == 0 ||
                precedence < least)
            {
                break;
            }

            position++;
            // Left-associative throughout, ^ included.
            var right = Expression(precedence + 1);
            left = new BinaryNode(next.Text, left, right);
        }

        return left;
    }

    static int Precedence(string op) =>
        op switch
        {
            "=" or "<>" or "<" or ">" or "<=" or ">=" => comparison,
            "&" => concatenation,
            "+" or "-" => additive,
            "*" or "/" => multiplicative,
            "^" => power,
            ":" => range,
            _ => 0
        };

    FormulaNode Prefix()
    {
        var token = Take();
        switch (token.Kind)
        {
            case FormulaTokenKind.Operator when token.Text is "-" or "+" or "@":
                return new UnaryNode(token.Text, Expression(prefix));
            case FormulaTokenKind.Number:
                return new NumberNode(token.Number);
            case FormulaTokenKind.Text:
                return new TextNode(token.Text);
            case FormulaTokenKind.Boolean:
                return new BooleanNode(token.Number != 0);
            case FormulaTokenKind.Error:
                return new ErrorNode(token.Error);
            case FormulaTokenKind.Reference:
                return new ReferenceNode(token.Reference!);
            case FormulaTokenKind.Name:
                return new NameNode(token.Sheet, Unqualified(token));
            case FormulaTokenKind.Structured:
                return Structured(token);
            case FormulaTokenKind.Function:
                return Call(token);
            case FormulaTokenKind.Open:
                var inner = Expression(0);
                Expect(FormulaTokenKind.Close, "A closing bracket is missing.");
                return inner;
            case FormulaTokenKind.OpenBrace:
                return Array();
            default:
                throw new FormulaSyntaxException($"'{token.Text}' is not expected here.", token.Start);
        }
    }

    // A name token on a sheet keeps "Sheet!" in its text; the node holds the two apart.
    static string Unqualified(FormulaToken token)
    {
        var bang = token.Text.LastIndexOf('!');
        if (bang < 0)
        {
            return token.Text;
        }

        return token.Text[(bang + 1)..];
    }

    static FormulaNode Structured(FormulaToken token)
    {
        var table = token.Sheet ?? string.Empty;
        var brackets = token.Text[table.Length..];
        return new StructuredNode(table, StructuredSpecifier.Parse(brackets));
    }

    FormulaNode Call(FormulaToken token)
    {
        Expect(FormulaTokenKind.Open, "An opening bracket is missing.");
        var arguments = new List<FormulaNode>();
        if (Peek() is {Kind: FormulaTokenKind.Close})
        {
            position++;
            return new CallNode(FunctionName(token.Text), arguments);
        }

        while (true)
        {
            if (Peek() is {Kind: FormulaTokenKind.Comma or FormulaTokenKind.Close})
            {
                arguments.Add(MissingNode.Instance);
            }
            else
            {
                arguments.Add(Expression(0));
            }

            var next = Take();
            if (next.Kind == FormulaTokenKind.Close)
            {
                return new CallNode(FunctionName(token.Text), arguments);
            }

            if (next.Kind != FormulaTokenKind.Comma)
            {
                throw new FormulaSyntaxException($"'{next.Text}' is not expected here.", next.Start);
            }
        }
    }

    // Excel writes the functions newer than 2007 with a prefix in the file: _xlfn.IFS, _xlfn._xlws.SORT.
    internal static string FunctionName(string name)
    {
        var upper = name.ToUpperInvariant();
        foreach (var each in (string[]) ["_XLFN._XLWS.", "_XLFN.", "_XLWS.", "_XLUDF."])
        {
            if (upper.StartsWith(each, StringComparison.Ordinal))
            {
                upper = upper[each.Length..];
            }
        }

        return upper;
    }

    FormulaNode Array()
    {
        var rows = new List<List<FormulaValue>>();
        var row = new List<FormulaValue>();
        while (true)
        {
            row.Add(Constant());
            var next = Take();
            switch (next.Kind)
            {
                case FormulaTokenKind.Comma:
                    continue;
                case FormulaTokenKind.Semicolon:
                    rows.Add(row);
                    row = [];
                    continue;
                case FormulaTokenKind.CloseBrace:
                    rows.Add(row);
                    if (rows.Any(_ => _.Count != rows[0].Count))
                    {
                        throw new FormulaSyntaxException("An array's rows are not all as long.", next.Start);
                    }

                    var values = new FormulaValue[rows.Count, rows[0].Count];
                    for (var r = 0; r < rows.Count; r++)
                    {
                        for (var c = 0; c < rows[r].Count; c++)
                        {
                            values[r, c] = rows[r][c];
                        }
                    }

                    return new ArrayNode(values);
                default:
                    throw new FormulaSyntaxException($"'{next.Text}' is not expected in an array.", next.Start);
            }
        }
    }

    FormulaValue Constant()
    {
        var token = Take();
        var negate = false;
        if (token is {Kind: FormulaTokenKind.Operator, Text: "-" or "+"})
        {
            negate = token.Text == "-";
            token = Take();
        }

        switch (token.Kind)
        {
            case FormulaTokenKind.Number:
                var number = token.Number;
                if (negate)
                {
                    number = -number;
                }

                return FormulaValue.FromNumber(number);
            case FormulaTokenKind.Text when !negate:
                return FormulaValue.FromText(token.Text);
            case FormulaTokenKind.Boolean when !negate:
                return FormulaValue.FromBoolean(token.Number != 0);
            case FormulaTokenKind.Error when !negate:
                return FormulaValue.FromError(token.Error);
            default:
                throw new FormulaSyntaxException("An array may hold only numbers, text, logicals and errors.", token.Start);
        }
    }

    void Expect(FormulaTokenKind kind, string message)
    {
        if (Peek() is { } next &&
            next.Kind == kind)
        {
            position++;
            return;
        }

        throw new FormulaSyntaxException(message, Peek()?.Start ?? End);
    }
}
