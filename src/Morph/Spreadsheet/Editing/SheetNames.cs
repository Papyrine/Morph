/// <summary>
/// A sheet's name as Excel has it (Excel 16 over COM, 2026-11-17): what it may be, how a formula spells
/// it before its <c>!</c>, and what a formula becomes when the sheet is renamed or deleted.
/// </summary>
static class SheetNames
{
    public const int MaxLength = 31;

    static readonly Regex plain = new(@"^[\p{L}_\\][\p{L}\p{N}_.]*$", RegexOptions.CultureInvariant);
    static readonly Regex cell = new(@"^[A-Za-z]{1,3}\d+$", RegexOptions.CultureInvariant);
    static readonly Regex r1c1 = new(@"^(?=.)([Rr]\d*)?([Cc]\d*)?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The name as a formula spells it before its <c>!</c>: in quotes, with any quote in it doubled, where
    /// it could be read as something else — a space or a sign in it, a digit first, a cell's reference
    /// (<c>'A1'</c>), an R1C1 one (<c>'C'</c>, <c>'R1C1'</c>) or a logical (<c>'TRUE'</c>).
    /// </summary>
    public static string Spelled(string name)
    {
        if (!plain.IsMatch(name) ||
            cell.IsMatch(name) ||
            r1c1.IsMatch(name) ||
            name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        return name;
    }

    /// <summary>
    /// Why a name can't be a sheet's, as Excel refuses it, or null when it can: blank, longer than 31
    /// characters, holding <c>\ / ? * [ ] :</c>, starting or ending with an apostrophe, History (which
    /// Excel keeps for itself), or another sheet's, whatever its capitals.
    /// </summary>
    public static string? Problem(string name, IEnumerable<string> others)
    {
        if (name.Length == 0)
        {
            return "A sheet's name can't be blank.";
        }

        if (name.Length > MaxLength)
        {
            return $"A sheet's name can be no longer than {MaxLength} characters.";
        }

        if (name.IndexOfAny(['\\', '/', '?', '*', '[', ']', ':']) >= 0)
        {
            return "A sheet's name can't hold \\ / ? * [ ] or :.";
        }

        if (name.StartsWith('\'') ||
            name.EndsWith('\''))
        {
            return "A sheet's name can't start or end with an apostrophe.";
        }

        if (name.Equals("History", StringComparison.OrdinalIgnoreCase))
        {
            return "History is a name Excel keeps for itself.";
        }

        if (others.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return "That name is already taken. Try a different one.";
        }

        return null;
    }

    /// <summary>The name a new sheet takes: Sheet1, Sheet2 and on, the first no sheet has, whatever its capitals.</summary>
    public static string Next(IEnumerable<string> names)
    {
        var taken = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var number = 1; ; number++)
        {
            var name = "Sheet" + number.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }

    /// <summary>
    /// A formula with every reference to the sheet, and every name qualified by it, spelling its new name.
    /// Text in quotes is text, and stays as it was.
    /// </summary>
    public static string Renamed(string formula, string old, string name) =>
        Rewrite(formula, token =>
        {
            if (token.Kind is not (FormulaTokenKind.Reference or FormulaTokenKind.Name) ||
                !string.Equals(token.Sheet, old, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return (token.Start, token.Text.LastIndexOf('!'), Spelled(name));
        });

    /// <summary>
    /// A formula with every reference to a deleted sheet, a name qualified by it, and a structured
    /// reference to one of its tables, made <c>#REF!</c> whole — the sheet's name with it, as Excel
    /// writes <c>Data!A1*2</c> as <c>#REF!*2</c> once Data is gone.
    /// </summary>
    public static string Deleted(string formula, string sheet, ISet<string> tables) =>
        Rewrite(formula, token =>
        {
            var gone = token.Kind switch
            {
                FormulaTokenKind.Reference or FormulaTokenKind.Name => string.Equals(token.Sheet, sheet, StringComparison.OrdinalIgnoreCase),
                FormulaTokenKind.Structured => token.Sheet != null && tables.Contains(token.Sheet),
                _ => false
            };
            if (!gone)
            {
                return null;
            }

            return (token.Start, token.Length, "#REF!");
        });

    // A formula with some of its tokens' text replaced: from where, how many characters, by what.
    static string Rewrite(string formula, Func<FormulaToken, (int Start, int Length, string Text)?> replace)
    {
        if (string.IsNullOrEmpty(formula))
        {
            return formula;
        }

        List<FormulaToken> tokens;
        try
        {
            tokens = FormulaLexer.Tokenize(formula);
        }
        catch (FormulaSyntaxException)
        {
            return formula;
        }

        StringBuilder? builder = null;
        var copied = 0;
        foreach (var token in tokens)
        {
            if (replace(token) is not var (start, length, text))
            {
                continue;
            }

            builder ??= new(formula.Length + 8);
            builder.Append(formula, copied, start - copied);
            builder.Append(text);
            copied = start + length;
        }

        if (builder == null)
        {
            return formula;
        }

        builder.Append(formula, copied, formula.Length - copied);
        return builder.ToString();
    }
}
