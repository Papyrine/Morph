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

    /// <summary>Rotation in degrees (clockwise). 0 means no rotation.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Source-rectangle crop (a:srcRect). Null = no crop.</summary>
    public ImageCrop? Crop { get; init; }

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
}