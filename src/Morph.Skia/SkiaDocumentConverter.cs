namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SkiaSharp.
/// </summary>
public sealed class SkiaDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        using var context = new SkiaRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new SkiaPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
