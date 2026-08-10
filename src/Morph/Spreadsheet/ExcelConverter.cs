namespace Morph;

/// <summary>
/// Abstract base for the XLSX → raster converters (<c>SkiaExcelConverter</c> and
/// <c>ImageSharpExcelConverter</c>). Also exposes the backend-free text exporters for callers that
/// only need HTML or Markdown out of a workbook. For multi-format export from a single parse, see
/// <see cref="ExcelDocument"/>.
///
/// Pages come from the print layout rather than the sheet: a long sheet paginates downward, and each
/// visible sheet starts a new page with its own paper size and orientation.
/// </summary>
public abstract class ExcelConverter
{
    /// <summary>Converts an XLSX file to PNG images saved to <paramref name="outputDirectory"/>.</summary>
    public ConversionResult ConvertToImages(string xlsxPath, string outputDirectory, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        return ConvertToImages(stream, outputDirectory, options);
    }

    /// <summary>Converts an XLSX stream to PNG images saved to <paramref name="outputDirectory"/>.</summary>
    public ConversionResult ConvertToImages(Stream xlsxStream, string outputDirectory, ImageExportOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();
        Directory.CreateDirectory(outputDirectory);

        var document = Parse(xlsxStream, options.DefaultFont);
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

    /// <summary>Converts an XLSX file to PNG image data in memory.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(string xlsxPath, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        return ConvertToImageData(stream, options);
    }

    /// <summary>Converts an XLSX stream to PNG image data in memory.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(Stream xlsxStream, ImageExportOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();

        var document = Parse(xlsxStream, options.DefaultFont);
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

    /// <summary>Converts an XLSX file to a normalized semantic HTML fragment.</summary>
    public static string ConvertToHtml(string xlsxPath, HtmlExportOptions? options = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        return ConvertToHtml(stream, options);
    }

    /// <summary>Converts an XLSX stream to a normalized semantic HTML fragment.</summary>
    public static string ConvertToHtml(Stream xlsxStream, HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Parse(xlsxStream, options?.DefaultFont), options);

    /// <summary>Converts an XLSX file to Markdown.</summary>
    public static string ConvertToMarkdown(string xlsxPath, MarkdownExportOptions? options = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        return ConvertToMarkdown(stream, options);
    }

    /// <summary>Converts an XLSX stream to Markdown.</summary>
    public static string ConvertToMarkdown(Stream xlsxStream, MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Parse(xlsxStream, options?.DefaultFont), options);

    internal static ParsedDocument Parse(Stream xlsxStream, string? defaultFont) =>
        new SpreadsheetParser(defaultFont ?? DefaultFontSettings.DefaultFont).Parse(xlsxStream);

    /// <summary>
    /// Renders a parsed workbook, invoking <paramref name="pageCallback"/> for each page.
    /// </summary>
    /// <returns>Number of pages produced.</returns>
    private protected abstract int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback);
}
