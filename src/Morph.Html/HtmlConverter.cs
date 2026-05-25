namespace Morph;

/// <summary>
/// Converts HTML content to PNG images.
/// </summary>
public abstract class HtmlConverter
{
    /// <summary>
    /// Converts an HTML string to PNG images saved to disk.
    /// </summary>
    /// <param name="html">The HTML content to render.</param>
    /// <param name="outputDirectory">Directory where PNG files will be saved.</param>
    /// <param name="options">Conversion options (optional).</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>Result containing paths to generated images and page count.</returns>
    public async Task<ConversionResult> ConvertToImages(string html, string outputDirectory, ConversionOptions? options = null, Cancel cancel = default)
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

    /// <summary>
    /// Converts an HTML string to PNG image data in memory.
    /// </summary>
    /// <param name="html">The HTML content to render.</param>
    /// <param name="options">Conversion options (optional).</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>List of PNG image data for each page.</returns>
    public async Task<IReadOnlyList<byte[]>> ConvertToImageData(string html, ConversionOptions? options = null, Cancel cancel = default)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();

        var document = await ParseHtml(html, cancel);
        var imageData = new List<byte[]>();

        RenderPages(document, options, writePng =>
        {
            using var ms = new MemoryStream();
            writePng(ms);
            imageData.Add(ms.ToArray());
        });

        return imageData;
    }

    /// <summary>
    /// Converts HTML content to a normalized semantic HTML fragment.
    /// </summary>
    public static async Task<string> ConvertToHtml(string html, Cancel cancel = default) =>
        HtmlExporter.Export(await ParseHtml(html, cancel));

    /// <summary>
    /// Converts HTML content to Pandoc-flavoured Markdown.
    /// </summary>
    public static async Task<string> ConvertToMarkdown(string html, Cancel cancel = default) =>
        MarkdownExporter.Export(await ParseHtml(html, cancel));

    static async Task<ParsedDocument> ParseHtml(string html, Cancel cancel)
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
    /// Renders a parsed document by calling pageCallback for each page.
    /// The callback receives an action that writes PNG data to a stream.
    /// Returns the total page count.
    /// </summary>
    private protected abstract int RenderPages(ParsedDocument document, ConversionOptions options, Action<Action<Stream>> pageCallback);
}
