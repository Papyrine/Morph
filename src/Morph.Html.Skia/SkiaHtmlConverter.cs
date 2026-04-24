using WordRender;

namespace Morph;

/// <summary>
/// Converts HTML content to PNG images using SkiaSharp.
/// </summary>
public sealed class SkiaHtmlConverter : HtmlConverter
{
    private protected override int RenderPages(ParsedDocument document, ConversionOptions options, Action<Action<Stream>> pageCallback)
    {
        using var context = new RenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new PageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
