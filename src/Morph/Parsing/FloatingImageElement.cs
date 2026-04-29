/// <summary>
/// Represents a floating/anchored image positioned relative to page or paragraph.
/// </summary>
sealed class FloatingImageElement : DocumentElement
{
    public required byte[] ImageData { get; init; }
    public required double WidthPoints { get; init; }
    public required double HeightPoints { get; init; }
    public string? ContentType { get; init; }

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
}