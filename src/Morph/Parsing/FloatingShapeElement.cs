/// <summary>
/// Represents a floating shape (solid-fill or image-fill, typically used as background).
/// </summary>
sealed class FloatingShapeElement : DocumentElement
{
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
}