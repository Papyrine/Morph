extern alias ImageSharp;
using ImageSharpRenderContext = ImageSharp::RenderContext;
using ImageSharpPageRenderer = ImageSharp::PageRenderer;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// ImageSharp counterpart of the Skia <c>HeaderFooterFloatingRenderTests</c>.
/// Regression guard for <c>PageRenderer.RenderHeader</c> / <c>RenderFooter</c>
/// silently dropping <c>FloatingImageElement</c> (and footer dropping behind-text
/// <c>FloatingShapeElement</c>) prior to the fix.
/// </summary>
public class ImageSharpHeaderFooterFloatingRenderTests
{
    const double pageWidthPoints = 300;
    const double pageHeightPoints = 400;

    static byte[] RenderDocument(ParsedDocument doc)
    {
        using var context = new ImageSharpRenderContext(doc.PageSettings, 96);
        using var renderer = new ImageSharpPageRenderer(context);

        byte[]? result = null;
        renderer.RenderDocument(doc, writePng =>
        {
            using var ms = new MemoryStream();
            writePng(ms);
            result ??= ms.ToArray();
        });

        return result!;
    }

    static byte[] MakeSolidPng(int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    static Rgba32 SamplePixel(byte[] pngBytes, int x, int y)
    {
        using var image = Image.Load<Rgba32>(pngBytes);
        return image[x, y];
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
        var red = MakeSolidPng(50, 50, new(255, 0, 0));
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

        var pixel = SamplePixel(pngBytes, 133, 133);
        await Assert.That(pixel.R).IsGreaterThan((byte) 200);
        await Assert.That(pixel.G).IsLessThan((byte) 50);
        await Assert.That(pixel.B).IsLessThan((byte) 50);
    }

    [Test]
    public async Task FooterFloatingImage_BehindText_IsRendered()
    {
        var red = MakeSolidPng(50, 50, new(255, 0, 0));
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

        var pixel = SamplePixel(pngBytes, 133, 400);
        await Assert.That(pixel.R).IsGreaterThan((byte) 200);
        await Assert.That(pixel.G).IsLessThan((byte) 50);
        await Assert.That(pixel.B).IsLessThan((byte) 50);
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
        await Assert.That(pixel.G).IsGreaterThan((byte) 200);
        await Assert.That(pixel.R).IsLessThan((byte) 50);
        await Assert.That(pixel.B).IsLessThan((byte) 50);
    }
}
