namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class ImageSharpDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        RenderPagesCounted(document, options, pageCallback);

    // Paginate with the backend-independent Fragmenter and draw with ImageSharpPainter — the one
    // raster path since the production ImageSharpPageRenderer + TextRenderer were deleted (step 7 of
    // docs/layout-engine.md). The engine knows its own page total
    // (LaidOutDocument.Pages.Count), so no NUMPAGES pre-count pass runs here; the count is returned
    // for the callers that need it. Internal so tests can drive it with a synthesized ParsedDocument.
    internal static int RenderPagesCounted(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        var laidOut = Layout(document, options);
        using var context = CreateContext(document, options);
        ImageSharpPainter.Paint(laidOut, context, options.Crop, pageCallback);
        return laidOut.Pages.Count;
    }

    // The two halves of RenderPagesCounted, split so a caller that paints pages on demand (the Blazor
    // viewer: lay out once, paint whichever page scrolls into view at whatever resolution the zoom asks
    // for) runs the one canonical layout recipe rather than a copy of it. Layout is DPI-independent —
    // the tree is in points — so the same LaidOutDocument serves every context CreateContext makes.
    internal static LaidOutDocument Layout(ParsedDocument document, ImageExportOptions options)
    {
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(fontResolver), options.FontWidthScale, document.Compatibility.CompatibilityMode);
        return new Fragmenter(measurer).Layout(
            document.Elements,
            document.PageSettings,
            document.Header,
            document.Footer,
            document.FirstPageHeader,
            document.FirstPageFooter,
            document.EvenPageHeader,
            document.EvenPageFooter,
            DocumentNotes.From(document))
            .Restrict(options.Pages);
    }

    internal static ImageSharpRenderContext CreateContext(ParsedDocument document, ImageExportOptions options) =>
        new(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
}
