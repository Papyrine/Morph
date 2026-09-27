/// <summary>
/// A paragraph's lines on one page, and the box its text is set in, in points from the page's top
/// left. <see cref="Paragraph"/> is the ordinal of its <c>w:p</c>; a paragraph that runs over a page
/// break has a block on each page.
/// </summary>
sealed record EditBlock(int Paragraph, int Page, ParagraphElement Element, IReadOnlyList<PlacedLine> Lines)
{
    /// <summary>Where a line other than the first starts.</summary>
    public required float Left { get; init; }

    /// <summary>Where a line has to have ended.</summary>
    public required float Right { get; init; }

    /// <summary>The top of the first line's box.</summary>
    public required float Top { get; init; }

    /// <summary>The bottom of the last line's box.</summary>
    public required float Bottom { get; init; }

    /// <summary>
    /// How far in from <see cref="Left"/> the paragraph's first line starts — or out, for a hanging
    /// indent. Zero for a block that holds the rest of a paragraph begun on an earlier page.
    /// </summary>
    public required float Indent { get; init; }

    /// <summary>What is behind the text: the paragraph's shading, its cell's, or the page's. Null for plain paper.</summary>
    public string? Fill { get; init; }

    public float Width => Right - Left;

    public float Height => Bottom - Top;
}
