namespace Morph;

/// <summary>What <see cref="ImageCodec.Probe"/> can tell about an encoded image without re-encoding it.</summary>
/// <param name="Width">Intrinsic width in pixels, after any EXIF orientation is applied.</param>
/// <param name="Height">Intrinsic height in pixels, after any EXIF orientation is applied.</param>
/// <param name="HasTranslucency">
/// True when at least one pixel is less than fully opaque. Decides whether a PNG can be converted
/// to JPEG, so an implementation that cannot answer cheaply must answer <c>true</c> rather than
/// guess — a wrong <c>false</c> flattens transparency the document was relying on.
/// </param>
public sealed record ImageProbe(int Width, int Height, bool HasTranslucency);
