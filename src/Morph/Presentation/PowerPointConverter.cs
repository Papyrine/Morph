namespace Morph;

/// <summary>
/// Abstract base for the PPTX → raster converters (<c>SkiaPowerPointConverter</c> and
/// <c>ImageSharpPowerPointConverter</c>). Also exposes the backend-free text exporters
/// (<see cref="ConvertToHtml(string, HtmlExportOptions?)"/>, <see cref="ConvertToMarkdown(string, MarkdownExportOptions?)"/>)
/// for callers that only need HTML or Markdown out of a deck. For multi-format export from a single
/// parse, see <see cref="PowerPointDocument"/>.
///
/// One rendered page per slide, in <c>p:sldIdLst</c> order.
/// </summary>
public abstract class PowerPointConverter
{
    /// <summary>Converts a PPTX file to PNG images saved to <paramref name="outputDirectory"/>, one per slide.</summary>
    public ConversionResult ConvertToImages(string pptxPath, string outputDirectory, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(pptxPath);
        return ConvertToImages(stream, outputDirectory, options);
    }

    /// <summary>Converts a PPTX stream to PNG images saved to <paramref name="outputDirectory"/>, one per slide.</summary>
    public ConversionResult ConvertToImages(Stream pptxStream, string outputDirectory, ImageExportOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();
        Directory.CreateDirectory(outputDirectory);

        var document = Parse(pptxStream, options.DefaultFont);
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

    /// <summary>Converts a PPTX file to PNG image data in memory, one entry per slide.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(string pptxPath, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(pptxPath);
        return ConvertToImageData(stream, options);
    }

    /// <summary>Converts a PPTX stream to PNG image data in memory, one entry per slide.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(Stream pptxStream, ImageExportOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();

        var document = Parse(pptxStream, options.DefaultFont);
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

    /// <summary>Converts a PPTX file to a normalized semantic HTML fragment.</summary>
    public static string ConvertToHtml(string pptxPath, HtmlExportOptions? options = null)
    {
        using var stream = File.OpenRead(pptxPath);
        return ConvertToHtml(stream, options);
    }

    /// <summary>Converts a PPTX stream to a normalized semantic HTML fragment.</summary>
    public static string ConvertToHtml(Stream pptxStream, HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Parse(pptxStream, options?.DefaultFont), options);

    /// <summary>Converts a PPTX file to Markdown.</summary>
    public static string ConvertToMarkdown(string pptxPath, MarkdownExportOptions? options = null)
    {
        using var stream = File.OpenRead(pptxPath);
        return ConvertToMarkdown(stream, options);
    }

    /// <summary>Converts a PPTX stream to Markdown.</summary>
    public static string ConvertToMarkdown(Stream pptxStream, MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Parse(pptxStream, options?.DefaultFont), options);

    internal static ParsedDocument Parse(Stream pptxStream, string? defaultFont) =>
        new PresentationParser(defaultFont ?? DefaultFontSettings.DefaultFont).Parse(pptxStream);

    /// <summary>
    /// Renders a parsed deck, invoking <paramref name="pageCallback"/> for each slide (the callback
    /// receives an action that writes the PNG data to a destination stream).
    /// </summary>
    /// <returns>Number of pages produced.</returns>
    private protected abstract int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback);
}
