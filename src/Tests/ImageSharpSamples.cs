// ReSharper disable UnusedVariable

using Morph;

[SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value")]
public class ImageSharpSamples
{
    [Test]
    public Task Simple()
    {
        var converter = new ImageSharpDocumentConverter();

        var data = converter.ConvertToImageData("sample.docx");

        return Verify(data.Select(_ => new Target("png", new MemoryStream(_))));
    }

    public static void BasicUsage()
    {
        var converter = new ImageSharpDocumentConverter();

        var result = converter.ConvertToImages(
            "document.docx",
            "output-folder");

        Console.WriteLine($"Generated {result.PageCount} pages");
        foreach (var path in result.ImagePaths)
        {
            Console.WriteLine($"Created: {path}");
        }
    }

    public static void InMemoryConversion()
    {
        var converter = new ImageSharpDocumentConverter();

        var imageData = converter.ConvertToImageData("document.docx");

        foreach (var pngBytes in imageData)
        {
            // Use the PNG byte array as needed
        }
    }

    public static void StreamBasedConversion()
    {
        var converter = new ImageSharpDocumentConverter();

        using var stream = File.OpenRead("document.docx");

        var result = converter.ConvertToImages(stream, "output-folder");

        var imageData = converter.ConvertToImageData(stream);
    }

    public static void CustomOptions()
    {
        var converter = new ImageSharpDocumentConverter();

        var options = new ConversionOptions
        {
            Dpi = 300,
            FontWidthScale = 1.07
        };

        var result = converter.ConvertToImages(
            "document.docx",
            "output-folder",
            options);
    }
}
