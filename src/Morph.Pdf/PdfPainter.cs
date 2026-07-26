/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> onto a PDF — the layout engine's first
/// painter (<c>docs/layout-engine-proposal.md</c>, step 5). It performs **no** measurement and **no**
/// pagination: every page size, line and run position comes from the tree the <c>Fragmenter</c> already
/// produced, so a paint is a pure draw pass. This proves the tree drives real output.
///
/// <para>Draws paragraph text (one <see cref="PlacedRun"/> per source run at the line's baseline, in its
/// resolved font and colour) and tables (each <see cref="PlacedCell"/>'s shading, content and borders).
/// Deferred to later slices: paragraph/run decorations (underline, strike, highlight, paragraph borders,
/// shading), list markers, tabs, images and shapes, in-cell vertical alignment and nested tables, and
/// per-glyph advances — until those land, the normal <c>PdfRenderer</c> stays the production path.</para>
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
                PaintItem(context, graphics, item);
            }
        }

        return context.Document;
    }

    static void PaintItem(PdfRenderContext context, XGraphics graphics, PlacedItem item)
    {
        switch (item)
        {
            case PlacedLine line:
                PaintLine(context, graphics, line);
                break;
            case PlacedTableRow row:
                PaintTableRow(context, graphics, row);
                break;

            // Images, rules and shapes are later slices.
        }
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

    // Each cell: shading first, then its content, then its borders on top — Word's cell paint order.
    static void PaintTableRow(PdfRenderContext context, XGraphics graphics, PlacedTableRow row)
    {
        foreach (var cell in row.Cells)
        {
            if (!string.IsNullOrEmpty(cell.BackgroundColorHex))
            {
                graphics.DrawRectangle(context.GetBrush(PdfRenderContext.ParseColor(cell.BackgroundColorHex)), cell.X, cell.Y, cell.Width, cell.Height);
            }

            foreach (var content in cell.Content)
            {
                PaintItem(context, graphics, content);
            }

            if (cell.Borders is { } borders)
            {
                PaintCellBorders(context, graphics, cell, borders);
            }
        }
    }

    static void PaintCellBorders(PdfRenderContext context, XGraphics graphics, PlacedCell cell, CellBorders borders)
    {
        float left = cell.X, top = cell.Y, right = cell.X + cell.Width, bottom = cell.Y + cell.Height;

        if (borders.Top.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Top), left, top, right, top);
        }

        if (borders.Right.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Right), right, top, right, bottom);
        }

        if (borders.Bottom.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Bottom), left, bottom, right, bottom);
        }

        if (borders.Left.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Left), left, top, left, bottom);
        }
    }

    static XPen EdgePen(PdfRenderContext context, BorderEdge edge) =>
        context.GetPen(PdfRenderContext.ParseColor(edge.ColorHex ?? "000000"), Math.Max(0.5, edge.WidthPoints));
}
