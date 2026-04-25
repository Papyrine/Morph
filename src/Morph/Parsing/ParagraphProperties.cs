/// <summary>
/// Paragraph-level properties.
/// </summary>
sealed record ParagraphProperties
{
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public double SpacingBeforePoints { get; init; }
    public double SpacingAfterPoints { get; init; } // OpenXML default when not specified

    /// <summary>
    /// Line spacing multiplier for Auto mode (1.0 = single, 1.5 = 1.5 lines, 2.0 = double).
    /// Only used when LineSpacingRule is Auto.
    /// </summary>
    public double LineSpacingMultiplier { get; init; } = 1.08;

    /// <summary>
    /// Fixed line spacing in points for Exactly/AtLeast modes.
    /// Only used when LineSpacingRule is Exactly or AtLeast.
    /// </summary>
    public double LineSpacingPoints { get; init; }

    /// <summary>
    /// The line spacing rule to apply.
    /// </summary>
    public LineSpacingRule LineSpacingRule { get; init; } = LineSpacingRule.Auto;

    public double FirstLineIndentPoints { get; init; }
    public double LeftIndentPoints { get; init; }
    public double RightIndentPoints { get; init; }

    /// <summary>
    /// Hanging indent in points. When positive, the first line is at LeftIndent
    /// and subsequent lines are further indented by this amount.
    /// OpenXML: w:ind/@w:hanging
    /// </summary>
    public double HangingIndentPoints { get; init; }

    /// <summary>
    /// When true, spacing before/after is collapsed between this paragraph and adjacent
    /// paragraphs that also have contextual spacing enabled.
    /// </summary>
    public bool ContextualSpacing { get; init; }

    /// <summary>
    /// When true, line numbers are suppressed for this paragraph.
    /// </summary>
    public bool SuppressLineNumbers { get; init; }

    /// <summary>
    /// When true, automatic hyphenation is suppressed for this paragraph.
    /// </summary>
    public bool SuppressAutoHyphens { get; init; }

    /// <summary>
    /// Numbering/bullet information for this paragraph. Null if not a list item.
    /// </summary>
    public NumberingInfo? Numbering { get; init; }

    /// <summary>
    /// When true, all lines of this paragraph must be kept on the same page.
    /// If the paragraph doesn't fit, move the entire paragraph to the next page.
    /// </summary>
    public bool KeepLines { get; init; }

    /// <summary>
    /// When true, this paragraph must be kept on the same page as the next paragraph.
    /// Prevents page breaks between this paragraph and the following one.
    /// </summary>
    public bool KeepNext { get; init; }

    /// <summary>
    /// When true, prevents widow/orphan lines at page breaks.
    /// A widow is the last line of a paragraph appearing alone at the top of a page.
    /// An orphan is the first line of a paragraph appearing alone at the bottom of a page.
    /// </summary>
    // Default is true per OpenXML spec
    public bool WidowControl { get; init; } = true;

    /// <summary>
    /// When true, forces a page break before this paragraph.
    /// </summary>
    public bool PageBreakBefore { get; init; }

    /// <summary>
    /// Font size in points for the paragraph mark (used for empty paragraphs).
    /// Null means use the default 12pt.
    /// </summary>
    public double? ParagraphMarkFontSizePoints { get; init; }

    /// <summary>
    /// Background/shading color for the paragraph (from w:shd element in w:pPr).
    /// </summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>
    /// The style ID of this paragraph (e.g., "Heading1", "Normal").
    /// Used for contextual spacing which only collapses spacing between paragraphs of the same style.
    /// </summary>
    public string? StyleId { get; init; }

    /// <summary>
    /// Paragraph borders (from w:pBdr element in w:pPr).
    /// </summary>
    public CellBorders? Borders { get; init; }

    /// <summary>
    /// Space between the top border and the paragraph text, in points.
    /// From w:pBdr/w:top/@w:space.
    /// </summary>
    public double BorderTopSpacePoints { get; init; }

    /// <summary>
    /// Space between the bottom border and the paragraph text, in points.
    /// From w:pBdr/w:bottom/@w:space.
    /// </summary>
    public double BorderBottomSpacePoints { get; init; }

    /// <summary>
    /// Space between the left border and the paragraph text, in points.
    /// From w:pBdr/w:left/@w:space.
    /// </summary>
    public double BorderLeftSpacePoints { get; init; }

    /// <summary>
    /// Space between the right border and the paragraph text, in points.
    /// From w:pBdr/w:right/@w:space.
    /// </summary>
    public double BorderRightSpacePoints { get; init; }

    /// <summary>
    /// Border drawn between consecutive paragraphs that share the same w:pBdr.
    /// From w:pBdr/w:between. When this and the matching neighbor's border match,
    /// the adjacent top/bottom edges collapse into a single between line.
    /// </summary>
    public BorderEdge BorderBetween { get; init; } = BorderEdge.None;

    /// <summary>
    /// Space around the between border, in points. From w:pBdr/w:between/@w:space.
    /// </summary>
    public double BorderBetweenSpacePoints { get; init; }

    /// <summary>
    /// Returns true if this paragraph and the next should collapse their adjacent
    /// top/bottom borders into a single between line.
    /// </summary>
    internal bool BordersCollapseWith(ParagraphProperties next) =>
        Borders is {HasAnyBorder: true} &&
        BorderBetween.IsVisible &&
        Borders == next.Borders &&
        BorderBetween == next.BorderBetween;

    /// <summary>
    /// Custom tab stops for this paragraph, sorted ascending by <see cref="TabStop.PositionPoints"/>.
    /// Excludes cleared stops. Inherited tabs from paragraph styles are merged in at parse time.
    /// </summary>
    public IReadOnlyList<TabStop> TabStops { get; init; } = [];

    /// <summary>
    /// Document-level default tab stop width in points (from w:defaultTabStop in settings.xml).
    /// Used to snap tab characters past the last explicit stop. Default 36 points (0.5 inch).
    /// </summary>
    public double DefaultTabStopPoints { get; init; } = 36;

    /// <summary>
    /// Drop cap position for this paragraph (w:framePr/w:dropCap). Default <see cref="DropCapPosition.None"/>.
    /// </summary>
    public DropCapPosition DropCap { get; init; } = DropCapPosition.None;

    /// <summary>
    /// Number of lines the drop cap spans (w:framePr/w:lines). Only relevant when <see cref="DropCap"/> is not None.
    /// </summary>
    public int DropCapLines { get; init; }
}