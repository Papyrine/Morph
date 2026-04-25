namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class ImageSharpDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ConversionOptions options, Action<Action<Stream>> pageCallback)
    {
        using var context = new ImageSharpRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new ImageSharpPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }
}
