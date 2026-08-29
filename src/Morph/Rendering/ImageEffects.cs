/// <summary>
/// Bakes a picture's blip effects — an <see cref="ImageRecolor"/> colour transform, a constant
/// <c>a:alphaModFix</c> transparency, or both — into encoded image bytes. Implemented by the raster
/// backends (<c>Morph.Skia</c> / <c>Morph.ImageSharp</c>) and discovered reflectively by the PDF
/// backend, which draws images by embedding their bytes and has no pixel pipeline to filter them
/// with — the same arrangement <see cref="IWordArtRasterizer"/> uses for WordArt.
/// </summary>
/// <remarks>
/// The raster painters do not go through this: each applies the effects with its own primitives at
/// draw time, which costs no decode and no re-encode.
/// </remarks>
interface IImageEffects
{
    /// <summary>
    /// Decodes <paramref name="data"/>, applies <paramref name="recolor"/> (when non-null) and
    /// <paramref name="opacity"/> (when below 1), and re-encodes as PNG. Returns <c>null</c> when
    /// the bytes could not be decoded, which leaves the caller drawing the original image.
    /// </summary>
    byte[]? Bake(byte[] data, ImageRecolor? recolor, double opacity);
}
