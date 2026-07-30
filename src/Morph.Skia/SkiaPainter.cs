using SkiaSharp;

/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> to PNG bitmaps — the raster analogue of
/// <c>PdfPainter</c> (docs/layout-engine-proposal.md, step 6). A pure draw pass: every page size, line and
/// run position comes from the tree the <c>Fragmenter</c> already produced, so there is no measurement and
/// no pagination here. The tree is in points and Skia draws in pixels, so every coordinate scales by
/// <see cref="RenderContextBase.PointsToPixels"/>. One RGBA8888 bitmap per page is encoded to PNG and
/// streamed through the same <paramref name="pageCallback"/> the production <c>SkiaPageRenderer</c> uses.
///
/// <para>Covers the block/table/column subset (<see cref="EngineCoverage"/>): paragraph text with its run
/// decorations, tables (cell shading, content, borders), paragraph shading and borders, inline images, and
/// tab leaders. Deferred to later slices: floating shapes (<c>PlacedShape</c>), image rotation/flip/crop,
/// and per-glyph advances.</para>
/// </summary>
static class SkiaPainter
{
    public static void Paint(LaidOutDocument document, SkiaRenderContext context, Action<Action<Stream>> pageCallback)
    {
        foreach (var laidOutPage in document.Pages)
        {
            using var bitmap = new SKBitmap(context.PageWidthPixels, context.PageHeightPixels, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);

            var background = laidOutPage.Settings.BackgroundColorHex;
            canvas.Clear(string.IsNullOrEmpty(background) ? SKColors.White : SkiaRenderContext.ParseColor(background));

            foreach (var item in laidOutPage.Items)
            {
                PaintItem(context, canvas, item);
            }

            using var pixmap = bitmap.PeekPixels();
            using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100)!;
            pageCallback(data.SaveTo);
        }
    }

    // Points (the tree's unit) to device pixels (Skia's unit).
    static float P(SkiaRenderContext context, double points) => context.PointsToPixels((float) points);

    static void PaintItem(SkiaRenderContext context, SKCanvas canvas, PlacedItem item)
    {
        switch (item)
        {
            case PlacedLine line:
                PaintLine(context, canvas, line);
                break;
            case PlacedTableRow row:
                PaintTableRow(context, canvas, row);
                break;
            case PlacedImage image:
                PaintImage(context, canvas, image);
                break;
            case PlacedShape shape:
                PaintShape(context, canvas, shape);
                break;
            case PlacedShading shading:
                Fill(context, canvas, shading.X, shading.Y, shading.Width, shading.Height, shading.ColorHex);
                break;
            case PlacedBorder border:
                PaintEdges(context, canvas, border.X, border.Y, border.Width, border.Height, border.Borders);
                break;
        }
    }

    static void Fill(SkiaRenderContext context, SKCanvas canvas, double x, double y, double width, double height, string? colorHex) =>
        canvas.DrawRect(P(context, x), P(context, y), P(context, width), P(context, height),
            context.GetReusableFillPaint(SkiaRenderContext.ParseColor(colorHex), antialias: false));

    static void PaintLine(SkiaRenderContext context, SKCanvas canvas, PlacedLine line)
    {
        var ascent = line.Baseline - line.Y;
        foreach (var run in line.Runs)
        {
            if (run.Leader != TabLeader.None)
            {
                DrawLeader(context, canvas, run, line.Baseline);
                continue;
            }

            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var properties = run.Properties;
            var color = SkiaRenderContext.ParseColor(properties.ColorHex);

            // Highlight / run shading fills behind the glyphs, over the line box.
            if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
            {
                Fill(context, canvas, run.X, line.Y, run.Width, line.Height, properties.BackgroundColorHex);
            }

            DrawTracked(context, canvas, run.Text, properties, run.X, line.Baseline);

            var strokeWidth = P(context, Math.Max(0.5, properties.FontSizePoints / 16));
            if (properties.Underline)
            {
                var underlineY = P(context, line.Baseline + properties.FontSizePoints * 0.12);
                canvas.DrawLine(P(context, run.X), underlineY, P(context, run.X + run.Width), underlineY, context.GetReusableRulePaint(color, strokeWidth));
            }

            if (properties.Strikethrough)
            {
                var strikeY = P(context, line.Baseline - ascent * 0.3);
                canvas.DrawLine(P(context, run.X), strikeY, P(context, run.X + run.Width), strikeY, context.GetReusableRulePaint(color, strokeWidth));
            }
        }

        foreach (var image in line.Images)
        {
            PaintImage(context, canvas, image);
        }
    }

    // Draws text, spreading each character by w:spacing tracking. The run's placed width already includes the
    // tracking, so the following run starts past it. Per-glyph so surrogate pairs stay intact — mirrors
    // PdfPainter.DrawTracked.
    static void DrawTracked(SkiaRenderContext context, SKCanvas canvas, string text, RunProperties properties, double penX, double baseline)
    {
        var font = context.CreateFont(properties);
        var paint = context.GetReusableTextPaint(properties);
        var y = P(context, baseline);

        if (properties.CharacterSpacingPoints == 0 || text.Length <= 1)
        {
            canvas.DrawText(text, P(context, penX), y, font, paint);
            return;
        }

        var trackingPixels = P(context, properties.CharacterSpacingPoints);
        var x = P(context, penX);
        for (var i = 0; i < text.Length; i++)
        {
            var length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            var piece = text.Substring(i, length);
            canvas.DrawText(piece, x, y, font, paint);
            x += font.MeasureText(piece) + trackingPixels;
            i += length - 1;
        }
    }

    // Fills a tab-leader gap: a baseline rule for underscore, otherwise the leader glyph tiled across the
    // span at ~2x its advance. Mirrors PdfPainter.DrawLeader.
    static void DrawLeader(SkiaRenderContext context, SKCanvas canvas, PlacedRun run, double baseline)
    {
        if (run.Width <= 0)
        {
            return;
        }

        var color = SkiaRenderContext.ParseColor(run.Properties.ColorHex);
        var fontSize = run.Properties.FontSizePoints;

        if (run.Leader == TabLeader.Underscore)
        {
            var underscoreY = P(context, baseline + fontSize * 0.12);
            canvas.DrawLine(P(context, run.X), underscoreY, P(context, run.X + run.Width), underscoreY, context.GetReusableRulePaint(color, P(context, Math.Max(0.5, fontSize / 16))));
            return;
        }

        var leaderChar = run.Leader switch
        {
            TabLeader.Hyphen => "-",
            TabLeader.MiddleDot => "·",
            TabLeader.Heavy => "—",
            _ => "."
        };

        var font = context.CreateFont(run.Properties);
        var glyphWidth = font.MeasureText(leaderChar);
        if (glyphWidth <= 0)
        {
            return;
        }

        var spacing = glyphWidth * 2;
        var runWidth = P(context, run.Width);
        var count = (int) Math.Floor((runWidth - glyphWidth) / spacing) + 1;
        if (count <= 0)
        {
            return;
        }

        var paint = context.GetReusableTextPaint(run.Properties);
        var y = P(context, baseline);
        var startX = P(context, run.X);
        for (var index = 0; index < count; index++)
        {
            canvas.DrawText(leaderChar, startX + index * spacing, y, font, paint);
        }
    }

    static void PaintImage(SkiaRenderContext context, SKCanvas canvas, PlacedImage image)
    {
        var bitmap = context.GetBitmap(image.Data);
        if (bitmap == null)
        {
            return;
        }

        // Rotation/flip/crop are deferred; inline images in the covered subset draw upright.
        canvas.DrawBitmap(bitmap, SKRect.Create(P(context, image.X), P(context, image.Y), P(context, image.Width), P(context, image.Height)));
    }

    // A behind-text floating shape: fill and outline of its freeform contours (reusing the production
    // subpath geometry) or its preset rect/ellipse box. Image-fill shapes are painted as a plain image by
    // the body-float path, so they are skipped. Gradient fill is deferred — the covered subset's float
    // shapes (e.g. labels/14's coloured panels) are solid-filled.
    static void PaintShape(SkiaRenderContext context, SKCanvas canvas, PlacedShape placed)
    {
        var shape = placed.Shape;
        if (shape.ImageData is { Length: > 0 })
        {
            return;
        }

        var fill = shape.FillColorHex is { } fillHex ? context.GetReusableFillPaint(SkiaRenderContext.ParseColor(fillHex), antialias: true) : null;
        var line = shape.LineColorHex is { } lineHex ? context.GetReusableRulePaint(SkiaRenderContext.ParseColor(lineHex), P(context, Math.Max(0.5, shape.LineWidthPoints ?? 1))) : null;
        if (fill == null && line == null)
        {
            return;
        }

        float x = P(context, placed.X), y = P(context, placed.Y), width = P(context, placed.Width), height = P(context, placed.Height);

        if (shape.Subpaths is { Count: > 0 })
        {
            using var path = SkiaPageRenderer.BuildPolygonPath(shape, x, y, width, height);
            if (fill != null)
            {
                canvas.DrawPath(path, fill);
            }

            if (line != null)
            {
                canvas.DrawPath(path, line);
            }

            return;
        }

        var rotated = shape.RotationDegrees != 0;
        if (rotated)
        {
            canvas.Save();
            canvas.RotateDegrees((float) shape.RotationDegrees, x + width / 2, y + height / 2);
        }

        if (shape.Preset == PresetShape.Ellipse)
        {
            if (fill != null)
            {
                canvas.DrawOval(x + width / 2, y + height / 2, width / 2, height / 2, fill);
            }

            if (line != null)
            {
                canvas.DrawOval(x + width / 2, y + height / 2, width / 2, height / 2, line);
            }
        }
        else
        {
            if (fill != null)
            {
                canvas.DrawRect(x, y, width, height, fill);
            }

            if (line != null)
            {
                canvas.DrawRect(x, y, width, height, line);
            }
        }

        if (rotated)
        {
            canvas.Restore();
        }
    }

    // Each cell: shading first, then its content, then its borders on top — Word's cell paint order.
    static void PaintTableRow(SkiaRenderContext context, SKCanvas canvas, PlacedTableRow row)
    {
        foreach (var cell in row.Cells)
        {
            if (!string.IsNullOrEmpty(cell.BackgroundColorHex))
            {
                Fill(context, canvas, cell.X, cell.Y, cell.Width, cell.Height, cell.BackgroundColorHex);
            }

            foreach (var content in cell.Content)
            {
                PaintItem(context, canvas, content);
            }

            if (cell.Borders is { } borders)
            {
                PaintEdges(context, canvas, cell.X, cell.Y, cell.Width, cell.Height, borders);
            }
        }
    }

    // Strokes each visible edge of a box (a table cell or a paragraph border), same geometry either way.
    static void PaintEdges(SkiaRenderContext context, SKCanvas canvas, double x, double y, double width, double height, CellBorders borders)
    {
        float left = P(context, x), top = P(context, y), right = P(context, x + width), bottom = P(context, y + height);

        if (borders.Top.IsVisible)
        {
            canvas.DrawLine(left, top, right, top, EdgePen(context, borders.Top));
        }

        if (borders.Bottom.IsVisible)
        {
            canvas.DrawLine(left, bottom, right, bottom, EdgePen(context, borders.Bottom));
        }

        if (borders.Left.IsVisible)
        {
            canvas.DrawLine(left, top, left, bottom, EdgePen(context, borders.Left));
        }

        if (borders.Right.IsVisible)
        {
            canvas.DrawLine(right, top, right, bottom, EdgePen(context, borders.Right));
        }
    }

    static SKPaint EdgePen(SkiaRenderContext context, BorderEdge edge) =>
        context.GetReusableRulePaint(SkiaRenderContext.ParseColor(edge.ColorHex), P(context, edge.WidthPoints));
}
