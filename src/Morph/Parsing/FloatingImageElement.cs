/// <summary>
/// Represents a floating/anchored image positioned relative to page or paragraph.
/// </summary>
sealed class FloatingImageElement : DocumentElement
{
    public required byte[] ImageData { get; init; }
    public required double WidthPoints { get; init; }
    public required double HeightPoints { get; init; }
    public string? ContentType { get; init; }

    /// <summary>Alt text from <c>wp:docPr</c> / <c>pic:cNvPr</c> (@descr, else @title). Null when
    /// the source supplies none. The text exporters surface it as the image's alt / caption.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Raster fallback for backends that don't render <see cref="ContentType"/> = "image/svg+xml".
    /// OOXML stores both an SVG and a raster equivalent for high-DPI artwork; ImageSharp lacks
    /// SVG support so it falls back to this when present.
    /// </summary>
    public byte[]? RasterFallbackData { get; init; }

    public string? RasterFallbackContentType { get; init; }

    /// <summary>Horizontal position in points from the anchor reference.</summary>
    public double HorizontalPositionPoints { get; init; }

    /// <summary>Vertical position in points from the anchor reference.</summary>
    public double VerticalPositionPoints { get; init; }

    /// <summary>What the horizontal position is relative to.</summary>
    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Column;

    /// <summary>What the vertical position is relative to.</summary>
    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Paragraph;

    /// <summary>How text wraps around this image.</summary>
    public WrapType WrapType { get; init; } = WrapType.None;

    /// <summary>Which side(s) the wrapped text may flow on (wp @wrapText).</summary>
    public WrapTextSide WrapTextSide { get; init; } = WrapTextSide.BothSides;

    /// <summary>Wrap clearance (wp:wrapSquare @distL etc.) between the image edge and wrapped
    /// text, in points. Zero when the document doesn't specify one.</summary>
    public double WrapDistanceLeftPoints { get; init; }

    public double WrapDistanceTopPoints { get; init; }

    public double WrapDistanceRightPoints { get; init; }

    public double WrapDistanceBottomPoints { get; init; }

    /// <summary>Whether this image is behind text (vs in front).</summary>
    public bool BehindText { get; init; }

    /// <summary>Word z-order (<c>wp:anchor@relativeHeight</c>): floating drawings draw in
    /// ascending order of this value within a batch.</summary>
    public uint RelativeHeight { get; init; }

    /// <summary>Whether cell-anchored positioning resolves against the anchor cell's frame
    /// (<c>wp:anchor@layoutInCell</c>, default true).</summary>
    public bool LayoutInCell { get; init; } = true;

    /// <summary>Rotation in degrees (clockwise). 0 means no rotation.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Horizontal mirror (a:xfrm/@flipH).</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Vertical mirror (a:xfrm/@flipV).</summary>
    public bool FlipVertical { get; init; }

    /// <summary>Source-rectangle crop (a:srcRect). Null = no crop.</summary>
    public ImageCrop? Crop { get; init; }

    /// <summary>Colour-transform effect to apply before drawing (a:duotone / a:grayscl / a:lum).</summary>
    public BlipColorEffect ColorEffect { get; init; } = BlipColorEffect.None;

    /// <summary>The duotone ramp's dark end (theme-resolved hex); see <see cref="ImageElement.DuotoneColorHex"/>.</summary>
    public string? DuotoneColorHex { get; init; }

    /// <summary>
    /// Width as a fraction (0..1) of <see cref="WidthRelativeFrom"/>, parsed from
    /// <c>wp14:sizeRelH/wp14:pctWidth</c>. Null when no percentage sizing is present.
    /// When set, the renderer overrides <see cref="WidthPoints"/> with the resolved
    /// percentage of the reference area.
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
    /// <c>wp14:pctPosHOffset</c>. When set, overrides <see cref="HorizontalPositionPoints"/>;
    /// the renderer multiplies it by the page or content-area width based on
    /// <see cref="HorizontalAnchor"/>.
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
    public FloatingImageElement WithAbsolutePosition(double x, double y) =>
        new()
        {
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalPositionPoints = x,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalPositionPoints = y,
            ImageData = ImageData,
            WidthPoints = WidthPoints,
            HeightPoints = HeightPoints,
            ContentType = ContentType,
            Description = Description,
            RasterFallbackData = RasterFallbackData,
            RasterFallbackContentType = RasterFallbackContentType,
            WrapType = WrapType,
            WrapTextSide = WrapTextSide,
            WrapDistanceLeftPoints = WrapDistanceLeftPoints,
            WrapDistanceTopPoints = WrapDistanceTopPoints,
            WrapDistanceRightPoints = WrapDistanceRightPoints,
            WrapDistanceBottomPoints = WrapDistanceBottomPoints,
            BehindText = BehindText,
            RelativeHeight = RelativeHeight,
            LayoutInCell = LayoutInCell,
            RotationDegrees = RotationDegrees,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            Crop = Crop,
            ColorEffect = ColorEffect,
            DuotoneColorHex = DuotoneColorHex,
            WidthPercent = WidthPercent,
            WidthRelativeFrom = WidthRelativeFrom,
            HeightPercent = HeightPercent,
            HeightRelativeFrom = HeightRelativeFrom,
        };

}