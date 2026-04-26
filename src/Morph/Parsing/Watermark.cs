/// <summary>
/// A watermark extracted from a header part. Word emits two flavours:
/// <list type="bullet">
///   <item>Picture (<c>WordPictureWatermark</c>): a faded image, controlled by VML
///     <c>gain</c> and <c>blacklevel</c> luminance settings.</item>
///   <item>Text (<c>WordTextWatermark</c>): VML textpath rendered diagonally in light grey.</item>
/// </list>
/// Picture and text fields are mutually exclusive — at most one of <see cref="ImageData"/>
/// / <see cref="Text"/> is non-null.
/// </summary>
sealed record Watermark
{
    /// <summary>Image bytes for picture watermarks. Null for text watermarks.</summary>
    public byte[]? ImageData { get; init; }

    /// <summary>Image MIME type (e.g. "image/jpeg"). Null for text watermarks.</summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// VML <c>gain</c> normalised to 0.0–1.0 (Word stores it as fixed-point /65536).
    /// Output luminance = input × Gain + BlackLevel. Defaults to no contrast change.
    /// </summary>
    public double Gain { get; init; } = 1.0;

    /// <summary>
    /// VML <c>blacklevel</c> normalised to 0.0–1.0. Adds to every pixel after gain — Word's
    /// "washout" preset uses Gain=0.30 + BlackLevel=0.50, producing the faded grey effect.
    /// </summary>
    public double BlackLevel { get; init; }

    /// <summary>Text content for text watermarks. Null for picture watermarks.</summary>
    public string? Text { get; init; }

    /// <summary>Font family for text watermarks. Pulled from <c>v:textpath/@style</c>.</summary>
    public string FontFamily { get; init; } = "Calibri";

    /// <summary>Font size in points for text watermarks.</summary>
    public double FontSizePoints { get; init; } = 36;

    /// <summary>Bold flag for text watermarks.</summary>
    public bool Bold { get; init; }

    /// <summary>
    /// Text colour (6-digit hex) for text watermarks. Word's default is light grey
    /// (<c>BFBFBF</c>) so the watermark shows through but doesn't fight the body content.
    /// </summary>
    public string ColorHex { get; init; } = "BFBFBF";

    /// <summary>
    /// Rotation angle in degrees for text watermarks (counter-clockwise; -45° matches
    /// Word's default diagonal layout).
    /// </summary>
    public double RotationDegrees { get; init; } = -45;
}
