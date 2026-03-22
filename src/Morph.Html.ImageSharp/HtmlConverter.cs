using WordRender;

namespace HtmlRender.ImageSharp;

/// <summary>
/// Converts HTML content to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class HtmlConverter : HtmlRender.HtmlConverter
{
    private protected override int RenderPages(ParsedDocument document, ConversionOptions options, Action<Action<Stream>> pageCallback)
    {
        using var context = new RenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback);
        using var renderer = new PageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
