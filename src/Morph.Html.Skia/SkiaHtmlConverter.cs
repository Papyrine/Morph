namespace Morph;

/// <summary>
/// Converts HTML content to PNG images using SkiaSharp.
/// </summary>
public sealed class SkiaHtmlConverter : HtmlConverter
{
    private protected override int RenderPages(ParsedDocument document, ConversionOptions options, Action<Action<Stream>> pageCallback)
    {
        using var context = new SkiaRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new SkiaPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
