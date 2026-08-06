namespace Morph;

/// <summary>
/// Converts HTML content to PNG images using SixLabors.ImageSharp.
/// </summary>
public sealed class ImageSharpHtmlConverter : HtmlConverter
{
    // An HTML source parses to the same ParsedDocument model as DOCX, so it paginates through the
    // same layout engine seam.
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        ImageSharpDocumentConverter.RenderPagesCounted(document, options, pageCallback);
}
