/// <summary>
/// Renders a parsed document to a PDF byte array through Krilla. Shared entry point for the
/// DOCX → PDF and HTML → PDF public converters. Krilla writes the same bytes for the same input — no
/// timestamp, no random identifier, deterministic font subset tags — so the output can be
/// snapshot-tested as it comes.
/// </summary>
static class PdfRenderer
{
    // Paginate with the backend-independent Fragmenter and draw with PdfPainter
    // (docs/layout-engine.md). The engine knows its own page total
    // (LaidOutDocument.Pages.Count), so no NUMPAGES pre-count pass runs here (the deleted
    // CountPagesIfRequired laid the whole document out a second time); PAGE restarts and SECTIONPAGES resolve
    // per section inside the engine (Fragmenter.PageNumbers).
    public static byte[] Render(ParsedDocument document, PdfExportOptions? optionsOrNull, Cancel cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var options = optionsOrNull ?? new PdfExportOptions();
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(fontResolver), options.FontWidthScale, document.Compatibility.SqueezesJustifiedLines);
        var laidOut = new Fragmenter(measurer, document.Compatibility)
            .Layout(
                document.Elements,
                document.PageSettings,
                document.Header,
                document.Footer,
                document.FirstPageHeader,
                document.FirstPageFooter,
                document.EvenPageHeader,
                document.EvenPageFooter,
                DocumentNotes.From(document));
        cancel.ThrowIfCancellationRequested();

        // The painter's context resolves fonts with the conversion's own width scale, fallback and
        // compatibility settings — the same values LayoutFonts gave the measurer above. They have to agree:
        // the measurer decides where a line breaks, the painter draws it, and a painter resolving a different
        // face would put text off its measured line.
        // A page range draws only the pages in it; the page numbers in the PDF run 1..N over them.
        using var context = PdfPainter.NewContext(laidOut, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory);
        PdfPainter.Paint(laidOut, context, options.RasterizeWordArt, options.OnWarning, options.Pages, cancel);
        return context.Document.Finish();
    }
}
