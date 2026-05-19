/// <summary>
/// A run of text with consistent formatting. Can also represent an inline image.
/// </summary>
sealed class Run
{
    public required string Text { get; init; }
    public RunProperties Properties { get; init; } = new();

    /// <summary>Inline image data (when the run represents an inline image).</summary>
    public byte[]? InlineImageData { get; init; }

    /// <summary>Width of inline image in points.</summary>
    public double InlineImageWidthPoints { get; init; }

    /// <summary>Height of inline image in points.</summary>
    public double InlineImageHeightPoints { get; init; }

    /// <summary>Content type of inline image (e.g., "image/png", "image/svg+xml").</summary>
    public string? InlineImageContentType { get; init; }

    /// <summary>Raster bytes from the primary <c>a:blip r:embed</c>, retained when
    /// <see cref="InlineImageData"/> holds the SVG variant so backends without SVG
    /// support can use this fallback.</summary>
    public byte[]? InlineImageRasterFallbackData { get; init; }

    /// <summary>Content type for <see cref="InlineImageRasterFallbackData"/>.</summary>
    public string? InlineImageRasterFallbackContentType { get; init; }

    /// <summary>Inline image rotation in degrees (clockwise). 0 means no rotation.</summary>
    public double InlineImageRotationDegrees { get; init; }

    /// <summary>Inline image source-rectangle crop (a:srcRect). Null = no crop.</summary>
    public ImageCrop? InlineImageCrop { get; init; }

    /// <summary>
    /// True when this run represents a single w:tab character.
    /// When true, <see cref="Text"/> is "\t" and the renderer snaps the cursor to the next tab stop.
    /// </summary>
    public bool IsTab { get; init; }

    /// <summary>
    /// Inline shape group (<c>wpg:wgp</c>) attached to this run, when the run hosts a primitive
    /// drawing made of connector lines / rectangles instead of a picture. Mutually exclusive
    /// with <see cref="InlineImageData"/>.
    /// </summary>
    public InlineShapeGroup? InlineShapeGroup { get; init; }
}