/// <summary>
/// The error values a formula can produce, as Excel spells them. They are values like any other — they
/// flow through arithmetic, are caught by <c>IFERROR</c> and are what the file caches for the cell
/// (<c>t="e"</c>) — rather than exceptions.
/// </summary>
enum FormulaError
{
    Null,
    DivideByZero,
    Value,
    Reference,
    Name,
    Number,
    NotAvailable,
    Spill,
    Calc
}

static class FormulaErrors
{
    static readonly (FormulaError Error, string Text)[] spellings =
    [
        (FormulaError.Null, "#NULL!"),
        (FormulaError.DivideByZero, "#DIV/0!"),
        (FormulaError.Value, "#VALUE!"),
        (FormulaError.Reference, "#REF!"),
        (FormulaError.Name, "#NAME?"),
        (FormulaError.Number, "#NUM!"),
        (FormulaError.NotAvailable, "#N/A"),
        (FormulaError.Spill, "#SPILL!"),
        (FormulaError.Calc, "#CALC!")
    ];

    public static string Text(FormulaError error)
    {
        foreach (var (each, text) in spellings)
        {
            if (each == error)
            {
                return text;
            }
        }

        return "#VALUE!";
    }

    // ERROR.TYPE's numbering, which is also the order Excel lists them in.
    public static int Number(FormulaError error) =>
        error switch
        {
            FormulaError.Null => 1,
            FormulaError.DivideByZero => 2,
            FormulaError.Value => 3,
            FormulaError.Reference => 4,
            FormulaError.Name => 5,
            FormulaError.Number => 6,
            FormulaError.NotAvailable => 7,
            FormulaError.Spill => 9,
            _ => 14
        };

    public static bool TryParse(string? text, out FormulaError error)
    {
        foreach (var (each, spelling) in spellings)
        {
            if (string.Equals(spelling, text, StringComparison.OrdinalIgnoreCase))
            {
                error = each;
                return true;
            }
        }

        error = FormulaError.Value;
        return false;
    }

    // The longest spelling that starts the text at an offset, for the lexer: "#N/A" is a prefix of
    // nothing else, but "#NUM!" and "#NULL!" share "#NU".
    public static (FormulaError Error, int Length)? At(string text, int offset)
    {
        foreach (var (each, spelling) in spellings)
        {
            if (string.Compare(text, offset, spelling, 0, spelling.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return (each, spelling.Length);
            }
        }

        return null;
    }
}
