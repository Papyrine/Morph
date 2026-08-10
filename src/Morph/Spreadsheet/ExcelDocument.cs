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
    /// <param name="xlsxPath">The workbook to read.</param>
    /// <param name="defaultFont">Overrides the fallback family for text the workbook leaves unstyled.</param>
    /// <param name="fontDirectory">
    /// Restricts font resolution to this directory, as <see cref="ExportOptions.FontDirectory"/> does.
    /// It belongs on the PARSE rather than only on the export because Excel measures column widths in
    /// glyphs of the workbook's body font, so which face resolves decides the grid's geometry. Pass the
    /// same value here as on the export options, or the two passes will disagree.
    /// </param>
    public ExcelDocument(string xlsxPath, string? defaultFont = null, string? fontDirectory = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        Document = ExcelConverter.Parse(stream, defaultFont, fontDirectory);
    }

    /// <summary>Parses an XLSX stream.</summary>
    /// <param name="xlsxStream">The workbook to read.</param>
    /// <param name="defaultFont">Overrides the fallback family for text the workbook leaves unstyled.</param>
    /// <param name="fontDirectory">Restricts font resolution to this directory; see the file overload.</param>
    public ExcelDocument(Stream xlsxStream, string? defaultFont = null, string? fontDirectory = null) =>
        Document = ExcelConverter.Parse(xlsxStream, defaultFont, fontDirectory);

    /// <summary>Exports the workbook as a normalized semantic HTML fragment.</summary>
    public string ExportToHtml(HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Document, options);

    /// <summary>Exports the workbook as Markdown.</summary>
    public string ExportToMarkdown(MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Document, options);
}
