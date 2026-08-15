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
        if (TryRenderWordArtOnPath(transform, text, fillColor, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, x, y, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        if (TryRenderWordArtPathWarp(transform, text, fillColor, x, y, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        if (TryRenderWordArtEnvelope(transform, text, fillColor, x, y, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = x + (width - scaledWidth) / 2;
        var textY = y + (pixelHeight + scaledHeight) / 2;

        DrawFlatText(wordArt, transform, x, y, width, pixelHeight, typeface, pixelFontSize * scale, textX, textY, scaledHeight);
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
        if (TryRenderWordArtOnPath(transform, text, fillColor, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        if (TryRenderWordArtPathWarp(transform, text, fillColor, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        if (TryRenderWordArtEnvelope(transform, text, fillColor, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale))
        {
            return;
        }

        // Calculate centered position
        var scaledWidth = textBounds.Width * scale;
        var scaledHeight = textBounds.Height * scale;
        var textX = pixelX + (width - scaledWidth) / 2;
        var textY = pixelY + (pixelHeight + scaledHeight) / 2;

        DrawFlatText(wordArt, transform, pixelX, pixelY, width, pixelHeight, typeface, pixelFontSize * scale, textX, textY, scaledHeight);
    }

    /// <summary>
    /// The unwarped fallback shared by the inline and floating paths: shadow, glow, outline, fill
    /// and reflection drawn as flat text under <see cref="ApplyWordArtTransform"/>'s affine
    /// approximation of the declared warp.
    /// </summary>
    void DrawFlatText(IWordArtVisual wordArt, WordArtTransform transform, float x, float y, float width, float pixelHeight, SKTypeface typeface, float fontSize, float textX, float textY, float scaledHeight)
    {
        // SkiaSharp 4 moved text size/typeface off SKPaint onto SKFont; one font, shared by
        // every draw below (all at the same scaled size). Matches the Antialias edging used
        // by the text-on-path WordArt helpers.
        using var font = new SKFont(typeface, fontSize)
        {
            Edging = SKFontEdging.Antialias
        };

        canvas.Save();

        // Apply transform based on WordArt type
        ApplyWordArtTransform(transform, x, y, width, pixelHeight);

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

        canvas.Restore();
    }

    /// <summary>
    /// Renders WordArt that follows a curved path (ArchUp / ArchDown / Circle) via
    /// <see cref="SKCanvas.DrawTextOnPath(string, SKPath, SKPoint, SKTextAlign, SKFont, SKPaint)"/>.
    /// Returns true when the warp was handled, false for warps that should fall back to
    /// flat-text rendering.
    /// </summary>
    /// <remarks>
    /// Word's <c>prstTxWarp</c> presets don't treat the WordArt bbox as the *full* arc
    /// bounding box — that would produce a tight half-ellipse, much sharper than Word
    /// actually draws. The chord-sagitta interpretation gives the right shape: bbox W is
    /// the arc chord, bbox H is the sagitta, and the circle radius is
    /// R = (W² + 4H²) / (8H). For typical 4:1 wide-and-flat WordArt bboxes this gives a
    /// large radius and a gentle, mostly-horizontal curve — matching Word.
    /// <para>
    /// The path is sized to the rendered text width and centred on the arc peak/dip so
    /// short text sits at the bbox-centre rather than being stretched along the full
    /// chord. <c>textCircle</c> uses a text-width arc on the right side of an inscribed
    /// circle (3 o'clock anchor) — short text wraps the right hemisphere reading downward.
    /// </para>
    /// </remarks>
    bool TryRenderWordArtOnPath(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        string? outlineColorHex,
        double outlineWidthPoints,
        float x, float y, float width, float height,
        SKTypeface typeface, float fontSize)
    {
        using var measureFont = new SKFont(typeface, fontSize);
        var textWidth = measureFont.MeasureText(text);
        if (textWidth <= 0)
        {
            return false;
        }

        SKPath? path;
        switch (transform)
        {
            case WordArtTransform.ArchUp:
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: false);
                break;
            case WordArtTransform.ArchDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: true);
                break;
            case WordArtTransform.Circle:
                {
                    var radius = Math.Min(width, height) / 2f;
                    var cx = x + width / 2f;
                    var cy = y + height / 2f;
                    var halfAngleDegrees = (float) (textWidth / radius * 90.0 / Math.PI);
                    var startAngle = 360f - halfAngleDegrees;
                    var sweepAngle = 2 * halfAngleDegrees;
                    path = BuildArc(new(cx - radius, cy - radius, cx + radius, cy + radius), startAngle, sweepAngle);
                    break;
                }
            case WordArtTransform.ChevronUp:
                // Word's textChevron renders as a single-peak smooth arch — same envelope as
                // ArchUp. Sharp-corner ^ paths cause per-glyph overlap at the apex.
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: false);
                break;
            case WordArtTransform.ChevronDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidth, archDown: true);
                break;
            case WordArtTransform.Wave:
                path = BuildWavePath(x, y, width, height, textWidth);
                break;
            case WordArtTransform.SlantUp:
                path = BuildSlantPath(x, y, width, height, textWidth, slantUp: true);
                break;
            case WordArtTransform.SlantDown:
                path = BuildSlantPath(x, y, width, height, textWidth, slantUp: false);
                break;
            default:
                return false;
        }

        using (path)
        using (var font = new SKFont(typeface, fontSize)
               {
                   Edging = SKFontEdging.Antialias
               })
        using (var fillPaint = new SKPaint
               {
                   IsAntialias = true,
                   Color = fillColorHex != null ? SkiaRenderContext.ParseColor(fillColorHex) : SKColors.Black
               })
        {
            if (outlineColorHex != null &&
                outlineWidthPoints > 0)
            {
                using var outlinePaint = new SKPaint
                {
                    IsAntialias = true,
                    Color = SkiaRenderContext.ParseColor(outlineColorHex),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = context.PointsToPixels((float) outlineWidthPoints)
                };
                canvas.DrawTextOnPath(text, path, new(0, 0), SKTextAlign.Left, font, outlinePaint);
            }

            canvas.DrawTextOnPath(text, path, new(0, 0), SKTextAlign.Left, font, fillPaint);
        }

        return true;
    }

    /// <summary>
    /// Builds a text-length-fitting arc on the chord-sagitta circle (chord = bbox width,
    /// sagitta = bbox height). Path is centred on the arc peak (archUp) or dip (archDown),
    /// with sweep limited to <paramref name="textWidth"/> arc length so short text sits at
    /// the bbox-centre rather than being stretched along the full chord.
    /// </summary>
    static SKPath BuildChordSagittaArc(float x, float y, float width, float height, float textWidth, bool archDown)
    {
        // Sagitta-to-radius identity: R = (chord² + 4·sagitta²) / (8·sagitta).
        var radius = (width * width + 4 * height * height) / (8 * height);
        var textHalfAngleDegrees = (float) (textWidth / (2 * radius) * 180.0 / Math.PI);
        var centerX = x + width / 2f;

        float bboxTop;
        float startAngle;
        float sweepAngle;
        if (archDown)
        {
            // Arc dips through y+H; circle center above chord at y - (R-H).
            // Path centred on 90° (bottom of circle = arc dip), runs CCW for symmetric span.
            bboxTop = y + height - 2 * radius;
            startAngle = 90f + textHalfAngleDegrees;
            sweepAngle = -(2 * textHalfAngleDegrees);
        }
        else
        {
            // Arc peaks at y; circle center below chord at y + R.
            // Path centred on 270° (top of circle = arc peak), runs CW for symmetric span.
            bboxTop = y;
            startAngle = 270f - textHalfAngleDegrees;
            sweepAngle = 2 * textHalfAngleDegrees;
        }

        var ovalLeft = centerX - radius;
        return BuildArc(new(ovalLeft, bboxTop, ovalLeft + 2 * radius, bboxTop + 2 * radius), startAngle, sweepAngle);
    }

    static SKPath BuildArc(SKRect oval, float startAngle, float sweepAngle)
    {
        var path = new SKPath();
        path.AddArc(oval, startAngle, sweepAngle);
        return path;
    }

    /// <summary>
    /// Renders the box-filling envelope warps (Inflate / Deflate / CanUp / CanDown) by
    /// extracting the text outline as an SKPath, walking each subpath as a polyline (via
    /// SKPathMeasure), and remapping every sample point so each glyph's top and bottom
    /// edges follow the envelope curve. Word distorts each glyph as a true non-affine
    /// trapezoid; affine per-glyph scaling (the legacy fallback) only varies glyph height,
    /// keeping each glyph rectangular. Path-level remap captures the per-column height
    /// variation that makes the warp look right.
    /// </summary>
    bool TryRenderWordArtPathWarp(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        SKTypeface typeface, float fontSize)
    {
        if (transform is not (WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.CanUp or WordArtTransform.CanDown))
        {
            return false;
        }

        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = fillColorHex != null ? SkiaRenderContext.ParseColor(fillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };

        var totalWidth = font.MeasureText(text);
        if (totalWidth <= 0)
        {
            return false;
        }

        var fontMetrics = font.Metrics;
        // Generate text outline at text-local coords: origin (0, -ascent) puts baseline at
        // y=0 with caps reaching up into negative Y. We use bounds-based normalisation, so
        // the exact origin doesn't matter — only consistency between path and bounds.
        using var textPath = font.GetTextPath(text, new SKPoint(0, -fontMetrics.Ascent));
        textPath.GetBounds(out var pathBounds);
        var glyphsTop = pathBounds.Top;
        var glyphsHeight = pathBounds.Height;
        if (glyphsHeight <= 0)
        {
            return false;
        }

        // Walk the outline verb by verb, flattening each quad/cubic into short line segments,
        // and warp every resulting point. (SKPathMeasure's contour sampling returns nothing for
        // these glyph outlines under SkiaSharp 4.x, which left the warp blank — iterating the raw
        // path is reliable and mirrors the ImageSharp backend's flatten-then-warp approach.)
        using var resultPath = new SKPath();
        const int curveSteps = 12;
        var iterator = textPath.CreateRawIterator();
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            SKPoint Warp(SKPoint sample)
            {
                var (warpedX, warpedY) = WordArtEnvelope.WarpPoint(
                    (sample.X, sample.Y), totalWidth, glyphsTop, glyphsHeight, x, y, width, height, transform);
                return new(warpedX, warpedY);
            }

            switch (verb)
            {
                case SKPathVerb.Move:
                    resultPath.MoveTo(Warp(points[0]));
                    break;
                case SKPathVerb.Line:
                    resultPath.LineTo(Warp(points[1]));
                    break;
                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    // Conics (rare in glyph outlines) are approximated as quads; the warp only
                    // needs a dense polyline, not an exact conic.
                    for (var step = 1; step <= curveSteps; step++)
                    {
                        resultPath.LineTo(Warp(QuadPoint(points[0], points[1], points[2], (float) step / curveSteps)));
                    }

                    break;
                case SKPathVerb.Cubic:
                    for (var step = 1; step <= curveSteps; step++)
                    {
                        resultPath.LineTo(Warp(CubicPoint(points[0], points[1], points[2], points[3], (float) step / curveSteps)));
                    }

                    break;
                case SKPathVerb.Close:
                    resultPath.Close();
                    break;
            }
        }

        canvas.DrawPath(resultPath, paint);
        return true;
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
    /// Per-glyph rendering for envelope warps that aren't text-on-path (Fade, Triangle).
    /// Each glyph is drawn separately with a vertical scale anchored at the baseline so
    /// the bottoms align and the tops shrink toward the baseline. Returns true when
    /// handled.
    /// </summary>
    bool TryRenderWordArtEnvelope(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        SKTypeface typeface, float fontSize)
    {
        var scaleY = WordArtEnvelope.ScaleY(transform);
        if (scaleY == null)
        {
            return false;
        }

        var anchor = WordArtEnvelope.AnchorFor(transform);

        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = fillColorHex != null ? SkiaRenderContext.ParseColor(fillColorHex) : SKColors.Black,
            Style = SKPaintStyle.Fill
        };

        var totalWidth = font.MeasureText(text);
        if (totalWidth <= 0)
        {
            return false;
        }

        var fontMetrics = font.Metrics;
        var glyphHeight = fontMetrics.Descent - fontMetrics.Ascent;

        // Inflate / Deflate / Can warps fill the bbox horizontally AND vertically — Word
        // stretches glyphs to span the box, then modulates each glyph's height by the warp
        // curve. Fade / Triangle leave the natural size (matches Word for those).
        // For the box-filling warps, scale so the PEAK (most-stretched) glyph fits the bbox
        // height. Inflate/Can peak at 1.5×, Deflate's largest is 1.0× (it shrinks).
        var fillsBox = transform is WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.CanUp or WordArtTransform.CanDown;
        var peakScale = transform switch
        {
            WordArtTransform.Inflate or WordArtTransform.CanUp or WordArtTransform.CanDown => 1.5f,
            _ => 1.0f
        };
        var sx = fillsBox ? width / totalWidth : 1f;
        var baseScaleY = fillsBox ? height / (peakScale * glyphHeight) : 1f;
        var stretchedWidth = totalWidth * sx;

        // Position each glyph so its anchor edge (baseline / centre / top) sits at the
        // chosen bbox edge. The Save/Scale matrix then scales around that anchor without
        // translating it, so the chosen edge stays fixed and the opposite edge moves.
        // For non-box-filling warps (Fade / Triangle) keep the legacy layout — text centred
        // vertically in the bbox with baseline anchor — so existing baselines stay stable.
        var startX = x + (width - stretchedWidth) / 2f;
        var legacyTopY = y + (height - glyphHeight) / 2f;
        var legacyBaselineY = legacyTopY - fontMetrics.Ascent;
        float anchorY;
        float baselineY;
        if (!fillsBox)
        {
            anchorY = legacyBaselineY;
            baselineY = legacyBaselineY;
        }
        else
        {
            switch (anchor)
            {
                case EnvelopeAnchor.Top:
                    anchorY = y;
                    baselineY = anchorY - fontMetrics.Ascent;
                    break;
                case EnvelopeAnchor.Centre:
                    anchorY = y + height / 2f;
                    baselineY = anchorY - (fontMetrics.Ascent + fontMetrics.Descent) / 2f;
                    break;
                default:
                    anchorY = y + height;
                    baselineY = anchorY;
                    break;
            }
        }

        var charCount = text.Length;
        var cursorX = startX;
        for (var i = 0; i < charCount; i++)
        {
            var ch = text[i].ToString();
            var charAdvance = font.MeasureText(ch);
            // For 1-character labels in a box-filling warp, t=0 collapses sin(πt)=0 (no
            // warp). Use 0.5 so a single glyph still gets the centre amplitude. For Fade /
            // Triangle a single glyph at the start (t=0) is intentional.
            var t = charCount > 1 ? (float) i / (charCount - 1) : fillsBox ? 0.5f : 0f;
            var sy = scaleY(t) * baseScaleY;

            canvas.Save();
            canvas.Scale(sx, sy, cursorX, anchorY);
            canvas.DrawText(ch, cursorX, baselineY, font, paint);
            canvas.Restore();

            cursorX += charAdvance * sx;
        }
        return true;
    }


    /// <summary>
    /// Straight diagonal path through the bbox centre with slope ±H/W. Path length matches
    /// <paramref name="textWidth"/> so glyphs sit on a slanted baseline.
    /// </summary>
    static SKPath BuildSlantPath(float x, float y, float width, float height, float textWidth, bool slantUp)
    {
        var slope = (slantUp ? -1f : 1f) * height / width;
        var halfTextLength = textWidth / 2f;
        var dx = halfTextLength / (float) Math.Sqrt(1 + slope * slope);
        var dy = dx * slope;
        var centerX = x + width / 2f;
        var centerY = y + height / 2f;

        var path = new SKPath();
        path.MoveTo(centerX - dx, centerY - dy);
        path.LineTo(centerX + dx, centerY + dy);
        return path;
    }

    /// <summary>
    /// Sine-wave polyline path (one full period across <paramref name="textWidth"/>) along the
    /// bbox horizontal midline. Amplitude is bbox H/4 so the wave excursion stays gentle
    /// relative to glyph height — matches Word's textWave1.
    /// </summary>
    static SKPath BuildWavePath(float x, float y, float width, float height, float textWidth)
    {
        var amplitude = height / 4f;
        var midY = y + height / 2f;
        var pathStartX = x + width / 2f - textWidth / 2f;

        const int segments = 64;
        var dx = textWidth / segments;
        var phaseScale = 2.0 * Math.PI / textWidth;

        var path = new SKPath();
        path.MoveTo(pathStartX, midY - amplitude);
        for (var i = 1; i <= segments; i++)
        {
            var t = i * dx;
            var py = midY - amplitude * (float) Math.Cos(t * phaseScale);
            path.LineTo(pathStartX + t, py);
        }
        return path;
    }

    void ApplyWordArtTransform(WordArtTransform transform, float x, float y, float width, float height)
    {
        var centerX = x + width / 2;
        var centerY = y + height / 2;

        switch (transform)
        {
            case WordArtTransform.ArchUp:
                // Simulate arch up with a slight rotation around center
                canvas.Translate(centerX, centerY);
                canvas.Scale(1, 0.8f);
                canvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ArchDown:
                // Simulate arch down
                canvas.Translate(centerX, centerY);
                canvas.Scale(1, 0.8f);
                canvas.RotateDegrees(180);
                // Flip back to readable
                canvas.Scale(1, -1);
                canvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.Wave:
                // Simulate wave with slight skew
                canvas.Translate(centerX, centerY);
                canvas.Skew(0.1f, 0);
                canvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ChevronUp:
                // Simulate chevron up
                canvas.Translate(centerX, centerY);
                canvas.Scale(1, 0.7f);
                canvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.ChevronDown:
                // Simulate chevron down
                canvas.Translate(centerX, y + height);
                canvas.Scale(1, 0.7f);
                canvas.Translate(-centerX, -(y + height));
                break;

            case WordArtTransform.SlantUp:
                // Slant up with rotation
                canvas.RotateDegrees(-10, centerX, centerY);
                break;

            case WordArtTransform.SlantDown:
                // Slant down with rotation
                canvas.RotateDegrees(10, centerX, centerY);
                break;

            case WordArtTransform.Triangle:
                // Triangle shape - scale width at bottom
                canvas.Translate(centerX, centerY);
                canvas.Scale(0.8f, 1);
                canvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.FadeRight:
                // Fade right - slight perspective
                canvas.Translate(x, centerY);
                canvas.Skew(0, 0.05f);
                canvas.Translate(-x, -centerY);
                break;

            case WordArtTransform.FadeLeft:
                // Fade left - slight perspective
                canvas.Translate(x + width, centerY);
                canvas.Skew(0, -0.05f);
                canvas.Translate(-(x + width), -centerY);
                break;

            case WordArtTransform.Circle:
                // Circle - approximate with scaling
                canvas.Translate(centerX, centerY);
                canvas.Scale(0.9f, 0.9f);
                canvas.Translate(-centerX, -centerY);
                break;

            case WordArtTransform.None:
            default:
                // No transform
                break;
        }
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

        return path.IsEmpty ? null : path;
    }
}
