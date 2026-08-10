namespace Morph;

/// <summary>
/// Converts PowerPoint presentations to PNG images using ImageSharp, one image per slide.
/// </summary>
public sealed class ImageSharpPowerPointConverter : PowerPointConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        ImageSharpDocumentConverter.RenderPagesCounted(document, options, pageCallback);
}
