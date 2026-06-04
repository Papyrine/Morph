// ReSharper disable UnusedVariable

[SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value")]
public class Samples
{
    [Test]
    public Task Simple()
    {
        var converter = new SkiaDocumentConverter();

        var data = converter.ConvertToImageData("sample.docx");

        return Verify(data.Select(_ => new Target("png", new MemoryStream(_))));
    }

    public static void BasicUsage()
    {
        #region BasicUsage

        var converter = new SkiaDocumentConverter();

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

        var converter = new SkiaDocumentConverter();

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

        var converter = new SkiaDocumentConverter();

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

        var converter = new SkiaDocumentConverter();

        var options = new ImageExportOptions
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

    public static void ConvertToHtml()
    {
        #region ConvertToHtml

        var html = DocumentConverter.ConvertToHtml("document.docx");
        File.WriteAllText("document.html", html);

        #endregion
    }

    public static void ConvertToMarkdown()
    {
        #region ConvertToMarkdown

        var markdown = DocumentConverter.ConvertToMarkdown("document.docx");
        File.WriteAllText("document.md", markdown);

        #endregion
    }

    public static void ConvertToPdf()
    {
        #region ConvertToPdf

        var outputPath = "document.pdf";
        PdfDocumentConverter.ConvertToPdf("document.docx", outputPath);

        #endregion
    }

    public static void ParseOnceExportMany()
    {
        #region ParseOnceExportMany

        // Parse once with WordDocument, then export to as many formats as you like — the source
        // .docx is only opened and parsed a single time.
        var document = new WordDocument("document.docx");

        File.WriteAllText("document.html", document.ExportToHtml());
        File.WriteAllText("document.md",   document.ExportToMarkdown());
        document.ExportToPdf("document.pdf");   // extension method from Morph.OpenXml.Pdf

        #endregion
    }

    public static void HtmlExportWithImageHandler()
    {
        #region HtmlExportWithImageHandler

        // Write images to a media folder and reference them relatively, instead of base64-inlining.
        Directory.CreateDirectory("media");
        var html = DocumentConverter.ConvertToHtml(
            "document.docx",
            new HtmlExportOptions
            {
                ImageHandler = image =>
                {
                    var extension = image.ContentType switch
                    {
                        "image/svg+xml" => "svg",
                        "image/jpeg"    => "jpg",
                        _               => "png"
                    };
                    var path = $"media/image-{image.Index}.{extension}";
                    File.WriteAllBytes(path, image.Data);
                    return path;
                }
            });

        #endregion
    }

    public static void WarningCallback()
    {
        #region WarningCallback

        // Discover features in the source that couldn't be fully represented in the output —
        // unsupported elements (ink strokes, vector shapes), missing fonts, etc.
        var warnings = new List<ExportWarning>();
        var html = DocumentConverter.ConvertToHtml(
            "document.docx",
            new HtmlExportOptions
            {
                OnWarning = warning => warnings.Add(warning)
            });

        foreach (var warning in warnings)
        {
            Console.WriteLine($"[{warning.Kind}] {warning.Message}");
        }

        #endregion
    }

    public static void PdfPageRange()
    {
        #region PdfPageRange

        // Render only the first three pages of the document.
        var firstThreePages = PdfDocumentConverter.ConvertToPdf(
            "document.docx",
            new PdfExportOptions {Pages = new(Start: 1, End: 3)});

        File.WriteAllBytes("document-preview.pdf", firstThreePages);

        #endregion
    }
}
