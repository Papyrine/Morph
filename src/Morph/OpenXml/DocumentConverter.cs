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

    /// <summary>Reports the page each bookmark in a DOCX file falls on.</summary>
    public IReadOnlyDictionary<string, int> GetBookmarkPages(string docxPath, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return GetBookmarkPages(stream, options);
    }

    /// <summary>
    /// Reports the page each bookmark in a DOCX stream falls on, keyed by bookmark name.
    /// </summary>
    /// <remarks>
    /// A cross-reference — a PAGEREF field, or a table-of-contents entry — needs a page number that
    /// only layout can supply, which is why Word leaves those fields for itself to compute on open.
    /// This lays the document out to answer the same question up front, so a generator can write the
    /// numbers into the file it produces.
    /// <para>
    /// The pages are laid out but not drawn, so this costs a layout pass rather than a full render.
    /// Bookmarks the layout cannot place — one outside the body flow, or naming a paragraph that
    /// never renders — are absent from the result rather than reported at a guessed page.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, int> GetBookmarkPages(Stream docxStream, ImageExportOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        if (document.Bookmarks.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var paragraphPages = MeasureParagraphPages(document, options);

        // A bookmark knows the ordinal of the body paragraph it sits in; layout knows where that
        // paragraph landed. Joining the two is the whole trick.
        var bodyParagraphs = document.Elements.OfType<ParagraphElement>().ToList();
        var pages = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var bookmark in document.Bookmarks)
        {
            if (bookmark.ParagraphIndex is not { } index ||
                index < 0 ||
                index >= bodyParagraphs.Count ||
                !paragraphPages.TryGetValue(bodyParagraphs[index], out var page))
            {
                continue;
            }

            pages[bookmark.Name] = page;
        }

        return pages;
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

    /// <summary>Converts a DOCX file to Markdown.</summary>
    public static string ConvertToMarkdown(string docxPath, MarkdownExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToMarkdown(stream, options);
    }

    /// <summary>Converts a DOCX stream to Markdown.</summary>
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

    /// <summary>
    /// Lays the document out without drawing it, reporting the page each paragraph starts on.
    /// </summary>
    private protected abstract IReadOnlyDictionary<ParagraphElement, int> MeasureParagraphPages(ParsedDocument document, ImageExportOptions options);
}
