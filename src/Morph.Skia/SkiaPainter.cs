using SkiaSharp;

/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> to PNG bitmaps — the raster analogue of
/// <c>PdfPainter</c> (docs/layout-engine-proposal.md, step 6). A pure draw pass: every page size, line and
/// run position comes from the tree the <c>Fragmenter</c> already produced, so there is no measurement and
/// no pagination here. The tree is in points and Skia draws in pixels, so every coordinate scales by
/// <see cref="RenderContextBase.PointsToPixels"/>. One RGBA8888 bitmap per page is encoded to PNG and
/// streamed through the same page callback the production <c>SkiaPageRenderer</c> uses.
///
/// <para>Covers the block/table/column subset: paragraph text with its run
/// decorations, tables (cell shading, content, borders), paragraph shading and borders, inline images, tab
/// leaders, and floating shapes (solid, gradient or outline fill, freeform or preset). Deferred to later
/// slices: cell-anchored image-fill shapes (a body one is routed to a PlacedImage and draws), image
/// rotation/flip/crop, and per-glyph advances.</para>
/// </summary>
static class SkiaPainter
{
    public static void Paint(LaidOutDocument document, SkiaRenderContext context, Action<Action<Stream>> pageCallback)
    {
        foreach (var laidOutPage in document.Pages)
        {
            var (pageWidth, pageHeight) = context.PagePixels(laidOutPage.Settings);
            using var bitmap = new SKBitmap(pageWidth, pageHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
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

    // A warped WordArt figure. The warp geometry (text on an arc, envelope, wave) lives in the backend's
    // WordArt rasterizer, which lays the shape out on a page sized to its box and returns a
    // transparent-background PNG — so the painter reuses it verbatim rather than reimplementing the presets.
    // The PNG is the box surrounded by WordArtRasterPage.Padding on every side (several warps draw past the
    // declared box), so the draw origin steps back by that padding and the rectangle grows to match, leaving
    // the box region at (X, Y) and letting the overflow spill onto the page — mirroring PdfPainter and the
    // production renderers. Rasterizing at the render's own DPI keeps the bitmap pixel-aligned with the page.
    static void PaintWordArt(SkiaRenderContext context, SKCanvas canvas, PlacedWordArt wordArt)
    {
        var options = new WordArtRasterOptions
        {
            Dpi = context.Dpi,
            FontWidthScale = context.FontWidthScale,
            FontFallback = context.FontFallback,
            FontDirectory = context.FontDirectory,
            Deterministic = context.DeterministicRendering
        };

        if (new SkiaWordArtRasterizer().Render(wordArt.Visual, options) is not { } png ||
            context.GetBitmap(png) is not { } bitmap)
        {
            return;
        }

        var pad = (float) WordArtRasterPage.Padding(wordArt.Visual);
        canvas.DrawBitmap(bitmap, SKRect.Create(
            P(context, wordArt.X - pad),
            P(context, wordArt.Y - pad),
            P(context, wordArt.Width + 2 * pad),
            P(context, wordArt.Height + 2 * pad)));
    }

    static void PaintImage(SkiaRenderContext context, SKCanvas canvas, PlacedImage image)
    {
        if (image.ShapeGroup != null)
        {
            PaintInlineGroup(context, canvas, image);
            return;
        }

        if (image.Data is not { } data || context.GetBitmap(data) is not { } bitmap)
        {
            return;
        }

        // Rotation/flip/crop are deferred; inline images in the covered subset draw upright.
        canvas.DrawBitmap(bitmap, SKRect.Create(P(context, image.X), P(context, image.Y), P(context, image.Width), P(context, image.Height)));
    }

    // EMU per point (914400 EMU/inch ÷ 72 pt/inch), matching TextRenderer — a group member's a:ln/@w is
    // absolute EMU and converts to points independent of the child-coordinate scale.
    const float emusPerPoint = 12700f;

    // An inline shape group (a grouped drawing embedded in a run): its child shapes scaled from the group's
    // child coordinate space into the inline box, painted back to front. A verbatim port of
    // SkiaShapeDrawing.RenderInlineShapeGroup that reuses the same colour/contour/primitive/picture helpers, so
    // the engine paints an inline group pixel-identically to production. The placed box is already in points
    // with its top at Y (the fragmenter set Y = baseline − height), so no baseline subtraction is needed.
    static void PaintInlineGroup(SkiaRenderContext context, SKCanvas canvas, PlacedImage placed)
    {
        var group = placed.ShapeGroup!;
        float pixelX = P(context, placed.X), pixelY = P(context, placed.Y), pixelWidth = P(context, placed.Width), pixelHeight = P(context, placed.Height);

        var sx = pixelWidth / (float) group.ChildExtentX;
        var sy = pixelHeight / (float) group.ChildExtentY;

        canvas.Save();
        if (group.RotationDegrees != 0)
        {
            canvas.RotateDegrees((float) group.RotationDegrees, pixelX + pixelWidth / 2f, pixelY + pixelHeight / 2f);
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

                using var linePaint = new SKPaint
                {
                    Color = SkiaShapeDrawing.ParseColor(shape.ColorHex, shape.LineAlpha),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float) (shape.LineWidthEmu > 0 ? shape.LineWidthEmu / emusPerPoint : 0.75) * context.Scale,
                    StrokeCap = SKStrokeCap.Square,
                    IsAntialias = true
                };
                canvas.DrawLine(startX, startY, endX, endY, linePaint);
                continue;
            }

            var isEllipse = shape.Geometry == GroupShapeGeometry.Ellipse;
            var rect = new SKRect(x1, y1, x1 + width, y1 + height);
            using var geometryPath = SkiaShapeDrawing.BuildGroupShapePath(shape, rect);

            if (shape.Shadow is { } shadow)
            {
                using var shadowPaint = new SKPaint
                {
                    Color = SkiaShapeDrawing.ParseColor(shadow.ColorHex, shadow.Alpha),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                var offset = rect;
                offset.Offset((float) shadow.OffsetX * sx, (float) shadow.OffsetY * sy);
                using var shadowPath = SkiaShapeDrawing.BuildGroupShapePath(shape, offset);
                if (shadowPath != null)
                {
                    canvas.DrawPath(shadowPath, shadowPaint);
                }
                else
                {
                    SkiaShapeDrawing.DrawGeometry(canvas, offset, isEllipse, shadowPaint);
                }
            }

            if (shape.ImageData != null)
            {
                SkiaShapeDrawing.RenderGroupPicture(context, canvas, shape, rect, isEllipse);
            }
            else if (shape.FillColorHex is { } fillHex)
            {
                using var fillPaint = new SKPaint
                {
                    Color = SkiaShapeDrawing.ParseColor(fillHex, shape.FillAlpha),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                if (geometryPath != null)
                {
                    canvas.DrawPath(geometryPath, fillPaint);
                }
                else
                {
                    SkiaShapeDrawing.DrawGeometry(canvas, rect, isEllipse, fillPaint);
                }
            }

            if (shape.LineWidthEmu > 0)
            {
                using var strokePaint = new SKPaint
                {
                    Color = SkiaShapeDrawing.ParseColor(shape.ColorHex, shape.LineAlpha),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float) (shape.LineWidthEmu / emusPerPoint) * context.Scale,
                    IsAntialias = true
                };
                if (geometryPath != null)
                {
                    canvas.DrawPath(geometryPath, strokePaint);
                }
                else
                {
                    SkiaShapeDrawing.DrawGeometry(canvas, rect, isEllipse, strokePaint);
                }
            }
        }

        canvas.Restore();
    }

    // A behind-text floating shape: fill and outline of its freeform contours (reusing the production
    // subpath geometry) or its preset rect/ellipse box. Fill is a solid colour or a linear gradient
    // (built the same way as the production SkiaPageRenderer). Image-fill shapes never reach here — the
    // Fragmenter routes them to PlacedImage — so ImageData is a defensive skip.
    static void PaintShape(SkiaRenderContext context, SKCanvas canvas, PlacedShape placed)
    {
        var shape = placed.Shape;
        if (shape.ImageData is { Length: > 0 })
        {
            return;
        }

        float x = P(context, placed.X), y = P(context, placed.Y), width = P(context, placed.Width), height = P(context, placed.Height);

        // A gradient paint owns its shader and is disposed here; a solid fill and the outline are reused
        // paints owned by the context and must not be disposed.
        SKPaint? gradientFill = null;
        if (shape.Gradient is { } gradient)
        {
            gradientFill = BuildGradientPaint(gradient, x, y, width, height);
        }

        // Honour the shape's fill/line opacity (a:alpha) — e.g. cover-letters/10's header banner is a 10%
        // accent tint that reads as near-white, not a solid panel. SkiaShapeDrawing.ParseColor applies the alpha.
        var fill = gradientFill ?? (shape.FillColorHex is { } fillHex ? context.GetReusableFillPaint(SkiaShapeDrawing.ParseColor(fillHex, shape.FillAlpha), antialias: true) : null);
        var line = shape.LineColorHex is { } lineHex ? context.GetReusableRulePaint(SkiaShapeDrawing.ParseColor(lineHex, shape.LineAlpha), P(context, Math.Max(0.5, shape.LineWidthPoints ?? 1))) : null;

        if (fill != null || line != null)
        {
            if (shape.Subpaths is { Count: > 0 })
            {
                using var path = SkiaShapeDrawing.BuildPolygonPath(shape, x, y, width, height);
                if (fill != null)
                {
                    canvas.DrawPath(path, fill);
                }

                if (line != null)
                {
                    canvas.DrawPath(path, line);
                }
            }
            else
            {
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
        }

        if (gradientFill != null)
        {
            gradientFill.Shader?.Dispose();
            gradientFill.Dispose();
        }
    }

    // A linear gradient across the shape's bounding box, matching SkiaPageRenderer: angle 0° points along
    // +X (OOXML a:lin/@ang), the stops run corner-to-corner through the box centre. The caller owns and
    // disposes the returned paint and its shader.
    static SKPaint BuildGradientPaint(GradientFill gradient, float x, float y, float width, float height)
    {
        var radians = gradient.DirectionDegrees * Math.PI / 180.0;
        var dx = (float) Math.Cos(radians);
        var dy = (float) Math.Sin(radians);
        var centreX = x + width / 2;
        var centreY = y + height / 2;
        var halfDiagonal = (float) Math.Sqrt(width * width + height * height) / 2;
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(centreX - dx * halfDiagonal, centreY - dy * halfDiagonal),
            new SKPoint(centreX + dx * halfDiagonal, centreY + dy * halfDiagonal),
            [SKColor.Parse(gradient.StartColorHex), SKColor.Parse(gradient.EndColorHex)],
            SKShaderTileMode.Clamp);
        return new SKPaint
        {
            Shader = shader,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
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
