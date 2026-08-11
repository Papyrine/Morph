/// <summary>
/// Images for the compression tests to chew on. They are deliberately noisy: a gradient encodes to
/// almost nothing as a PNG, which would leave every "did this get smaller" assertion measuring the
/// wrong thing. Noise is the case where PNG is a poor choice and re-encoding has real work to do.
/// Generation is deterministic and cached, since the same 3-megapixel fixture is asked for by
/// several tests.
/// </summary>
static class TestImages
{
    static readonly Dictionary<(int, int, bool, bool), byte[]> cache = [];
    static readonly Lock gate = new();

    public static byte[] Photograph(int width, int height, bool translucent = false, bool jpeg = false)
    {
        lock (gate)
        {
            if (cache.TryGetValue((width, height, translucent, jpeg), out var cached))
            {
                return cached;
            }

            var generated = Generate(width, height, translucent, jpeg);
            cache[(width, height, translucent, jpeg)] = generated;
            return generated;
        }
    }

    static byte[] Generate(int width, int height, bool translucent, bool jpeg)
    {
        using var image = new Image<Rgba32>(width, height);

        // a plain 32-bit LCG, so the fixture is the same on every machine and every run
        var seed = 0x5EEDu;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    seed = seed * 1664525 + 1013904223;
                    row[x] = new((byte) (seed >> 24), (byte) (seed >> 16), (byte) (seed >> 8), byte.MaxValue);
                }
            }
        });

        if (translucent)
        {
            image[0, 0] = new(0, 0, 0, 0);
        }

        using var buffer = new MemoryStream();
        if (jpeg)
        {
            image.SaveAsJpeg(
                buffer,
                new()
                {
                    Quality = 90
                });
        }
        else
        {
            image.SaveAsPng(buffer);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A JPEG whose pixels are landscape but whose EXIF orientation says it should be shown
    /// portrait — the shape a phone photograph arrives in.
    /// </summary>
    public static byte[] SideOn(int width, int height)
    {
        using var image = Image.Load<Rgba32>(Photograph(width, height, jpeg: true));

        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort) 6);
        image.Metadata.ExifProfile = profile;

        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer);
        return buffer.ToArray();
    }

    public static int Width(byte[] data) =>
        Image.Identify(data).Width;

    public static int Height(byte[] data) =>
        Image.Identify(data).Height;

    public static byte[] Svg { get; } = """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect width="64" height="64" fill="#4488cc"/></svg>"""u8.ToArray();
}
