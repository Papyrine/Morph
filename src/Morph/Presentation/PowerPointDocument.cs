namespace Morph;

/// <summary>
/// A parsed PowerPoint presentation that can be exported to multiple formats without re-parsing the
/// source. Construct once, then call as many <c>ExportToXxx</c> methods as needed.
/// </summary>
/// <example>
/// <code>
/// var deck = new PowerPointDocument("slides.pptx");
/// File.WriteAllText("slides.html", deck.ExportToHtml());
/// File.WriteAllText("slides.md",   deck.ExportToMarkdown());
/// File.WriteAllBytes("slides.pdf", deck.ExportToPdf());   // via Morph.Pdf
/// </code>
/// </example>
public sealed class PowerPointDocument
{
    internal ParsedDocument Document { get; }

    /// <summary>Parses a PPTX file from disk.</summary>
    public PowerPointDocument(string pptxPath, string? defaultFont = null)
    {
        using var stream = File.OpenRead(pptxPath);
        Document = PowerPointConverter.Parse(stream, defaultFont);
    }

    /// <summary>Parses a PPTX stream.</summary>
    public PowerPointDocument(Stream pptxStream, string? defaultFont = null) =>
        Document = PowerPointConverter.Parse(pptxStream, defaultFont);

    /// <summary>Exports the deck as a normalized semantic HTML fragment.</summary>
    public string ExportToHtml(HtmlExportOptions? options = null) =>
        HtmlExporter.Export(Document, options);

    /// <summary>Exports the deck as Markdown.</summary>
    public string ExportToMarkdown(MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(Document, options);
}
