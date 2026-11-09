namespace Morph;

/// <summary>
/// Abstract base for the DOCX → PNG converters, and home of the backend-free HTML and Markdown
/// exporters. For several formats from one parse, see <see cref="WordDocument"/>.
/// </summary>
public abstract class DocumentConverter
{
    /// <summary>Converts a DOCX file to PNG images saved to <paramref name="outputDirectory"/>.</summary>
    public ConversionResult ConvertToImages(string docxPath, string outputDirectory, ImageExportOptions? options = null, Cancel cancel = default)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToImages(stream, outputDirectory, options, cancel);
    }

    /// <summary>Converts a DOCX stream to PNG images saved to <paramref name="outputDirectory"/>.</summary>
    public ConversionResult ConvertToImages(Stream docxStream, string outputDirectory, ImageExportOptions? options = null, Cancel cancel = default)
    {
        options = (options ?? new()) with { Cancellation = cancel };
        cancel.ThrowIfCancellationRequested();
        DefaultFontSettings.MarkRenderOccurred();
        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, options.UseLetterPageSize, culture: options.Culture).Parse(docxStream);
        return PageSink.ToDirectory(
            outputDirectory,
            sink => RenderPages(document, options, sink));
    }

    /// <summary>Converts a DOCX file to PNG image data in memory.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(string docxPath, ImageExportOptions? options = null, Cancel cancel = default)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToImageData(stream, options, cancel);
    }

    /// <summary>Converts a DOCX stream to PNG image data in memory.</summary>
    public IReadOnlyList<byte[]> ConvertToImageData(Stream docxStream, ImageExportOptions? options = null, Cancel cancel = default)
    {
        options = (options ?? new()) with { Cancellation = cancel };
        cancel.ThrowIfCancellationRequested();
        DefaultFontSettings.MarkRenderOccurred();

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, options.UseLetterPageSize, culture: options.Culture).Parse(docxStream);
        return PageSink.ToMemory(sink => RenderPages(document, options, sink));
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
    /// placed items, so no page is ever drawn and no backend is involved — the same pagination every
    /// rendered output goes through, so a bookmark's reported page is the page it renders on. A bookmark
    /// that cannot be placed (one outside the body flow) is absent from the result rather than reported
    /// at a guessed page.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, int> GetBookmarkPages(Stream docxStream, ImageExportOptions? options = null)
    {
        options ??= new();
        var pages = new Dictionary<string, int>(StringComparer.Ordinal);

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, options.UseLetterPageSize, culture: options.Culture).Parse(docxStream);
        if (document.Bookmarks.Count == 0)
        {
            return pages;
        }

        DefaultFontSettings.MarkRenderOccurred();
        var paragraphPages = ParagraphPages(document, options);

        // A bookmark knows the paragraph it sits in; layout knows where that paragraph landed.
        // Counting paragraphs instead does not agree: a table's cells hold paragraphs too, and a
        // w:p a break divides is several paragraphs of the model, or none.
        foreach (var bookmark in document.Bookmarks)
        {
            if (bookmark.Paragraph is not { } paragraph ||
                !paragraphPages.TryGetValue(paragraph, out var page))
            {
                continue;
            }

            pages[bookmark.Name] = page;
        }

        return pages;
    }

    // Every paragraph under these elements, depth-first, in document order.
    internal static IEnumerable<ParagraphElement> Flatten(IEnumerable<DocumentElement> elements)
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

    // Every placed line under these items, a table cell's included (recursively, as a cell can hold a
    // table). A cell's lines hang off its PlacedTableRow rather than sitting on the page, so reading
    // only the top level leaves a bookmark in a table with no page, which a caller writing a PAGEREF
    // renders as the field's placeholder text. Floating text boxes and frames need no case:
    // PlaceTextBox and PlaceFrame add their content to the page as body floats.
    internal static IEnumerable<PlacedLine> Lines(IEnumerable<PlacedItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case PlacedLine line:
                    yield return line;
                    break;
                case PlacedTableRow row:
                    foreach (var cell in row.Cells)
                    {
                        foreach (var nested in Lines(cell.Content))
                        {
                            yield return nested;
                        }
                    }

                    break;
                case PlacedRotatedGroup group:
                    foreach (var nested in Lines(group.Items))
                    {
                        yield return nested;
                    }

                    break;
                case PlacedClipGroup clip:
                    foreach (var nested in Lines(clip.Items))
                    {
                        yield return nested;
                    }

                    break;
                case PlacedScaledGroup scaled:
                    foreach (var nested in Lines(scaled.Items))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    /// <summary>Reports the text of each page of a DOCX file.</summary>
    public static IReadOnlyList<string> GetPageTexts(string docxPath, ImageExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return GetPageTexts(stream, options);
    }

    /// <summary>
    /// Reports the text of each page of a DOCX stream: one entry for every page, in page order, so
    /// the count is the number of pages and the entry of a page with no text is empty.
    /// </summary>
    /// <remarks>
    /// Where a page ends is a product of pagination, so the text of a document can only be divided
    /// by page once it is laid out. This paginates it, as
    /// <see cref="GetBookmarkPages(Stream, ImageExportOptions?)"/> does and at the same cost: a
    /// layout pass, with no page drawn and no backend involved. It is the pagination every rendered
    /// output goes through, so the text of a page is the text drawn on it.
    /// <para>
    /// The text is that of the body, in document order, with a line (<c>\n</c>) for each paragraph
    /// and for each row of a table, whose cells are separated by tabs. A paragraph or a row that
    /// runs over the end of a page is divided where the page ends. Headers, footers and notes are
    /// left out. It is the text as laid out rather than as stored, so a list paragraph starts with
    /// its marker.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> GetPageTexts(Stream docxStream, ImageExportOptions? options = null)
    {
        options ??= new();
        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, options.UseLetterPageSize, culture: options.Culture).Parse(docxStream);

        DefaultFontSettings.MarkRenderOccurred();
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        return PageTextReader.Read(document, Layout(document, options, fontResolver));
    }

    static LaidOutDocument Layout(ParsedDocument document, ImageExportOptions options, FontResolver<FontMetrics> fontResolver)
    {
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(fontResolver), options.FontWidthScale, document.Compatibility, LayoutFonts.Covers(fontResolver));
        return new Fragmenter(measurer, document.Compatibility).Layout(
            document.Elements,
            document.PageSettings,
            document.Header,
            document.Footer,
            document.FirstPageHeader,
            document.FirstPageFooter,
            document.EvenPageHeader,
            document.EvenPageFooter,
            bookmarks: document.Bookmarks);
    }

    // The page each paragraph starts on, read straight off the laid-out tree: a placed line is
    // anchored back to the paragraph it came from, and the page it sits on knows its own number. A
    // paragraph split across a page boundary contributes lines to both, so the lowest page wins —
    // a cross-reference points at where the paragraph begins. That minimum is also what keeps a
    // w:tblHeader row honest: its cells are re-emitted on every continuation page carrying the same
    // ParagraphElement instances, and a bookmark in one belongs to the page the header first drew on.
    static Dictionary<ParagraphElement, int> ParagraphPages(ParsedDocument document, ImageExportOptions options)
    {
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        var laidOut = Layout(document, options, fontResolver);

        var pages = new Dictionary<ParagraphElement, int>();
        foreach (var page in laidOut.Pages)
        {
            foreach (var line in Lines(page.Items))
            {
                // A paragraph holding a page field is placed as the copy the layout rebuilt it as,
                // and a bookmark knows the parsed one
                var paragraph = line.Paragraph.Parsed;
                if (!pages.TryGetValue(paragraph, out var existing) ||
                    page.Number < existing)
                {
                    pages[paragraph] = page.Number;
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
        HtmlExporter.Export(Parse(docxStream, options?.DefaultFont, options?.UseLetterPageSize, culture: options?.Culture), options);

    /// <summary>Converts a DOCX file to Markdown.</summary>
    public static string ConvertToMarkdown(string docxPath, MarkdownExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToMarkdown(stream, options);
    }

    /// <summary>Converts a DOCX stream to Markdown.</summary>
    public static string ConvertToMarkdown(Stream docxStream, MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Parse(docxStream, options?.DefaultFont, options?.UseLetterPageSize, culture: options?.Culture), options);

    // captureSources stamps every run with where its text came from in the package (Run.Source). Only
    // the viewer asks for it, to tie a place on a page back to the markup a comment is anchored in.
    internal static ParsedDocument Parse(Stream docxStream, string? defaultFont, bool? useLetterPageSize = null, bool captureSources = false, CultureInfo? culture = null) =>
        new DocumentParser(defaultFont ?? DefaultFontSettings.CustomizedDefaultFont, useLetterPageSize, captureSources, culture).Parse(docxStream);

    // A package someone else holds open, read as it stands and left as it was (DocumentParser.Parse).
    internal static ParsedDocument Parse(WordprocessingDocument document, string? defaultFont, bool? useLetterPageSize = null, bool captureSources = false, CultureInfo? culture = null) =>
        new DocumentParser(defaultFont ?? DefaultFontSettings.CustomizedDefaultFont, useLetterPageSize, captureSources, culture).Parse(document);

    /// <summary>
    /// Renders a parsed document, invoking <paramref name="pageCallback"/> for each page (the
    /// callback receives an action that writes the PNG data to a destination stream).
    /// </summary>
    /// <returns>Number of pages produced.</returns>
    private protected abstract int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback);
}
