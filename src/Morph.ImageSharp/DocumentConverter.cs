namespace WordRender.ImageSharp;

/// <summary>
/// Converts DOCX documents to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class DocumentConverter : WordRender.DocumentConverter
{
    private protected override IReadOnlyList<byte[]> RenderToImageData(ParsedDocument document, ConversionOptions options)
    {
        using var context = new RenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale, options.FontFallback);
        using var renderer = new PageRenderer(context);

        var pages = renderer.RenderDocument(document);

        var imageData = new List<byte[]>();

        foreach (var page in pages)
        {
            using var ms = new MemoryStream();
            page.SaveAsPng(ms);
            imageData.Add(ms.ToArray());
            page.Dispose();
        }

        return imageData;
    }
}
