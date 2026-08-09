namespace Morph;

/// <summary>
/// The pixel half of <see cref="ImageCompressor"/>: reads an encoded image and writes one back.
/// Core <c>Morph</c> takes no dependency on an imaging library, so a codec has to come from
/// somewhere else — reference <c>Morph.ImageSharp</c> or <c>Morph.Skia</c> and one is discovered
/// automatically, or set <see cref="ImageCompressionOptions.Codec"/> to supply your own (a
/// mozjpeg or oxipng binding will beat both).
/// </summary>
/// <remarks>
/// Implementations must be safe to call from multiple threads and must never throw: an image that
/// cannot be handled is reported by returning <c>null</c>, which leaves the part untouched.
/// </remarks>
public abstract class ImageCodec
{
    /// <summary>
    /// Reads the intrinsic dimensions of <paramref name="data"/>, and whether any pixel in it is
    /// less than fully opaque.
    /// </summary>
    /// <returns>The probe, or null when the format cannot be read.</returns>
    public abstract ImageProbe? Probe(byte[] data);

    /// <summary>
    /// Decodes <paramref name="data"/> and re-encodes it as described by <paramref name="request"/>.
    /// </summary>
    /// <remarks>
    /// Decoding to pixels discards EXIF, XMP and ICC metadata, which is intentional — but EXIF
    /// orientation has to be <em>applied</em> before it is dropped, or photographs come back
    /// rotated.
    /// </remarks>
    /// <returns>The encoded bytes, or null when the image cannot be decoded or the requested
    /// content type cannot be written.</returns>
    public abstract byte[]? Encode(byte[] data, ImageEncodeRequest request);
}
