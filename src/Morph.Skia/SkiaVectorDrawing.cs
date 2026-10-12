/// <summary>
/// Draws an <see cref="SvgDrawing"/> — a picture's SVG as the core reader resolved it — with Skia
/// paths, straight onto the page canvas at the page's own resolution. This is how a picture placed
/// by the layout engine (<see cref="PlacedImage.Vector"/>) is drawn from its vector original
/// rather than from the PNG Office saved beside it. The canvas carries whatever rotation, flip and
/// outline clip the painter has already set for the picture, exactly as it would for the bitmap.
/// </summary>
static class SkiaVectorDrawing
{
    public static void Draw(SKCanvas canvas, SvgDrawing drawing, SKRect box, ImageCrop? crop, ImageRecolor? recolor, double opacity)
    {
        var placement = drawing.Place(box.Left, box.Top, box.Width, box.Height, crop);
        if (!placement.IsVisible || opacity <= 0)
        {
            return;
        }

        var restoreTo = canvas.Save();

        // Nothing of the drawing shows outside its own frame, or outside the picture's box.
        canvas.ClipRect(new((float) placement.Left, (float) placement.Top, (float) placement.Right, (float) placement.Bottom), antialias: true);

        // a:alphaModFix is the transparency of the picture as one image, so the drawing is
        // composed opaque on a layer and the layer faded — fading each path would let the ones
        // beneath show through the ones above.
        if (opacity < 1)
        {
            using var layer = new SKPaint();
            layer.Color = SKColors.White.WithAlpha((byte) Math.Round(opacity * 255));
            canvas.SaveLayer(layer);
        }

        // The paths are in the drawing's own coordinates; the canvas takes them to the page, and
        // scales each stroke with them.
        canvas.Translate((float) placement.OffsetX, (float) placement.OffsetY);
        canvas.Scale((float) placement.ScaleX, (float) placement.ScaleY);

        using var paint = new SKPaint();
        paint.IsAntialias = true;
        SvgClip? clip = null;
        foreach (var path in drawing.Recolored(recolor).Paths)
        {
            // Paths under one clip-path share its instance, so the clip is set once for the run.
            if (!ReferenceEquals(path.Clip, clip))
            {
                if (clip != null)
                {
                    canvas.Restore();
                }

                clip = path.Clip;
                if (clip != null)
                {
                    canvas.Save();
                    for (var level = clip; level != null; level = level.Parent)
                    {
                        using var outline = Build(level.Figures, level.EvenOdd);
                        canvas.ClipPath(outline, antialias: true);
                    }
                }
            }

            using var geometry = Build(path.Figures, path.EvenOdd);
            if (path.FillColorHex is { } fill)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = SkiaShapeDrawing.ParseColor(fill, path.FillAlpha);
                canvas.DrawPath(geometry, paint);
            }

            if (path.Stroke is { } stroke)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = SkiaShapeDrawing.ParseColor(stroke.ColorHex, stroke.Alpha);
                paint.StrokeWidth = (float) stroke.Width;
                paint.StrokeMiter = (float) stroke.MiterLimit;
                paint.StrokeCap = stroke.Cap switch
                {
                    SvgLineCap.Round => SKStrokeCap.Round,
                    SvgLineCap.Square => SKStrokeCap.Square,
                    _ => SKStrokeCap.Butt
                };
                paint.StrokeJoin = stroke.Join switch
                {
                    SvgLineJoin.Round => SKStrokeJoin.Round,
                    SvgLineJoin.Bevel => SKStrokeJoin.Bevel,
                    _ => SKStrokeJoin.Miter
                };
                canvas.DrawPath(geometry, paint);
            }
        }

        canvas.RestoreToCount(restoreTo);
    }

    static SKPath Build(IReadOnlyList<SvgFigure> figures, bool evenOdd)
    {
        var path = new SKPath {FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding};
        foreach (var figure in figures)
        {
            path.MoveTo((float) figure.StartX, (float) figure.StartY);
            foreach (var segment in figure.Segments)
            {
                if (segment.IsCurve)
                {
                    path.CubicTo((float) segment.Control1X, (float) segment.Control1Y, (float) segment.Control2X, (float) segment.Control2Y, (float) segment.X, (float) segment.Y);
                }
                else
                {
                    path.LineTo((float) segment.X, (float) segment.Y);
                }
            }

            if (figure.Closed)
            {
                path.Close();
            }
        }

        return path;
    }
}
