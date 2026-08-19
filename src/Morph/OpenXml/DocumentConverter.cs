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
        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, options.UseLetterPageSize).Parse(docxStream);
        return PageSink.ToDirectory(
            outputDirectory,
            sink => RenderPages(document, options, sink));
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

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, options.UseLetterPageSize).Parse(docxStream);
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

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, options.UseLetterPageSize).Parse(docxStream);
        if (document.Bookmarks.Count == 0)
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

    // Every placed line under these items, a table cell's included. A cell's lines hang off its
    // PlacedTableRow rather than sitting on the page, so reading only the top level leaves every
    // paragraph inside a table out of the map below — and a bookmark anchored there comes back with
    // no page at all rather than a wrong one, which a caller writing a PAGEREF renders as the
    // field's placeholder text. Measured on a 316-paragraph report: 199 of its paragraphs resolved
    // and the 117 that did not were exactly the ones in tables, so a summary table cross-referencing
    // its own rows resolved nothing.
    //
    // This is the mirror of Flatten above, which already counts cell paragraphs on the ordinal side
    // of the join — the two sides disagreed about table content in opposite directions, and the
    // ordinal side was the one that was right. The recursion is real rather than one level deep
    // because a cell can hold a table of its own.
    //
    // Floating text boxes and frames need no case: PlaceTextBox and PlaceFrame add their content to
    // the page as body floats, so those lines are already top-level items here.
    static IEnumerable<PlacedLine> Lines(IEnumerable<PlacedItem> items)
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
            }
        }
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
            foreach (var line in Lines(page.Items))
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
        HtmlExporter.Export(Parse(docxStream, options?.DefaultFont, options?.UseLetterPageSize), options);

    /// <summary>Converts a DOCX file to Markdown.</summary>
    public static string ConvertToMarkdown(string docxPath, MarkdownExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToMarkdown(stream, options);
    }

    /// <summary>Converts a DOCX stream to Markdown.</summary>
    public static string ConvertToMarkdown(Stream docxStream, MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Parse(docxStream, options?.DefaultFont, options?.UseLetterPageSize), options);

    internal static ParsedDocument Parse(Stream docxStream, string? defaultFont, bool? useLetterPageSize = null) =>
        new DocumentParser(defaultFont ?? DefaultFontSettings.CustomizedDefaultFont, useLetterPageSize).Parse(docxStream);

    /// <summary>
    /// Renders a parsed document, invoking <paramref name="pageCallback"/> for each page (the
    /// callback receives an action that writes the PNG data to a destination stream).
    /// </summary>
    /// <returns>Number of pages produced.</returns>
    private protected abstract int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback);
}
