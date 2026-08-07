/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> onto a PDF — the layout engine's first
/// painter (<c>docs/layout-engine.md</c>, step 5). It performs **no** measurement and **no**
/// pagination: every page size, line and run position comes from the tree the <c>Fragmenter</c> already
/// produced, so a paint is a pure draw pass. This proves the tree drives real output.
///
/// <para>Draws paragraph text (one <see cref="PlacedRun"/> per source run at the line's baseline, in its
/// resolved font and colour), tables (each <see cref="PlacedCell"/>'s shading, content and borders),
/// run decorations, list markers, tabs and leaders, images, shapes and nested tables — the feature log
/// is in the proposal doc. Still deferred: per-glyph advances (a run anchors at its canonical start and
/// the font library fills it), image recolour/duotone, cell-anchored image-fill shapes (a body one is
/// routed to a <see cref="PlacedImage"/> and draws), and foreground header/footer
/// images. <c>PdfRenderer</c> routes every document here — this is the only PDF path since the
/// 2026-08-06 flip and the step-8.3 deletion of <c>PdfTextEngine</c>/<c>PdfPageRenderer</c>.</para>
/// </summary>
static class PdfPainter
{
    // Positions text by its baseline at the drawn point, matching how the tree records PlacedLine.Baseline.
    static readonly XStringFormat baselineFormat = new() { LineAlignment = XLineAlignment.BaseLine };

    /// <summary>
    /// The context is created once for the font resolver, cache and PdfDocument; its PageSettings is only a
    /// constructor formality here (each page's size comes from the tree, positions are absolute, and the
    /// painter never measures), so the first page's settings serve. Prefer the overload taking a context when
    /// the conversion's font settings matter — this one resolves at the defaults.
    /// </summary>
    public static PdfDocument Paint(LaidOutDocument document, string? fontDirectory) =>
        Paint(document, NewContext(document, compatibility: null, fontWidthScale: 1, fontFallback: null, fontDirectory));

    public static PdfRenderContext NewContext(
        LaidOutDocument document,
        CompatibilitySettings? compatibility,
        double fontWidthScale,
        Func<string, string?>? fontFallback,
        string? fontDirectory) =>
        new(document.Pages.Count > 0 ? document.Pages[0].Settings : new PageSettings(),
            compatibility,
            fontWidthScale,
            fontFallback,
            fontDirectory);

    /// <summary>
    /// Draws the tree onto <paramref name="context"/>'s document. The caller owns the context so it can
    /// resolve fonts with the conversion's own width scale, fallback and compatibility settings — the
    /// measurer already does, and a painter resolving a different face would draw text off its measured
    /// line — and so it can dispose the image cache after saving.
    /// </summary>
    public static PdfDocument Paint(
        LaidOutDocument document,
        PdfRenderContext context,
        bool rasterizeWordArt = true,
        Action<ExportWarning>? onWarning = null)
    {
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
                PaintItem(context, graphics, item, rasterizeWordArt, onWarning);
            }
        }

        return context.Document;
    }

    static void PaintItem(PdfRenderContext context, XGraphics graphics, PlacedItem item, bool rasterizeWordArt = true, Action<ExportWarning>? onWarning = null)
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
            case PlacedWordArt wordArt:
                PaintWordArt(context, graphics, wordArt, rasterizeWordArt, onWarning);
                break;
            case PlacedShading shading:
                graphics.DrawRectangle(context.GetBrush(PdfRenderContext.ParseColor(shading.ColorHex)), shading.X, shading.Y, shading.Width, shading.Height);
                break;
            case PlacedBorder border:
                PaintBorder(context, graphics, border);
                break;
        }
    }

    // Resolution the WordArt raster is produced at. PDF is vector, so the shape has no natural pixel size;
    // 300 dpi keeps the embedded bitmap sharp in print without bloating the file — the same value the
    // deleted production PdfPageRenderer embedded at.
    const int wordArtRasterDpi = 300;

    // A warped WordArt figure. PdfSharp cannot draw the warp geometry, so — exactly as the deleted
    // production TryEmbedWordArt did — a raster backend rasterizes the shape to a transparent PNG
    // (discovered reflectively, so Morph.Pdf keeps no compile-time dependency on either engine) and the PNG
    // is embedded. The PNG is the box surrounded by WordArtRasterPage.Padding on every side, since several
    // warps draw past the declared box, so the draw origin steps back by that padding and the rectangle grows
    // to match — leaving the box region at (X, Y) with the overflow spilling onto the page. When no raster
    // backend is present the shape is dropped rather than drawn wrong; the caller reserved its height either
    // way, so pagination is unaffected.
    static void PaintWordArt(PdfRenderContext context, XGraphics graphics, PlacedWordArt wordArt, bool rasterizeWordArt, Action<ExportWarning>? onWarning)
    {
        if (!rasterizeWordArt)
        {
            // The conversion asked for no rasterized WordArt (a text-only or size-sensitive PDF), so the
            // warp is dropped rather than approximated. Its height was reserved either way, so the flow
            // below it is unaffected.
            return;
        }

        if (WordArtRasterizerFactory.TryGet() is not { } rasterizer)
        {
            onWarning?.Invoke(new(WarningKind.UnsupportedElement,
                "WordArt could not be drawn in the PDF: no raster backend (Morph.Skia or Morph.ImageSharp) is loaded."));
            return;
        }

        var options = new WordArtRasterOptions
        {
            Dpi = wordArtRasterDpi,
            FontWidthScale = context.FontWidthScale,
            FontFallback = context.FontFallback,
            FontDirectory = context.FontDirectory,
            Deterministic = true
        };

        byte[]? png;
        try
        {
            png = rasterizer.Render(wordArt.Visual, options);
        }
        catch (Exception exception)
        {
            onWarning?.Invoke(new(WarningKind.UnsupportedElement,
                $"WordArt could not be rasterized for the PDF and was dropped: {exception.Message}"));
            return;
        }

        if (png == null)
        {
            return;
        }

        var pad = WordArtRasterPage.Padding(wordArt.Visual);
        graphics.DrawImage(context.GetImage(png), wordArt.X - pad, wordArt.Y - pad, wordArt.Width + 2 * pad, wordArt.Height + 2 * pad);
    }

    static void PaintLine(PdfRenderContext context, XGraphics graphics, PlacedLine line)
    {
        var ascent = line.Baseline - line.Y;
        foreach (var run in line.Runs)
        {
            // A tab-leader filler fills its span with the leader (tiled glyph, or a baseline rule for
            // underscore) instead of text — its Text is empty.
            if (run.Leader != TabLeader.None)
            {
                DrawLeader(context, graphics, run, line.Baseline);
                continue;
            }

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

            // Underline below the baseline, strike through the x-height — geometry carried over from the
            // deleted PdfTextEngine.
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
    // Per-glyph, so surrogate pairs stay intact.
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

    // Fills a tab-leader gap: a baseline rule for underscore, otherwise the leader glyph tiled across the
    // span (leaving ~one glyph of trailing padding).
    static void DrawLeader(PdfRenderContext context, XGraphics graphics, PlacedRun run, double baseline)
    {
        if (run.Width <= 0)
        {
            return;
        }

        var color = PdfRenderContext.ParseColor(run.Properties.ColorHex);
        var fontSize = run.Properties.FontSizePoints;

        if (run.Leader == TabLeader.Underscore)
        {
            var underscoreY = baseline + fontSize * 0.12;
            graphics.DrawLine(context.GetPen(color, Math.Max(0.5, fontSize / 16)), run.X, underscoreY, run.X + run.Width, underscoreY);
            return;
        }

        var leaderChar = run.Leader switch
        {
            TabLeader.Hyphen => '-',
            TabLeader.MiddleDot => '·',
            TabLeader.Heavy => '—',
            _ => '.'
        };

        var font = context.GetFont(run.Properties);
        var glyphWidth = graphics.MeasureString(leaderChar.ToString(), font).Width;
        if (glyphWidth <= 0)
        {
            return;
        }

        // Word spaces leader dots about a glyph-width apart, so tile at ~2x the advance and draw each glyph
        // at its own X — adjacent glyphs read as a dense line, not the spaced dots Word draws.
        var spacing = glyphWidth * 2;
        var count = (int) Math.Floor((run.Width - glyphWidth) / spacing) + 1;
        if (count <= 0)
        {
            return;
        }

        var brush = context.GetBrush(color);
        var glyph = leaderChar.ToString();
        for (var index = 0; index < count; index++)
        {
            graphics.DrawString(glyph, font, brush, new XPoint(run.X + index * spacing, baseline), baselineFormat);
        }
    }

    static void PaintImage(PdfRenderContext context, XGraphics graphics, PlacedImage image)
    {
        if (image.ShapeGroup is { } group)
        {
            PaintInlineGroup(context, graphics, image, group);
            return;
        }

        if (image.Data is not { Length: > 0 } data)
        {
            return;
        }

        try
        {
            var decoded = context.GetImage(data);
            // a:xfrm transforms happen about the box centre, then draw; an ellipse/freeform clip is an
            // alternative (Word does not combine the two).
            if (Math.Abs(image.RotationDegrees) > 0.01 || image.FlipHorizontal || image.FlipVertical)
            {
                var centerX = image.X + image.Width / 2;
                var centerY = image.Y + image.Height / 2;
                var state = graphics.Save();
                if (Math.Abs(image.RotationDegrees) > 0.01)
                {
                    graphics.RotateAtTransform(image.RotationDegrees, new XPoint(centerX, centerY));
                }

                if (image.FlipHorizontal || image.FlipVertical)
                {
                    graphics.TranslateTransform(centerX, centerY);
                    graphics.ScaleTransform(image.FlipHorizontal ? -1 : 1, image.FlipVertical ? -1 : 1);
                    graphics.TranslateTransform(-centerX, -centerY);
                }

                DrawIntoBox(graphics, decoded, image);
                graphics.Restore(state);
            }
            else if (image.ClipToEllipse || image.ClipSubpaths != null)
            {
                var state = graphics.Save();
                var clipPath = new XGraphicsPath();
                if (image.ClipToEllipse)
                {
                    clipPath.AddEllipse(image.X, image.Y, image.Width, image.Height);
                }
                else
                {
                    foreach (var contour in image.ClipSubpaths!)
                    {
                        var points = new XPoint[contour.Count];
                        for (var pointIndex = 0; pointIndex < contour.Count; pointIndex++)
                        {
                            var (unitX, unitY) = contour[pointIndex];
                            points[pointIndex] = new XPoint(image.X + unitX * image.Width, image.Y + unitY * image.Height);
                        }

                        clipPath.AddPolygon(points);
                    }
                }

                graphics.IntersectClip(clipPath);
                DrawIntoBox(graphics, decoded, image);
                graphics.Restore(state);
            }
            else
            {
                DrawIntoBox(graphics, decoded, image);
            }
        }
        catch
        {
            // Undecodable image bytes: skip this image rather than fail the whole paint.
        }
    }

    // Draws the decoded image into its box, honouring a source-rectangle crop by enlarging the image so its
    // visible sub-rectangle fills the box and clipping back (PdfSharp has no source-rect API) — the same
    // technique the deleted production DrawRaster used.
    static void DrawIntoBox(XGraphics graphics, XImage decoded, PlacedImage image)
    {
        if (image.Crop is { IsCropped: true } crop)
        {
            var (dx, dy, dw, dh) = crop.Expand(image.X, image.Y, image.Width, image.Height);
            var state = graphics.Save();
            graphics.IntersectClip(new XRect(image.X, image.Y, image.Width, image.Height));
            graphics.DrawImage(decoded, dx, dy, dw, dh);
            graphics.Restore(state);
        }
        else
        {
            graphics.DrawImage(decoded, image.X, image.Y, image.Width, image.Height);
        }
    }

    // EMU per point (914400 EMU/inch ÷ 72 pt/inch).
    const double emusPerPoint = 12700;

    // An inline shape group (a grouped drawing embedded in a run): its child shapes scaled from the group's
    // child coordinate space into the inline box, painted back to front. A verbatim port of
    // the deleted PdfTextEngine.DrawShapeGroup, reusing the same contour/picture/pen/alpha helpers (now on
    // PdfShapeDrawing), so the engine
    // paints an inline group identically to production. PdfSharp is point-native, so the placed box (already
    // in points, its top at Y = baseline − height) is drawn directly.
    static void PaintInlineGroup(PdfRenderContext context, XGraphics graphics, PlacedImage placed, InlineShapeGroup group)
    {
        var penX = placed.X;
        var top = placed.Y;
        var scaleX = placed.Width / group.ChildExtentX;
        var scaleY = placed.Height / group.ChildExtentY;

        var state = graphics.Save();
        if (group.RotationDegrees != 0)
        {
            graphics.RotateAtTransform(group.RotationDegrees, new(penX + placed.Width / 2, top + placed.Height / 2));
        }

        foreach (var shape in group.Shapes)
        {
            var x = penX + shape.X * scaleX;
            var y = top + shape.Y * scaleY;
            var width = shape.Width * scaleX;
            var height = shape.Height * scaleY;

            if (shape.Geometry == GroupShapeGeometry.Line)
            {
                var startX = x;
                var startY = y;
                var endX = x + width;
                var endY = y + height;
                if (shape.FlipVertical)
                {
                    (startY, endY) = (endY, startY);
                }

                if (shape.FlipHorizontal)
                {
                    (startX, endX) = (endX, startX);
                }

                var strokeWidth = shape.LineWidthEmu > 0 ? shape.LineWidthEmu / emusPerPoint : 0.75;
                graphics.DrawLine(PdfShapeDrawing.StrokePen(shape, strokeWidth), startX, startY, endX, endY);
                continue;
            }

            var isEllipse = shape.Geometry == GroupShapeGeometry.Ellipse;
            var geometryPath = PdfShapeDrawing.BuildGroupShapePath(shape, x, y, width, height);

            if (shape.Shadow is { } shadow)
            {
                var shadowRgb = PdfRenderContext.ParseColor(shadow.ColorHex);
                var shadowBrush = new XSolidBrush(XColor.FromArgb(PdfShapeDrawing.AlphaByte(shadow.Alpha), shadowRgb.R, shadowRgb.G, shadowRgb.B));
                var shadowX = x + shadow.OffsetX * scaleX;
                var shadowY = y + shadow.OffsetY * scaleY;
                if (geometryPath != null)
                {
                    graphics.DrawPath(shadowBrush, PdfShapeDrawing.BuildGroupShapePath(shape, shadowX, shadowY, width, height)!);
                }
                else if (isEllipse)
                {
                    graphics.DrawEllipse(shadowBrush, shadowX, shadowY, width, height);
                }
                else
                {
                    graphics.DrawRectangle(shadowBrush, shadowX, shadowY, width, height);
                }
            }

            if (shape.ImageData != null)
            {
                PdfShapeDrawing.DrawGroupPicture(context, graphics, shape, x, y, width, height, isEllipse);
            }
            else if (shape.FillColorHex is { } fillHex)
            {
                var rgb = PdfRenderContext.ParseColor(fillHex);
                var brush = new XSolidBrush(XColor.FromArgb(PdfShapeDrawing.AlphaByte(shape.FillAlpha), rgb.R, rgb.G, rgb.B));
                if (geometryPath != null)
                {
                    graphics.DrawPath(brush, geometryPath);
                }
                else if (isEllipse)
                {
                    graphics.DrawEllipse(brush, x, y, width, height);
                }
                else
                {
                    graphics.DrawRectangle(brush, x, y, width, height);
                }
            }

            if (shape.LineWidthEmu > 0)
            {
                var pen = PdfShapeDrawing.StrokePen(shape, shape.LineWidthEmu / emusPerPoint);
                if (geometryPath != null)
                {
                    graphics.DrawPath(pen, geometryPath);
                }
                else if (isEllipse)
                {
                    graphics.DrawEllipse(pen, x, y, width, height);
                }
                else
                {
                    graphics.DrawRectangle(pen, x, y, width, height);
                }
            }
        }

        graphics.Restore(state);
    }

    // A behind-text floating shape: fill and outline of either its freeform contours (subpaths, scaled from
    // the unit square into the box with flip/rotation baked in by BuildShapePath) or, absent those, its
    // preset box (rect or ellipse). The fill is a linear gradient (reusing the production brush) or a solid
    // colour. An image-fill shape is painted as a plain image by the body-float path, so it is skipped here.
    static void PaintShape(PdfRenderContext context, XGraphics graphics, PlacedShape placed)
    {
        var shape = placed.Shape;
        if (shape.ImageData is { Length: > 0 })
        {
            return;
        }

        // Honour the shape's fill/line opacity (a:alpha) — e.g. cover-letters/10's header banner is a 10%
        // accent tint that reads as near-white, not a solid panel.
        XBrush? fill;
        if (shape.Gradient is { } gradient)
        {
            fill = PdfShapeDrawing.BuildGradientBrush(gradient, placed.X, placed.Y, placed.Width, placed.Height);
        }
        else if (shape.FillColorHex is { } fillHex)
        {
            var rgb = PdfRenderContext.ParseColor(fillHex);
            fill = new XSolidBrush(XColor.FromArgb(PdfShapeDrawing.AlphaByte(shape.FillAlpha), rgb.R, rgb.G, rgb.B));
        }
        else
        {
            fill = null;
        }

        XPen? pen = null;
        if (shape.LineColorHex is { } lineHex)
        {
            var rgb = PdfRenderContext.ParseColor(lineHex);
            pen = new XPen(XColor.FromArgb(PdfShapeDrawing.AlphaByte(shape.LineAlpha), rgb.R, rgb.G, rgb.B), Math.Max(0.5, shape.LineWidthPoints ?? 1));
        }
        if (fill == null && pen == null)
        {
            return;
        }

        if (shape.Subpaths is { Count: > 0 })
        {
            var path = PdfShapeDrawing.BuildShapePath(shape, placed.X, placed.Y, placed.Width, placed.Height);
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

    // A paragraph border box: stroke each visible edge around the box the Fragmenter already expanded by
    // the edge spaces. Same edge geometry as a table cell, just around a paragraph rather than a cell.
    static void PaintBorder(PdfRenderContext context, XGraphics graphics, PlacedBorder border)
    {
        float left = border.X, top = border.Y, right = border.X + border.Width, bottom = border.Y + border.Height;
        var borders = border.Borders;

        if (borders.Top.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Top), left, top, right, top);
        }

        if (borders.Bottom.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Bottom), left, bottom, right, bottom);
        }

        if (borders.Left.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Left), left, top, left, bottom);
        }

        if (borders.Right.IsVisible)
        {
            graphics.DrawLine(EdgePen(context, borders.Right), right, top, right, bottom);
        }
    }

    static XPen EdgePen(PdfRenderContext context, BorderEdge edge) =>
        context.GetPen(PdfRenderContext.ParseColor(edge.ColorHex ?? "000000"), Math.Max(0.5, edge.WidthPoints));
}
