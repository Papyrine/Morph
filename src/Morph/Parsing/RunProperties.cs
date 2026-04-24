/// <summary>
/// Run-level text properties.
/// </summary>
sealed record RunProperties
{
    public string FontFamily { get; init; } = DefaultFontSettings.DefaultFont;
    public double FontSizePoints { get; init; } = 11;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public bool AllCaps { get; init; }
    public string? ColorHex { get; init; } // null = black

    /// <summary>
    /// Background/shading color for text (from w:shd element).
    /// </summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>
    /// Extra spacing between characters in points (from w:spacing in rPr).
    /// Positive values expand, negative values condense.
    /// </summary>
    public double CharacterSpacingPoints { get; init; }

    /// <summary>
    /// Vertical alignment for subscript/superscript text.
    /// </summary>
    public VerticalRunAlignment VerticalAlignment { get; init; } = VerticalRunAlignment.Baseline;
}