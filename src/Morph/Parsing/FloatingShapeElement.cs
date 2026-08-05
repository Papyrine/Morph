/// <summary>
/// Represents a floating shape (solid-fill or image-fill, typically used as background).
/// </summary>
sealed class FloatingShapeElement : DocumentElement
{
    /// <summary>
    /// The paragraph this float is anchored to — the ParagraphElement produced by the same
    /// ParseParagraph call, when that call produced one (a shape-only paragraph may not). A
    /// paragraph-relative vertical offset resolves against this paragraph's laid-out top; null falls
    /// back to the flow cursor at emission. Assigned after the paragraph is constructed, so settable.
    /// </summary>
    public ParagraphElement? AnchorParagraph { get; set; }

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

    /// <summary>Whether this shape is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Word z-order (<c>wp:anchor@relativeHeight</c>): floating drawings draw in
    /// ascending order of this value within a batch.</summary>
    public uint RelativeHeight { get; init; }

    /// <summary>Whether cell-anchored positioning resolves against the anchor cell's frame
    /// (<c>wp:anchor@layoutInCell</c>, default true).</summary>
    public bool LayoutInCell { get; init; } = true;

    /// <summary>
    /// Ordinal of the anchor paragraph within the owning cell's flow content (index into
    /// <c>TableCell.Content</c> counting paragraphs), recorded when the float is detached into
    /// <c>TableCell.Floats</c>. Paragraph-relative vertical anchors resolve against that
    /// paragraph's laid-out position; −1 when unknown (falls back to the cell top).
    /// </summary>
    public int CellAnchorParagraphIndex { get; init; } = -1;

    /// <summary>Fill color (hex RGB without #, e.g. "FF0000" for red). Null if using image fill.</summary>
    public string? FillColorHex { get; init; }

    /// <summary>Fill opacity, 0.0 (fully transparent) to 1.0 (fully opaque). Defaults to 1.0.</summary>
    public double FillAlpha { get; init; } = 1.0;

    /// <summary>Linear gradient fill. When set, takes precedence over <see cref="FillColorHex"/>.</summary>
    public GradientFill? Gradient { get; init; }

    /// <summary>Image data for image-filled shapes. Null if using solid color fill.</summary>
    public byte[]? ImageData { get; init; }

    /// <summary>Content type of the image (e.g., "image/jpeg"). Null if using solid color fill.</summary>
    public string? ImageContentType { get; init; }

    /// <summary>Stroke color for the shape outline (hex RGB, no #). Null when no outline is drawn.</summary>
    public string? LineColorHex { get; init; }

    /// <summary>Stroke width in points. Null when no outline is drawn.</summary>
    public double? LineWidthPoints { get; init; }

    /// <summary>Outline opacity from the line's solid fill (a:alpha), 0..1. Defaults to opaque.</summary>
    public double LineAlpha { get; init; } = 1;

    /// <summary>Preset geometry kind. Currently only differentiates rect vs ellipse for rendering.</summary>
    public PresetShape Preset { get; init; } = PresetShape.Rect;

    /// <summary>
    /// Custom geometry from <c>a:custGeom</c>, as one or more sub-path contours — each a
    /// flattened polyline of points in the unit square (0..1) of the shape's pre-rotation
    /// bounding box. When non-null, takes precedence over <see cref="Preset"/>. A shape with a
    /// fill (<see cref="FillColorHex"/>/<see cref="Gradient"/>) renders these as a filled path
    /// (each contour implicitly closed, nonzero winding); a stroke-only shape
    /// (<see cref="LineColorHex"/> with no fill) strokes them instead — e.g. the thin accent
    /// rules in the Agenda template, which are single two-point line segments. Keeping the
    /// contours separate preserves holes and disjoint pieces — e.g. the outlines in a leaf
    /// cluster — that collapsing every <c>moveTo</c> into one polyline would fuse together with
    /// spurious connector lines. Null for shapes without a custom geometry.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Subpaths { get; init; }

    /// <summary>Rotation in degrees clockwise around the bounding-box center. 0 = no rotation.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Whether the shape geometry is flipped horizontally around the bounding-box center.</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Whether the shape geometry is flipped vertically around the bounding-box center.</summary>
    public bool FlipVertical { get; init; }

    /// <summary>
    /// Width as a fraction (0..1) of <see cref="WidthRelativeFrom"/>, parsed from
    /// <c>wp14:sizeRelH/wp14:pctWidth</c>. Null when no percentage sizing is present.
    /// </summary>
    public double? WidthPercent { get; init; }

    /// <summary>Reference area for <see cref="WidthPercent"/>.</summary>
    public SizeRelativeFrom WidthRelativeFrom { get; init; } = SizeRelativeFrom.Margin;

    /// <summary>
    /// Height as a fraction (0..1) of <see cref="HeightRelativeFrom"/>, parsed from
    /// <c>wp14:sizeRelV/wp14:pctHeight</c>. Null when no percentage sizing is present.
    /// </summary>
    public double? HeightPercent { get; init; }

    /// <summary>Reference area for <see cref="HeightPercent"/>.</summary>
    public SizeRelativeFrom HeightRelativeFrom { get; init; } = SizeRelativeFrom.Margin;

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
    /// <summary>
    /// Stroke dash pattern as alternating on/off lengths in MULTIPLES of the line width
    /// (DrawingML's preset-dash convention), or null for a solid stroke. Only line connectors
    /// carry it today (labels/03's sysDot tear lines).
    /// </summary>
    public IReadOnlyList<double>? LineDashPattern { get; init; }

    public FloatingShapeElement WithAbsolutePosition(double x, double y) =>
        new()
        {
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalPositionPoints = x,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalPositionPoints = y,
            CellAnchorParagraphIndex = CellAnchorParagraphIndex,
            WidthPoints = WidthPoints,
            HeightPoints = HeightPoints,
            BehindText = BehindText,
            RelativeHeight = RelativeHeight,
            LayoutInCell = LayoutInCell,
            FillColorHex = FillColorHex,
            FillAlpha = FillAlpha,
            Gradient = Gradient,
            ImageData = ImageData,
            ImageContentType = ImageContentType,
            LineColorHex = LineColorHex,
            LineWidthPoints = LineWidthPoints,
            LineAlpha = LineAlpha,
            LineDashPattern = LineDashPattern,
            Preset = Preset,
            Subpaths = Subpaths,
            RotationDegrees = RotationDegrees,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            WidthPercent = WidthPercent,
            WidthRelativeFrom = WidthRelativeFrom,
            HeightPercent = HeightPercent,
            HeightRelativeFrom = HeightRelativeFrom,
        };

}

/// <summary>Preset geometry kinds we render. Anything outside this enum falls back to Rect.</summary>
enum PresetShape
{
    Rect,
    Ellipse

}
