/// <summary>
/// A formula as it reads copied by some rows and columns: what a shared formula is in each cell it
/// covers, and what a formula pasted elsewhere becomes. Only the references' own text is rewritten,
/// so the rest stays as it was typed — its spaces, its capitals, its structured references (which name
/// columns, not cells, and do not move).
/// </summary>
static class FormulaShifter
{
    public static string Shift(string formula, int rows, int columns)
    {
        if (rows == 0 &&
            columns == 0)
        {
            return formula;
        }

        var tokens = FormulaLexer.Tokenize(formula);
        var builder = new StringBuilder(formula.Length + 8);
        var copied = 0;
        foreach (var token in tokens)
        {
            if (token is not
                {
                    Kind: FormulaTokenKind.Reference,
                    Reference: { } reference
                })
            {
                continue;
            }

            builder.Append(formula, copied, token.ReferenceStart - copied);
            builder.Append(reference.Shift(rows, columns).Write());
            copied = token.Start + token.Length;
        }

        builder.Append(formula, copied, formula.Length - copied);
        return builder.ToString();
    }
}
