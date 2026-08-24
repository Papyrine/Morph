class InlineStyle
{
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public string? Color { get; set; }
    public string? BackgroundColor { get; set; }
    public double? TextIndent { get; set; }
    public double? LineHeight { get; set; }
    public double? MarginLeftPoints { get; set; }
    public double? MarginRightPoints { get; set; }
    public double? MarginTopPoints { get; set; }
    public double? MarginBottomPoints { get; set; }

    /// <summary>The CSS border box, mapped onto the w:pBdr model Word's own HTML import
    /// produces; edges the style never declares are <see cref="BorderEdge.None"/>.</summary>
    public CellBorders? Borders { get; set; }

    // The CSS padding, as each bordered edge's w:space — whole points, like Word stores them.
    // Padding on a borderless edge is dropped, so these are only set where the edge exists.
    public double BorderTopSpacePoints { get; set; }
    public double BorderBottomSpacePoints { get; set; }
    public double BorderLeftSpacePoints { get; set; }
    public double BorderRightSpacePoints { get; set; }
}
