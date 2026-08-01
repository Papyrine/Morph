namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class ImageSharpDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        // Step 6 raster cutover (docs/layout-engine-proposal.md): the engine paginates and ImageSharpPainter
        // draws the documents it covers — the default now that it covers 98.8% of the corpus at production
        // parity; everything else falls through to the production ImageSharpPageRenderer. MORPH_IMAGESHARP_ENGINE=off
        // forces the production path (a kill switch while the last emission gaps — warp WordArt, float wrap — close).
        if (Environment.GetEnvironmentVariable("MORPH_IMAGESHARP_ENGINE") != "off" && EngineCoverage.Covers(document))
        {
            return RenderViaEngine(document, options, pageCallback);
        }

        var totalPageCount = CountPagesIfRequired(document, options);

        using var context = new ImageSharpRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering)
        {
            TotalPageCount = totalPageCount
        };
        using var renderer = new ImageSharpPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }

    // Paginate with the backend-independent Fragmenter and draw with ImageSharpPainter, in place of
    // ImageSharpPageRenderer + PageRendererBase + TextRenderer. Internal so a test drives the engine path
    // without the process-global MORPH_IMAGESHARP_ENGINE toggle.
    internal static int RenderViaEngine(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
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

        using var context = new ImageSharpRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        ImageSharpPainter.Paint(laidOut, context, pageCallback);
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

        using var context = new ImageSharpRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new ImageSharpPageRenderer(context) {CountOnly = true};
        return renderer.RenderDocument(document, _ => { });
    }
}
