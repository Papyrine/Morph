using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace Morph;

/// <summary>
/// The <see cref="ImageCodec"/> <see cref="ImageCompressor"/> uses when <c>Morph.ImageSharp</c> is
/// deployed. Preferred over the Skia codec because ImageSharp's PNG encoder exposes a compression
/// level and filter strategy, which is most of what makes a re-encoded PNG smaller than the one
/// it replaced.
/// </summary>
public sealed class ImageSharpImageCodec : ImageCodec
{
    /// <inheritdoc/>
    public override ImageProbe? Probe(byte[] data)
    {
        try
        {
            using var image = Image.Load<Rgba32>(data);

            // orientation is applied before the dimensions are reported, so a portrait photograph
            // stored as landscape-plus-EXIF is measured the way it is drawn
            image.Mutate(_ => _.AutoOrient());

            return new(image.Width, image.Height, HasTranslucency(image));
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public override byte[]? Encode(byte[] data, ImageEncodeRequest request)
    {
        try
        {
            using var image = Image.Load<Rgba32>(data);
            image.Mutate(_ => _.AutoOrient());

            if (image.Width != request.Width ||
                image.Height != request.Height)
            {
                image.Mutate(_ => _.Resize(request.Width, request.Height, KnownResamplers.Lanczos3));
            }

            // decoding to pixels already dropped EXIF and XMP; clearing the profiles stops the
            // encoder writing back the ones ImageSharp carries on the metadata
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.IptcProfile = null;

            using var buffer = new MemoryStream();
            image.Save(buffer, Encoder(request));
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    }

    static IImageEncoder Encoder(ImageEncodeRequest request) =>
        ImageMediaTypes.Matches(request.ContentType, ImageMediaTypes.Jpeg)
            ? new JpegEncoder
            {
                Quality = request.Quality
            }
            : new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.BestCompression,
                FilterMethod = PngFilterMethod.Adaptive
            };

    static bool HasTranslucency(Image<Rgba32> image)
    {
        var translucent = false;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && !translucent; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A != byte.MaxValue)
                    {
                        translucent = true;
                        break;
                    }
                }
            }
        });

        return translucent;
    }
}
