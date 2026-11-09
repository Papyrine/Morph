namespace Morph;

/// <summary>
/// Converts DOCX documents to PNG images using SkiaSharp.
/// </summary>
public sealed class SkiaDocumentConverter : DocumentConverter
{
    private protected override int RenderPages(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback) =>
        RenderPagesCounted(document, options, pageCallback);

    // Paginate with the backend-independent Fragmenter and draw with SkiaPainter. The engine knows its
    // own page total (LaidOutDocument.Pages.Count), so no NUMPAGES pre-count pass runs here; the count
    // is returned for the callers that need it. Internal so tests can drive it with a synthesized ParsedDocument.
    internal static int RenderPagesCounted(ParsedDocument document, ImageExportOptions options, Action<Action<Stream>> pageCallback)
    {
        using var fontResolver = LayoutFonts.CreateResolver(options.FontDirectory, options.FontFallback);
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(fontResolver), options.FontWidthScale, document.Compatibility, LayoutFonts.Covers(fontResolver));
        var laidOut = new Fragmenter(measurer, document.Compatibility).Layout(
            document.Elements,
            document.PageSettings,
            document.Header,
            document.Footer,
            document.FirstPageHeader,
            document.FirstPageFooter,
            document.EvenPageHeader,
            document.EvenPageFooter,
            DocumentNotes.From(document),
            document.Bookmarks,
            ProgressReports.Layout(options.OnProgress))
            .WithComments(document, measurer, options.ShowComments)
            .FitToContent(options.Dpi)
            .Restrict(options.Pages);

        var cancel = options.Cancellation;
        cancel.ThrowIfCancellationRequested();
        var dpi = options.EffectiveDpi(laidOut.Pages.Select(_ => _.Settings));

        SkiaRenderContext CreateContext() =>
            new(laidOut.ContextSettings(document.PageSettings), dpi, document.Compatibility, options.FontWidthScale, options.FontFallback, options.FontDirectory, options.DeterministicRendering);

        var progress = ProgressReports.Paint(options.OnProgress, laidOut.Pages.Count);
        var parallelism = Math.Min(options.MaxDegreeOfParallelism, laidOut.Pages.Count);
        if (parallelism > 1)
        {
            ParallelPages.Paint(laidOut.Pages, parallelism, CreateContext, (context, page, callback) => SkiaPainter.PaintPage(page, context, options.Crop, callback), pageCallback, cancel, progress);
            return laidOut.Pages.Count;
        }

        using var context = CreateContext();
        foreach (var page in laidOut.Pages)
        {
            cancel.ThrowIfCancellationRequested();
            SkiaPainter.PaintPage(page, context, options.Crop, pageCallback);
            progress?.Painted();
        }

        return laidOut.Pages.Count;
    }
}
