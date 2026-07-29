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

            // Page background fill (w:background) behind everything — without it a dark-themed page's
            // white text lands invisibly on white.
            var background = laidOutPage.Settings.BackgroundColorHex;
            if (!string.IsNullOrEmpty(background))
            {
                graphics.DrawRectangle(context.GetBrush(PdfRenderContext.ParseColor(background)), 0, 0, page.Width.Point, page.Height.Point);
            }

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
            case PlacedImage image:
                PaintImage(context, graphics, image);
                break;
            case PlacedShape shape:
                PaintShape(context, graphics, shape);
                break;

            // Rules are a later slice.
        }
    }

    static void PaintLine(PdfRenderContext context, XGraphics graphics, PlacedLine line)
    {
        var ascent = line.Baseline - line.Y;
        foreach (var run in line.Runs)
        {
            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var properties = run.Properties;
            var color = PdfRenderContext.ParseColor(properties.ColorHex);

            // Highlight (w:highlight / run shading) fills behind the glyphs, over the line box.
            if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
            {
                graphics.DrawRectangle(context.GetBrush(PdfRenderContext.ParseColor(properties.BackgroundColorHex)), run.X, line.Y, run.Width, line.Height);
            }

            DrawTracked(graphics, run.Text, context.GetFont(properties), context.GetBrush(color), run.X, line.Baseline, properties.CharacterSpacingPoints);

            // Underline below the baseline, strike through the x-height — geometry from PdfTextEngine.
            var strokeWidth = Math.Max(0.5, properties.FontSizePoints / 16);
            if (properties.Underline)
            {
                var underlineY = line.Baseline + properties.FontSizePoints * 0.12;
                graphics.DrawLine(context.GetPen(color, strokeWidth), run.X, underlineY, run.X + run.Width, underlineY);
            }

            if (properties.Strikethrough)
            {
                var strikeY = line.Baseline - ascent * 0.3;
                graphics.DrawLine(context.GetPen(color, strokeWidth), run.X, strikeY, run.X + run.Width, strikeY);
            }
        }

        foreach (var image in line.Images)
        {
            PaintImage(context, graphics, image);
        }
    }

    // Draws text, spreading each character by w:spacing tracking (letter-spacing). The run's placed width
    // already includes the tracking (the canonical measurer widened it), so a following run starts past it.
    // Mirrors PdfTextEngine.DrawTrackedString — per-glyph, so surrogate pairs stay intact.
    static void DrawTracked(XGraphics graphics, string text, XFont font, XBrush brush, double penX, double baseline, double trackingPoints)
    {
        if (trackingPoints == 0 || text.Length <= 1)
        {
            graphics.DrawString(text, font, brush, new XPoint(penX, baseline), baselineFormat);
            return;
        }

        var x = penX;
        for (var i = 0; i < text.Length; i++)
        {
            var length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            var piece = text.Substring(i, length);
            graphics.DrawString(piece, font, brush, new XPoint(x, baseline), baselineFormat);
            x += graphics.MeasureString(piece, font).Width + trackingPoints;
            i += length - 1;
        }
    }

    static void PaintImage(PdfRenderContext context, XGraphics graphics, PlacedImage image)
    {
        if (image.Data.Length == 0)
        {
            return;
        }

        try
        {
            graphics.DrawImage(context.GetImage(image.Data), image.X, image.Y, image.Width, image.Height);
        }
        catch
        {
            // Undecodable image bytes: skip this image rather than fail the whole paint.
        }
    }

    // A behind-text floating shape: solid fill and outline of either its freeform contours (subpaths,
    // scaled from the unit square into the box with flip/rotation baked in by BuildShapePath) or, absent
    // those, its preset box (rect or ellipse). Gradient and image fills are later slices.
    static void PaintShape(PdfRenderContext context, XGraphics graphics, PlacedShape placed)
    {
        var shape = placed.Shape;
        if (shape.Gradient != null || shape.ImageData is { Length: > 0 })
        {
            return;
        }

        var fill = shape.FillColorHex is { } fillHex ? context.GetBrush(PdfRenderContext.ParseColor(fillHex)) : null;
        var pen = shape.LineColorHex is { } lineHex ? context.GetPen(PdfRenderContext.ParseColor(lineHex), Math.Max(0.5, shape.LineWidthPoints ?? 1)) : null;
        if (fill == null && pen == null)
        {
            return;
        }

        if (shape.Subpaths is { Count: > 0 })
        {
            var path = PdfPageRenderer.BuildShapePath(shape, placed.X, placed.Y, placed.Width, placed.Height);
            if (fill != null)
            {
                graphics.DrawPath(fill, path);
            }

            if (pen != null)
            {
                graphics.DrawPath(pen, path);
            }

            return;
        }

        var box = new XRect(placed.X, placed.Y, placed.Width, placed.Height);
        if (shape.Preset == PresetShape.Ellipse)
        {
            if (fill != null)
            {
                graphics.DrawEllipse(fill, box);
            }

            if (pen != null)
            {
                graphics.DrawEllipse(pen, box);
            }

            return;
        }

        if (fill != null)
        {
            graphics.DrawRectangle(fill, box);
        }

        if (pen != null)
        {
            graphics.DrawRectangle(pen, box);
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
