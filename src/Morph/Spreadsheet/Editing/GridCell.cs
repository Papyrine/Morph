/// <summary>
/// A cell as the grid shows it: the text it reads as (<see cref="Display"/>, its number format applied),
/// what the formula bar shows and editing starts from (<see cref="Input"/>: the formula, or the value as
/// it would be typed), and how it looks.
/// </summary>
sealed record GridCell(int Row, int Column, string Display, string Input, GridLook Look)
{
    /// <summary>Whether the value is a number (or a date), which the grid sets to the right unless told otherwise.</summary>
    public bool Numeric { get; init; }

    /// <summary>A logical or an error, which the grid centres unless told otherwise.</summary>
    public bool Centred { get; init; }

    public bool HasFormula { get; init; }

    /// <summary>The value, for the status bar's sum and average of what is selected.</summary>
    public double? Number { get; init; }

    /// <summary>A colour the number format's own section asks for, as <c>[Red]</c> does.</summary>
    public string? FormatColorHex { get; init; }
}
