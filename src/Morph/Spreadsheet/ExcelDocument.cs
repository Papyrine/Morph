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
    /// <param name="fontFallback">
    /// Substitutes a family the directory does not hold, as <see cref="ExportOptions.FontFallback"/>
    /// does. It belongs on the parse for the same reason <paramref name="fontDirectory"/> does — it
    /// decides which face the column-width unit is measured from — and must match the export options
    /// just as closely.
    /// </param>
    public ExcelDocument(
        string xlsxPath,
        string? defaultFont = null,
        string? fontDirectory = null,
        Func<string, string?>? fontFallback = null)
    {
        using var stream = File.OpenRead(xlsxPath);
        Document = ExcelConverter.Parse(stream, defaultFont, fontDirectory, fontFallback);
    }

    /// <summary>Parses an XLSX stream.</summary>
    /// <param name="xlsxStream">The workbook to read.</param>
    /// <param name="defaultFont">Overrides the fallback family for text the workbook leaves unstyled.</param>
    /// <param name="fontDirectory">Restricts font resolution to this directory; see the file overload.</param>
    /// <param name="fontFallback">Substitutes a family the directory misses; see the file overload.</param>
    public ExcelDocument(
        Stream xlsxStream,
        string? defaultFont = null,
        string? fontDirectory = null,
        Func<string, string?>? fontFallback = null) =>
        Document = ExcelConverter.Parse(xlsxStream, defaultFont, fontDirectory, fontFallback);

    /// <summary>Exports the workbook as a normalized semantic HTML fragment.</summary>
    public string ExportToHtml(HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Document, options);

    /// <summary>Exports the workbook as Markdown.</summary>
    public string ExportToMarkdown(MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Document, options);
}
