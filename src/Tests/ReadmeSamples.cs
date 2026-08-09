// ReSharper disable UnusedVariable

[SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value")]
public class Samples
{
    [Test]
    public Task Simple()
    {
        // Page snapshots are container renders; host rasterization drifts subtly (fonts / AA)
        // and only passed on Windows while the difference sat under the SSIM threshold.
        ContainerOnly.Require();
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
            FontWidthScale = 1.08
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
        // extension method from Morph.Pdf
        document.ExportToPdf("document.pdf");

        #endregion
    }

    public static void HtmlExportWithImageHandler()
    {
        #region HtmlExportWithImageHandler

        // Write images to a media folder and reference them relatively, instead of base64-inlining.
        Directory.CreateDirectory("media");
        var html = DocumentConverter.ConvertToHtml(
            "document.docx",
            new()
            {
                ImageHandler = image =>
                {
                    var extension = image.ContentType switch
                    {
                        "image/svg+xml" => "svg",
                        "image/jpeg" => "jpg",
                        _ => "png"
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
            new()
            {
                OnWarning = warnings.Add
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
            new()
            {
                Pages = new(Start: 1, End: 3)
            });

        File.WriteAllBytes("document-preview.pdf", firstThreePages);

        #endregion
    }

    public static async Task HtmlToImages()
    {
        #region HtmlToImages

        var converter = new SkiaHtmlConverter();

        var result = await converter.ConvertToImages(
            "<h1>Hello</h1><p>World</p>",
            "output-folder");

        Console.WriteLine($"Generated {result.PageCount} pages");
        foreach (var path in result.ImagePaths)
        {
            Console.WriteLine($"Created: {path}");
        }

        #endregion
    }

    public static async Task HtmlToImageData()
    {
        #region HtmlToImageData

        var converter = new SkiaHtmlConverter();

        var imageData = await converter.ConvertToImageData("<h1>Hello</h1><p>World</p>");

        foreach (var pngBytes in imageData)
        {
            // Use the PNG byte array as needed
        }

        #endregion
    }

    public static async Task HtmlToMarkdown()
    {
        #region HtmlToMarkdown

        var markdown = await HtmlConverter.ConvertToMarkdown("<h1>Hello</h1><p>World</p>");
        await File.WriteAllTextAsync("page.md", markdown);

        #endregion
    }

    public static async Task HtmlToPdf()
    {
        #region HtmlToPdf

        var pdf = await PdfHtmlConverter.ConvertToPdf("<h1>Hello</h1><p>World</p>");
        await File.WriteAllBytesAsync("page.pdf", pdf);

        #endregion
    }

    public static async Task HtmlParseOnceExportMany()
    {
        #region HtmlParseOnceExportMany

        // Parse once with HtmlDocument, then export to as many formats as you like — the
        // source HTML is only parsed a single time.
        var document = await HtmlDocument.LoadAsync("<h1>Hello</h1><p>World</p>");

        await File.WriteAllTextAsync("page.html", document.ExportToHtml());
        await File.WriteAllTextAsync("page.md",   document.ExportToMarkdown());
        // extension method from Morph.Pdf
        document.ExportToPdf("page.pdf");

        #endregion
    }

    public static void ShrinkDocx()
    {
        #region ShrinkDocx

        // Strips every part that carries no rendering information. Returns what was
        // actually removed, or DocumentParts.None if there was nothing to strip — in
        // which case the file is left byte-for-byte untouched.
        var removed = DocumentCleaner.Remove("document.docx");

        Console.WriteLine($"Removed: {removed}");

        #endregion
    }

    public static void ShrinkDocxSelectively()
    {
        #region ShrinkDocxSelectively

        // Drop only the Explorer preview picture, keeping building blocks and custom XML.
        DocumentCleaner.Remove("document.docx", DocumentParts.Thumbnail);

        // Or report what a package is carrying without modifying it.
        var present = DocumentCleaner.Find("document.docx");
        if (present.HasFlag(DocumentParts.Thumbnail))
        {
            Console.WriteLine("This document has a preview picture");
        }

        // Stream overloads write the cleaned package to a destination of your choosing.
        using var source = File.OpenRead("document.docx");
        using var target = File.Create("document-clean.docx");
        DocumentCleaner.Remove(source, target, DocumentParts.Thumbnail | DocumentParts.Glossary);

        #endregion
    }

    public static void CompressImages()
    {
        #region CompressImages

        // Resamples every picture down to the resolution it is actually drawn at, and
        // re-encodes it. A picture is only ever replaced by a smaller one, and the file
        // is left byte-for-byte untouched when nothing got smaller.
        var result = ImageCompressor.Compress("document.docx");

        Console.WriteLine($"Saved {result.Saved} bytes across {result.Images.Count} images");

        #endregion
    }

    public static void CompressImagesSelectively()
    {
        #region CompressImagesSelectively

        // Report what a package is carrying without touching it. RenderedDpi is the one
        // to look at: an image far above the target is holding pixels nothing can show.
        foreach (var image in ImageCompressor.Inspect("document.docx"))
        {
            Console.WriteLine($"{image.PartName} {image.Width}x{image.Height} at {image.RenderedDpi:F0} DPI");
        }

        // Word's Compress Pictures defaults to 220 DPI for print, which is a safer target
        // if the document is going to be printed rather than read on screen.
        ImageCompressor.Compress("document.docx", new()
        {
            TargetDpi = 220,
            JpegQuality = 85,

            // Opt in to writing opaque PNGs out as JPEG. Lossy, and it renames the package
            // part, but for photographic content it is much the largest saving available.
            ConvertOpaquePngToJpeg = true
        });

        // Stream overloads write the compressed package to any destination.
        using var source = File.OpenRead("document.docx");
        using var target = File.Create("document-small.docx");
        ImageCompressor.Compress(source, target);

        #endregion
    }

    public static void GetBookmarkPages()
    {
        #region GetBookmarkPages

        // Which page each bookmark falls on — the number a PAGEREF field or a table-of-contents
        // entry needs, and which only pagination can answer.
        var pages = DocumentConverter.GetBookmarkPages("report.docx");

        foreach (var (bookmark, page) in pages)
        {
            Console.WriteLine($"{bookmark} is on page {page}");
        }

        #endregion
    }
}
