/// <summary>
/// Represents an inline image.
/// </summary>
sealed class ImageElement : DocumentElement
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

    /// <summary>The duotone ramp's dark end (theme-resolved hex). Word's Recolor gallery emits
    /// <c>a:duotone</c> as (darkColor, white): image luminance maps onto a dark→white ramp.
    /// Null when the effect isn't duotone or the colour couldn't be resolved (black assumed
    /// when the light end resolved — letters/02 pairs prstClr black with a tinted accent).</summary>
    public string? DuotoneColorHex { get; init; }

    /// <summary>The duotone ramp's light end (theme-resolved hex). Null = white, which keeps
    /// the Recolor-gallery (darkColor, white) form on the historical single-colour path.</summary>
    public string? DuotoneLightColorHex { get; init; }
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

    /// <summary>a:duotone — luminance mapped onto a two-colour ramp from
    /// <c>DuotoneColorHex</c> (dark, default black) to <c>DuotoneLightColorHex</c> (light,
    /// default white); greyscale fallback when neither colour resolved.</summary>
    Duotone,

    /// <summary>a:lum bright="N" with N &gt; 0 — washout / lighten effect.</summary>
    Washout
}
