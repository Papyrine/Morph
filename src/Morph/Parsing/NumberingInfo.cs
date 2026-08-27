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
    /// Marker colour from the numbering level's own <c>w:lvl/w:rPr/w:color</c> (hex, no #).
    /// Null = no level colour; the marker takes the paragraph's first-run colour instead
    /// (business-plans/12's SWOT bullets declare lavender/grey at the level).
    /// </summary>
    public string? ColorHex { get; init; }

    /// <summary>
    /// The indent position for the number/bullet in points (from left margin).
    /// </summary>
    public double IndentPoints { get; init; }

    /// <summary>
    /// The hanging indent (space between number and text) in points.
    /// </summary>
    public double HangingIndentPoints { get; init; }

    /// <summary>
    /// <c>w:lvlJc="right"</c>: the marker right-aligns — its RIGHT edge sits at the number
    /// position (left − hanging), the numeral growing leftward into the margin so the periods of
    /// I./VIII./XVIII. line up. Probed at 24pt (<c>_probe_numtab</c>): right edges landed within
    /// 3px of the position at two geometries while left-jc markers start there instead.
    /// </summary>
    public bool MarkerRightAligned { get; init; }

    /// <summary>
    /// The level's counter style (<c>w:numFmt</c>). Drives the ordered-list marker style in the
    /// HTML export so roman/letter lists don't collapse to decimal; <see cref="ListNumberFormat.Decimal"/>
    /// is the browser default and stays clean.
    /// </summary>
    public ListNumberFormat Format { get; init; } = ListNumberFormat.Decimal;
}