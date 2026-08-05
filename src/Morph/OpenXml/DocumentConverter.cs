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
    public static IReadOnlyDictionary<string, int> GetBookmarkPages(string docxPath, ImageExportOptions? options = null)
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
    /// This paginates the document to answer the same question up front, so a generator can write the
    /// numbers into the file it produces.
    /// <para>
    /// It costs a layout pass and nothing more: the answer comes off the <see cref="Fragmenter"/>'s
    /// placed items, so no page is ever drawn and no backend is involved. Bookmarks that cannot be
    /// placed — one outside the body flow, or in a document the engine does not cover — are absent
    /// from the result rather than reported at a guessed page.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, int> GetBookmarkPages(Stream docxStream, ImageExportOptions? options = null)
    {
        options ??= new();
        var pages = new Dictionary<string, int>(StringComparer.Ordinal);

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        if (document.Bookmarks.Count == 0 ||
            !EngineCoverage.Covers(document))
        {
            return pages;
        }

        DefaultFontSettings.MarkRenderOccurred();
        var paragraphPages = ParagraphPages(document, options);

        // A bookmark knows the ordinal of the paragraph it sits in; layout knows where that
        // paragraph landed. Joining the two is the whole trick — and the two sides have to count
        // the same paragraphs, which means every w:p in document order rather than only the ones at
        // body level. A table's cells hold paragraphs too, so counting only the top level shifts
        // every bookmark below a table onto some other paragraph's page.
        var bodyParagraphs = Flatten(document.Elements).ToList();
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

    // Every paragraph under these elements, depth-first, matching the document order the parser
    // assigns bookmark ordinals in (see DocumentParser's paragraph-ordinal map, which walks
    // body.Descendants&lt;Paragraph&gt;()).
    static IEnumerable<ParagraphElement> Flatten(IEnumerable<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case ParagraphElement paragraph:
                    yield return paragraph;
                    break;
                case TableElement table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var nested in Flatten(cell.Content))
                            {
                                yield return nested;
                            }
                        }
                    }

                    break;
                case FloatingTextBoxElement textBox:
                    foreach (var nested in Flatten(textBox.Content))
                    {
                        yield return nested;
                    }

                    break;
                case PositionedFrameElement frame:
                    foreach (var nested in Flatten(frame.Content))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    // The page each paragraph starts on, read straight off the laid-out tree: a placed line is
    // anchored back to the paragraph it came from, and the page it sits on knows its own number. A
    // paragraph split across a page boundary contributes lines to both, so the lowest page wins —
    // a cross-reference points at where the paragraph begins.
    static Dictionary<ParagraphElement, int> ParagraphPages(ParsedDocument document, ImageExportOptions options)
    {
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(fontResolver), options.FontWidthScale);
        var laidOut = new Fragmenter(measurer).Layout(
            document.Elements,
            document.PageSettings,
            document.Header,
            document.Footer,
            document.FirstPageHeader,
            document.FirstPageFooter,
            document.EvenPageHeader,
            document.EvenPageFooter);

        var pages = new Dictionary<ParagraphElement, int>();
        foreach (var page in laidOut.Pages)
        {
            foreach (var line in page.Items.OfType<PlacedLine>())
            {
                if (!pages.TryGetValue(line.Paragraph, out var existing) ||
                    page.Number < existing)
                {
                    pages[line.Paragraph] = page.Number;
                }
            }
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
}
