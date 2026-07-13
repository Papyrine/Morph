namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class ImageSharpDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        var totalPageCount = CountPagesIfRequired(document, options);

        using var context = new ImageSharpRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering)
        {
            TotalPageCount = totalPageCount
        };
        using var renderer = new ImageSharpPageRenderer(context);

        return renderer.RenderDocument(document, pageCallback);
    }

    // A NUMPAGES/SECTIONPAGES field needs the final page total, which is only known after the
    // document is laid out. Run a counting pass first (no PNG encoding) so the real render can
    // substitute the total. Documents without such a field render in a single pass.
    static int CountPagesIfRequired(ParsedDocument document, ImageExportOptions options)
    {
        if (!document.RequiresTotalPageCount)
        {
            return 0;
        }

        using var context = new ImageSharpRenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);
        using var renderer = new ImageSharpPageRenderer(context) {CountOnly = true};
        return renderer.RenderDocument(document, _ => { });
    }
}
