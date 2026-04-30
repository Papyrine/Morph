extern alias ImageSharp;

/// <summary>
/// Rendering tests for content control elements through the ImageSharp pipeline.
/// </summary>
public class ImageSharpContentControlRenderTests
{
    static byte[] RenderElements(params DocumentElement[] elements)
    {
        var doc = new ParsedDocument
        {
            PageSettings = new()
            {
                WidthPoints = 300,
                HeightPoints = 100,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20
            },
            Elements = elements
        };

        using var context = new ImageSharpRenderContext(doc.PageSettings, 96, fontDirectory: ProjectFonts.Directory);
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

    static Task VerifyRendered(params DocumentElement[] elements) =>
        Verify(new Target("png", new MemoryStream(RenderElements(elements))));

    [Test]
    public Task ContentControl_Date_WithValue() =>
        VerifyRendered(new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            DateValue = new DateTime(2025, 6, 15),
            WidthPoints = 120
        });

    [Test]
    public Task ContentControl_Date_Empty() =>
        VerifyRendered(new ContentControlElement
        {
            ControlType = ContentControlType.Date,
            PlaceholderText = "Click to enter a date.",
            WidthPoints = 160
        });
}
