/// <summary>
/// Character formatting an edit sets. A null member leaves that property as it is; a value sets it
/// outright, over whatever the run's style says.
/// </summary>
/// <param name="Font">The typeface, written for every script but East Asian and over the theme's.</param>
/// <param name="SizePoints">The size, in points; Word keeps it in half points.</param>
/// <param name="Color">Six hex digits, or <c>auto</c> for the automatic colour.</param>
/// <param name="Highlight">One of Word's highlight colours (<c>yellow</c>, <c>brightGreen</c> …), or <c>none</c>.</param>
/// <param name="Vertical">Superscript, subscript, or back on the baseline.</param>
readonly record struct RunFormat(
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    bool? Strike = null,
    string? Font = null,
    double? SizePoints = null,
    string? Color = null,
    string? Highlight = null,
    VerticalPositionValues? Vertical = null)
{
    public bool IsEmpty =>
        Bold == null &&
        Italic == null &&
        Underline == null &&
        Strike == null &&
        Font == null &&
        SizePoints == null &&
        Color == null &&
        Highlight == null &&
        Vertical == null;
}
