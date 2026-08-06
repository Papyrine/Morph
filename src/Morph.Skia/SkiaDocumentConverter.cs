namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SkiaSharp.
/// </summary>
public sealed class SkiaDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        RenderPagesCounted(document, options, pageCallback);

    // Paginate with the backend-independent Fragmenter and draw with SkiaPainter — the only raster
    // path since the production SkiaPageRenderer + TextRenderer were deleted (step 7 of
    // docs/layout-engine-proposal.md). The engine knows its own page total
    // (LaidOutDocument.Pages.Count), so no NUMPAGES pre-count pass runs here; the count is returned
    // for the callers that need it. Internal so tests can drive it with a synthesized ParsedDocument.
    internal static int RenderPagesCounted(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(fontResolver), options.FontWidthScale);
        var laidOut = new Fragmenter(measurer).Layout(
            NotesAppendix.AppendTo(document),
            document.PageSettings,
            document.Header,
            document.Footer,
            document.FirstPageHeader,
            document.FirstPageFooter,
            document.EvenPageHeader,
            document.EvenPageFooter);

        using var context = new SkiaRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        SkiaPainter.Paint(laidOut, context, pageCallback);
        return laidOut.Pages.Count;
    }
}
