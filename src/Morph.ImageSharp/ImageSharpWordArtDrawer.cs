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

        if (TryRenderArch(wordArt, x, y, width, pixelHeight))
        {
            return;
        }

        if (TryRenderEnvelope(wordArt, x, y, width, pixelHeight, font))
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

        if (TryRenderArch(wordArt, pixelX, pixelY, width, pixelHeight))
        {
            return;
        }

        if (TryRenderEnvelope(wordArt, pixelX, pixelY, width, pixelHeight, font))
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
    /// Renders <c>textArchUp</c> / <c>textArchDown</c> / <c>textCircle</c> as Word does: the text drawn smaller by
    /// <see cref="WordArtArch.Scale"/>, each glyph turned to the ellipse of the scaled text rect
    /// at its own place along it — see <see cref="WordArtArch"/> for the probed rules. Returns false
    /// for any other warp, or a box with no room for a text rect.
    /// </summary>
    bool TryRenderArch(IWordArtVisual wordArt, float x, float y, float width, float height)
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

        var declaredFont = context.GetFontForFamily(wordArt.FontFamily, (float) wordArt.FontSizePoints, wordArt.Bold, wordArt.Italic);
        var inkHeight = TextMeasurer.MeasureBounds(wordArt.Text, new(declaredFont) {Dpi = context.Dpi}).Height;
        var scale = WordArtArch.Scale(WordArtArch.ScaleSide(wordArt.Transform, textWidth, textHeight), inkHeight);

        var drawnSizePoints = (float) wordArt.FontSizePoints * scale;
        var font = context.GetFontForFamily(wordArt.FontFamily, drawnSizePoints, wordArt.Bold, wordArt.Italic);
        var measureOptions = new RichTextOptions(font) {Dpi = context.Dpi};
        var glyphs = WordArtArch.Glyphs(wordArt.Text);
        var metrics = context.LayoutMetrics(wordArt.FontFamily, wordArt.Bold, wordArt.Italic);

        // Word's bold simulation for a face with no bold of its own, as SkiaWordArtDrawer draws it.
        var emboldened = metrics == null ? SyntheticBold.None : BoldSimulation.For(wordArt.Bold, metrics);
        var widening = emboldened == SyntheticBold.None ? 0 : context.PointsToPixels((float) BoldSimulation.AdvancePoints);
        var advances = glyphs.Select(_ => TextMeasurer.MeasureAdvance(_, measureOptions).Width + widening).ToList();
        var baselineDepth = context.PointsToPixels(WordArtArch.BaselineDepthEm(metrics) * drawnSizePoints);
        var placements = WordArtArch.Place(
            advances, textLeft, textTop, textWidth, textHeight, scale, baselineDepth,
            wordArt.TextAlignment, wordArt.Transform);

        var brush = context.GetBrush(wordArt.FillColorHex == null ? Color.Black : ImageSharpRenderContext.ParseColor(wordArt.FillColorHex));
        var outlinePen = wordArt is {OutlineColorHex: { } outlineColor, OutlineWidthPoints: > 0}
            ? context.GetPen(ImageSharpRenderContext.ParseColor(outlineColor), context.PointsToPixels((float) wordArt.OutlineWidthPoints))
            : null;
        var boldPen = emboldened == SyntheticBold.None
            ? null
            : context.GetPen(
                wordArt.FillColorHex == null ? Color.Black : ImageSharpRenderContext.ParseColor(wordArt.FillColorHex),
                context.PointsToPixels((float) BoldSimulation.StrokeWidthPoints(emboldened, drawnSizePoints, 72.0 / context.Dpi)));

        for (var i = 0; i < glyphs.Count; i++)
        {
            var (originX, originY, angle) = placements[i];
            canvas.Save(
                new()
                {
                    Transform = new(Matrix3x2.CreateRotation(angle * MathF.PI / 180, new(originX, originY)))
                });

            // Anchored on the alphabetic baseline, as ImageSharpPainter draws a line.
            var glyphOptions = new RichTextOptions(font)
            {
                Dpi = context.Dpi,
                Origin = new(originX, originY),
                TextBaseline = TextBaseline.Alphabetic
            };
            if (outlinePen != null)
            {
                canvas.DrawText(glyphOptions, glyphs[i].AsSpan(), null, outlinePen);
            }

            canvas.DrawText(glyphOptions, glyphs[i].AsSpan(), brush, boldPen);
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
    bool TryRenderEnvelope(IWordArtVisual wordArt, float x, float y, float width, float height, Font font)
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

        var contours = new List<IReadOnlyList<(float X, float Y)>>();
        foreach (var glyphPath in TextBuilder.GeneratePaths(wordArt.Text, new RichTextOptions(font) {Dpi = context.Dpi}))
        {
            foreach (var simplePath in glyphPath.Flatten())
            {
                var points = simplePath.Points.Span;
                if (points.Length == 0)
                {
                    continue;
                }

                var contour = new List<(float X, float Y)>(points.Length);
                foreach (var point in points)
                {
                    contour.Add((point.X, point.Y));
                }

                contours.Add(contour);
            }
        }

        var warped = WordArtEnvelope.Warp(contours, wordArt.Transform, wordArt.WarpAdjust, rectLeft, rectTop, rectWidth, rectHeight);
        if (warped.Count == 0)
        {
            return false;
        }

        var pathBuilder = new PathBuilder();
        foreach (var contour in warped)
        {
            pathBuilder.StartFigure();
            pathBuilder.AddLines(contour.Select(_ => new PointF(_.X, _.Y)).ToArray());
            pathBuilder.CloseFigure();
        }

        var path = pathBuilder.Build();
        var fillColor = wordArt.FillColorHex == null ? Color.Black : ImageSharpRenderContext.ParseColor(wordArt.FillColorHex);
        canvas.Fill(context.GetBrush(fillColor), path);

        if (wordArt is {OutlineColorHex: { } outlineHex, OutlineWidthPoints: > 0})
        {
            var outlinePen = context.GetPen(ImageSharpRenderContext.ParseColor(outlineHex), context.PointsToPixels((float) wordArt.OutlineWidthPoints));
            canvas.Draw(outlinePen, path);
        }

        return true;
    }
}
