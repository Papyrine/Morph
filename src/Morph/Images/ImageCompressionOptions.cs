namespace Morph;

/// <summary>Options for <see cref="ImageCompressor"/>.</summary>
/// <remarks>
/// Stripping metadata is not a knob. Decoding to pixels and re-encoding discards EXIF, XMP and
/// embedded ICC profiles as a matter of course, so any image that is rewritten loses them.
/// </remarks>
public sealed record ImageCompressionOptions
{
    /// <summary>
    /// Resolution to resample down to, in pixels per inch of the size the document draws the image
    /// at. Default is 150, matching <see cref="ImageExportOptions.Dpi"/> — an image is never left
    /// carrying more pixels than Morph itself would rasterize. Word's own Compress Pictures
    /// defaults to 220 for print. Set to null to disable resampling and only re-encode.
    /// </summary>
    public int? TargetDpi { get; init; } = 150;

    /// <summary>Encoder quality for JPEG, 1-100. Default is 80.</summary>
    public int JpegQuality { get; init; } = 80;

    /// <summary>
    /// Whether a PNG with no translucent pixel may be written out as a JPEG, which for
    /// photographic content is a large saving. Default is false: it is lossy, and it renames the
    /// package part and retargets every relationship that reaches it, which is more disruption
    /// than the other steps.
    /// </summary>
    public bool ConvertOpaquePngToJpeg { get; init; }

    /// <summary>
    /// The codec to encode with. When null (the default) one is discovered from
    /// <c>Morph.ImageSharp</c> or <c>Morph.Skia</c>, whichever is deployed.
    /// </summary>
    public ImageCodec? Codec { get; init; }

    /// <summary>Called for each image that could not be read, and so was left alone.</summary>
    public Action<ExportWarning>? OnWarning { get; init; }
}
