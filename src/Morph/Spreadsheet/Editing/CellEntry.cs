/// <summary>What is typed into a cell is read as.</summary>
enum CellEntryKind
{
    /// <summary>Nothing: the cell's content is cleared, its formatting kept.</summary>
    Clear,
    Number,
    Text,
    Boolean,
    Error,
    Formula
}

/// <summary>
/// What a cell is to hold, read from what was typed into it the way Excel reads typing: <c>=</c> starts a
/// formula, <c>'</c> keeps what follows as text, <c>TRUE</c> is a logical, <c>#N/A</c> an error, and
/// anything that reads as a number is one — with the format the typing implies, where the cell has
/// none of its own: <c>50%</c> is 0.5 shown as a percentage, <c>$1,200</c> is 1200 shown as
/// currency, <c>3/14/2026</c> is a date. A cell formatted as text (<c>@</c>) keeps what is typed as text.
/// </summary>
sealed record CellEntry(CellEntryKind Kind, double Number = 0, string Text = "", FormulaError Error = FormulaError.Value)
{
    /// <summary>The format typing a number implies (<c>0%</c>, <c>m/d/yyyy</c>), for a General cell to take.</summary>
    public string? ImpliedFormat { get; init; }

    static readonly Regex currency = new(@"^\(?-?\$\s?-?[\d,]*\.?\d*\)?$", RegexOptions.CultureInvariant);

    public static CellEntry Read(string? typed, string? formatCode = null)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return new(CellEntryKind.Clear);
        }

        if (typed.StartsWith('\''))
        {
            return new(CellEntryKind.Text, Text: typed[1..]);
        }

        if (formatCode == "@")
        {
            return new(CellEntryKind.Text, Text: typed);
        }

        // "=" alone, or "=" with only spaces, is text, as Excel takes it.
        if (typed.Length > 1 &&
            typed[0] is '=' or '+' &&
            !typed.AsSpan(1).IsWhiteSpace())
        {
            var formula = typed.AsSpan(1).Trim();
            // +5 typed is the number 5, as Excel reads it; +A1 is a formula.
            if (typed[0] == '+' &&
                FormulaCoercion.ParseNumber(formula) is { } signed)
            {
                return new(CellEntryKind.Number, signed);
            }

            return new(CellEntryKind.Formula, Text: formula.ToString());
        }

        var trimmed = typed.Trim();
        if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new(CellEntryKind.Boolean, 1);
        }

        if (trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new(CellEntryKind.Boolean);
        }

        if (FormulaErrors.TryParse(trimmed, out var error))
        {
            return new(CellEntryKind.Error, Error: error);
        }

        if (FormulaCoercion.ParseNumber(trimmed) is { } number)
        {
            return new(CellEntryKind.Number, number)
            {
                ImpliedFormat = Implied(trimmed)
            };
        }

        return new(CellEntryKind.Text, Text: typed);
    }

    // The format the shape of a typed number asks for; null for a plain number, which General shows.
    static string? Implied(string typed)
    {
        if (typed.EndsWith('%'))
        {
            if (typed.Contains('.'))
            {
                return "0.00%";
            }

            return "0%";
        }

        if (currency.IsMatch(typed))
        {
            if (typed.Contains('.'))
            {
                return "\"$\"#,##0.00";
            }

            return "\"$\"#,##0";
        }

        if (FormulaCoercion.ParseDate(typed) is { } serial &&
            !double.TryParse(typed.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            var hasTime = typed.Contains(':');
            var hasDate = serial >= 1 || typed.Contains('/') || typed.Contains('-');
            if (hasTime && hasDate)
            {
                return "m/d/yyyy h:mm";
            }

            if (hasTime)
            {
                if (typed.Contains("AM", StringComparison.OrdinalIgnoreCase) ||
                    typed.Contains("PM", StringComparison.OrdinalIgnoreCase))
                {
                    return "h:mm AM/PM";
                }

                return "h:mm";
            }

            return "m/d/yyyy";
        }

        if (typed.Contains(',') &&
            !typed.Contains('E') &&
            !typed.Contains('e'))
        {
            if (typed.Contains('.'))
            {
                return "#,##0.00";
            }

            return "#,##0";
        }

        if (typed.Contains('E') ||
            typed.Contains('e'))
        {
            return "0.00E+00";
        }

        return null;
    }
}
