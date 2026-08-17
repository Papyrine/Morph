using Morph;

/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> to PNG bitmaps — the ImageSharp analogue of
/// <c>SkiaPainter</c> (docs/layout-engine.md, step 6). A pure draw pass over the tree the
/// <c>Fragmenter</c> produced. ImageSharp records draw ops onto a deferred <see cref="DrawingCanvas"/> per
/// page and flushes them when the canvas is disposed, then encodes the page image to PNG through the same
/// page callback the production <c>ImageSharpPageRenderer</c> uses. The tree is in points
/// and ImageSharp draws in pixels, so every coordinate scales by <see cref="RenderContextBase.PointsToPixels"/>;
/// text is top-anchored, so a run's baseline drops by the font ascent.
///
/// <para>Covers the block/table/column subset: paragraph text with its run decorations, tables, paragraph
/// shading and borders, inline images (crop/rotation/flip baked by the context), tab leaders, and floating
/// shapes (solid, gradient or outline fill, freeform or preset). Deferred: cell-anchored image-fill shapes
/// (a body one is routed to a PlacedImage and draws) and per-glyph advances.</para>
/// </summary>
static class ImageSharpPainter
{

    public static void Paint(LaidOutDocument document, ImageSharpRenderContext context, PageCrop crop, Action<Action<Stream>> pageCallback)
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

            // Cropping the finished image rather than translating the canvas — see SkiaPainter.Encode
            // for why both painters do it this way. It has to follow the canvas's disposal above,
            // since that is what flushes the deferred draw ops into the image.
            var rect = context.PageRect(laidOutPage.Settings, crop);
            if (rect is not { X: 0, Y: 0 } || rect.Width != pageWidth || rect.Height != pageHeight)
            {
                image.Mutate(_ => _.Crop(new(rect.X, rect.Y, rect.Width, rect.Height)));
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
            case PlacedWordArt wordArt:
                PaintWordArt(context, canvas, wordArt);
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

            // w:bdr — see SkiaPainter.PaintLine.
            if (properties.Border is {} runBorder &&
                BorderStroke.Draws(runBorder))
            {
                PaintEdges(context, canvas, run.X, line.Y, run.Width, line.Height, CellBorders.Uniform(runBorder));
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

    // A warped WordArt figure. The warp geometry (text on an arc, envelope, wave) lives in this backend's
    // WordArt rasterizer, which lays the shape out on a page sized to its box and returns a
    // transparent-background PNG — reused verbatim rather than reimplementing the presets. The PNG is the box
    // surrounded by WordArtRasterPage.Padding on every side (several warps draw past the declared box), so the
    // draw origin steps back by that padding, leaving the box region at (X, Y) and letting the overflow spill
    // onto the page. Rasterizing at the render's own DPI keeps the bitmap pixel-aligned with the page. This
    // backend's own rasterizer is used directly — never the reflective factory, which prefers Morph.Skia and
    // would draw Skia's glyphs onto an ImageSharp page.
    static void PaintWordArt(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedWordArt wordArt)
    {
        var options = new WordArtRasterOptions
        {
            Dpi = context.Dpi,
            FontWidthScale = context.FontWidthScale,
            FontFallback = context.FontFallback,
            FontDirectory = context.FontDirectory,
            Deterministic = context.DeterministicRendering
        };

        if (new ImageSharpWordArtRasterizer().Render(wordArt.Visual, options) is not { } png)
        {
            return;
        }

        var pad = WordArtRasterPage.Padding(wordArt.Visual);
        var width = (int) Math.Round(P(context, wordArt.Width + 2 * pad));
        var height = (int) Math.Round(P(context, wordArt.Height + 2 * pad));
        if (context.GetProcessedImage(png, width, height, null, default, 0) is not {} processed)
        {
            return;
        }

        canvas.DrawImage(
            processed,
            new(
                (int) Math.Round(P(context, wordArt.X - pad)),
                (int) Math.Round(P(context, wordArt.Y - pad))));
    }

    static void PaintImage(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedImage image)
    {
        if (image.ShapeGroup is { } group)
        {
            PaintInlineGroup(context, canvas, image, group);
            return;
        }

        if (image.Data is not { } data)
        {
            return;
        }

        var width = (int) Math.Round(P(context, image.Width));
        var height = (int) Math.Round(P(context, image.Height));
        var processed = context.GetProcessedImage(data, width, height, image.Crop, default, (float) image.RotationDegrees, image.FlipHorizontal, image.FlipVertical);
        if (processed == null)
        {
            return;
        }

        canvas.DrawImage(processed, new((int) Math.Round(P(context, image.X)), (int) Math.Round(P(context, image.Y))));
    }

    // An inline shape group (a grouped drawing embedded in a run): its child shapes scaled from the group's
    // child coordinate space into the inline box, painted back to front. A verbatim port of
    // ImageSharpShapeDrawing.RenderInlineShapeGroup that reuses the same colour/contour/picture helpers, so the engine
    // paints an inline group pixel-identically to production. The placed box is already in points with its
    // top at Y (the fragmenter set Y = baseline − height), so no baseline subtraction is needed.
    static void PaintInlineGroup(ImageSharpRenderContext context, DrawingCanvas canvas, PlacedImage placed, InlineShapeGroup group)
    {
        float pixelX = P(context, placed.X), pixelY = P(context, placed.Y), pixelWidth = P(context, placed.Width), pixelHeight = P(context, placed.Height);

        var sx = pixelWidth / (float) group.ChildExtentX;
        var sy = pixelHeight / (float) group.ChildExtentY;

        var hasRotation = group.RotationDegrees != 0;
        if (hasRotation)
        {
            canvas.Save(ImageSharpShapeDrawing.BuildRotation((float) (group.RotationDegrees * Math.PI / 180.0), pixelX + pixelWidth / 2f, pixelY + pixelHeight / 2f));
        }

        foreach (var shape in group.Shapes)
        {
            var x1 = pixelX + (float) shape.X * sx;
            var y1 = pixelY + (float) shape.Y * sy;
            var width = (float) shape.Width * sx;
            var height = (float) shape.Height * sy;

            if (shape.Geometry == GroupShapeGeometry.Line)
            {
                var startX = x1;
                var startY = y1;
                var endX = x1 + width;
                var endY = y1 + height;
                if (shape.FlipVertical)
                {
                    (startY, endY) = (endY, startY);
                }

                if (shape.FlipHorizontal)
                {
                    (startX, endX) = (endX, startX);
                }

                var strokeWidth = (float) (shape.LineWidthEmu > 0 ? shape.LineWidthEmu / OoxmlUnits.EmusPerPointF : 0.75) * context.Scale;
                var linePen = new SolidPen(new PenOptions(ImageSharpShapeDrawing.ParseColor(shape.ColorHex, shape.LineAlpha), strokeWidth)
                {
                    StrokeOptions = new()
                    {
                        LineCap = LineCap.Square,
                        LineJoin = LineJoin.Bevel
                    }
                });
                canvas.DrawLine(linePen, new PointF(startX, startY), new PointF(endX, endY));
                continue;
            }

            var isEllipse = shape.Geometry == GroupShapeGeometry.Ellipse;
            var path = ImageSharpShapeDrawing.BuildGroupShapePath(shape, x1, y1, width, height)
                       ?? (isEllipse
                           ? new EllipsePolygon(x1 + width / 2, y1 + height / 2, width, height)
                           : new RectanglePolygon(x1, y1, width, height));

            if (shape.Shadow is { } shadow)
            {
                var shadowX = x1 + (float) shadow.OffsetX * sx;
                var shadowY = y1 + (float) shadow.OffsetY * sy;
                var shadowPath = ImageSharpShapeDrawing.BuildGroupShapePath(shape, shadowX, shadowY, width, height)
                                 ?? (isEllipse
                                     ? new EllipsePolygon(shadowX + width / 2, shadowY + height / 2, width, height)
                                     : new RectanglePolygon(shadowX, shadowY, width, height));
                canvas.Fill(context.GetBrush(ImageSharpShapeDrawing.ParseColor(shadow.ColorHex, shadow.Alpha)), shadowPath);
            }

            if (shape.ImageData != null)
            {
                ImageSharpShapeDrawing.RenderGroupPicture(context, canvas, shape, x1, y1, width, height, isEllipse ? path : null, hasRotation);
            }
            else if (shape.FillColorHex is { } fillHex)
            {
                canvas.Fill(context.GetBrush(ImageSharpShapeDrawing.ParseColor(fillHex, shape.FillAlpha)), path);
            }

            if (shape.LineWidthEmu > 0)
            {
                var strokeWidth = (float) (shape.LineWidthEmu / OoxmlUnits.EmusPerPointF) * context.Scale;
                canvas.Draw(context.GetPen(ImageSharpShapeDrawing.ParseColor(shape.ColorHex, shape.LineAlpha), strokeWidth), path);
            }
        }

        if (hasRotation)
        {
            canvas.Restore();
        }
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

        // Honour the shape's fill/line opacity (a:alpha) — e.g. cover-letters/10's header banner is a 10%
        // accent tint that reads as near-white, not a solid panel. ImageSharpShapeDrawing.ParseColor applies the alpha.
        Brush? fill;
        if (shape.Gradient is { } gradient)
        {
            fill = BuildGradientBrush(gradient, x, y, width, height);
        }
        else if (shape.FillColorHex is { } fillHex)
        {
            fill = context.GetBrush(ImageSharpShapeDrawing.ParseColor(fillHex, shape.FillAlpha));
        }
        else
        {
            fill = null;
        }

        var line = shape.LineColorHex is { } lineHex ? context.GetPen(ImageSharpShapeDrawing.ParseColor(lineHex, shape.LineAlpha), P(context, Math.Max(0.5, shape.LineWidthPoints ?? 1))) : null;
        if (fill == null && line == null)
        {
            return;
        }

        if (shape.Subpaths is { Count: > 0 })
        {
            var path = ImageSharpShapeDrawing.BuildPath(shape, x, y, width, height);
            canvas.Save(ImageSharpShapeDrawing.NonzeroFill);
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
            canvas.Save(ImageSharpShapeDrawing.BuildRotation((float) (shape.RotationDegrees * Math.PI / 180.0), x + width / 2, y + height / 2));
        }

        var presetPath = ImageSharpShapeDrawing.BuildPresetPath(shape, x, y, width, height);
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
        return new(
            new(centreX - dx * halfDiagonal, centreY - dy * halfDiagonal),
            new(centreX + dx * halfDiagonal, centreY + dy * halfDiagonal),
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

            // See SkiaPainter.PaintTableRow — the clip bounds the content only, never the shading or
            // the borders.
            var clipped = cell.ClipContent;
            if (clipped)
            {
                canvas.Save();
                canvas.Clip(new RectanglePolygon(
                    P(context, cell.X - cell.ClipSpillLeft),
                    P(context, cell.Y),
                    P(context, cell.Width + cell.ClipSpillLeft + cell.ClipSpillRight),
                    P(context, cell.Height)));
            }

            foreach (var content in cell.Content)
            {
                PaintItem(context, canvas, content);
            }

            if (clipped)
            {
                canvas.Restore();
            }

            if (cell.Borders is { } borders)
            {
                PaintEdges(context, canvas, cell.X, cell.Y, cell.Width, cell.Height, borders, BorderStroke.Scope.Cell);
            }
        }
    }

    static void PaintEdges(ImageSharpRenderContext context, DrawingCanvas canvas, double x, double y, double width, double height, CellBorders borders, BorderStroke.Scope scope = BorderStroke.Scope.Paragraph)
    {
        float left = P(context, x), top = P(context, y), right = P(context, x + width), bottom = P(context, y + height);

        // See SkiaPainter.StrokeEdge — one band per line of a multi-line style, offset
        // perpendicular to the edge; single-line styles come back as one band at offset 0.
        // See SkiaPainter.PaintEdges — corner fill.
        var drawsLeft = BorderStroke.Draws(borders.Left);
        var drawsRight = BorderStroke.Draws(borders.Right);
        var drawsTop = BorderStroke.Draws(borders.Top);
        var drawsBottom = BorderStroke.Draws(borders.Bottom);

        StrokeEdge(context, canvas, borders.Top, horizontal: true, left, right, top, scope, outward: -1, drawsLeft, drawsRight);
        StrokeEdge(context, canvas, borders.Bottom, horizontal: true, left, right, bottom, scope, outward: 1, drawsLeft, drawsRight);
        StrokeEdge(context, canvas, borders.Left, horizontal: false, top, bottom, left, scope, outward: -1, drawsTop, drawsBottom);
        StrokeEdge(context, canvas, borders.Right, horizontal: false, top, bottom, right, scope, outward: 1, drawsTop, drawsBottom);
    }

    // See SkiaPainter.StrokeWaves — a wave edge is a fixed-geometry zigzag, not a straight band.
    static void StrokeWaves(ImageSharpRenderContext context, DrawingCanvas canvas, BorderEdge edge, BorderStroke.WaveBand[] waves, bool horizontal, float from, float to, float at, int outward)
    {
        var color = ImageSharpRenderContext.ParseColor(edge.ColorHex);
        foreach (var wave in waves)
        {
            var centre = at + outward * P(context, wave.Offset);
            var pen = context.GetPen(color, P(context, wave.Thickness));
            var points = new List<PointF>();
            foreach (var (along, across) in BorderStroke.WavePoints(from, to, P(context, wave.Period), P(context, wave.Amplitude)))
            {
                points.Add(horizontal
                    ? new PointF((float) along, centre + (float) across)
                    : new PointF(centre + (float) across, (float) along));
            }

            if (points.Count > 1)
            {
                // DrawingCanvas has no polyline overload, so the zigzag goes through an open path.
                canvas.Draw(pen, new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(points.ToArray())));
            }
        }
    }

    // See SkiaPainter.Shaded.
    static Color Shaded(Color color, double shade)
    {
        if (shade >= 1)
        {
            return color;
        }

        var p = color.ToPixel<Rgba32>();
        return Color.FromPixel(new Rgba32((byte) (p.R * shade), (byte) (p.G * shade), (byte) (p.B * shade), p.A));
    }

    // outward is -1 for the top/left edges and +1 for bottom/right: the direction that moves AWAY
    // from the box. The span grows by the same offset at both ends so this band meets the
    // perpendicular edges' matching band at the corner.
    static void StrokeEdge(ImageSharpRenderContext context, DrawingCanvas canvas, BorderEdge edge, bool horizontal, float from, float to, float at, BorderStroke.Scope scope, int outward, bool extendStart, bool extendEnd)
    {
        if (!BorderStroke.Draws(edge))
        {
            return;
        }

        if (BorderStroke.Waves(edge.Style) is {Length: > 0} waves)
        {
            StrokeWaves(context, canvas, edge, waves, horizontal, from, to, at, outward);
            return;
        }

        var dash = BorderStroke.DashPattern(edge.Style, edge.WidthPoints);
        foreach (var band in BorderStroke.Bands(edge.Style, edge.WidthPoints, scope))
        {
            var offset = P(context, band.Offset);
            var half = P(context, band.Thickness) / 2;
            var line = at + outward * offset;
            var start = from - offset - (extendStart ? half : 0);
            var end = to + offset + (extendEnd ? half : 0);
            var pen = EdgePen(context, edge, band.Thickness, dash, band.Shade);
            if (horizontal)
            {
                canvas.DrawLine(pen, new PointF(start, line), new PointF(end, line));
            }
            else
            {
                canvas.DrawLine(pen, new PointF(line, start), new PointF(line, end));
            }
        }
    }

    static Pen EdgePen(ImageSharpRenderContext context, BorderEdge edge, double thickness, float[]? dashPoints, double shade)
    {
        var color = Shaded(ImageSharpRenderContext.ParseColor(edge.ColorHex), shade);
        var strokeWidth = P(context, thickness);
        if (dashPoints == null)
        {
            return context.GetPen(color, strokeWidth);
        }

        // ImageSharp expresses a stroke pattern in multiples of the stroke WIDTH, not in pixels,
        // so the point-space pattern is normalised here. A hairline would divide by ~0, hence the
        // floor.
        var unit = Math.Max(strokeWidth, 0.01f);
        var pattern = new float[dashPoints.Length];
        for (var i = 0; i < dashPoints.Length; i++)
        {
            pattern[i] = P(context, dashPoints[i]) / unit;
        }

        return new PatternPen(color, strokeWidth, pattern);
    }
}
