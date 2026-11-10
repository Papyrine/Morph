/// <summary>What a value is: a cell's content, a formula's result, or something in between.</summary>
enum FormulaValueKind
{
    Blank,
    Number,
    Text,
    Boolean,
    Error,

    /// <summary>A rectangle of values: an array constant, or what an operation over a range made.</summary>
    Array,

    /// <summary>
    /// Cells, not yet read: what a reference evaluates to, so that a function can tell <c>SUM(A1:A9)</c>
    /// from <c>SUM(1, 2)</c> — it counts text and logicals typed as arguments but not ones in cells — and
    /// <c>ROW</c> or <c>ISBLANK</c> can see the cell rather than what is in it.
    /// </summary>
    Reference
}

/// <summary>
/// One value of a formula, as Excel has them: blank, a number, text, a logical, an error, an array of
/// values, or a reference to cells. Dates are numbers (serial days), as they are in the file.
/// </summary>
readonly struct FormulaValue
{
    FormulaValue(FormulaValueKind kind, double number = 0, string? text = null, FormulaError error = FormulaError.Value, FormulaValue[,]? array = null, FormulaArea area = default)
    {
        Kind = kind;
        Number = number;
        Text = text;
        Error = error;
        Array = array;
        Area = area;
    }

    public FormulaValueKind Kind { get; }

    /// <summary>The number, or for a logical 1 or 0.</summary>
    public double Number { get; }

    public string Text => field ?? string.Empty;

    public bool Boolean => Number != 0;

    public FormulaError Error { get; }

    public FormulaValue[,] Array => field ?? new FormulaValue[0, 0];

    public FormulaArea Area { get; }

    public bool IsBlank => Kind == FormulaValueKind.Blank;

    public bool IsError => Kind == FormulaValueKind.Error;

    public bool IsNumber => Kind == FormulaValueKind.Number;

    public bool IsText => Kind == FormulaValueKind.Text;

    public bool IsBoolean => Kind == FormulaValueKind.Boolean;

    public bool IsArray => Kind == FormulaValueKind.Array;

    public bool IsReference => Kind == FormulaValueKind.Reference;

    public static FormulaValue Blank => default;

    public static FormulaValue True { get; } = new(FormulaValueKind.Boolean, 1);

    public static FormulaValue False { get; } = new(FormulaValueKind.Boolean);

    public static FormulaValue EmptyText { get; } = new(FormulaValueKind.Text, text: string.Empty);

    /// <summary>A number; an infinity or a NaN, which Excel has no way to hold, is <c>#NUM!</c>.</summary>
    public static FormulaValue FromNumber(double number)
    {
        if (double.IsNaN(number) ||
            double.IsInfinity(number))
        {
            return FromError(FormulaError.Number);
        }

        // Excel has no negative zero.
        if (number == 0)
        {
            number = 0;
        }

        return new(FormulaValueKind.Number, number);
    }

    public static FormulaValue FromText(string text) =>
        new(FormulaValueKind.Text, text: text);

    public static FormulaValue FromBoolean(bool value)
    {
        if (value)
        {
            return True;
        }

        return False;
    }

    public static FormulaValue FromError(FormulaError error) =>
        new(FormulaValueKind.Error, error: error);

    public static FormulaValue FromArray(FormulaValue[,] values)
    {
        // A one-by-one array is the value in it, as Excel treats {5}.
        if (values.GetLength(0) == 1 &&
            values.GetLength(1) == 1)
        {
            return values[0, 0];
        }

        return new(FormulaValueKind.Array, array: values);
    }

    public static FormulaValue FromArea(FormulaArea area) =>
        new(FormulaValueKind.Reference, area: area);

    public override string ToString() =>
        Kind switch
        {
            FormulaValueKind.Blank => "(blank)",
            FormulaValueKind.Number => Number.ToString(CultureInfo.InvariantCulture),
            FormulaValueKind.Text => $"\"{Text}\"",
            FormulaValueKind.Boolean when Boolean => "TRUE",
            FormulaValueKind.Boolean => "FALSE",
            FormulaValueKind.Error => FormulaErrors.Text(Error),
            FormulaValueKind.Array => $"{{{Array.GetLength(0)}x{Array.GetLength(1)}}}",
            _ => Area.ToString()
        };
}
