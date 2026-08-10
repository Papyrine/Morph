namespace Morph;

/// <summary>
/// Converts PowerPoint presentations to PNG images using SkiaSharp, one image per slide.
/// </summary>
public sealed class SkiaPowerPointConverter : PowerPointConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        SkiaDocumentConverter.RenderPagesCounted(document, options, pageCallback);
}
