/// <summary>
/// A picture's blip effects as ImageSharp processors.
/// <see cref="ImageSharpRenderContext.GetProcessedImage"/> applies <see cref="Apply"/> as one more
/// step in its decode pipeline, while <see cref="Bake"/> writes the same effects into PNG bytes for
/// the PDF backend, which embeds images rather than filtering them.
/// </summary>
sealed class ImageSharpImageEffects : IImageEffects
{
    /// <summary>
    /// Applies the recolour and the transparency to a decoded image, in that order. Opacity is its
    /// own processor rather than the colour matrix's alpha row: the matrix runs on the pixel's
    /// colour channels and folding alpha in there would depend on whether the filter sees
    /// premultiplied data, which this does not have to know.
    /// </summary>
    public static void Apply(IImageProcessingContext context, ImageRecolor? recolor, double opacity)
    {
        if (recolor != null)
        {
            context.Filter(ColorMatrixFor(recolor));
        }

        // a:alphaModFix is a multiplier and may legally exceed 100%, which on an opaque source
        // means no change.
        if (opacity < 1)
        {
            context.Opacity((float) Math.Clamp(opacity, 0, 1));
        }
    }

    /// <summary>
    /// The recolour as a colour matrix. ImageSharp multiplies the pixel as a row vector, so its
    /// matrix is the transpose of the row-per-output-channel form <see cref="ImageRecolor.Rows"/>
    /// states: each recipe row becomes a COLUMN here. The last row is the constant, and the alpha
    /// column is the identity so a transparent PNG keeps its cut-out.
    /// </summary>
    static ColorMatrix ColorMatrixFor(ImageRecolor recolor)
    {
        var (red, green, blue) = recolor.Rows();
        return new(
            red.Red, green.Red, blue.Red, 0,
            red.Green, green.Green, blue.Green, 0,
            red.Blue, green.Blue, blue.Blue, 0,
            0, 0, 0, 1,
            red.Offset, green.Offset, blue.Offset, 0);
    }

    /// <inheritdoc/>
    public byte[]? Bake(byte[] data, ImageRecolor? recolor, double opacity)
    {
        try
        {
            using var image = Image.Load<Rgba32>(data);
            image.Mutate(_ => Apply(_, recolor, opacity));

            // PNG regardless of what came in: the effects can introduce colours a palette or a
            // greyscale source has no room for, and an alpha channel the source may not have had.
            using var buffer = new MemoryStream();
            image.SaveAsPng(buffer);
            return buffer.ToArray();
        }
        catch
        {
            // Undecodable bytes — the caller draws the original image, as it did before any
            // effects existed.
            return null;
        }
    }
}
