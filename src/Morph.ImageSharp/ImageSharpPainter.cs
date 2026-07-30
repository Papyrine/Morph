using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> to PNG bitmaps — the ImageSharp analogue of
/// <c>SkiaPainter</c> (docs/layout-engine-proposal.md, step 6). A pure draw pass over the tree the
/// <c>Fragmenter</c> produced. ImageSharp records draw ops onto a deferred <see cref="DrawingCanvas"/> per
/// page and flushes them when the canvas is disposed, then encodes the page image to PNG through the same
/// page callback the production <c>ImageSharpPageRenderer</c> uses. The tree is in points
/// and ImageSharp draws in pixels, so every coordinate scales by <see cref="RenderContextBase.PointsToPixels"/>;
/// text is top-anchored, so a run's baseline drops by the font ascent.
///
/// <para>Covers the block/table/column subset: paragraph text with its run decorations, tables, paragraph
/// shading and borders, inline images (crop/rotation/flip baked by the context), tab leaders, and behind-text
/// floating shapes (solid fill and outline, freeform or preset). Deferred: gradient shape fills and per-glyph
/// advances.</para>
/// </summary>
static class ImageSharpPainter
{
    public static void Paint(LaidOutDocument document, ImageSharpRenderContext context, Action<Action<Stream>> pageCallback)
    {
        foreach (var laidOutPage in document.Pages)
        {
            var (pageWidth, pageHeight) = context.PagePixels(laidOutPage.Settings);
            using var image = new Image<Rgba32>(pageWidth, pageHeight);
            using (var canvas = image.Frames.RootFrame.CreateCanvas(Configuration.Default, new()))
            {
                var background = laidOutPage.Settings.BackgroundColorHex;
                var backgroundColor = string.IsNullOrEmpty(background) ? Color.White : ImageSharpRenderContext.ParseColor(background);
                canvas.Fill(context.GetBrush(backgroundColor), new RectangleF(0, 0, pageWidth, pageHeight));

                foreach (var item in laidOutPage.Items)
                {
                    PaintItem(context, canvas, item);
                }
            }

            pageCallback(image.SaveAsPng);
        }
    }

    static float P(ImageSharpRenderContext context, double points) => context.PointsToPixels((float) points);

    static void PaintItem(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedItem item)
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

    static void Fill(ImageSharpRenderContext context, DrawingCanvas canvas, double x, double y, double width, double height, string? colorHex) =>
        canvas.Fill(context.GetBrush(ImageSharpRenderContext.ParseColor(colorHex)), new RectangleF(P(context, x), P(context, y), P(context, width), P(context, height)));

    static void PaintLine(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedLine line)
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
            var color = ImageSharpRenderContext.ParseColor(properties.ColorHex);

            if (!string.IsNullOrEmpty(properties.BackgroundColorHex))
            {
                Fill(context, canvas, run.X, line.Y, run.Width, line.Height, properties.BackgroundColorHex);
            }

            DrawTracked(context, canvas, run.Text, properties, run.X, line.Baseline);

            var strokeWidth = P(context, Math.Max(0.5, properties.FontSizePoints / 16));
            if (properties.Underline)
            {
                var underlineY = P(context, line.Baseline + properties.FontSizePoints * 0.12);
                canvas.DrawLine(context.GetPen(color, strokeWidth), new PointF(P(context, run.X), underlineY), new PointF(P(context, run.X + run.Width), underlineY));
            }

            if (properties.Strikethrough)
            {
                var strikeY = P(context, line.Baseline - ascent * 0.3);
                canvas.DrawLine(context.GetPen(color, strokeWidth), new PointF(P(context, run.X), strikeY), new PointF(P(context, run.X + run.Width), strikeY));
            }
        }

        foreach (var image in line.Images)
        {
            PaintImage(context, canvas, image);
        }
    }

    // Text is top-anchored in ImageSharp, so the baseline drops by the font ascent (points) scaled to
    // pixels. Per-character tracking mirrors SkiaPainter.DrawTracked, surrogate-safe.
    static void DrawTracked(ImageSharpRenderContext context, DrawingCanvas canvas, string text, RunProperties properties, double penX, double baseline)
    {
        var font = context.GetFont(properties);
        var brush = context.GetBrush(ImageSharpRenderContext.ParseColor(properties.ColorHex));
        var (_, ascent) = ImageSharpRenderContext.GetFontMetrics(font);
        var top = P(context, baseline) - ascent * context.Scale;

        if (properties.CharacterSpacingPoints == 0 || text.Length <= 1)
        {
            canvas.DrawText(Options(context, font, P(context, penX), top), text.AsSpan(), brush, null);
            return;
        }

        var trackingPixels = P(context, properties.CharacterSpacingPoints);
        var x = P(context, penX);
        for (var i = 0; i < text.Length; i++)
        {
            var length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            var piece = text.Substring(i, length);
            canvas.DrawText(Options(context, font, x, top), piece.AsSpan(), brush, null);
            x += P(context, context.MeasureText(font, piece)) + trackingPixels;
            i += length - 1;
        }
    }

    static RichTextOptions Options(ImageSharpRenderContext context, Font font, float x, float top) =>
        new(font)
        {
            Dpi = context.Dpi,
            Origin = new PointF(x, top),
            KerningMode = KerningMode.Standard
        };

    static void DrawLeader(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedRun run, double baseline)
    {
        if (run.Width <= 0)
        {
            return;
        }

        var color = ImageSharpRenderContext.ParseColor(run.Properties.ColorHex);
        var fontSize = run.Properties.FontSizePoints;

        if (run.Leader == TabLeader.Underscore)
        {
            var underscoreY = P(context, baseline + fontSize * 0.12);
            canvas.DrawLine(context.GetPen(color, P(context, Math.Max(0.5, fontSize / 16))), new PointF(P(context, run.X), underscoreY), new PointF(P(context, run.X + run.Width), underscoreY));
            return;
        }

        var leaderChar = run.Leader switch
        {
            TabLeader.Hyphen => "-",
            TabLeader.MiddleDot => "·",
            TabLeader.Heavy => "—",
            _ => "."
        };

        var font = context.GetFont(run.Properties);
        var glyphWidth = P(context, context.MeasureText(font, leaderChar));
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

        var brush = context.GetBrush(color);
        var (_, ascent) = ImageSharpRenderContext.GetFontMetrics(font);
        var top = P(context, baseline) - ascent * context.Scale;
        var startX = P(context, run.X);
        for (var index = 0; index < count; index++)
        {
            canvas.DrawText(Options(context, font, startX + index * spacing, top), leaderChar.AsSpan(), brush, null);
        }
    }

    static void PaintImage(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedImage image)
    {
        var width = (int) Math.Round(P(context, image.Width));
        var height = (int) Math.Round(P(context, image.Height));
        var processed = context.GetProcessedImage(image.Data, width, height, image.Crop, default, (float) image.RotationDegrees, image.FlipHorizontal, image.FlipVertical);
        if (processed == null)
        {
            return;
        }

        canvas.DrawImage(processed, new Point((int) Math.Round(P(context, image.X)), (int) Math.Round(P(context, image.Y))));
    }

    // A behind-text floating shape: fill and outline of its freeform subpath contours (reusing the
    // production BuildPath geometry, rotation already baked, nonzero winding pushed via Save) or its preset
    // rect/ellipse box (rotated about its centre). Fill is a solid colour or a linear gradient (built the
    // same way as the production ImageSharpPageRenderer). Image-fill shapes never reach here — the
    // Fragmenter routes them to PlacedImage.
    static void PaintShape(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedShape placed)
    {
        var shape = placed.Shape;
        if (shape.ImageData is { Length: > 0 })
        {
            return;
        }

        float x = P(context, placed.X), y = P(context, placed.Y), width = P(context, placed.Width), height = P(context, placed.Height);

        Brush? fill;
        if (shape.Gradient is { } gradient)
        {
            fill = BuildGradientBrush(gradient, x, y, width, height);
        }
        else if (shape.FillColorHex is { } fillHex)
        {
            fill = context.GetBrush(ImageSharpRenderContext.ParseColor(fillHex));
        }
        else
        {
            fill = null;
        }

        var line = shape.LineColorHex is { } lineHex ? context.GetPen(ImageSharpRenderContext.ParseColor(lineHex), P(context, Math.Max(0.5, shape.LineWidthPoints ?? 1))) : null;
        if (fill == null && line == null)
        {
            return;
        }

        if (shape.Subpaths is { Count: > 0 })
        {
            var path = ImageSharpPageRenderer.BuildPath(shape, x, y, width, height);
            canvas.Save(ImageSharpPageRenderer.NonzeroFill);
            if (fill != null)
            {
                canvas.Fill(fill, path);
            }

            if (line != null)
            {
                canvas.Draw(line, path);
            }

            canvas.Restore();
            return;
        }

        var rotated = shape.RotationDegrees != 0;
        if (rotated)
        {
            canvas.Save(ImageSharpPageRenderer.BuildRotation((float) (shape.RotationDegrees * Math.PI / 180.0), x + width / 2, y + height / 2));
        }

        var presetPath = ImageSharpPageRenderer.BuildPresetPath(shape, x, y, width, height);
        if (fill != null)
        {
            canvas.Fill(fill, presetPath);
        }

        if (line != null)
        {
            canvas.Draw(line, presetPath);
        }

        if (rotated)
        {
            canvas.Restore();
        }
    }

    // A linear gradient across the shape's bounding box, matching ImageSharpPageRenderer: angle 0° points
    // along +X (OOXML a:lin/@ang), the stops run corner-to-corner through the box centre.
    static LinearGradientBrush BuildGradientBrush(GradientFill gradient, float x, float y, float width, float height)
    {
        var radians = gradient.DirectionDegrees * Math.PI / 180.0;
        var dx = (float) Math.Cos(radians);
        var dy = (float) Math.Sin(radians);
        var centreX = x + width / 2;
        var centreY = y + height / 2;
        var halfDiagonal = (float) Math.Sqrt(width * width + height * height) / 2;
        return new LinearGradientBrush(
            new PointF(centreX - dx * halfDiagonal, centreY - dy * halfDiagonal),
            new PointF(centreX + dx * halfDiagonal, centreY + dy * halfDiagonal),
            GradientRepetitionMode.None,
            new ColorStop(0f, ImageSharpRenderContext.ParseColor(gradient.StartColorHex)),
            new ColorStop(1f, ImageSharpRenderContext.ParseColor(gradient.EndColorHex)));
    }

    static void PaintTableRow(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedTableRow row)
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

    static void PaintEdges(ImageSharpRenderContext context, DrawingCanvas canvas, double x, double y, double width, double height, CellBorders borders)
    {
        float left = P(context, x), top = P(context, y), right = P(context, x + width), bottom = P(context, y + height);

        if (borders.Top.IsVisible)
        {
            canvas.DrawLine(EdgePen(context, borders.Top), new PointF(left, top), new PointF(right, top));
        }

        if (borders.Bottom.IsVisible)
        {
            canvas.DrawLine(EdgePen(context, borders.Bottom), new PointF(left, bottom), new PointF(right, bottom));
        }

        if (borders.Left.IsVisible)
        {
            canvas.DrawLine(EdgePen(context, borders.Left), new PointF(left, top), new PointF(left, bottom));
        }

        if (borders.Right.IsVisible)
        {
            canvas.DrawLine(EdgePen(context, borders.Right), new PointF(right, top), new PointF(right, bottom));
        }
    }

    static SolidPen EdgePen(ImageSharpRenderContext context, BorderEdge edge) =>
        context.GetPen(ImageSharpRenderContext.ParseColor(edge.ColorHex), P(context, edge.WidthPoints));
}
