/// <summary>
/// Represents a floating/positioned text box (shape with text content).
/// </summary>
sealed class FloatingTextBoxElement : DocumentElement
{
    /// <summary>Text content of the text box.</summary>
    public required IReadOnlyList<DocumentElement> Content { get; init; }

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

    /// <summary>How text wraps around this text box.</summary>
    public WrapType WrapType { get; init; } = WrapType.None;

    /// <summary>Whether this text box is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Word z-order (<c>wp:anchor@relativeHeight</c>): floating drawings draw in
    /// ascending order of this value within a batch.</summary>
    public uint RelativeHeight { get; init; }

    /// <summary>Whether cell-anchored positioning resolves against the anchor cell's frame
    /// (<c>wp:anchor@layoutInCell</c>, default true).</summary>
    public bool LayoutInCell { get; init; } = true;

    /// <summary>Background color (hex). Null for transparent.</summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>Outline color as 6-digit hex (a:ln solid fill). Null = no outline.</summary>
    public string? LineColorHex { get; init; }

    /// <summary>Outline width in points. 0 = no outline.</summary>
    public double LineWidthPoints { get; init; }

    /// <summary>Outline opacity from the line's solid fill (a:alpha), 0..1. Defaults to opaque.</summary>
    public double LineAlpha { get; init; } = 1;

    /// <summary>
    /// Outline contours when the shape is richer than a rectangle — an <c>a:custGeom</c> or a
    /// built preset (roundRect ticket outlines, plaque frames, …) — normalized to the unit
    /// square exactly like <see cref="FloatingShapeElement.Subpaths"/>. The fill and outline
    /// draw these; the text content still lays out in the rectangular box.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Subpaths { get; init; }

    /// <summary>Rotation in degrees (clockwise). 0 = no rotation.</summary>
    public double RotationDegrees { get; init; }

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
    public FloatingTextBoxElement WithAbsolutePosition(double x, double y) =>
        new()
        {
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalPositionPoints = x,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalPositionPoints = y,
            Content = Content,
            WidthPoints = WidthPoints,
            HeightPoints = HeightPoints,
            WrapType = WrapType,
            BehindText = BehindText,
            RelativeHeight = RelativeHeight,
            LayoutInCell = LayoutInCell,
            BackgroundColorHex = BackgroundColorHex,
            LineColorHex = LineColorHex,
            LineWidthPoints = LineWidthPoints,
            LineAlpha = LineAlpha,
            Subpaths = Subpaths,
            RotationDegrees = RotationDegrees,
        };

}