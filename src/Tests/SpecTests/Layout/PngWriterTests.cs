/// <summary>
/// A page is written as the smallest 8-bit PNG its pixels allow (<see cref="PngWriter"/>), and every form
/// decodes to exactly the pixels it was given.
/// </summary>
public class PngWriterTests
{
    const int width = 37;
    const int height = 23;

    // The colour type a PNG declares (IHDR byte 9, the file's 26th byte).
    static byte ColourType(byte[] png) => png[25];

    static byte[] Pixels(Func<int, int, (byte R, byte G, byte B, byte A)> colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = colour(x, y);
                var offset = (y * width + x) * 4;
                pixels[offset] = r;
                pixels[offset + 1] = g;
                pixels[offset + 2] = b;
                pixels[offset + 3] = a;
            }
        }

        return pixels;
    }

    static byte[] Encode(byte[] pixels)
    {
        using var output = new MemoryStream();
        PngWriter.Write(output, pixels, width, height, width * 4);
        return output.ToArray();
    }

    [Test]
    [Arguments("grey", 0)]
    [Arguments("flat", 3)]
    [Arguments("photo", 2)]
    [Arguments("translucent", 6)]
    public async Task Each_page_takes_the_smallest_form_and_decodes_to_the_same_pixels(string kind, int colourType)
    {
        var pixels = Pixels(kind switch
        {
            // Black text antialiased on white: every pixel a grey.
            "grey" => (x, y) => ((byte) (x * 7 + y), (byte) (x * 7 + y), (byte) (x * 7 + y), 255),
            // A handful of flat colours.
            "flat" => (x, y) => x < 10 ? ((byte) 255, (byte) 0, (byte) 0, (byte) 255) : y < 10 ? ((byte) 0, (byte) 128, (byte) 255, (byte) 255) : ((byte) 255, (byte) 255, (byte) 255, (byte) 255),
            // More colours than a palette holds.
            "photo" => (x, y) => ((byte) (x * 7), (byte) (y * 11), (byte) (x * y), 255),
            _ => (x, y) => ((byte) x, (byte) y, 0, (byte) (x * 6))
        });

        var png = Encode(pixels);
        var decoded = PngDecoder.Decode(new MemoryStream(png));

        await Assert.That((int) ColourType(png)).IsEqualTo(colourType);
        await Assert.That(decoded.Rgba.SequenceEqual(pixels)).IsTrue();
    }

    [Test]
    public async Task A_row_stride_wider_than_the_page_is_honoured()
    {
        // A cropped page is a window onto a wider bitmap: each row starts a whole bitmap row on.
        var wide = new byte[(width + 5) * height * 4];
        var pixels = Pixels((x, y) => ((byte) (x * 7), (byte) (y * 11), (byte) (x * y), 255));
        for (var y = 0; y < height; y++)
        {
            pixels.AsSpan(y * width * 4, width * 4).CopyTo(wide.AsSpan(y * (width + 5) * 4));
        }

        using var output = new MemoryStream();
        PngWriter.Write(output, wide, width, height, (width + 5) * 4);

        await Assert.That(PngDecoder.Decode(new MemoryStream(output.ToArray())).Rgba.SequenceEqual(pixels)).IsTrue();
    }
}
