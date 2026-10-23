/// <summary>
/// Turns an <see cref="SvgDrawing"/> — a picture's SVG as the core reader resolved it — into a Krilla
/// <see cref="Graphic"/> of real path operators, so the picture stays a vector one in the PDF: sharp at
/// any zoom, where the PNG Office saved beside the SVG is a rendering at one size. A graphic rather than
/// paths written into each page, because a graphic is captured once: a picture drawn many times (a
/// master's ornament on every slide, a header logo on every page) is stored once, as its bitmap was.
/// The graphic is in the drawing's own units with its frame at the origin; <see cref="PdfPicture"/>
/// scales it into the box it is placed in.
/// </summary>
static class PdfVectorDrawing
{
    public static Graphic Capture(PdfRenderContext context, Surface surface, SvgDrawing drawing, double opacity) =>
        surface.CaptureGraphic(_ => Draw(context, _, drawing, opacity));

    static void Draw(PdfRenderContext context, Surface surface, SvgDrawing drawing, double opacity)
    {
        // Nothing outside the viewBox shows.
        using var frame = PdfShapeDrawing.ClipRectangle(surface, 0, 0, drawing.Width, drawing.Height);

        SvgClip? clip = null;
        var clipLayers = new Stack<Layer>();
        foreach (var path in drawing.Paths)
        {
            // Paths under one clip-path share its instance, so the clip is set once for the run.
            if (!ReferenceEquals(path.Clip, clip))
            {
                while (clipLayers.Count > 0)
                {
                    clipLayers.Pop().Dispose();
                }

                clip = path.Clip;
                for (var level = clip; level != null; level = level.Parent)
                {
                    using var clipPath = Build(level.Figures, drawing);
                    clipLayers.Push(surface.PushClip(clipPath, Rule(level.EvenOdd)));
                }
            }

            if (path.FillColorHex is { } fill)
            {
                var color = PdfRenderContext.ParseColor(fill).WithAlpha(path.FillAlpha * opacity);
                PdfShapeDrawing.Fill(surface, Build(path.Figures, drawing), context.GetFill(color, Rule(path.EvenOdd)));
            }

            if (path.Stroke is { } stroke)
            {
                var color = PdfRenderContext.ParseColor(stroke.ColorHex).WithAlpha(stroke.Alpha * opacity);
                PdfShapeDrawing.Stroke(
                    surface,
                    Build(path.Figures, drawing),
                    context.GetStroke(color, stroke.Width) with
                    {
                        LineCap = stroke.Cap switch
                        {
                            SvgLineCap.Round => LineCap.Round,
                            SvgLineCap.Square => LineCap.Square,
                            _ => LineCap.Butt
                        },
                        LineJoin = stroke.Join switch
                        {
                            SvgLineJoin.Round => LineJoin.Round,
                            SvgLineJoin.Bevel => LineJoin.Bevel,
                            _ => LineJoin.Miter
                        },
                        MiterLimit = (float) stroke.MiterLimit
                    });
            }
        }

        while (clipLayers.Count > 0)
        {
            clipLayers.Pop().Dispose();
        }
    }

    static FillRule Rule(bool evenOdd)
    {
        if (evenOdd)
        {
            return FillRule.EvenOdd;
        }

        return FillRule.NonZero;
    }

    // A fill closes an open figure by itself in PDF and a stroke leaves it open, so one path
    // serves both.
    static PdfPath Build(IReadOnlyList<SvgFigure> figures, SvgDrawing drawing)
    {
        using var builder = new PathBuilder();
        foreach (var figure in figures)
        {
            var (startX, startY) = Map(figure.StartX, figure.StartY);
            builder.MoveTo(startX, startY);
            foreach (var segment in figure.Segments)
            {
                var (x, y) = Map(segment.X, segment.Y);
                if (segment.IsCurve)
                {
                    var (control1X, control1Y) = Map(segment.Control1X, segment.Control1Y);
                    var (control2X, control2Y) = Map(segment.Control2X, segment.Control2Y);
                    builder.CubicTo(control1X, control1Y, control2X, control2Y, x, y);
                }
                else
                {
                    builder.LineTo(x, y);
                }
            }

            if (figure.Closed)
            {
                builder.Close();
            }
        }

        return builder.Build();

        (float X, float Y) Map(double x, double y) =>
            ((float) (x - drawing.X), (float) (y - drawing.Y));
    }
}
