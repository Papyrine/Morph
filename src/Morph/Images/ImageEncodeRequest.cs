namespace Morph;

/// <summary>What <see cref="ImageCompressor"/> is asking an <see cref="ImageCodec"/> to produce.</summary>
/// <param name="Width">Target width in pixels. Equal to the source width when no resampling is wanted.</param>
/// <param name="Height">Target height in pixels.</param>
/// <param name="ContentType">
/// The media type to write — <c>image/png</c> or <c>image/jpeg</c>. Carried as a media type rather
/// than an enum because that is already how image formats travel through this library.
/// </param>
/// <param name="Quality">Encoder quality for lossy formats, 1-100. Ignored when writing PNG.</param>
public sealed record ImageEncodeRequest(int Width, int Height, string ContentType, int Quality);
