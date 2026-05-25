namespace Morph;

/// <summary>
/// Renders a parsed document to a PDF byte array using PdfSharp. Shared entry point for the
/// DOCX → PDF and HTML → PDF public converters.
/// </summary>
static class PdfRenderer
{
    public static byte[] Render(ParsedDocument document, ConversionOptions? options)
    {
        options ??= new();
        var context = new PdfRenderContext(
            document.PageSettings,
            document.Compatibility,
            options.FontWidthScale,
            options.FontFallback,
            options.FontDirectory);

        var renderer = new PdfPageRenderer(context);
        renderer.RenderDocument(document);

        using var stream = new MemoryStream();
        context.Document.Save(stream, closeStream: false);
        return stream.ToArray();
    }
}
