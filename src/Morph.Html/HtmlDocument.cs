namespace Morph;

/// <summary>
/// A parsed HTML document that can be exported to multiple formats without re-parsing the source.
/// Use <see cref="LoadAsync"/> to construct (HTML parsing is async via AngleSharp), then call as
/// many <c>ExportToXxx</c> methods as needed.
/// </summary>
/// <example>
/// <code>
/// var document = await HtmlDocument.LoadAsync(html);
/// File.WriteAllText("page.html", document.ExportToHtml());
/// File.WriteAllText("page.md",   document.ExportToMarkdown());
/// File.WriteAllBytes("page.pdf", document.ExportToPdf());   // via Morph.Html.Pdf
/// </code>
/// </example>
public sealed class HtmlDocument
{
    internal ParsedDocument Document { get; }

    HtmlDocument(ParsedDocument document) => Document = document;

    /// <summary>Parses an HTML string. Asynchronous because the underlying AngleSharp parser is.</summary>
    public static async Task<HtmlDocument> LoadAsync(string html, Cancel cancel = default)
    {
        var elements = await HtmlParser.Parse(html, cancel);
        return new(new()
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
        });
    }

    /// <summary>Exports the document as a normalized semantic HTML fragment.</summary>
    public string ExportToHtml(HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Document, options);

    /// <summary>Exports the document as Pandoc-flavoured Markdown.</summary>
    public string ExportToMarkdown(MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Document, options);
}
