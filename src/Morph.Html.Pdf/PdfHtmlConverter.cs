namespace Morph;

/// <summary>
/// Converts HTML content to vector-text PDF using PdfSharp.
/// </summary>
public sealed class PdfHtmlConverter
{
    /// <summary>Converts an HTML string to a PDF byte array.</summary>
    public static async Task<byte[]> ConvertToPdf(string html, ConversionOptions? options = null, Cancel cancel = default)
    {
        options ??= new();
        var elements = await HtmlParser.Parse(html, cancel);
        var document = new ParsedDocument
        {
            PageSettings = new()
            {
                WidthPoints = DefaultPageSize.WidthPoints,
                HeightPoints = DefaultPageSize.HeightPoints,
                MarginTop = 72,
                MarginBottom = 72,
                MarginLeft = 72,
                MarginRight = 72
            },
            Elements = elements
        };
        return PdfRenderer.Render(document, options);
    }
}
