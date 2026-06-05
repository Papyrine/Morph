namespace Morph;

/// <summary>
/// Abstract base for the DOCX → raster converters (<c>SkiaDocumentConverter</c> and
/// <c>ImageSharpDocumentConverter</c>). Also exposes the backend-free text exporters
/// (<see cref="ConvertToHtml(string, HtmlExportOptions?)"/>, <see cref="ConvertToMarkdown(string, MarkdownExportOptions?)"/>)
/// for callers that only need HTML or Markdown out of a DOCX. For multi-format export from a single
/// parse, see <see cref="WordDocument"/>.
/// </summary>
public abstract class DocumentConverter
{
    /// <summary>Converts a DOCX file to PNG images saved to <paramref name="outputDirectory"/>.</summary>
    public ConversionResult ConvertToImages(string docxPath, string outputDirectory, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToImages(stream, outputDirectory, options);
    }

    /// <summary>Converts a DOCX stream to PNG images saved to <paramref name="outputDirectory"/>.</summary>
    public ConversionResult ConvertToImages(Stream docxStream, string outputDirectory, ImageExportOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();
        Directory.CreateDirectory(outputDirectory);

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        var imagePaths = new List<string>();

        var pageIndex = 0;
        var pageCount = RenderPages(
            document,
            options,
            writePng =>
            {
                var filePath = Path.Combine(outputDirectory, $"page_{++pageIndex:D4}.png");
                imagePaths.Add(filePath);
                using var fs = File.Create(filePath);
                writePng(fs);
            });

        return new(imagePaths, pageCount);
    }

    /// <summary>Converts a DOCX file to PNG image data in memory.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(string docxPath, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToImageData(stream, options);
    }

    /// <summary>Converts a DOCX stream to PNG image data in memory.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(Stream docxStream, ImageExportOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        var imageData = new List<byte[]>();

        RenderPages(
            document,
            options,
            writePng =>
            {
                using var ms = new MemoryStream();
                writePng(ms);
                imageData.Add(ms.ToArray());
            });

        return imageData;
    }

    /// <summary>Converts a DOCX file to a semantic HTML fragment.</summary>
    /// <returns>An HTML fragment (body-level content, no <c>&lt;html&gt;</c> wrapper).</returns>
    public static string ConvertToHtml(string docxPath, HtmlExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToHtml(stream, options);
    }

    /// <summary>Converts a DOCX stream to a semantic HTML fragment.</summary>
    public static string ConvertToHtml(Stream docxStream, HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Parse(docxStream, options?.DefaultFont), options);

    /// <summary>Converts a DOCX file to Pandoc-flavoured Markdown.</summary>
    public static string ConvertToMarkdown(string docxPath, MarkdownExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToMarkdown(stream, options);
    }

    /// <summary>Converts a DOCX stream to Pandoc-flavoured Markdown.</summary>
    public static string ConvertToMarkdown(Stream docxStream, MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Parse(docxStream, options?.DefaultFont), options);

    internal static ParsedDocument Parse(Stream docxStream, string? defaultFont) =>
        new DocumentParser(defaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);

    /// <summary>
    /// Renders a parsed document, invoking <paramref name="pageCallback"/> for each page (the
    /// callback receives an action that writes the PNG data to a destination stream).
    /// </summary>
    /// <returns>Number of pages produced.</returns>
    private protected abstract int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback);
}
