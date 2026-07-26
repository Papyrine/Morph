/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> onto a PDF — the layout engine's first
/// painter (<c>docs/layout-engine-proposal.md</c>, step 5). It performs **no** measurement and **no**
/// pagination: every page size, line and run position comes from the tree the <c>Fragmenter</c> already
/// produced, so a paint is a pure draw pass. This proves the tree drives real output.
///
/// <para>This first slice draws paragraph text — one <see cref="PlacedRun"/> per line at the line's
/// baseline, in its resolved font and colour. Deferred to later slices: table rows
/// (<see cref="PlacedTableRow"/>), paragraph/run decorations (underline, strike, highlight, borders,
/// shading), list markers, tabs, images and shapes, and per-run/per-glyph fidelity (mixed-format lines
/// and canonical advances) — until those land, the normal <c>PdfRenderer</c> stays the production path.</para>
/// </summary>
static class PdfPainter
{
    // Positions text by its baseline at the drawn point, matching how the tree records PlacedLine.Baseline.
    static readonly XStringFormat baselineFormat = new() { LineAlignment = XLineAlignment.BaseLine };

    public static PdfDocument Paint(LaidOutDocument document, string? fontDirectory)
    {
        // The context is created once for the font resolver, cache and PdfDocument; its PageSettings is
        // only a constructor formality here (each page's size comes from the tree, positions are absolute,
        // and the painter never measures), so the first page's settings serve.
        var settings = document.Pages.Count > 0 ? document.Pages[0].Settings : new PageSettings();
        var context = new PdfRenderContext(settings, compatibility: null, fontWidthScale: 1, fontFallback: null, fontDirectory);

        foreach (var laidOutPage in document.Pages)
        {
            var page = context.Document.AddPage();
            page.Width = XUnit.FromPoint(laidOutPage.Settings.WidthPoints);
            page.Height = XUnit.FromPoint(laidOutPage.Settings.HeightPoints);

            using var graphics = XGraphics.FromPdfPage(page);
            foreach (var item in laidOutPage.Items)
            {
                if (item is PlacedLine line)
                {
                    PaintLine(context, graphics, line);
                }

                // PlacedTableRow and other placed-item kinds are later slices.
            }
        }

        return context.Document;
    }

    static void PaintLine(PdfRenderContext context, XGraphics graphics, PlacedLine line)
    {
        foreach (var run in line.Runs)
        {
            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var font = context.GetFont(run.Properties);
            var brush = context.GetBrush(PdfRenderContext.ParseColor(run.Properties.ColorHex));
            graphics.DrawString(run.Text, font, brush, new XPoint(run.X, line.Baseline), baselineFormat);
        }
    }
}
