namespace Morph;

/// <summary>
/// Abstract base for the HTML → raster converters (<c>SkiaHtmlConverter</c> and
/// <c>ImageSharpHtmlConverter</c>). Also exposes the backend-free text exporters
/// (<see cref="ConvertToHtml(string, HtmlExportOptions?, Cancel)"/> for re-serialization and
/// <see cref="ConvertToMarkdown(string, MarkdownExportOptions?, Cancel)"/> for conversion).
/// For multi-format export from a single parse, see <see cref="HtmlDocument"/>.
/// </summary>
public abstract class HtmlConverter
{
    /// <summary>Converts an HTML string to PNG images saved to <paramref name="outputDirectory"/>.</summary>
    public async Task<ConversionResult> ConvertToImages(string html, string outputDirectory, ImageExportOptions? options = null, Cancel cancel = default)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();
        Directory.CreateDirectory(outputDirectory);

        var document = await ParseHtml(html, cancel);
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

    /// <summary>Converts an HTML string to PNG image data in memory.</summary>
    public async Task<IReadOnlyList<byte[]>> ConvertToImageData(string html, ImageExportOptions? options = null, Cancel cancel = default)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();

        var document = await ParseHtml(html, cancel);
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

    /// <summary>Converts an HTML string to a normalized semantic HTML fragment.</summary>
    public static async Task<string> ConvertToHtml(string html, HtmlExportOptions? options = null, Cancel cancel = default) =>
        HtmlExporter.Export(await ParseHtml(html, cancel), options);

    /// <summary>Converts an HTML string to Markdown.</summary>
    public static async Task<string> ConvertToMarkdown(string html, MarkdownExportOptions? options = null, Cancel cancel = default) =>
        MarkdownExporter.Export(await ParseHtml(html, cancel), options);

    internal static async Task<ParsedDocument> ParseHtml(string html, Cancel cancel)
    {
        var elements = await HtmlParser.Parse(html, cancel);
        return new()
        {
            PageSettings = new()
            {
                WidthPoints = DefaultPageSize.WidthPoints,
                HeightPoints = DefaultPageSize.HeightPoints,
                MarginTop = 72,
                MarginBottom = 72,
                MarginLeft = 72,
                MarginRight = 72
            },
            Elements = elements
        };
    }

    /// <summary>
    /// Renders a parsed document, invoking <paramref name="pageCallback"/> for each page (the
    /// callback receives an action that writes the PNG data to a destination stream).
    /// </summary>
    /// <returns>Number of pages produced.</returns>
    private protected abstract int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback);
}
