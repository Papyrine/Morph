namespace WordRender.Skia;

/// <summary>
/// Converts DOCX documents to PNG images using SkiaSharp.
/// </summary>
public sealed class DocumentConverter : WordRender.DocumentConverter
{
    private protected override IReadOnlyList<byte[]> RenderToImageData(ParsedDocument document, ConversionOptions options)
    {
        using var context = new RenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale);
        using var renderer = new PageRenderer(context);

        var pages = renderer.RenderDocument(document);

        var imageData = new List<byte[]>();

        foreach (var page in pages)
        {
            using var image = SKImage.FromBitmap(page);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            imageData.Add(data.ToArray());
            page.Dispose();
        }

        return imageData;
    }
}
