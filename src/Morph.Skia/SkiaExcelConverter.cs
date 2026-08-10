namespace Morph;

/// <summary>Converts Excel workbooks to PNG images using SkiaSharp.</summary>
public sealed class SkiaExcelConverter : ExcelConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        SkiaDocumentConverter.RenderPagesCounted(document, options, pageCallback);
}
