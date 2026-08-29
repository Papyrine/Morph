/// <summary>
/// A picture's blip effects as Skia primitives. <see cref="SkiaPainter"/> draws through
/// <see cref="Paint"/> — a colour filter for the recolour and the paint's own alpha for the
/// transparency, so neither costs a decode — while <see cref="Bake"/> writes the same effects into
/// PNG bytes for the PDF backend, which embeds images rather than filtering them.
/// </summary>
sealed class SkiaImageEffects : IImageEffects
{
    /// <summary>
    /// The paint a picture carrying <paramref name="recolor"/> and <paramref name="opacity"/> draws
    /// with, or null when it carries neither and the untouched draw applies. The caller owns the
    /// result. Alpha rides on the paint's colour, whose RGB is ignored when drawing a bitmap.
    /// </summary>
    public static SKPaint? Paint(ImageRecolor? recolor, double opacity)
    {
        var filter = Filter(recolor);
        if (filter is null && opacity >= 1)
        {
            return null;
        }

        return new()
        {
            ColorFilter = filter,
            Color = SKColors.White.WithAlpha(Alpha(opacity))
        };
    }

    /// <summary>
    /// The colour filter for a recolour, or null when there is none to apply. Skia's colour matrix
    /// is 4 rows of 5 (one row per output channel: its red, green, blue and alpha weights, then a
    /// constant), applied to unpremultiplied 0-1 channels — the space
    /// <see cref="ImageRecolor.Rows"/> states its weights in. Alpha is the identity row, so a
    /// transparent PNG keeps its cut-out.
    /// </summary>
    static SKColorFilter? Filter(ImageRecolor? recolor)
    {
        if (recolor is null)
        {
            return null;
        }

        var (red, green, blue) = recolor.Rows();
        return SKColorFilter.CreateColorMatrix([
            red.Red, red.Green, red.Blue, 0, red.Offset,
            green.Red, green.Green, green.Blue, 0, green.Offset,
            blue.Red, blue.Green, blue.Blue, 0, blue.Offset,
            0, 0, 0, 1, 0
        ]);
    }

    /// <inheritdoc/>
    public byte[]? Bake(byte[] data, ImageRecolor? recolor, double opacity)
    {
        using var source = SKBitmap.Decode(data);
        if (source is null)
        {
            return null;
        }

        using var target = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(target))
        using (var paint = Paint(recolor, opacity))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0, paint);
        }

        // PNG regardless of what came in: the effects can introduce colours a palette or a
        // greyscale source has no room for, and an alpha channel the source may not have had.
        using var encoded = target.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    // a:alphaModFix is a multiplier and may legally exceed 100%, which on an opaque source means
    // no change; clamping here keeps the byte conversion in range.
    static byte Alpha(double opacity) =>
        (byte) Math.Round(Math.Clamp(opacity, 0, 1) * 255);
}
