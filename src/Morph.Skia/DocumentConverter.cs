namespace WordRender.Skia;

/// <summary>
/// Converts DOCX documents to PNG images using SkiaSharp.
/// </summary>
public sealed class DocumentConverter : WordRender.DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ConversionOptions options, Action<Action<Stream>> pageCallback)
    {
        using var context = new RenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback);
        using var renderer = new PageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
