using Morph;

/// <summary>
/// Paints a backend-independent <see cref="LaidOutDocument"/> to PNG bitmaps — the raster analogue of
/// <c>PdfPainter</c> (docs/layout-engine.md, step 6). A pure draw pass: every page size, line and
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
    public static void Paint(LaidOutDocument document, SkiaRenderContext context, PageCrop crop, Action<Action<Stream>> pageCallback)
    {
        foreach (var laidOutPage in document.Pages)
        {
            PaintPage(laidOutPage, context, crop, pageCallback);
        }
    }

    // One page, drawn and encoded. Touches no state but the context's, so pages on different threads
    // need only different contexts (ParallelPages).
    public static void PaintPage(LaidOutPage laidOutPage, SkiaRenderContext context, PageCrop crop, Action<Action<Stream>> pageCallback)
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

        // Decorative page borders (w:pgBorders) frame every page, over the content (Word's
        // default zOrder is front). Geometry is shared via PageBorders.EdgeRect.
        if (laidOutPage.Settings.PageBorders is { HasAnyBorder: true } pageBorders)
        {
            var (borderX, borderY, borderWidth, borderHeight) = pageBorders.EdgeRect(laidOutPage.Settings);
            PaintEdges(context, canvas, borderX, borderY, borderWidth, borderHeight, pageBorders.Edges, BorderStroke.Scope.Page);
        }

        Encode(bitmap, context.PageRect(laidOutPage.Settings, crop), pageCallback);
    }

    // The page is always painted whole and a rectangle of it emitted, rather than the canvas being
    // translated onto a smaller surface. That keeps the crop provably pure — the surviving pixels
    // are the ones a full-page render produced, untouched — and it is the only option on the
    // ImageSharp side, whose nested Save(DrawingOptions) calls replace a page-level transform
    // rather than composing with it. Both painters therefore work the same way.
    static void Encode(SKBitmap bitmap, (int X, int Y, int Width, int Height) rect, Action<Action<Stream>> pageCallback)
    {
        if (rect is { X: 0, Y: 0 } &&
            rect.Width == bitmap.Width &&
            rect.Height == bitmap.Height)
        {
            Write(bitmap, pageCallback);
            return;
        }

        // ExtractSubset points the destination at a window onto the source's own pixels rather than
        // copying, so it stays valid only while the page bitmap does — hence encoding here, inside
        // the loop, rather than handing the subset back to the caller.
        using var cropped = new SKBitmap();
        bitmap.ExtractSubset(cropped, new(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height));
        Write(cropped, pageCallback);
    }

    // A page is opaque (its background is painted first), so premultiplied pixels are its colours as
    // they stand, and PngWriter picks the smallest PNG form they allow.
    static void Write(SKBitmap bitmap, Action<Action<Stream>> pageCallback)
    {
        using var pixmap = bitmap.PeekPixels();
        pageCallback(_ => PngWriter.Write(_, pixmap.GetPixelSpan(), pixmap.Width, pixmap.Height, pixmap.RowBytes));
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
            case PlacedRotatedGroup group:
                PaintRotatedGroup(context, canvas, group);
                break;
            case PlacedClipGroup clip:
                PaintClipGroup(context, canvas, clip);
                break;
            case PlacedScaledGroup scaled:
                PaintScaledGroup(context, canvas, scaled);
                break;
        }
    }

    // Items laid out on a page of their own, drawn shrunk onto this one (PlacedScaledGroup): a page
    // with its comment markup.
    static void PaintScaledGroup(SkiaRenderContext context, SKCanvas canvas, PlacedScaledGroup group)
    {
        canvas.Save();
        canvas.Translate(P(context, group.X), P(context, group.Y));
        canvas.Scale(group.Scale);
        foreach (var item in group.Items)
        {
            PaintItem(context, canvas, item);
        }

        canvas.Restore();
    }

    // Items drawn only inside the group's box (PlacedClipGroup): the part of a sheet's drawings that
    // falls on the rows one page holds.
    static void PaintClipGroup(SkiaRenderContext context, SKCanvas canvas, PlacedClipGroup clip)
    {
        canvas.Save();
        canvas.ClipRect(new(P(context, clip.X), P(context, clip.Y), P(context, clip.X + clip.Width), P(context, clip.Y + clip.Height)));
        foreach (var item in clip.Items)
        {
            PaintItem(context, canvas, item);
        }

        canvas.Restore();
    }

    // Items laid out in the group's unrotated box, drawn rotated about that box's centre
    // (PlacedRotatedGroup): a w:textDirection cell or a rotated text box.
    static void PaintRotatedGroup(SkiaRenderContext context, SKCanvas canvas, PlacedRotatedGroup group)
    {
        canvas.Save();
        canvas.RotateDegrees((float) group.RotationDegrees, P(context, group.X + group.Width / 2), P(context, group.Y + group.Height / 2));
        foreach (var item in group.Items)
        {
            PaintItem(context, canvas, item);
        }

        canvas.Restore();
    }

    static void Fill(SkiaRenderContext context, SKCanvas canvas, double x, double y, double width, double height, string? colorHex) =>
        canvas.DrawRect(P(context, x), P(context, y), P(context, width), P(context, height),
            context.GetReusableFillPaint(SkiaRenderContext.ParseColor(colorHex), antialias: false));

    static void PaintLine(SkiaRenderContext context, SKCanvas canvas, PlacedLine line)
    {
        var ascent = line.Baseline - line.Y;

        // A line carrying a tracked change gets Word's change bar: a thin black rule in the left
        // margin spanning the line (Word-measured on tracked_changes/01 — a ~0.5pt rule at half
        // the left margin, x=36pt inside a 72pt margin at 150 DPI).
        foreach (var run in line.Runs)
        {
            if (run.Properties.IsRevisionMark)
            {
                var barX = P(context, context.PageSettings.MarginLeft / 2);
                canvas.DrawLine(barX, P(context, line.Y), barX, P(context, line.Y + line.Height), context.GetReusableRulePaint(SKColors.Black, P(context, 0.75)));
                break;
            }
        }

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

            // A superscript or subscript sits off the line baseline by its shift (VerticalRunPosition).
            var baseline = line.Baseline - run.BaselineShift;
            // w:w stretches the run about its own start, glyphs and advances alike, as Word's XPS draws it.
            if (properties.CharacterScalePercent != 100)
            {
                canvas.Save();
                canvas.Scale((float) (properties.CharacterScalePercent / 100), 1, P(context, run.X), 0);
                DrawTracked(context, canvas, run.Text, properties, run.X, baseline);
                canvas.Restore();
            }
            else
            {
                DrawTracked(context, canvas, run.Text, properties, run.X, baseline);
            }

            if (properties.Underline || properties.Strikethrough || properties.DoubleStrikethrough)
            {
                PaintDecorations(context, canvas, run, properties, color, baseline, ascent);
            }

            // w:bdr — a box around this run alone, over the glyphs like Word's, its rules the drawn
            // stack plus the floored w:space outside the font's line box (BorderStroke.RunBorderBox).
            if (properties.Border is {} runBorder &&
                BorderStroke.Draws(runBorder))
            {
                var (boxX, boxY, boxWidth, boxHeight) = BorderStroke.RunBorderBox(runBorder, run.X, run.Width, line.Y, line.Height, BorderStroke.LinePad(line.Runs), !run.BorderInside);
                PaintEdges(context, canvas, boxX, boxY, boxWidth, boxHeight, CellBorders.Uniform(runBorder));
            }
        }

        foreach (var image in line.Images)
        {
            // An inline chart arrives drawn: its shapes and labels, placed (PlacedImage.Composed).
            if (image.Composed is { } composed)
            {
                foreach (var item in composed)
                {
                    PaintItem(context, canvas, item);
                }

                continue;
            }

            PaintImage(context, canvas, image);
        }
    }

    // The run's underline and strike rules (TextDecorations). w:u/@w:color paints the underline in its
    // own colour; absent means the text colour, which the strike always takes.
    static void PaintDecorations(SkiaRenderContext context, SKCanvas canvas, PlacedRun run, RunProperties properties, SKColor color, float baseline, float ascent)
    {
        var underlineColor = properties.UnderlineColorHex == null
            ? color
            : SkiaRenderContext.ParseColor(properties.UnderlineColorHex);
        var rules = new List<TextDecorations.Rule>();
        var waves = new List<TextDecorations.Wave>();
        TextDecorations.Collect(properties, run.Text, run.X, run.Width, baseline, ascent, _ => MeasurePrefix(context, properties, _), rules, waves);
        foreach (var rule in rules)
        {
            var y = P(context, rule.Y);
            canvas.DrawLine(P(context, rule.X0), y, P(context, rule.X1), y, context.GetReusableRulePaint(rule.IsStrike ? color : underlineColor, P(context, rule.Thickness)));
        }

        foreach (var wave in waves)
        {
            var paint = context.GetReusableRulePaint(underlineColor, P(context, wave.Thickness));
            for (var index = 1; index < wave.Points.Count; index++)
            {
                var (x0, y0) = wave.Points[index - 1];
                var (x1, y1) = wave.Points[index];
                canvas.DrawLine(P(context, x0), P(context, y0), P(context, x1), P(context, y1), paint);
            }
        }
    }

    // The drawn width in points of a prefix of a run's text, as DrawTracked advances it (kerning aside).
    static float MeasurePrefix(SkiaRenderContext context, RunProperties properties, string prefix)
    {
        var font = context.CreateFont(properties);
        return (float) (font.MeasureText(prefix) / context.Scale + properties.CharacterSpacingPoints * prefix.Length);
    }

    // Draws text, spreading each character by w:spacing tracking and pulling each by its kern. The run's
    // placed width already includes both (the measurer kerns and tracks the advances), so the following run
    // starts past it; Skia's own DrawText takes no pair adjustment, so a kerned run is drawn glyph by glyph
    // with the kern the layout measured added to each pen (CanonicalTextMeasurer.KernShiftsPoints).
    // Per-glyph so surrogate pairs stay intact — mirrors PdfPainter.DrawTracked.
    static void DrawTracked(SkiaRenderContext context, SKCanvas canvas, string text, RunProperties properties, double penX, double baseline)
    {
        var font = context.CreateFont(properties);
        var paint = context.GetReusableTextPaint(properties);
        var y = P(context, baseline);
        var kernShifts = KernShifts(context, text, properties);

        // A bold run in a face that is not bold: the glyphs are filled and stroked, which thickens every
        // stem by the stroke's width (BoldSimulation). The paint is shared, so it is put back after.
        if (properties.SyntheticBold != SyntheticBold.None)
        {
            paint.Style = SKPaintStyle.StrokeAndFill;
            paint.StrokeWidth = P(context, BoldSimulation.StrokeWidthPoints(properties.SyntheticBold, VerticalRunPosition.RenderSizePoints(properties), 72.0 / context.Dpi));
            paint.StrokeJoin = SKStrokeJoin.Miter;
            DrawSpaced(context, canvas, text, properties, penX, y, font, paint, kernShifts);
            paint.Style = SKPaintStyle.Fill;
            paint.StrokeWidth = 0;
            return;
        }

        DrawSpaced(context, canvas, text, properties, penX, y, font, paint, kernShifts);
    }

    static void DrawSpaced(SkiaRenderContext context, SKCanvas canvas, string text, RunProperties properties, double penX, float y, SKFont font, SKPaint paint, double[]? kernShifts)
    {
        if ((properties.CharacterSpacingPoints == 0 &&
             kernShifts == null) ||
            text.Length <= 1)
        {
            canvas.DrawText(text, P(context, penX), y, font, paint);
            return;
        }

        // Tracked text goes glyph by glyph; kerned text splits only where a pair adjusts.
        var trackingPixels = P(context, properties.CharacterSpacingPoints);
        var start = P(context, penX);
        var advanced = 0f;
        var i = 0;
        while (i < text.Length)
        {
            var end = SegmentEnd(text, i, trackingPixels != 0, kernShifts);
            var piece = text.Substring(i, end - i);
            var x = start + advanced;
            if (kernShifts != null)
            {
                x += P(context, kernShifts[i]);
            }

            canvas.DrawText(piece, x, y, font, paint);
            advanced += font.MeasureText(piece) + trackingPixels;
            i = end;
        }
    }

    // One glyph (a surrogate pair intact) for tracked text, else the stretch sharing one kern.
    static int SegmentEnd(string text, int start, bool tracked, double[]? kernShifts)
    {
        if (tracked || kernShifts == null)
        {
            if (char.IsHighSurrogate(text[start]) &&
                start + 1 < text.Length)
            {
                return start + 2;
            }

            return start + 1;
        }

        return CanonicalTextMeasurer.KernSegmentEnd(text, kernShifts, start);
    }

    // The kern the layout gave each glyph of a run, at the face and size it was measured with (a
    // superscript's reduced size included), or null for a run that does not kern.
    static double[]? KernShifts(SkiaRenderContext context, string text, RunProperties properties)
    {
        if (!CanonicalParagraphMeasurer.KerningEnabled(properties))
        {
            return null;
        }

        var metrics = context.LayoutMetrics(properties.FontFamily, properties.Bold, properties.Italic);
        if (metrics == null)
        {
            return null;
        }

        return CanonicalTextMeasurer.KernShiftsPoints(metrics, text, VerticalRunPosition.RenderSizePoints(properties), context.FontWidthScale);
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

        // Word draws a leader as the glyph repeated at its NATURAL advance — a literal run of
        // dots — not at a doubled stride. Measured on table_of_contents/01: Word's dot pitch is
        // ~6.3px at 150 DPI (the '.' advance) with the last dot within one advance of the page
        // number, where the doubled stride tiled half as many dots and stopped ~14px short.
        var runWidth = P(context, run.Width);
        if (!LeaderTiling.TryGetRange(P(context, run.X), runWidth, glyphWidth, glyphWidth, P(context, context.PageSettings.WidthPoints), out var startX, out var count))
        {
            return;
        }

        var paint = context.GetReusableTextPaint(run.Properties);
        var y = P(context, baseline);
        for (var index = 0; index < count; index++)
        {
            canvas.DrawText(leaderChar, (float) startX + index * glyphWidth, y, font, paint);
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

        // A picture stored as SVG is drawn from the drawing the reader made of it (PlacedImage.Vector),
        // and its raster fallback is then never decoded. Until 2026-10-12 this painter drew the
        // fallback for every inline and floating picture: the layout engine handed it nothing else,
        // so icons_multiple's red and green stars — whose fallbacks are one blue PNG — came out blue.
        SKBitmap? bitmap = null;
        if (image.Vector == null &&
            (image.Data is not { } data || (bitmap = context.GetBitmap(data)) == null))
        {
            return;
        }

        // An a:srcRect crop draws through the shared helper, so an engine-painted picture matches the
        // group-picture path. business-plans/13's cover photo is one: 15% off each side and 1.2% off the
        // top, which Skia alone ignored — ImageSharp and PDF already routed Crop into their own draws,
        // and the uncropped stretch was 79% of that page's error against Word.
        //
        // a:xfrm rotation and flips transform about the box centre, exactly as PdfPainter.PaintImage
        // does — this painter drew rotated inline images UPRIGHT until 2026-08-19 (image_rotation/01
        // showed a plain rectangle against the other backends' diamonds).
        //
        // The blip's effects — Word's Recolor gallery (a:duotone / a:grayscl / a:lum) and the
        // a:alphaModFix transparency — ride on the draw paint, so the pixels are filtered on their
        // way to the canvas rather than decoded and rewritten. Both draws below take it, since a
        // rotated picture carries its effects too.
        using var paint = SkiaImageEffects.Paint(image.Recolor, image.Opacity);

        var box = SKRect.Create(P(context, image.X), P(context, image.Y), P(context, image.Width), P(context, image.Height));

        // The outline the picture is cut to - an ellipse, or the freeform contours of the shape it
        // fills (PlacedImage.ClipToEllipse / ClipSubpaths). It is set in the box's own frame, so it
        // turns and mirrors with the picture.
        using var clip = ImageClip(image, box);
        var transformed = Math.Abs(image.RotationDegrees) > 0.01 || image.FlipHorizontal || image.FlipVertical;
        if (!transformed &&
            clip == null)
        {
            DrawPicture(context, canvas, bitmap, box, image, paint);
            PaintImageOutline(context, canvas, image, box);
            return;
        }

        canvas.Save();
        if (Math.Abs(image.RotationDegrees) > 0.01)
        {
            canvas.RotateDegrees((float) image.RotationDegrees, box.MidX, box.MidY);
        }

        if (image.FlipHorizontal || image.FlipVertical)
        {
            canvas.Translate(box.MidX, box.MidY);
            canvas.Scale(image.FlipHorizontal ? -1 : 1, image.FlipVertical ? -1 : 1);
            canvas.Translate(-box.MidX, -box.MidY);
        }

        if (clip == null)
        {
            DrawPicture(context, canvas, bitmap, box, image, paint);
            PaintImageOutline(context, canvas, image, box);
        }
        else
        {
            canvas.Save();
            canvas.ClipPath(clip, antialias: true);
            DrawPicture(context, canvas, bitmap, box, image, paint);
            canvas.Restore();
            PaintImageOutline(context, canvas, image, clip);
        }

        canvas.Restore();
    }

    // The picture into its box: stretched over it through its crop, or - a tiled fill - repeated at
    // the tile's size from the tile's origin. The tiles are one shader fill of the box, so
    // neighbours meet without a seam and the clip already set cuts them like any other picture.
    static void DrawPicture(SkiaRenderContext context, SKCanvas canvas, SKBitmap? bitmap, SKRect box, PlacedImage image, SKPaint? paint)
    {
        // The vector form of an SVG picture, or with none the bitmap PaintImage decoded.
        if (image.Vector is { } vector)
        {
            SkiaVectorDrawing.Draw(canvas, vector, box, image.Crop, image.Recolor, image.Opacity);
            return;
        }

        if (bitmap == null)
        {
            return;
        }

        if (image.Tile is not { } tile)
        {
            SkiaShapeDrawing.DrawCropped(canvas, bitmap, box, image.Crop, paint);
            return;
        }

        var (originX, originY) = tile.Origin(image.Width, image.Height);
        var placement = SKMatrix.CreateScale(P(context, tile.WidthPoints) / bitmap.Width, P(context, tile.HeightPoints) / bitmap.Height)
            .PostConcat(SKMatrix.CreateTranslation(box.Left + P(context, originX), box.Top + P(context, originY)));
        using var shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, placement);
        using var fill = paint?.Clone() ?? new SKPaint();
        fill.Shader = shader;
        canvas.DrawRect(box, fill);
    }

    static SKPath? ImageClip(PlacedImage image, SKRect box)
    {
        if (image.ClipSubpaths is { } contours)
        {
            var path = new SKPath();
            foreach (var contour in contours)
            {
                if (contour.Count < 3)
                {
                    continue;
                }

                for (var index = 0; index < contour.Count; index++)
                {
                    var point = new SKPoint(box.Left + (float) contour[index].X * box.Width, box.Top + (float) contour[index].Y * box.Height);
                    if (index == 0)
                    {
                        path.MoveTo(point);
                    }
                    else
                    {
                        path.LineTo(point);
                    }
                }

                path.Close();
            }

            return path;
        }

        if (image.ClipToEllipse)
        {
            var path = new SKPath();
            path.AddOval(box);
            return path;
        }

        return null;
    }

    // A cut picture's a:ln follows the cut, centred on its edge.
    static void PaintImageOutline(SkiaRenderContext context, SKCanvas canvas, PlacedImage image, SKPath clip)
    {
        if (image.Outline is not { } outline)
        {
            return;
        }

        using var strokePaint = new SKPaint
        {
            Color = SkiaShapeDrawing.ParseColor(outline.ColorHex, outline.Alpha),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = P(context, (float) outline.WidthPoints),
            IsAntialias = true
        };
        canvas.DrawPath(clip, strokePaint);
    }

    // The picture's a:ln, stroked after the bitmap (PlacedImage.Outline) as a band lying entirely
    // outside the picture's box — from its edge to the line width beyond it (ImageOutline).
    static void PaintImageOutline(SkiaRenderContext context, SKCanvas canvas, PlacedImage image, SKRect box)
    {
        if (image.Outline is not { } outline)
        {
            return;
        }

        var strokeWidth = P(context, (float) outline.WidthPoints);
        using var strokePaint = new SKPaint
        {
            Color = SkiaShapeDrawing.ParseColor(outline.ColorHex, outline.Alpha),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };
        canvas.DrawRect(SKRect.Inflate(box, strokeWidth / 2, strokeWidth / 2), strokePaint);
    }

    // EMU per point (914400 EMU/inch ÷ 72 pt/inch), matching TextRenderer — a group member's a:ln/@w is

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
                    StrokeWidth = (float) (shape.LineWidthEmu > 0 ? shape.LineWidthEmu / OoxmlUnits.EmusPerPointF : 0.75) * context.Scale,
                    StrokeCap = SKStrokeCap.Square,
                    IsAntialias = true
                };
                using var lineDash = Dash(linePaint, shape.LineDashPattern);
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
                    StrokeWidth = (float) (shape.LineWidthEmu / OoxmlUnits.EmusPerPointF) * context.Scale,
                    StrokeCap = shape.RoundCap ? SKStrokeCap.Round : SKStrokeCap.Butt,
                    IsAntialias = true
                };
                using var strokeDash = Dash(strokePaint, shape.LineDashPattern);
                if (shape.OpenOutline &&
                    SkiaShapeDrawing.BuildGroupShapePath(shape, rect, close: false) is { } openPath)
                {
                    canvas.DrawPath(openPath, strokePaint);
                    openPath.Dispose();
                }
                else if (geometryPath != null)
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
    // Gives a stroke its preset dash (a:prstDash): the pattern is in multiples of the stroke's own
    // width, and a dashed stroke ends each dash flat, as Word draws it. Returns the effect for the
    // caller to dispose, or null for a solid stroke.
    static SKPathEffect? Dash(SKPaint paint, IReadOnlyList<double>? pattern)
    {
        if (ShapeStroke.DashLengths(pattern, paint.StrokeWidth) is not { } lengths)
        {
            return null;
        }

        var effect = SKPathEffect.CreateDash(lengths, 0);
        paint.PathEffect = effect;
        paint.StrokeCap = SKStrokeCap.Butt;
        return effect;
    }

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
        var line = shape.LineColorHex is { } lineHex ? context.GetReusableRulePaint(SkiaShapeDrawing.ParseColor(lineHex, shape.LineAlpha), P(context, ShapeStroke.WidthPoints(shape.LineWidthPoints))) : null;

        // The outline's preset dash. The paint is the context's shared one, so the effect comes off it
        // again below, before it is disposed.
        var lineDash = line == null ? null : Dash(line, shape.LineDashPattern);

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
                    // The shared rule paint is butt-capped; a round-capped outline takes it round
                    // for this path only.
                    line.StrokeCap = shape.RoundCap ? SKStrokeCap.Round : SKStrokeCap.Butt;
                    canvas.DrawPath(path, line);
                    line.StrokeCap = SKStrokeCap.Butt;
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

        if (lineDash != null)
        {
            line!.PathEffect = null;
            lineDash.Dispose();
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
            new(centreX - dx * halfDiagonal, centreY - dy * halfDiagonal),
            new(centreX + dx * halfDiagonal, centreY + dy * halfDiagonal),
            [SkiaShapeDrawing.ParseColor(gradient.StartColorHex, gradient.StartAlpha), SkiaShapeDrawing.ParseColor(gradient.EndColorHex, gradient.EndAlpha)],
            SKShaderTileMode.Clamp);
        return new()
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

            // Cell-anchored art overhangs the box freely (PlacedCell.Floats).
            foreach (var floating in cell.Floats)
            {
                PaintItem(context, canvas, floating);
            }

            // A clipping cell bounds only its CONTENT — the shading above and the borders below draw
            // in full, exactly as Excel draws a gridline over text it has cut off.
            var clipped = cell.ClipContent;
            if (clipped)
            {
                canvas.Save();
                canvas.ClipRect(new(
                    cell.ClipHorizontally ? P(context, cell.X - cell.ClipSpillLeft) : float.MinValue / 2,
                    cell.ClipVertically ? P(context, cell.Y) : float.MinValue / 2,
                    cell.ClipHorizontally ? P(context, cell.X + cell.Width + cell.ClipSpillRight) : float.MaxValue / 2,
                    cell.ClipVertically ? P(context, cell.Y + cell.Height) : float.MaxValue / 2));
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
                PaintCellEdges(context, canvas, cell, borders);
            }

            if (cell.Diagonals is {HasAny: true} diagonals)
            {
                PaintCellDiagonals(context, canvas, cell, diagonals);
            }
        }
    }

    // w:tl2br runs from the top-left corner to the bottom-right, w:tr2bl from the top-right to the
    // bottom-left, each at its grid-floored width (BorderStroke.DiagonalThickness).
    static void PaintCellDiagonals(SkiaRenderContext context, SKCanvas canvas, PlacedCell cell, CellDiagonals diagonals)
    {
        float left = P(context, cell.X), top = P(context, cell.Y), right = P(context, cell.X + cell.Width), bottom = P(context, cell.Y + cell.Height);
        Diagonal(diagonals.Down, left, top, right, bottom);
        Diagonal(diagonals.Up, right, top, left, bottom);

        void Diagonal(BorderEdge edge, float x0, float y0, float x1, float y1)
        {
            if (BorderStroke.DiagonalThickness(edge) is > 0 and var thickness)
            {
                canvas.DrawLine(x0, y0, x1, y1, EdgePen(context, edge, thickness, BorderStroke.DashPattern(edge.Style, edge.WidthPoints), 1));
            }
        }
    }

    // A cell's edges in Word's cell geometry (BorderStroke.CellEdgeLines): the horizontals hang down from
    // their grid line, the verticals are centred on theirs, and every band is on the 120-dpi grid. Only the
    // waves keep the paragraph path along the box edge.
    static void PaintCellEdges(SkiaRenderContext context, SKCanvas canvas, PlacedCell cell, CellBorders borders)
    {
        foreach (var line in BorderStroke.CellEdgeLines(cell.X, cell.Y, cell.Width, cell.Height, borders, cell.BottomEdgeInset))
        {
            var pen = EdgePen(context, line.Edge, line.Thickness, BorderStroke.DashPattern(line.Edge.Style, line.Edge.WidthPoints), line.Shade);
            var at = P(context, line.At);
            var from = P(context, line.From);
            var to = P(context, line.To);
            if (line.Horizontal)
            {
                canvas.DrawLine(from, at, to, at, pen);
            }
            else
            {
                canvas.DrawLine(at, from, at, to, pen);
            }
        }

        float left = P(context, cell.X), top = P(context, cell.Y), right = P(context, cell.X + cell.Width), bottom = P(context, cell.Y + cell.Height);
        Wave(borders.Top, horizontal: true, left, right, top, outward: -1);
        Wave(borders.Bottom, horizontal: true, left, right, bottom, outward: 1);
        Wave(borders.Left, horizontal: false, top, bottom, left, outward: -1);
        Wave(borders.Right, horizontal: false, top, bottom, right, outward: 1);

        void Wave(BorderEdge edge, bool horizontal, float from, float to, float at, int outward)
        {
            if (BorderStroke.Draws(edge) &&
                BorderStroke.Waves(edge.Style) is {Length: > 0} waves)
            {
                StrokeWaves(context, canvas, edge, waves, horizontal, from, to, at, outward);
            }
        }
    }

    // Strokes each visible edge of a box (a table cell or a paragraph border), same geometry either way.
    static void PaintEdges(SkiaRenderContext context, SKCanvas canvas, double x, double y, double width, double height, CellBorders borders, BorderStroke.Scope scope = BorderStroke.Scope.Paragraph)
    {
        float left = P(context, x), top = P(context, y), right = P(context, x + width), bottom = P(context, y + height);

        // A multi-line style (double, triple, the thin/thick pairs) strokes each of its lines in
        // turn, offset perpendicular to the edge; a single-line style yields one band at offset 0
        // and draws exactly where it always did.
        // Each edge is extended at an end where the PERPENDICULAR edge also draws, by half its own
        // thickness, so the two overlap and fill the corner square. Without it a butt-capped stroke
        // leaves a notch of half the width at every corner — invisible at 0.5pt, a 3px bite at 3pt.
        // Not extended where the neighbour is absent, so a top-only rule still spans exactly its box.
        var drawsLeft = BorderStroke.Draws(borders.Left);
        var drawsRight = BorderStroke.Draws(borders.Right);
        var drawsTop = BorderStroke.Draws(borders.Top);
        var drawsBottom = BorderStroke.Draws(borders.Bottom);

        StrokeEdge(context, canvas, borders.Top, horizontal: true, left, right, top, scope, outward: -1, drawsLeft, drawsRight);
        StrokeEdge(context, canvas, borders.Bottom, horizontal: true, left, right, bottom, scope, outward: 1, drawsLeft, drawsRight);
        StrokeEdge(context, canvas, borders.Left, horizontal: false, top, bottom, left, scope, outward: -1, drawsTop, drawsBottom);
        StrokeEdge(context, canvas, borders.Right, horizontal: false, top, bottom, right, scope, outward: 1, drawsTop, drawsBottom);
    }

    // Darkens a bevel band's colour. Shade is 1 for every ordinary border, so this is identity
    // except on threeDEngrave/threeDEmboss.
    static SKColor Shaded(SKColor color, double shade)
    {
        if (shade >= 1)
        {
            return color;
        }

        return new((byte) (color.Red * shade), (byte) (color.Green * shade), (byte) (color.Blue * shade), color.Alpha);
    }

    // A wave edge is a triangular zigzag of fixed geometry rather than a straight band — see
    // BorderStroke.Waves. Each zigzag is one polyline; the shared vertex list keeps the three
    // backends drawing the same squiggle.
    static void StrokeWaves(SkiaRenderContext context, SKCanvas canvas, BorderEdge edge, BorderStroke.WaveBand[] waves, bool horizontal, float from, float to, float at, int outward)
    {
        var color = SkiaRenderContext.ParseColor(edge.ColorHex);
        foreach (var wave in waves)
        {
            var centre = at + outward * P(context, wave.Offset);
            var pen = context.GetReusableRulePaint(color, P(context, wave.Thickness));
            using var path = new SKPath();
            var first = true;
            foreach (var (along, across) in BorderStroke.WavePoints(from, to, P(context, wave.Period), P(context, wave.Amplitude)))
            {
                var x = horizontal ? (float) along : centre + (float) across;
                var y = horizontal ? centre + (float) across : (float) along;
                if (first)
                {
                    path.MoveTo(x, y);
                    first = false;
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            if (!first)
            {
                canvas.DrawPath(path, pen);
            }
        }
    }

    // outward is -1 for the top/left edges and +1 for bottom/right: the direction that moves AWAY
    // from the box. The span grows by the same offset at both ends so this band meets the
    // perpendicular edges' matching band at the corner.
    static void StrokeEdge(SkiaRenderContext context, SKCanvas canvas, BorderEdge edge, bool horizontal, float from, float to, float at, BorderStroke.Scope scope, int outward, bool extendStart, bool extendEnd)
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
        var bands = BorderStroke.Bands(edge.Style, edge.WidthPoints, scope, trailingEdge: outward > 0);
        var shift = BorderStroke.OutwardShift(bands, scope);
        foreach (var band in bands)
        {
            var offset = P(context, band.Offset + shift);
            var half = P(context, band.Thickness) / 2;
            var line = at + outward * offset;
            var start = from - offset - (extendStart ? half : 0);
            var end = to + offset + (extendEnd ? half : 0);
            var pen = EdgePen(context, edge, band.Thickness, dash, band.Shade);
            if (horizontal)
            {
                canvas.DrawLine(start, line, end, line, pen);
            }
            else
            {
                canvas.DrawLine(line, start, line, end, pen);
            }
        }
    }

    static SKPaint EdgePen(SkiaRenderContext context, BorderEdge edge, double thickness, float[]? dashPoints, double shade)
    {
        // GetReusableRulePaint clears any previous PathEffect, so a solid edge needs no reset here.
        var pen = context.GetReusableRulePaint(Shaded(SkiaRenderContext.ParseColor(edge.ColorHex), shade), P(context, thickness));
        if (dashPoints != null)
        {
            var intervals = new float[dashPoints.Length];
            for (var i = 0; i < dashPoints.Length; i++)
            {
                intervals[i] = P(context, dashPoints[i]);
            }

            pen.PathEffect = context.GetDashEffect(intervals);
        }

        return pen;
    }
}
