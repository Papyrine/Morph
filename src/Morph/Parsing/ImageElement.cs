/// <summary>
/// Represents an inline image.
/// </summary>
sealed class ImageElement : DocumentElement
{
    public required byte[] ImageData { get; init; }
    public required double WidthPoints { get; init; }
    public required double HeightPoints { get; init; }
    public string? ContentType { get; init; }

    /// <summary>
    /// Raster fallback for backends that don't render <see cref="ContentType"/> = "image/svg+xml".
    /// OOXML stores both an SVG and a raster equivalent for high-DPI artwork; ImageSharp lacks
    /// SVG support so it falls back to this when present.
    /// </summary>
    public byte[]? RasterFallbackData { get; init; }

    public string? RasterFallbackContentType { get; init; }

    /// <summary>Rotation in degrees (clockwise). 0 means no rotation.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Source-rectangle crop (a:srcRect). Null = no crop.</summary>
    public ImageCrop? Crop { get; init; }

    /// <summary>Colour-transform effect to apply before drawing (a:duotone / a:grayscl / a:lum).</summary>
    public BlipColorEffect ColorEffect { get; init; } = BlipColorEffect.None;
}

/// <summary>
/// Image colour transforms emitted by Word's "Recolor" gallery. We model the most common
/// presets; other transforms (clrChange, biLevel, tint, alphaModFix) fall back to None.
/// </summary>
enum BlipColorEffect
{
    None,

    /// <summary>a:grayscl — straight luminance preservation, all colour stripped.</summary>
    Grayscale,

    /// <summary>a:duotone — image mapped to two-tone gradient. We render as Grayscale fallback.</summary>
    Duotone,

    /// <summary>a:lum bright="N" with N &gt; 0 — washout / lighten effect.</summary>
    Washout
}
