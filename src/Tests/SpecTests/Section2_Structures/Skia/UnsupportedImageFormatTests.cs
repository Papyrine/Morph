extern alias Skia;
using SkiaRenderContext = Skia::RenderContext;
using SkiaPageRenderer = Skia::PageRenderer;

/// <summary>
/// Verifies that rendering gracefully handles image data in formats
/// that SkiaSharp cannot decode (e.g. EMF, WMF, corrupted data),
/// instead of throwing ArgumentNullException from SKBitmap.Decode.
/// </summary>
public class UnsupportedImageFormatTests
{
    static readonly byte[] bogusImageData = [0x00, 0x01, 0x02, 0x03];

    static int RenderToPages(params DocumentElement[] elements)
    {
        var doc = new ParsedDocument
        {
            PageSettings = new()
            {
                WidthPoints = 300,
                HeightPoints = 200,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20
            },
            Elements = elements
        };

        using var context = new SkiaRenderContext(doc.PageSettings, 96, fontDirectory: ProjectFonts.Directory);
        using var renderer = new SkiaPageRenderer(context);

        return renderer.RenderDocument(doc, _ => { });
    }

    [Test]
    public async Task InlineImage_UndecodableData_DoesNotThrow()
    {
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new()
                {
                    Text = "",
                    InlineImageData = bogusImageData,
                    InlineImageWidthPoints = 50,
                    InlineImageHeightPoints = 50,
                    InlineImageContentType = "image/x-emf"
                }
            ]
        };

        await Assert.That(() => RenderToPages(paragraph)).ThrowsNothing();
    }

    [Test]
    public async Task BlockImage_UndecodableData_DoesNotThrow()
    {
        var image = new ImageElement
        {
            ImageData = bogusImageData,
            WidthPoints = 50,
            HeightPoints = 50,
            ContentType = "image/x-emf"
        };

        await Assert.That(() => RenderToPages(image)).ThrowsNothing();
    }

    [Test]
    public async Task FloatingImage_UndecodableData_DoesNotThrow()
    {
        var image = new FloatingImageElement
        {
            ImageData = bogusImageData,
            WidthPoints = 50,
            HeightPoints = 50,
            ContentType = "image/x-emf"
        };

        await Assert.That(() => RenderToPages(image)).ThrowsNothing();
    }

    [Test]
    public async Task FloatingShape_UndecodableImageFill_DoesNotThrow()
    {
        var shape = new FloatingShapeElement
        {
            ImageData = bogusImageData,
            ImageContentType = "image/x-emf",
            WidthPoints = 50,
            HeightPoints = 50
        };

        await Assert.That(() => RenderToPages(shape)).ThrowsNothing();
    }
}
