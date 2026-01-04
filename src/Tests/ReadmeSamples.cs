// ReSharper disable UnusedVariable

[SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value")]
public class Samples
{
    [Test]
    public Task Simple()
    {
        var converter = new DocumentConverter();

        var imageData = converter.ConvertToImageData("sample.docx");

        return Verify(imageData.Select(_ => new Target("png", new MemoryStream(_))));
    }

    public static void BasicUsage()
    {
        #region BasicUsage

        var converter = new DocumentConverter();

        var result = converter.ConvertToImages(
            "document.docx",
            "output-folder");

        Console.WriteLine($"Generated {result.PageCount} pages");
        foreach (var path in result.ImagePaths)
        {
            Console.WriteLine($"Created: {path}");
        }

        #endregion
    }

    public static void InMemoryConversion()
    {
        #region InMemoryConversion

        var converter = new DocumentConverter();

        var imageData = converter.ConvertToImageData("document.docx");

        foreach (var pngBytes in imageData)
        {
            // Use the PNG byte array as needed
        }

        #endregion
    }

    public static void StreamBasedConversion()
    {
        #region StreamBasedConversion

        var converter = new DocumentConverter();

        using var stream = File.OpenRead("document.docx");

        // From stream to files
        var result = converter.ConvertToImages(stream, "output-folder");

        // Or from stream to memory
        var imageData = converter.ConvertToImageData(stream);

        #endregion
    }

    public static void CustomOptions()
    {
        #region CustomOptions

        var converter = new DocumentConverter();

        var options = new ConversionOptions
        {
            Dpi = 300,
            FontWidthScale = 1.07
        };

        var result = converter.ConvertToImages(
            "document.docx",
            "output-folder",
            options);

        #endregion
    }
}
