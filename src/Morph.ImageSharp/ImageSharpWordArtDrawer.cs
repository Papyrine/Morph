/// <summary>
/// Draws WordArt shapes (warps, outline, shadow) on a <see cref="DrawingCanvas"/>. Extracted
/// verbatim from <c>ImageSharpPageRenderer</c> so the drawing has no page-renderer dependency:
/// the page renderer positions the shape in the flow and delegates here, and
/// <see cref="ImageSharpWordArtRasterizer"/> draws straight onto its own transparent image without
/// running a page render.
/// </summary>
sealed class ImageSharpWordArtDrawer(ImageSharpRenderContext context, DrawingCanvas canvas)
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

    /// <summary>Applies a 0..1 opacity to a colour (used for a:ln stroke alpha).</summary>
    internal static Color WithAlpha(Color color, double alpha)
    {
        var clamped = Math.Clamp(alpha, 0, 1);
        if (clamped >= 0.999)
        {
            return color;
        }

        var pixel = color.ToPixel<Rgba32>();
        pixel.A = (byte) Math.Round(clamped * 255);
        return Color.FromPixel(pixel);
    }

    public void DrawInline(WordArtElement wordArt, float x, float y, float width, float pixelHeight)
    {
        // An unwarped pseudo-WordArt is Word's inline text box: stroke its a:ln frame
        // under the text (business/06's LOGO box).
        // A prst="frame" box is a RING, so its contours fill even-odd (ImageSharp's default rule);
        // an ellipse draws as a true oval.
        IPath? boxShape = null;
        if (wordArt.BoxSubpaths is { } contours)
        {
            var builder = new PathBuilder();
            foreach (var contour in contours)
            {
                if (contour.Count < 3)
                {
                    continue;
                }

                builder.AddLines(contour
                    .Select(_ => new PointF(x + (float) _.X * width, y + (float) _.Y * pixelHeight))
                    .ToArray());
                builder.CloseFigure();
            }

            var built = builder.Build();
            boxShape = built.Bounds.Width > 0 ? built : null;
        }
        else if (wordArt.BoxIsEllipse)
        {
            boxShape = new EllipsePolygon(x + width / 2, y + pixelHeight / 2, width / 2, pixelHeight / 2);
        }

        var boxRect = new RectangleF(x, y, width, pixelHeight);

        if (wordArt.BoxFillColorHex is { } boxFill)
        {
            var boxBrush = context.GetBrush(ImageSharpRenderContext.ParseColor(boxFill));
            if (boxShape != null)
            {
                canvas.Fill(boxBrush, boxShape);
            }
            else
            {
                canvas.Fill(boxBrush, boxRect);
            }
        }

        if (wordArt is { BoxLineColorHex: { } boxLine, BoxLineWidthPoints: > 0 })
        {
            var boxPen = context.GetPen(
                WithAlpha(ImageSharpRenderContext.ParseColor(boxLine), wordArt.BoxLineAlpha),
                context.PointsToPixels((float) wordArt.BoxLineWidthPoints));
            if (boxShape != null)
            {
                canvas.Draw(boxPen, boxShape);
            }
            else
            {
                canvas.Draw(boxPen, boxRect);
            }
        }

        var font = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints,
            wordArt.Bold,
            wordArt.Italic);

        var textSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(font)
            {
                Dpi = context.Dpi
            });

        // Only shrink to fit; never enlarge text past the explicit font size. The bounding
        // box for a WordArt shape (especially arc/circle warps) is much larger than the
        // rendered glyphs because Word lays the text along a curve inside the box.
        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, x, y, width, pixelHeight, scaledFont))
        {
            return;
        }

        if (TryRenderWordArtPathWarp(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, x, y, width, pixelHeight, scaledFont))
        {
            return;
        }

        if (TryRenderWordArtEnvelope(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, x, y, width, pixelHeight, scaledFont))
        {
            return;
        }

        var scaledSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(scaledFont)
            {
                Dpi = context.Dpi
            });

        var textX = x + (width - scaledSize.Width) / 2;
        var textY = y + (pixelHeight - scaledSize.Height) / 2;

        DrawFlatText(wordArt, scaledFont, textX, textY);
    }

    public void DrawFloating(FloatingWordArtElement wordArt, float pixelX, float pixelY, float width, float pixelHeight)
    {
        var font = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints,
            wordArt.Bold,
            wordArt.Italic);

        var textSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(font)
            {
                Dpi = context.Dpi
            });

        // Only shrink to fit; never enlarge text past the explicit font size — see note in
        // DrawInline above.
        var scaleX = textSize.Width > 0 ? width / textSize.Width : 1;
        var scaleY = textSize.Height > 0 ? pixelHeight / textSize.Height : 1;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1f);

        var scaledFont = context.GetFontForFamily(
            wordArt.FontFamily,
            (float) wordArt.FontSizePoints * scale,
            wordArt.Bold,
            wordArt.Italic);

        if (TryRenderWordArtOnPath(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, wordArt.OutlineColorHex, wordArt.OutlineWidthPoints, pixelX, pixelY, width, pixelHeight, scaledFont))
        {
            return;
        }

        if (TryRenderWordArtPathWarp(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, pixelX, pixelY, width, pixelHeight, scaledFont))
        {
            return;
        }

        if (TryRenderWordArtEnvelope(wordArt.Transform, wordArt.Text, wordArt.FillColorHex, pixelX, pixelY, width, pixelHeight, scaledFont))
        {
            return;
        }

        var scaledSize = TextMeasurer.MeasureAdvance(
            wordArt.Text,
            new(scaledFont)
            {
                Dpi = context.Dpi
            });

        var textX = pixelX + (width - scaledSize.Width) / 2;
        var textY = pixelY + (pixelHeight - scaledSize.Height) / 2;

        DrawFlatText(wordArt, scaledFont, textX, textY);
    }

    /// <summary>
    /// The unwarped fallback shared by the inline and floating paths: shadow, outline and fill
    /// drawn as flat text centred in the box.
    /// </summary>
    void DrawFlatText(IWordArtVisual wordArt, Font scaledFont, float textX, float textY)
    {
        Color fillColor;
        if (wordArt.FillColorHex == null)
        {
            fillColor = Color.Black;
        }
        else
        {
            fillColor = ImageSharpRenderContext.ParseColor(wordArt.FillColorHex);
        }

        if (wordArt.HasShadow)
        {
            var shadowColor = Color.FromPixel(new Rgba32(0, 0, 0, 80));
            canvas.DrawText(wordArt.Text, scaledFont, shadowColor, new(textX + 3, textY + 3));
        }

        if (wordArt is {OutlineColorHex: not null, OutlineWidthPoints: > 0})
        {
            var outlineColor = ImageSharpRenderContext.ParseColor(wordArt.OutlineColorHex);
            var outlinePen = context.GetPen(outlineColor, context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            canvas.DrawText(wordArt.Text, scaledFont, outlinePen, new(textX, textY));
        }

        canvas.DrawText(wordArt.Text, scaledFont, fillColor, new(textX, textY));
    }

    /// <summary>
    /// Renders WordArt that follows a curved path (ArchUp / ArchDown / Circle) through
    /// <see cref="DrawingCanvas.DrawText(RichTextOptions, ReadOnlySpan{char}, IPath, Brush, Pen)"/>.
    /// Returns true when the warp was handled, false for warps that should fall back to
    /// flat-text rendering.
    /// </summary>
    /// <remarks>
    /// Word's <c>prstTxWarp</c> presets don't treat the WordArt bbox as the *full* arc
    /// bounding box — that produces a tight half-ellipse, much sharper than Word actually
    /// draws (and the typical bbox is 4:1 wide-and-flat, so the half-ellipse is also far
    /// off-centre vertically).
    /// <para>
    /// The correct geometry for <c>textArchUp</c>/<c>textArchDown</c> treats bbox W as the
    /// arc <em>chord</em> and bbox H as the <em>sagitta</em> (perpendicular distance from
    /// chord midpoint to arc midpoint). The circle radius is R = (W² + 4H²) / (8H), and the
    /// arc sweep is 2·asin(W/(2R)). Wide-and-flat bboxes give large R and small sweep —
    /// gentle, mostly-horizontal curves, matching Word.
    /// </para>
    /// <para>
    /// <c>textCircle</c> wraps text around the right side of an inscribed circle. Short
    /// text covers a small arc centred on 3 o'clock and reads downward — matches Word's
    /// behaviour where bbox width determines the circle diameter and text sits on the
    /// right hemisphere.
    /// </para>
    /// </remarks>
    bool TryRenderWordArtOnPath(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        string? outlineColorHex,
        double outlineWidthPoints,
        float x, float y, float width, float height,
        Font font)
    {
        // We size the path to exactly fit the text, centred on the desired anchor (peak/dip
        // for arches, 3 o'clock for circle). ImageSharp's HorizontalAlignment.Center doesn't
        // centre text along a path baseline the way Skia's SKTextAlign.Center does — it
        // places glyphs starting at the path origin regardless. Sizing the path to text
        // length sidesteps that and gives Word-shaped output.
        var textWidthPixels = TextMeasurer.MeasureAdvance(text, new(font) {Dpi = context.Dpi}).Width;
        if (textWidthPixels <= 0)
        {
            return false;
        }

        IPath path;
        switch (transform)
        {
            case WordArtTransform.ArchUp:
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: false);
                break;
            case WordArtTransform.ArchDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: true);
                break;
            case WordArtTransform.Circle:
                {
                    var radius = Math.Min(width, height) / 2f;
                    var cx = x + width / 2f;
                    var cy = y + height / 2f;
                    // Text-length arc centred on 3 o'clock (right side of inscribed circle),
                    // running CW from upper-right to lower-right. Word's textCircle wraps
                    // short text on this hemisphere reading downward.
                    var halfAngleDegrees = (float) (textWidthPixels / radius * 90.0 / Math.PI);
                    var startAngle = 360f - halfAngleDegrees;
                    var sweepAngle = 2 * halfAngleDegrees;
                    path = BuildArc(new(cx - radius, cy - radius, 2 * radius, 2 * radius), startAngle, sweepAngle);
                    break;
                }
            case WordArtTransform.ChevronUp:
                // Word's textChevron renders as a single-peak smooth arch, not a sharp ^ —
                // the chord-sagitta arc gives the right visual without per-glyph overlap at
                // a discontinuous apex.
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: false);
                break;
            case WordArtTransform.ChevronDown:
                path = BuildChordSagittaArc(x, y, width, height, textWidthPixels, archDown: true);
                break;
            case WordArtTransform.Wave:
                path = BuildWavePath(x, y, width, height, textWidthPixels);
                break;
            case WordArtTransform.SlantUp:
                path = BuildSlantPath(x, y, width, height, textWidthPixels, slantUp: true);
                break;
            case WordArtTransform.SlantDown:
                path = BuildSlantPath(x, y, width, height, textWidthPixels, slantUp: false);
                break;
            default:
                return false;
        }

        var fillColor = fillColorHex == null ? Color.Black : ImageSharpRenderContext.ParseColor(fillColorHex);
        var options = new RichTextOptions(font)
        {
            Dpi = context.Dpi
        };

        if (outlineColorHex != null && outlineWidthPoints > 0)
        {
            var outlineColor = ImageSharpRenderContext.ParseColor(outlineColorHex);
            var outlinePen = context.GetPen(outlineColor, context.PointsToPixels((float) outlineWidthPoints));
            canvas.DrawText(options, text.AsSpan(), path, null, outlinePen);
        }

        canvas.DrawText(options, text.AsSpan(), path, context.GetBrush(fillColor), null);
        return true;
    }

    /// <summary>
    /// Renders the box-filling envelope warps (Inflate / Deflate / CanUp / CanDown) by
    /// extracting glyph outline paths and remapping every point so each glyph's top and
    /// bottom edges follow the envelope curve. Word distorts each glyph as a true non-affine
    /// trapezoid — affine per-glyph scaling (the legacy fallback) only varies glyph height,
    /// keeping each glyph rectangular. Path-level remap captures the per-column height
    /// variation that makes the warp look right.
    ///
    /// Algorithm: render text glyphs at natural size to get outline paths in
    /// text-local coords (x ∈ [0, totalWidth], y ∈ ~[0, glyphHeight]). For every point,
    /// normalise (x, y) into (t, normY), look up the envelope's top/bottom Y at t, and
    /// remap y linearly between them. X is stretched to fill the bbox width.
    /// </summary>
    bool TryRenderWordArtPathWarp(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        Font font)
    {
        if (transform is not (WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.CanUp or WordArtTransform.CanDown))
        {
            return false;
        }

        var measureOptions = new RichTextOptions(font) {Dpi = context.Dpi};
        var totalWidth = TextMeasurer.MeasureAdvance(text, measureOptions).Width;
        if (totalWidth <= 0)
        {
            return false;
        }

        var (_, baselineFromTop) = ImageSharpRenderContext.GetFontMetrics(font);
        var naturalAscent = baselineFromTop * context.Scale;

        // Render glyph outlines at text-local coords: baseline at y = ascent. X spans
        // 0..totalWidth. Use the resulting paths' actual bounds for Y normalisation rather
        // than font metrics — the visible glyph extent (cap-top to descender-bottom) is what
        // the envelope curve should map onto, and that's tighter than the font's metric box.
        var pathOptions = new RichTextOptions(font)
        {
            Dpi = context.Dpi,
            Origin = new(0, naturalAscent)
        };
        var glyphPaths = TextBuilder.GeneratePaths(text, pathOptions);
        var pathsBounds = glyphPaths.Bounds;
        var glyphsTop = pathsBounds.Top;
        var glyphsHeight = pathsBounds.Height;
        if (glyphsHeight <= 0)
        {
            return false;
        }

        var pathBuilder = new PathBuilder();
        foreach (var glyphPath in glyphPaths)
        {
            foreach (var simplePath in glyphPath.Flatten())
            {
                var points = simplePath.Points.Span;
                if (points.Length == 0)
                {
                    continue;
                }

                PointF Warp(PointF sample)
                {
                    var (warpedX, warpedY) = WordArtEnvelope.WarpPoint(
                        (sample.X, sample.Y), totalWidth, glyphsTop, glyphsHeight, x, y, width, height, transform);
                    return new(warpedX, warpedY);
                }

                pathBuilder.StartFigure();
                pathBuilder.MoveTo(Warp(points[0]));
                for (var i = 1; i < points.Length; i++)
                {
                    pathBuilder.LineTo(Warp(points[i]));
                }
                if (simplePath.IsClosed)
                {
                    pathBuilder.CloseFigure();
                }
            }
        }

        var fillColor = fillColorHex == null ? Color.Black : ImageSharpRenderContext.ParseColor(fillColorHex);
        canvas.Fill(context.GetBrush(fillColor), pathBuilder.Build());
        return true;
    }

    /// <summary>
    /// Renders WordArt envelope warps that aren't text-on-path — Fade, Triangle, Inflate,
    /// Deflate, CanUp, CanDown — by drawing each glyph individually with a per-glyph
    /// vertical scale. The anchor depends on the warp: baseline for Fade/Triangle/CanUp
    /// (bottom stays put), glyph centre for Inflate/Deflate (both edges move symmetrically),
    /// glyph top for CanDown (top stays put). Returns true when handled. Per-glyph
    /// rendering is slower than a single DrawText so the path-based warps are tried first.
    /// </summary>
    bool TryRenderWordArtEnvelope(
        WordArtTransform transform,
        string text,
        string? fillColorHex,
        float x, float y, float width, float height,
        Font font)
    {
        var scaleY = WordArtEnvelope.ScaleY(transform);
        if (scaleY == null)
        {
            return false;
        }

        var anchor = WordArtEnvelope.AnchorFor(transform);

        var measureOptions = new RichTextOptions(font) {Dpi = context.Dpi};
        var totalWidth = TextMeasurer.MeasureAdvance(text, measureOptions).Width;
        if (totalWidth <= 0)
        {
            return false;
        }

        var (glyphHeight, baselineFromTop) = ImageSharpRenderContext.GetFontMetrics(font);
        var glyphHeightPixels = glyphHeight * context.Scale;
        var baselineOffsetPixels = baselineFromTop * context.Scale;

        var fillColor = fillColorHex == null ? Color.Black : ImageSharpRenderContext.ParseColor(fillColorHex);
        var brush = context.GetBrush(fillColor);

        // Inflate / Deflate / Can warps fill the bbox horizontally AND vertically — Word
        // stretches glyphs to span the box, then modulates each glyph's height by the warp
        // curve. Fade / Triangle leave the natural size (matches Word for those).
        // For the box-filling warps, scale so the PEAK (most-stretched) glyph fits the bbox
        // height: Inflate/Can peak at 1.4× the base, Deflate's largest is 1.0× (it shrinks
        // toward the centre rather than growing).
        var fillsBox = transform is WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.CanUp or WordArtTransform.CanDown;
        var peakScale = transform switch
        {
            WordArtTransform.Inflate or WordArtTransform.CanUp or WordArtTransform.CanDown => 1.5f,
            // Deflate's biggest glyph is at the edges (sy=1.0) — it shrinks toward middle.
            _ => 1.0f
        };
        var sx = fillsBox ? width / totalWidth : 1f;
        var baseScaleY = fillsBox ? height / (peakScale * glyphHeightPixels) : 1f;
        var stretchedWidth = totalWidth * sx;

        // Position the natural-size glyph so its anchor point lands at the desired bbox
        // anchor. The Matrix3x2 then scales around that anchor without translating it, so
        // the chosen edge (top / centre / baseline) stays fixed and the opposite edge moves.
        // For non-box-filling warps (Fade / Triangle) keep the legacy layout — text centred
        // vertically in the bbox with baseline anchor — so existing baselines stay stable.
        var startX = x + (width - stretchedWidth) / 2f;
        var legacyTopY = y + (height - glyphHeightPixels) / 2f;
        var legacyBaselineY = legacyTopY + baselineOffsetPixels;
        float anchorY;
        float originY;
        if (!fillsBox)
        {
            originY = legacyTopY;
            anchorY = legacyBaselineY;
        }
        else
        {
            switch (anchor)
            {
                case EnvelopeAnchor.Top:
                    anchorY = y;
                    originY = anchorY;
                    break;
                case EnvelopeAnchor.Centre:
                    anchorY = y + height / 2f;
                    originY = anchorY - glyphHeightPixels / 2f;
                    break;
                default:
                    anchorY = y + height;
                    originY = anchorY - baselineOffsetPixels;
                    break;
            }
        }

        var charCount = text.Length;
        var cursorX = startX;
        for (var i = 0; i < charCount; i++)
        {
            var ch = text[i].ToString();
            var charAdvance = TextMeasurer.MeasureAdvance(ch, measureOptions).Width;
            // For 1-character labels in a box-filling warp, t=0 collapses sin(πt)=0 (no
            // warp). Use 0.5 so a single glyph still gets the centre amplitude. For Fade /
            // Triangle a single glyph at the start (t=0) is intentional.
            var t = charCount > 1 ? (float) i / (charCount - 1) : fillsBox ? 0.5f : 0f;
            var sy = scaleY(t) * baseScaleY;

            // Scale anchored at (cursorX, anchorY): the X scale stretches each glyph
            // horizontally from its left edge so cursorX increments stay in stretched space;
            // the Y scale anchors at the warp anchor line so the chosen edge stays put.
            var matrix = Matrix3x2.CreateScale(new Vector2(sx, sy), new(cursorX, anchorY));
            canvas.Save(
                new()
                {
                    Transform = new(matrix)
                });

            var charOpts = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new(cursorX, originY)
            };
            canvas.DrawText(charOpts, ch.AsSpan(), brush, null);
            canvas.Restore();

            cursorX += charAdvance * sx;
        }

        return true;
    }


    /// <summary>
    /// Builds a text-length-fitting arc on the chord-sagitta circle (chord = bbox width,
    /// sagitta = bbox height). Path is centred on the bbox horizontal midline at the arc
    /// peak (archUp) or dip (archDown), with sweep limited to <paramref name="textWidth"/>
    /// arc length so glyphs sit at the page-centre of the WordArt without relying on
    /// path-text alignment.
    /// </summary>
    static IPath BuildChordSagittaArc(float x, float y, float width, float height, float textWidth, bool archDown)
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

        return BuildArc(new(centerX - radius, bboxTop, 2 * radius, 2 * radius), startAngle, sweepAngle);
    }

    static IPath BuildArc(RectangleF oval, float startAngle, float sweepAngle)
    {
        var builder = new PathBuilder();
        builder.AddArc(oval, rotation: 0, startAngle, sweepAngle);
        return builder.Build();
    }

    /// <summary>
    /// Straight diagonal path through the bbox centre with slope derived from bbox aspect
    /// (dy/dx = ±H/W). Path length matches <paramref name="textWidth"/> so glyphs sit on a
    /// slanted baseline. Text-on-path naturally rotates each glyph to the line angle —
    /// a small visual difference from Word (which keeps glyphs upright) but captures the
    /// slant effect with a single uniform path.
    /// </summary>
    static IPath BuildSlantPath(float x, float y, float width, float height, float textWidth, bool slantUp)
    {
        var slope = (slantUp ? -1f : 1f) * height / width;
        var halfTextLength = textWidth / 2f;
        var dx = halfTextLength / (float) Math.Sqrt(1 + slope * slope);
        var dy = dx * slope;
        var centerX = x + width / 2f;
        var centerY = y + height / 2f;

        var builder = new PathBuilder();
        builder.MoveTo(new(centerX - dx, centerY - dy));
        builder.LineTo(new(centerX + dx, centerY + dy));
        return builder.Build();
    }

    /// <summary>
    /// Builds a sine-wave polyline path centred on the bbox. Word's <c>textWave1</c> fits one
    /// full period across the text length: text starts high, dips through the middle, and
    /// ends high again — formula <c>y = midY - amplitude·cos(2π·t/textWidth)</c>. Amplitude
    /// is bbox H/4 (not H/2) — half the box reserves space for the glyph height itself so
    /// text stays within the bbox visually, matching Word's textWave1 where the wave
    /// excursion is gentle relative to glyph size. 64 polyline segments per period — dense
    /// enough that per-glyph tangent jumps at segment joins are visually smooth.
    /// </summary>
    static IPath BuildWavePath(float x, float y, float width, float height, float textWidth)
    {
        var amplitude = height / 4f;
        var midY = y + height / 2f;
        // Path covers exactly textWidth horizontal extent, centred on bbox horizontal centre.
        var pathStartX = x + width / 2f - textWidth / 2f;

        const int segmentsPerPeriod = 64;
        var dx = textWidth / segmentsPerPeriod;
        var phaseScale = 2.0 * Math.PI / textWidth;

        var builder = new PathBuilder();
        builder.MoveTo(new(pathStartX, midY - amplitude));
        for (var i = 1; i <= segmentsPerPeriod; i++)
        {
            var t = i * dx;
            var py = midY - amplitude * (float) Math.Cos(t * phaseScale);
            builder.LineTo(new(pathStartX + t, py));
        }
        return builder.Build();
    }
}
