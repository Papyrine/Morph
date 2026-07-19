/// <summary>
/// Represents a floating/positioned WordArt text element with special formatting.
/// Unlike WordArtElement (inline), this is positioned at absolute coordinates and doesn't consume flow space.
/// </summary>
sealed class FloatingWordArtElement : DocumentElement, IWordArtVisual
{
    /// <summary>The text content of the WordArt.</summary>
    public required string Text { get; init; }

    /// <summary>Width in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Horizontal position in points from the anchor reference.</summary>
    public double HorizontalPositionPoints { get; init; }

    /// <summary>Vertical position in points from the anchor reference.</summary>
    public double VerticalPositionPoints { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to.</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>Whether this WordArt is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Word z-order (<c>wp:anchor@relativeHeight</c>): floating drawings draw in
    /// ascending order of this value within a batch.</summary>
    public uint RelativeHeight { get; init; }

    /// <summary>Whether cell-anchored positioning resolves against the anchor cell's frame
    /// (<c>wp:anchor@layoutInCell</c>, default true).</summary>
    public bool LayoutInCell { get; init; } = true;

    /// <summary>Font family for the text.</summary>
    public string FontFamily { get; init; } = DefaultFontSettings.DefaultFont;

    /// <summary>Font size in points.</summary>
    public double FontSizePoints { get; init; } = 36;

    /// <summary>Whether the text is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether the text is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Text fill color (hex). Null for default black.</summary>
    public string? FillColorHex { get; init; }

    /// <summary>Text outline color (hex). Null for no outline.</summary>
    public string? OutlineColorHex { get; init; }

    /// <summary>Text outline width in points.</summary>
    public double OutlineWidthPoints { get; init; }

    /// <summary>Whether the text has a shadow effect.</summary>
    public bool HasShadow { get; init; }

    /// <summary>Whether the text has a reflection effect.</summary>
    public bool HasReflection { get; init; }

    /// <summary>Whether the text has a glow effect.</summary>
    public bool HasGlow { get; init; }

    /// <summary>The preset text transform/warp type.</summary>
    public WordArtTransform Transform { get; init; } = WordArtTransform.None;

    /// <summary>
    /// Horizontal position as a fraction (0..1) of the anchor reference frame, parsed from
    /// <c>wp14:pctPosHOffset</c>. When set, overrides <see cref="HorizontalPositionPoints"/>.
    /// </summary>
    public double? HorizontalPositionPercent { get; init; }

    /// <summary>
    /// Vertical position as a fraction (0..1) of the anchor reference frame, parsed from
    /// <c>wp14:pctPosVOffset</c>. When set, overrides <see cref="VerticalPositionPoints"/>.
    /// </summary>
    public double? VerticalPositionPercent { get; init; }


    /// <summary>
    /// Copy re-anchored at an absolute page position — used by the table renderer to place
    /// a cell-attached float against its cell's resolved rectangle (layoutInCell semantics).
    /// Percent positioning is cleared (the absolute coordinates already resolved it); every
    /// other member is preserved.
    /// </summary>
    public FloatingWordArtElement WithAbsolutePosition(double x, double y) =>
        new()
        {
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalPositionPoints = x,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalPositionPoints = y,
            Text = Text,
            WidthPoints = WidthPoints,
            HeightPoints = HeightPoints,
            BehindText = BehindText,
            RelativeHeight = RelativeHeight,
            LayoutInCell = LayoutInCell,
            FontFamily = FontFamily,
            FontSizePoints = FontSizePoints,
            Bold = Bold,
            Italic = Italic,
            FillColorHex = FillColorHex,
            OutlineColorHex = OutlineColorHex,
            OutlineWidthPoints = OutlineWidthPoints,
            HasShadow = HasShadow,
            HasReflection = HasReflection,
            HasGlow = HasGlow,
            Transform = Transform,
        };

}