/// <summary>
/// Draws WordArt shapes (warps, outline, shadow, glow, reflection) on an <see cref="SKCanvas"/>.
/// Extracted verbatim from <c>SkiaPageRenderer</c> so the drawing has no page-renderer dependency:
/// the page renderer positions the shape in the flow and delegates here, and
/// <see cref="SkiaWordArtRasterizer"/> draws straight onto its own transparent bitmap without
/// running a page render.
/// </summary>
sealed class SkiaWordArtDrawer(SkiaRenderContext context, SKCanvas canvas)
{
    // Word centres or right-aligns an inline WordArt box per its paragraph's w:jc. Without this the
    // box always sat at the content-box left edge (brochures/08's logo frame landed 43px left of
    // Word's). Clamped at 0 so an over-wide box still starts at the left rather than off-box.
    internal static float AlignWordArtOffset(WordArtElement wordArt, float availableWidth, float boxWidth) =>
        wordArt.Alignment switch
        {
            TextAlignment.Center => Math.Max(0, (availableWidth - boxWidth) / 2),
            TextAlignment.Right => Math.Max(0, availableWidth - boxWidth),
            _ => 0
        };

    public void DrawInline(WordArtElement wordArt, float x, float y, float width, float pixelHeight)
    {
        // Resolve through the bundled FontDirectory. SKTypeface.FromFamilyName only sees system
        // fonts, so a bundled WordArt face like "Impact" returned a non-rendering typeface in the
        // container (GetTextPath produced an empty outline) and the WordArt came out blank.
        var typeface = context.GetTypeface(wordArt.FontFamily, wordArt.Bold, wordArt.Italic);

        var pixelFontSize = context.PointsToPixels((float) wordArt.FontSizePoints);

        // An unwarped pseudo-WordArt is Word's inline text box: stroke its a:ln frame
        // under the text (business/06's LOGO box).
        // The box is not always a rectangle: a prst="frame" is a RING and must fill even-odd, an
        // ellipse draws as a true oval. Fill the background, then stroke the a:ln frame, both under
        // the text.
        using var boxPath = BuildWordArtBoxPath(wordArt, x, y, width, pixelHeight);
        var boxRect = new SKRect(x, y, x + width, y + pixelHeight);

        void DrawBox(SKPaint paint)
        {
            if (boxPath != null)
            {
                canvas.DrawPath(boxPath, paint);
            }
            else if (wordArt.BoxIsEllipse)
            {
                canvas.DrawOval(boxRect, paint);
            }
            else
            {
                canvas.DrawRect(boxRect, paint);
            }
        }

        if (wordArt.BoxFillColorHex is { } boxFill)
        {
            using var boxFillPaint = new SKPaint
            {
                Color = SKColor.Parse(boxFill),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            DrawBox(boxFillPaint);
        }

        if (wordArt is { BoxLineColorHex: { } boxLine, BoxLineWidthPoints: > 0 })
        {
            using var boxPaint = new SKPaint
            {
                Color = SKColor.Parse(boxLine)
                    .WithAlpha((byte) Math.Round(Math.Clamp(wordArt.BoxLineAlpha, 0, 1) * 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.BoxLineWidthPoints),
                IsAntialias = true
            };
            DrawBox(boxPaint);
        }

        // Measure text to calculate scale
        using var measureFont = new SKFont(typeface, pixelFontSize);
        var text = wordArt.Text;
        measureFont.MeasureText(text, out var textBounds);

        // Only shrink to fit; never enlarge past the explicit font size. The shape's
        // bounding box for arc/circle warps is sized for the curve, not the glyph cluster.
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        var transform = wordArt.Transform;
        var fillColor = wordArt.FillColorHex;
        if (TryRenderArch(wordArt, x, y, width, pixelHeight, typeface, pixelFontSize))
        {
            return;
        }

        if (TryRenderEnvelope(wordArt, x, y, width, pixelHeight, typeface, pixelFontSize))
        {
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = x + (width - scaledWidth) / 2;
        var textY = y + (pixelHeight + scaledHeight) / 2;

        DrawFlatText(wordArt, typeface, pixelFontSize * scale, textX, textY, scaledHeight);
    }

    public void DrawFloating(FloatingWordArtElement wordArt, float pixelX, float pixelY, float width, float pixelHeight)
    {
        // Resolve through the bundled FontDirectory. SKTypeface.FromFamilyName only sees system
        // fonts, so a bundled WordArt face like "Impact" returned a non-rendering typeface in the
        // container (GetTextPath produced an empty outline) and the WordArt came out blank.
        var typeface = context.GetTypeface(wordArt.FontFamily, wordArt.Bold, wordArt.Italic);

        var pixelFontSize = context.PointsToPixels((float) wordArt.FontSizePoints);

        // Measure text to calculate scale
        using var measureFont = new SKFont(typeface, pixelFontSize);
        var text = wordArt.Text;
        measureFont.MeasureText(text, out var textBounds);

        // Only shrink to fit; never enlarge past the explicit font size — see note above.
        var scaleX = textBounds.Width > 0 ? width / textBounds.Width : 1;
        var scaleY = textBounds.Height > 0 ? pixelHeight / textBounds.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        var transform = wordArt.Transform;
        var fillColor = wordArt.FillColorHex;
        if (TryRenderArch(wordArt, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize))
        {
            return;
        }

        if (TryRenderEnvelope(wordArt, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize))
        {
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = pixelX + (width - scaledWidth) / 2;
        var textY = pixelY + (pixelHeight + scaledHeight) / 2;

        DrawFlatText(wordArt, typeface, pixelFontSize * scale, textX, textY, scaledHeight);
    }

    /// <summary>
    /// The unwarped fallback shared by the inline and floating paths: shadow, glow, outline, fill
    /// and reflection drawn as flat text. Reached by unwarped text and by a warp no renderer here
    /// draws.
    /// </summary>
    void DrawFlatText(IWordArtVisual wordArt, SKTypeface typeface, float fontSize, float textX, float textY, float scaledHeight)
    {
        // SkiaSharp 4 moved text size/typeface off SKPaint onto SKFont; one font, shared by
        // every draw below (all at the same scaled size). Matches the Antialias edging used
        // by the text-on-path WordArt helpers.
        using var font = new SKFont(typeface, fontSize)
        {
            Edging = SKFontEdging.Antialias
        };

        // Draw shadow first if enabled
        if (wordArt.HasShadow)
        {
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new(0, 0, 0, 80),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawText(wordArt.Text, textX + 3, textY + 3, font, shadowPaint);
        }

        // Draw glow if enabled
        if (wordArt.HasGlow)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                // Gold glow
                Color = new(255, 215, 0, 100),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels(4),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
            };
            canvas.DrawText(wordArt.Text, textX, textY, font, glowPaint);
        }

        // Draw outline if specified
        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SkiaRenderContext.ParseColor(wordArt.OutlineColorHex),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.OutlineWidthPoints)
            };
            canvas.DrawText(wordArt.Text, textX, textY, font, outlinePaint);
        }

        // Draw text fill
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = wordArt.FillColorHex != null ? SkiaRenderContext.ParseColor(wordArt.FillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawText(wordArt.Text, textX, textY, font, fillPaint);

        // Draw reflection if enabled
        if (wordArt.HasReflection)
        {
            canvas.Save();
            canvas.Scale(1, -0.5f, textX, textY + scaledHeight / 2);

            using var reflectionPaint = new SKPaint
            {
                IsAntialias = true,
                Color = fillPaint.Color.WithAlpha(60),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawText(wordArt.Text, textX, textY + scaledHeight * 2, font, reflectionPaint);
            canvas.Restore();
        }
    }

    /// <summary>
    /// Renders <c>textArchUp</c> / <c>textArchDown</c> / <c>textCircle</c> as Word does: the text drawn smaller by
    /// <see cref="WordArtArch.Scale"/>, each glyph turned to the ellipse of the scaled text rect
    /// at its own place along it — see <see cref="WordArtArch"/> for the probed rules. Returns false
    /// for any other warp, or a box with no room for a text rect.
    /// </summary>
    bool TryRenderArch(IWordArtVisual wordArt, float x, float y, float width, float height, SKTypeface typeface, float fontSize)
    {
        if (!WordArtArch.Covers(wordArt.Transform))
        {
            return false;
        }

        var insets = wordArt.Insets;
        var textLeft = x + context.PointsToPixels((float) insets.Left);
        var textTop = y + context.PointsToPixels((float) insets.Top);
        var textWidth = width - context.PointsToPixels((float) (insets.Left + insets.Right));
        var textHeight = height - context.PointsToPixels((float) (insets.Top + insets.Bottom));
        if (textWidth <= 0 || textHeight <= 0)
        {
            return false;
        }

        using var declaredFont = new SKFont(typeface, fontSize);
        declaredFont.MeasureText(wordArt.Text, out var inkBounds);
        var scale = WordArtArch.Scale(WordArtArch.ScaleSide(wordArt.Transform, textWidth, textHeight), inkBounds.Height);

        using var font = new SKFont(typeface, fontSize * scale)
        {
            Edging = SKFontEdging.Antialias
        };
        var glyphs = WordArtArch.Glyphs(wordArt.Text);
        var advances = glyphs.Select(_ => font.MeasureText(_)).ToList();
        var baselineDepth = WordArtArch.BaselineDepthEm(context.LayoutMetrics(wordArt.FontFamily, wordArt.Bold, wordArt.Italic)) * font.Size;
        var placements = WordArtArch.Place(
            advances, textLeft, textTop, textWidth, textHeight, scale, baselineDepth,
            wordArt.TextAlignment, wordArt.Transform);

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = wordArt.FillColorHex != null ? SkiaRenderContext.ParseColor(wordArt.FillColorHex) : SKColors.Black
        };
        using var outlinePaint = wordArt is {OutlineColorHex: { } outlineColor, OutlineWidthPoints: > 0}
            ? new SKPaint
            {
                IsAntialias = true,
                Color = SkiaRenderContext.ParseColor(outlineColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.OutlineWidthPoints)
            }
            : null;

        for (var i = 0; i < glyphs.Count; i++)
        {
            var (originX, originY, angle) = placements[i];
            canvas.Save();
            canvas.RotateDegrees(angle, originX, originY);
            if (outlinePaint != null)
            {
                canvas.DrawText(glyphs[i], originX, originY, font, outlinePaint);
            }

            canvas.DrawText(glyphs[i], originX, originY, font, fillPaint);
            canvas.Restore();
        }

        return true;
    }

    /// <summary>
    /// Renders the envelope warps — every preset <see cref="WordArtEnvelope"/> covers — as Word does:
    /// the line's outline is taken as polylines and its ink box bent onto the envelope drawn in the
    /// shape's text rect, so the text fills the rect whatever size it declares. Returns false for any
    /// other warp, a box with no room for a text rect, or text with no ink.
    /// </summary>
    bool TryRenderEnvelope(IWordArtVisual wordArt, float x, float y, float width, float height, SKTypeface typeface, float fontSize)
    {
        if (!WordArtEnvelope.Covers(wordArt.Transform))
        {
            return false;
        }

        var insets = wordArt.Insets;
        var rectLeft = x + context.PointsToPixels((float) insets.Left);
        var rectTop = y + context.PointsToPixels((float) insets.Top);
        var rectWidth = width - context.PointsToPixels((float) (insets.Left + insets.Right));
        var rectHeight = height - context.PointsToPixels((float) (insets.Top + insets.Bottom));
        if (rectWidth <= 0 || rectHeight <= 0)
        {
            return false;
        }

        using var font = new SKFont(typeface, fontSize);
        using var textPath = font.GetTextPath(wordArt.Text, new SKPoint(0, 0));
        var warped = WordArtEnvelope.Warp(Flatten(textPath), wordArt.Transform, wordArt.WarpAdjust, rectLeft, rectTop, rectWidth, rectHeight);
        if (warped.Count == 0)
        {
            return false;
        }

        using var path = new SKPath();
        foreach (var contour in warped)
        {
            path.MoveTo(contour[0].X, contour[0].Y);
            for (var i = 1; i < contour.Count; i++)
            {
                path.LineTo(contour[i].X, contour[i].Y);
            }

            path.Close();
        }

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Color = wordArt.FillColorHex != null ? SkiaRenderContext.ParseColor(wordArt.FillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(path, fillPaint);

        if (wordArt is {OutlineColorHex: { } outlineColor, OutlineWidthPoints: > 0})
        {
            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SkiaRenderContext.ParseColor(outlineColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = context.PointsToPixels((float) wordArt.OutlineWidthPoints)
            };
            canvas.DrawPath(path, outlinePaint);
        }

        return true;
    }

    /// <summary>
    /// A text outline as closed polylines, each curve cut into short chords. The verbs are walked
    /// directly: SKPathMeasure's contour sampling returns nothing for glyph outlines under
    /// SkiaSharp 4.x.
    /// </summary>
    static List<IReadOnlyList<(float X, float Y)>> Flatten(SKPath outline)
    {
        const int curveSteps = 12;
        var contours = new List<IReadOnlyList<(float X, float Y)>>();
        List<(float X, float Y)>? current = null;
        var iterator = outline.CreateRawIterator();
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    current = [(points[0].X, points[0].Y)];
                    contours.Add(current);
                    break;
                case SKPathVerb.Line:
                    current?.Add((points[1].X, points[1].Y));
                    break;
                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    // Conics are rare in glyph outlines and are cut as quads: the warp needs a dense
                    // polyline, not an exact conic.
                    for (var step = 1; step <= curveSteps; step++)
                    {
                        var sample = QuadPoint(points[0], points[1], points[2], (float) step / curveSteps);
                        current?.Add((sample.X, sample.Y));
                    }

                    break;
                case SKPathVerb.Cubic:
                    for (var step = 1; step <= curveSteps; step++)
                    {
                        var sample = CubicPoint(points[0], points[1], points[2], points[3], (float) step / curveSteps);
                        current?.Add((sample.X, sample.Y));
                    }

                    break;
            }
        }

        return contours;
    }

    static SKPoint QuadPoint(SKPoint p0, SKPoint p1, SKPoint p2, float t)
    {
        var mt = 1 - t;
        var a = mt * mt;
        var b = 2 * mt * t;
        var c = t * t;
        return new(a * p0.X + b * p1.X + c * p2.X, a * p0.Y + b * p1.Y + c * p2.Y);
    }

    static SKPoint CubicPoint(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float t)
    {
        var mt = 1 - t;
        var a = mt * mt * mt;
        var b = 3 * mt * mt * t;
        var c = 3 * mt * t * t;
        var d = t * t * t;
        return new(a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
    }

    /// <summary>
    /// The WordArt box's normalized contours scaled into its box, or null for a plain rectangle or
    /// an ellipse. Even-odd keeps a prst="frame" ring hollow.
    /// </summary>
    static SKPath? BuildWordArtBoxPath(WordArtElement wordArt, float x, float y, float width, float height)
    {
        if (wordArt.BoxSubpaths == null)
        {
            return null;
        }

        var path = new SKPath {FillType = SKPathFillType.EvenOdd};
        foreach (var contour in wordArt.BoxSubpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var localX = x + (float) pointX * width;
                var localY = y + (float) pointY * height;
                if (index == 0)
                {
                    path.MoveTo(localX, localY);
                }
                else
                {
                    path.LineTo(localX, localY);
                }
            }

            path.Close();
        }

        if (path.IsEmpty)
        {
            return null;
        }

        return path;
    }
}
