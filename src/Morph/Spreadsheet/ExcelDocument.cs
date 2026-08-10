namespace Morph;

/// <summary>
/// A parsed Excel workbook that can be exported to multiple formats without re-parsing the source.
/// </summary>
/// <example>
/// <code>
/// var workbook = new ExcelDocument("budget.xlsx");
/// File.WriteAllText("budget.html", workbook.ExportToHtml());
/// File.WriteAllText("budget.md",   workbook.ExportToMarkdown());
/// File.WriteAllBytes("budget.pdf", workbook.ExportToPdf());   // via Morph.Pdf
/// </code>
/// </example>
public sealed class ExcelDocument
{
    internal ParsedDocument Document { get; }

    /// <summary>Parses an XLSX file from disk.</summary>
    public ExcelDocument(string xlsxPath, string? defaultFont = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        Document = ExcelConverter.Parse(stream, defaultFont);
    }

    /// <summary>Parses an XLSX stream.</summary>
    public ExcelDocument(Stream xlsxStream, string? defaultFont = null) =>
        Document = ExcelConverter.Parse(xlsxStream, defaultFont);

    /// <summary>Exports the workbook as a normalized semantic HTML fragment.</summary>
    public string ExportToHtml(HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Document, options);

    /// <summary>Exports the workbook as Markdown.</summary>
    public string ExportToMarkdown(MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Document, options);
}
