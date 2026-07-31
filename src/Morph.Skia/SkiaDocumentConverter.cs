namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SkiaSharp.
/// </summary>
public sealed class SkiaDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        // Step 6 raster cutover (docs/layout-engine-proposal.md): the engine paginates and SkiaPainter draws
        // the documents it covers — the default now that it covers 98.8% of the corpus at production parity;
        // everything else falls through to the production SkiaPageRenderer path. MORPH_SKIA_ENGINE=off forces
        // the production path (a kill switch while the last emission gaps — warp WordArt, float wrap — close).
        if (Environment.GetEnvironmentVariable("MORPH_SKIA_ENGINE") != "off" && EngineCoverage.Covers(document))
        {
            return RenderViaEngine(document, options, pageCallback);
        }

        var totalPageCount = CountPagesIfRequired(document, options);

        using var context = new SkiaRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering)
        {
            TotalPageCount = totalPageCount
        };
        using var renderer = new SkiaPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }

    // Paginate with the backend-independent Fragmenter and draw with SkiaPainter, in place of
    // SkiaPageRenderer + PageRendererBase + TextRenderer. Internal so a test can drive the engine path
    // directly without the process-global MORPH_SKIA_ENGINE toggle. The engine knows its own page total
    // (LaidOutDocument.Pages.Count), so no NUMPAGES pre-count pass runs here.
    internal static int RenderViaEngine(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(fontResolver));
        var laidOut = new Fragmenter(measurer).Layout(
            document.Elements,
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

    // A NUMPAGES/SECTIONPAGES field needs the final page total, which is only known after the
    // document is laid out. Run a counting pass first (no PNG encoding) so the real render can
    // substitute the total. Documents without such a field render in a single pass.
    static int CountPagesIfRequired(ParsedDocument document, ImageExportOptions options)
    {
        if (!document.RequiresTotalPageCount)
        {
            return 0;
        }

        using var context = new SkiaRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new SkiaPageRenderer(context) {CountOnly = true};
        return renderer.RenderDocument(document, _ => { });
    }
}
