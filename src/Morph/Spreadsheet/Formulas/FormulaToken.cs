enum FormulaTokenKind
{
    Number,
    Text,
    Boolean,
    Error,

    /// <summary>An A1 reference, perhaps on a named sheet: <see cref="FormulaToken.Reference"/>.</summary>
    Reference,

    /// <summary>A defined name, perhaps on a named sheet (<see cref="FormulaToken.Sheet"/>).</summary>
    Name,

    /// <summary>A function's name, its <c>(</c> following.</summary>
    Function,

    /// <summary>A table's structured reference: <c>Table1[[#This Row],[Price]]</c>, or <c>[@Price]</c>.</summary>
    Structured,

    Operator,
    Open,
    Close,
    Comma,
    Semicolon,
    OpenBrace,
    CloseBrace
}

/// <summary>
/// One token of a formula, with where it stands in the text: the parser reads the tokens, and
/// <see cref="FormulaShifter"/> rewrites the references among them in place, leaving the rest of the
/// text as it was typed.
/// </summary>
sealed record FormulaToken(FormulaTokenKind Kind, string Text, int Start, int Length)
{
    public double Number { get; init; }

    /// <summary>The sheet a reference or a name is qualified by, unquoted.</summary>
    public string? Sheet { get; init; }

    /// <summary>Where the reference itself starts, after any <c>Sheet!</c>.</summary>
    public int ReferenceStart { get; init; }

    public FormulaReference? Reference { get; init; }

    public FormulaError Error { get; init; }
}
