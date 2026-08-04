namespace Morph;

/// <summary>
/// Converts HTML content to PNG images using SkiaSharp.
/// </summary>
public sealed class SkiaHtmlConverter : HtmlConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        // The same seam as SkiaDocumentConverter: an HTML source parses to the same ParsedDocument model, so
        // a covered document paginates through the one layout engine and only an uncovered one falls through
        // to the production renderer. Until this, HTML→PNG bypassed the engine entirely — the last raster
        // path still constructing a page renderer unconditionally.
        if (Environment.GetEnvironmentVariable("MORPH_SKIA_ENGINE") != "off" && EngineCoverage.Covers(document))
        {
            return SkiaDocumentConverter.RenderViaEngine(document, options, pageCallback);
        }

        using var context = new SkiaRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new SkiaPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
