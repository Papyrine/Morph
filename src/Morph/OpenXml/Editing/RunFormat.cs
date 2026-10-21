/// <summary>
/// Character formatting an edit sets. A null member leaves that property as it is; a value sets it
/// outright, over whatever the run's style says.
/// </summary>
/// <param name="Bold">Bold on or off.</param>
/// <param name="Italic">Italic on or off.</param>
/// <param name="Underline">A single underline, or none.</param>
/// <param name="Strike">Strikethrough on or off.</param>
/// <param name="Font">The typeface, written for every script but East Asian and over the theme's.</param>
/// <param name="SizePoints">The size, in points; Word keeps it in half points.</param>
/// <param name="Color">Six hex digits, or <c>auto</c> for the automatic colour.</param>
/// <param name="Highlight">One of Word's highlight colours (<c>yellow</c>, <c>green</c> …), or <c>none</c>.</param>
/// <param name="Vertical">Superscript, subscript, or back on the baseline.</param>
/// <param name="UnderlineStyle">One of Word's underlines by its w:u value (<c>single</c>, <c>double</c>, <c>thick</c>, <c>dotted</c>, <c>dash</c>, <c>wave</c>, <c>words</c> …), or <c>none</c>.</param>
/// <param name="DoubleStrike">Double strikethrough on or off; on takes strikethrough off, as Word's Font dialog does.</param>
/// <param name="SmallCaps">Small capitals on or off; on takes all capitals off.</param>
/// <param name="AllCaps">All capitals on or off; on takes small capitals off.</param>
/// <param name="Hidden">Hidden text on or off.</param>
readonly record struct RunFormat(
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    bool? Strike = null,
    string? Font = null,
    double? SizePoints = null,
    string? Color = null,
    string? Highlight = null,
    VerticalPositionValues? Vertical = null,
    string? UnderlineStyle = null,
    bool? DoubleStrike = null,
    bool? SmallCaps = null,
    bool? AllCaps = null,
    bool? Hidden = null)
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
        Vertical == null &&
        UnderlineStyle == null &&
        DoubleStrike == null &&
        SmallCaps == null &&
        AllCaps == null &&
        Hidden == null;
}
