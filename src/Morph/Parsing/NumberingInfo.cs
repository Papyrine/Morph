/// <summary>
/// Numbering/bullet information for a paragraph.
/// </summary>
sealed record NumberingInfo
{
    /// <summary>
    /// The text to display before the paragraph content (e.g., "•", "1.", "A)").
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Zero-based multilevel-list level (<c>w:ilvl</c>). Null when the source carries no level
    /// concept (e.g. a synthetic document). The exporters use this to reconstruct list nesting;
    /// visual indentation is resolved separately into <see cref="ParagraphProperties.LeftIndentPoints"/>.
    /// </summary>
    public int? Level { get; init; }

    /// <summary>
    /// Font family for the numbering text. Null means use paragraph font.
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>
    /// The indent position for the number/bullet in points (from left margin).
    /// </summary>
    public double IndentPoints { get; init; }

    /// <summary>
    /// The hanging indent (space between number and text) in points.
    /// </summary>
    public double HangingIndentPoints { get; init; }
}