/// <summary>
/// Splits a formula's text (without its leading <c>=</c>) into tokens, as the file spells formulas:
/// commas between arguments, a point for decimals, function names in English. The few places where
/// one spelling could be two things are settled the way Excel settles them — <c>A1</c> is a cell, not a
/// name; <c>1:3</c> is rows, not a number; <c>LOG10(</c> is a function — and anything left over is a
/// name, which evaluates to <c>#NAME?</c> if the workbook defines no such name.
/// </summary>
static class FormulaLexer
{
    static readonly Regex number = new(@"\G(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?", RegexOptions.CultureInvariant);
    static readonly Regex rows = new(@"\G\$?\d{1,7}:\$?\d{1,7}(?![\p{L}\p{N}_.\\?(!\[])", RegexOptions.CultureInvariant);
    static readonly Regex cells = new(@"\G\$?[A-Za-z]{1,3}\$?\d{1,7}(:\$?[A-Za-z]{1,3}\$?\d{1,7})?(?![\p{L}\p{N}_.\\?(!\[])", RegexOptions.CultureInvariant);
    static readonly Regex columns = new(@"\G\$?[A-Za-z]{1,3}:\$?[A-Za-z]{1,3}(?![\p{L}\p{N}_.\\?(!\[])", RegexOptions.CultureInvariant);
    static readonly Regex identifier = new(@"\G[\p{L}_\\][\p{L}\p{N}_.\\?]*", RegexOptions.CultureInvariant);
    static readonly Regex sheetReference = new(@"\G(\$?[A-Za-z]{1,3}\$?\d{1,7}(:\$?[A-Za-z]{1,3}\$?\d{1,7})?|\$?[A-Za-z]{1,3}:\$?[A-Za-z]{1,3}|\$?\d{1,7}:\$?\d{1,7}|#REF!)(?![\p{L}\p{N}_.\\?(\[])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static List<FormulaToken> Tokenize(string formula)
    {
        var tokens = new List<FormulaToken>();
        var index = 0;
        while (index < formula.Length)
        {
            var ch = formula[index];
            if (char.IsWhiteSpace(ch))
            {
                index++;
                continue;
            }

            var token = Next(formula, index, Previous(tokens));
            tokens.Add(token);
            index = token.Start + token.Length;
        }

        return tokens;
    }

    static FormulaToken? Previous(List<FormulaToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        return tokens[^1];
    }

    static FormulaToken Next(string text, int start, FormulaToken? previous)
    {
        var ch = text[start];
        switch (ch)
        {
            case '"':
                return Quoted(text, start);
            case '(':
                return new(FormulaTokenKind.Open, "(", start, 1);
            case ')':
                return new(FormulaTokenKind.Close, ")", start, 1);
            case ',':
                return new(FormulaTokenKind.Comma, ",", start, 1);
            case ';':
                return new(FormulaTokenKind.Semicolon, ";", start, 1);
            case '{':
                return new(FormulaTokenKind.OpenBrace, "{", start, 1);
            case '}':
                return new(FormulaTokenKind.CloseBrace, "}", start, 1);
            case '#':
                if (FormulaErrors.At(text, start) is var (error, length))
                {
                    return new(FormulaTokenKind.Error, text.Substring(start, length), start, length)
                    {
                        Error = error
                    };
                }

                throw new FormulaSyntaxException($"'{ch}' is not understood here.", start);
            case '\'':
                return QuotedSheet(text, start);
            case '[':
                // A structured reference with no table, inside the table it is in: [@Price].
                var end = Bracketed(text, start);
                return new(FormulaTokenKind.Structured, text[start..end], start, end - start);
            case '<' or '>':
                if (start + 1 < text.Length &&
                    (text[start + 1] == '=' ||
                     (ch == '<' && text[start + 1] == '>')))
                {
                    return new(FormulaTokenKind.Operator, text.Substring(start, 2), start, 2);
                }

                return new(FormulaTokenKind.Operator, ch.ToString(), start, 1);
            case '=' or '+' or '-' or '*' or '/' or '^' or '&' or '%' or ':' or '@':
                return new(FormulaTokenKind.Operator, ch.ToString(), start, 1);
        }

        if (char.IsAsciiDigit(ch) ||
            ch == '.')
        {
            // Rows (3:5) before a number, which would otherwise take the 3.
            if (AfterOperand(previous) &&
                rows.Match(text, start) is {Success: true} rowMatch)
            {
                return Reference(text, start, rowMatch.Length, null, start);
            }

            var match = number.Match(text, start);
            if (!match.Success)
            {
                throw new FormulaSyntaxException($"'{ch}' is not understood here.", start);
            }

            return new(FormulaTokenKind.Number, match.Value, start, match.Length)
            {
                Number = double.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture)
            };
        }

        if (ch == '$' ||
            char.IsLetter(ch) ||
            ch is '_' or '\\')
        {
            if (cells.Match(text, start) is {Success: true} cellMatch &&
                FormulaReference.Parse(null, cellMatch.Value) is not null)
            {
                return Reference(text, start, cellMatch.Length, null, start);
            }

            if (columns.Match(text, start) is {Success: true} columnMatch &&
                FormulaReference.Parse(null, columnMatch.Value) is not null)
            {
                return Reference(text, start, columnMatch.Length, null, start);
            }

            var name = identifier.Match(text, start);
            if (!name.Success)
            {
                throw new FormulaSyntaxException($"'{ch}' is not understood here.", start);
            }

            var after = start + name.Length;
            if (after < text.Length)
            {
                switch (text[after])
                {
                    case '!':
                        return Qualified(text, start, after + 1, name.Value);
                    case '(':
                        return new(FormulaTokenKind.Function, name.Value, start, name.Length);
                    case '[':
                        var close = Bracketed(text, after);
                        return new(FormulaTokenKind.Structured, text[start..close], start, close - start)
                        {
                            Sheet = name.Value
                        };
                }
            }

            if (name.Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                name.Value.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return new(FormulaTokenKind.Boolean, name.Value, start, name.Length)
                {
                    Number = name.Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? 1 : 0
                };
            }

            return new(FormulaTokenKind.Name, name.Value, start, name.Length);
        }

        throw new FormulaSyntaxException($"'{ch}' is not understood here.", start);
    }

    // Whether what comes next starts an operand, so "1:3" there is rows: after an operator, a bracket
    // or a comma, or at the start.
    static bool AfterOperand(FormulaToken? previous) =>
        previous == null ||
        previous.Kind is FormulaTokenKind.Operator or FormulaTokenKind.Open or FormulaTokenKind.Comma or FormulaTokenKind.Semicolon or FormulaTokenKind.Function;

    static FormulaToken Quoted(string text, int start)
    {
        var value = new StringBuilder();
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                if (index + 1 < text.Length &&
                    text[index + 1] == '"')
                {
                    value.Append('"');
                    index += 2;
                    continue;
                }

                return new(FormulaTokenKind.Text, value.ToString(), start, index + 1 - start);
            }

            value.Append(text[index]);
            index++;
        }

        throw new FormulaSyntaxException("A quotation mark is missing.", start);
    }

    // 'My sheet'!A1, where '' inside is one quote.
    static FormulaToken QuotedSheet(string text, int start)
    {
        var sheet = new StringBuilder();
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '\'')
            {
                if (index + 1 < text.Length &&
                    text[index + 1] == '\'')
                {
                    sheet.Append('\'');
                    index += 2;
                    continue;
                }

                if (index + 1 < text.Length &&
                    text[index + 1] == '!')
                {
                    return Qualified(text, start, index + 2, sheet.ToString());
                }

                break;
            }

            sheet.Append(text[index]);
            index++;
        }

        throw new FormulaSyntaxException("A sheet's name is not closed.", start);
    }

    // What follows "Sheet!": a reference, or a name scoped to that sheet.
    static FormulaToken Qualified(string text, int start, int after, string sheet)
    {
        if (sheetReference.Match(text, after) is {Success: true} match &&
            FormulaReference.Parse(sheet, match.Value) is not null)
        {
            return Reference(text, start, after + match.Length - start, sheet, after);
        }

        if (identifier.Match(text, after) is {Success: true} name)
        {
            return new(FormulaTokenKind.Name, name.Value, start, after + name.Length - start)
            {
                Sheet = sheet
            };
        }

        throw new FormulaSyntaxException("A reference is missing after the sheet's name.", after);
    }

    static FormulaToken Reference(string text, int start, int length, string? sheet, int referenceStart)
    {
        var part = text[referenceStart..(start + length)];
        return new(FormulaTokenKind.Reference, text.Substring(start, length), start, length)
        {
            Sheet = sheet,
            ReferenceStart = referenceStart,
            Reference = FormulaReference.Parse(sheet, part)
        };
    }

    // The end of a structured reference's brackets, nested, where ' escapes the character after it.
    static int Bracketed(string text, int start)
    {
        var depth = 0;
        var index = start;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '\'')
            {
                index += 2;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index + 1;
                }
            }

            index++;
        }

        throw new FormulaSyntaxException("A bracket is not closed.", start);
    }
}
