namespace Morph;

/// <summary>
/// Converts HTML content to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class ImageSharpHtmlConverter : HtmlConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        using var context = new ImageSharpRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new ImageSharpPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
