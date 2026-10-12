using Morph.PDFium;
using SkiaSharp;

/// <summary>
/// A turned drawing is turned about the centre of its box by every painter. Two had not been: the PDF
/// painter drew a rotated preset shape unrotated (resumes/06's black side strip lay across the page),
/// and the ImageSharp painter drew a turned picture's bitmap from the box's top-left corner, where the
/// rotation had grown it (letters/13's left banner was off the page altogether).
/// </summary>
public class RotatedDrawingPaintTests
{
    // A 120 x 30pt box at (100, 100) turned a quarter turn about its centre (160, 115) covers
    // x 145..175, y 55..175. At 72 dpi a pixel is a point.
    const int dpi = 72;

    static readonly PageSettings page = new() { WidthPoints = 300, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    static ParsedDocument Document(DocumentElement drawing) =>
        new()
        {
            PageSettings = page,
            Elements =
            [
                drawing,
                new ParagraphElement { Runs = [], Properties = new() }
            ]
        };

    static FloatingShapeElement Shape =>
        new()
        {
            WidthPoints = 120,
            HeightPoints = 30,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalPositionPoints = 100,
            VerticalPositionPoints = 100,
            FillColorHex = "000000",
            RotationDegrees = 90
        };

    static FloatingImageElement Picture
    {
        get
        {
            using var bitmap = new SKBitmap(8, 2);
            bitmap.Erase(SKColors.Black);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return new()
            {
                ImageData = data.ToArray(),
                ContentType = "image/png",
                WidthPoints = 120,
                HeightPoints = 30,
                HorizontalAnchor = HorizontalAnchor.Page,
                VerticalAnchor = VerticalAnchor.Page,
                HorizontalPositionPoints = 100,
                VerticalPositionPoints = 100,
                RotationDegrees = 90
            };
        }
    }

    [Test]
    public async Task The_pdf_painter_turns_a_preset_shape_about_its_centre()
    {
        var pdf = PdfRenderer.Render(Document(Shape), new() { FontDirectory = ProjectFonts.Directory });
        using var rasterized = PdfiumDocument.Load(pdf);

        await AssertTurned(rasterized.RenderPage(0, dpi));
    }

    [Test]
    public async Task The_raster_painters_turn_a_preset_shape_about_its_centre()
    {
        await AssertTurned(Raster(Document(Shape), skia: true));
        await AssertTurned(Raster(Document(Shape), skia: false));
    }

    [Test]
    public async Task The_raster_painters_turn_a_picture_about_its_centre()
    {
        await AssertTurned(Raster(Document(Picture), skia: true));
        await AssertTurned(Raster(Document(Picture), skia: false));
    }

    static async Task AssertTurned(byte[] png)
    {
        var (left, top, right, bottom) = InkBounds(png);

        await Assert.That(left).IsEqualTo(145).Within(2);
        await Assert.That(right).IsEqualTo(175).Within(2);
        await Assert.That(top).IsEqualTo(55).Within(2);
        await Assert.That(bottom).IsEqualTo(175).Within(2);
    }

    static byte[] Raster(ParsedDocument document, bool skia)
    {
        var options = new ImageExportOptions { Dpi = dpi, FontDirectory = ProjectFonts.Directory, DeterministicRendering = true };
        byte[]? result = null;
        void Sink(Action<Stream> writePng)
        {
            using var stream = new MemoryStream();
            writePng(stream);
            result ??= stream.ToArray();
        }

        if (skia)
        {
            SkiaDocumentConverter.RenderPagesCounted(document, options, Sink);
        }
        else
        {
            ImageSharpDocumentConverter.RenderPagesCounted(document, options, Sink);
        }

        return result!;
    }

    // The box of the pixels darker than mid-grey, right and bottom exclusive.
    static (int Left, int Top, int Right, int Bottom) InkBounds(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, 0, 0);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var colour = bitmap.GetPixel(x, y);
                if (colour.Alpha < 128 || colour.Red + colour.Green + colour.Blue > 384)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return (left, top, right, bottom);
    }
}
