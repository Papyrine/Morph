extern alias Skia;
using SkiaSharp;

/// <summary>
/// Verifies that floating images and behind-text shapes placed in a header
/// or footer actually reach the canvas. Regression guard for
/// <c>PageRenderer.RenderHeader</c> / <c>RenderFooter</c> silently dropping
/// <c>FloatingImageElement</c> (and footer dropping behind-text
/// <c>FloatingShapeElement</c>) prior to the fix.
/// </summary>
public class HeaderFooterFloatingRenderTests
{
    const double pageWidthPoints = 300;
    const double pageHeightPoints = 400;

    static byte[] RenderDocument(ParsedDocument doc)
    {
        using var context = new SkiaRenderContext(doc.PageSettings, 96, fontDirectory: ProjectFonts.Directory);
        using var renderer = new SkiaPageRenderer(context);

        byte[]? result = null;
        renderer.RenderDocument(doc, writePng =>
        {
            using var ms = new MemoryStream();
            writePng(ms);
            result ??= ms.ToArray();
        });

        return result!;
    }

    static byte[] MakeSolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static SKColor SamplePixel(byte[] pngBytes, int x, int y)
    {
        using var image = SKBitmap.Decode(pngBytes);
        return image.GetPixel(x, y);
    }

    static ParsedDocument MakeDocument(HeaderFooterContent? header = null, HeaderFooterContent? footer = null) =>
        new()
        {
            PageSettings = new()
            {
                WidthPoints = pageWidthPoints,
                HeightPoints = pageHeightPoints,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20,
                HeaderDistance = 0,
                FooterDistance = 0
            },
            Elements = [],
            Header = header,
            Footer = footer
        };

    [Test]
    public async Task HeaderFloatingImage_BehindText_IsRendered()
    {
        var red = MakeSolidPng(50, 50, SKColors.Red);
        var image = new FloatingImageElement
        {
            ImageData = red,
            ContentType = "image/png",
            WidthPoints = 100,
            HeightPoints = 100,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalPositionPoints = 50,
            VerticalPositionPoints = 50,
            BehindText = true
        };

        var doc = MakeDocument(header: new() { Elements = [image] });
        var pngBytes = RenderDocument(doc);

        // Image covers points (50,50)–(150,150). At 96 DPI, point 100 = pixel ~133.
        var pixel = SamplePixel(pngBytes, 133, 133);
        await Assert.That(pixel.Red).IsGreaterThan((byte) 200);
        await Assert.That(pixel.Green).IsLessThan((byte) 50);
        await Assert.That(pixel.Blue).IsLessThan((byte) 50);
    }

    [Test]
    public async Task FooterFloatingImage_BehindText_IsRendered()
    {
        var red = MakeSolidPng(50, 50, SKColors.Red);
        var image = new FloatingImageElement
        {
            ImageData = red,
            ContentType = "image/png",
            WidthPoints = 100,
            HeightPoints = 100,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalPositionPoints = 50,
            VerticalPositionPoints = 250,
            BehindText = true
        };

        var doc = MakeDocument(footer: new() { Elements = [image] });
        var pngBytes = RenderDocument(doc);

        // Image covers points (50,250)–(150,350). Sample mid-image.
        var pixel = SamplePixel(pngBytes, 133, 400);
        await Assert.That(pixel.Red).IsGreaterThan((byte) 200);
        await Assert.That(pixel.Green).IsLessThan((byte) 50);
        await Assert.That(pixel.Blue).IsLessThan((byte) 50);
    }

    [Test]
    public async Task FooterFloatingShape_BehindText_IsRendered()
    {
        var shape = new FloatingShapeElement
        {
            FillColorHex = "00FF00",
            WidthPoints = 100,
            HeightPoints = 100,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalPositionPoints = 50,
            VerticalPositionPoints = 250,
            BehindText = true
        };

        var doc = MakeDocument(footer: new() { Elements = [shape] });
        var pngBytes = RenderDocument(doc);

        var pixel = SamplePixel(pngBytes, 133, 400);
        await Assert.That(pixel.Green).IsGreaterThan((byte) 200);
        await Assert.That(pixel.Red).IsLessThan((byte) 50);
        await Assert.That(pixel.Blue).IsLessThan((byte) 50);
    }
}
