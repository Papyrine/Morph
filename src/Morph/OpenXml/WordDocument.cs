namespace Morph;

/// <summary>
/// A parsed Word document that can be exported to multiple formats without re-parsing the source.
/// Construct once from a path or stream, then call as many <c>ExportToXxx</c> methods as needed.
/// </summary>
/// <example>
/// <code>
/// var document = new WordDocument("report.docx");
/// File.WriteAllText("report.html", document.ExportToHtml());
/// File.WriteAllText("report.md",   document.ExportToMarkdown());
/// File.WriteAllBytes("report.pdf", document.ExportToPdf());   // via Morph.Pdf
/// </code>
/// </example>
public sealed class WordDocument
{
    internal ParsedDocument Document { get; }

    /// <summary>Loads a DOCX file from disk.</summary>
    /// <param name="docxPath">Path to the .docx file.</param>
    /// <param name="defaultFont">Optional default font family name override. When null, a
    /// customized <see cref="DefaultFontSettings.DefaultFont"/> is used, else the parser's
    /// built-in.</param>
    public WordDocument(string docxPath, string? defaultFont = null)
    {
        using var stream = File.OpenRead(docxPath);
        Document = new DocumentParser(defaultFont ?? DefaultFontSettings.CustomizedDefaultFont).Parse(stream);
    }

    /// <summary>Loads a DOCX from a stream.</summary>
    public WordDocument(Stream docxStream, string? defaultFont = null) =>
        Document = new DocumentParser(defaultFont ?? DefaultFontSettings.CustomizedDefaultFont).Parse(docxStream);

    /// <summary>Exports the document as a semantic HTML fragment.</summary>
    public string ExportToHtml(HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Document, options);

    /// <summary>Exports the document as Markdown.</summary>
    public string ExportToMarkdown(MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Document, options);
}
