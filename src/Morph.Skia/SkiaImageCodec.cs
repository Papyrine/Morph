namespace Morph;

/// <summary>
/// The <see cref="ImageCodec"/> <see cref="ImageCompressor"/> uses when <c>Morph.Skia</c> is
/// deployed and <c>Morph.ImageSharp</c> is not.
/// </summary>
/// <remarks>
/// SkiaSharp's JPEG encoder is libjpeg-turbo and matches ImageSharp's, but its PNG encoder takes
/// no compression level or filter strategy, so re-encoded PNGs come out larger here than they do
/// there. That costs nothing beyond the missed saving: <see cref="ImageCompressor"/> keeps the
/// original bytes whenever the replacement is not smaller.
/// </remarks>
public sealed class SkiaImageCodec : ImageCodec
{
    /// <inheritdoc/>
    public override ImageProbe? Probe(byte[] data)
    {
        using var bitmap = Decode(data);
        if (bitmap is null)
        {
            return null;
        }

        return new(bitmap.Width, bitmap.Height, HasTranslucency(bitmap));
    }

    /// <inheritdoc/>
    public override byte[]? Encode(byte[] data, ImageEncodeRequest request)
    {
        using var decoded = Decode(data);
        if (decoded is null)
        {
            return null;
        }

        using var resized = decoded.Width == request.Width && decoded.Height == request.Height
            ? null
            : decoded.Resize(
                new SKImageInfo(request.Width, request.Height, decoded.ColorType, decoded.AlphaType),
                new SKSamplingOptions(SKCubicResampler.Mitchell));

        var bitmap = resized ?? decoded;

        var jpeg = ImageMediaTypes.Matches(request.ContentType, ImageMediaTypes.Jpeg);
        using var encoded = bitmap.Encode(
            jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png,
            jpeg ? request.Quality : 100);

        return encoded?.ToArray();
    }

    /// <summary>
    /// Decodes to pixels, applying the EXIF orientation the codec reports. Skia hands back the
    /// stored orientation rather than acting on it, so without this a photograph written by a
    /// phone comes back on its side.
    /// </summary>
    static SKBitmap? Decode(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        if (codec is null)
        {
            return null;
        }

        var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
        {
            return null;
        }

        var oriented = Orient(bitmap, codec.EncodedOrigin);
        if (!ReferenceEquals(oriented, bitmap))
        {
            bitmap.Dispose();
        }

        return oriented;
    }

    static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft)
        {
            return bitmap;
        }

        // the four origins that transpose the axes swap the dimensions with them
        var turned = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        var width = turned ? bitmap.Height : bitmap.Width;
        var height = turned ? bitmap.Width : bitmap.Height;

        var target = new SKBitmap(width, height, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(target);

        canvas.SetMatrix(OriginMatrix(origin, width, height));
        canvas.DrawBitmap(bitmap, 0, 0);

        return target;
    }

    /// <summary>
    /// Maps a source pixel to where the stated EXIF origin says it belongs, in a target already
    /// sized for the result. <c>RightTop</c>, for instance, sends <c>(x, y)</c> to
    /// <c>(width - y, x)</c> — a quarter turn clockwise.
    /// </summary>
    static SKMatrix OriginMatrix(SKEncodedOrigin origin, int width, int height) =>
        origin switch
        {
            SKEncodedOrigin.TopRight => new(-1, 0, width, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new(-1, 0, width, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new(1, 0, 0, 0, -1, height, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new(0, -1, width, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new(0, -1, width, -1, 0, height, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new(0, 1, 0, -1, 0, height, 0, 0, 1),
            _ => SKMatrix.Identity
        };

    static bool HasTranslucency(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque)
        {
            return false;
        }

        // read the alpha bytes in place rather than materializing an SKColor per pixel, which on a
        // multi-megapixel photograph is a large allocation for a question answered by one byte in
        // every four
        var alpha = bitmap.ColorType switch
        {
            SKColorType.Bgra8888 or SKColorType.Rgba8888 => 3,
            SKColorType.Alpha8 => 0,
            _ => -1
        };

        if (alpha < 0)
        {
            return bitmap.Pixels.Any(_ => _.Alpha != byte.MaxValue);
        }

        var stride = bitmap.BytesPerPixel;
        var pixels = bitmap.GetPixelSpan();

        for (var offset = alpha; offset < pixels.Length; offset += stride)
        {
            if (pixels[offset] != byte.MaxValue)
            {
                return true;
            }
        }

        return false;
    }
}
