namespace Morph;

/// <summary>Converts Excel workbooks to PNG images using ImageSharp.</summary>
public sealed class ImageSharpExcelConverter : ExcelConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        ImageSharpDocumentConverter.RenderPagesCounted(document, options, pageCallback);
}
