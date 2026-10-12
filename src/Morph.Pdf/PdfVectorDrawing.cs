/// <summary>
/// Turns an <see cref="SvgDrawing"/> — a picture's SVG as the core reader resolved it — into a PDF
/// form XObject of real path operators, so the picture stays a vector one in the PDF: sharp at any
/// zoom, where the PNG Office saved beside the SVG is a rendering at one size. A form rather than
/// paths written into each page, because a form is an <see cref="XImage"/>: the painter places it
/// through the same calls as a decoded bitmap — crop, tile, rotation, flip and outline clip all
/// carry over untouched — and a picture drawn many times (a master's ornament on every slide, a
/// header logo on every page) is stored once, as its bitmap was.
/// </summary>
static class PdfVectorDrawing
{
    // The least the form's longer side is, in its own units. PDFsharp writes path coordinates to
    // three decimal places, so the drawing is laid out at a size where that is fine enough whatever
    // units its viewBox happened to use — an icon on a 24-unit grid as much as a figure a thousand
    // across.
    const double formExtent = 1000;

    public static XForm BuildForm(PdfDocument document, SvgDrawing drawing, double opacity)
    {
        // The enlargement is a power of ten, so that it moves the decimal point and nothing else: a
        // coordinate of 52 is written as 5200, not as 541.667. A scale that makes whole numbers
        // into rounded thirds puts an edge meant for a pixel boundary a hair past it, and PDFium
        // then fills the whole extra pixel (a bar-chart icon's bars each gained a column).
        var scale = Math.Pow(10, Math.Max(0, Math.Ceiling(Math.Log10(formExtent / Math.Max(drawing.Width, drawing.Height)))));

        // The form's box is the drawing's frame, and a form draws nothing outside its box — which
        // is the rule that nothing outside the viewBox shows.
        var form = new XForm(document, new XSize(drawing.Width * scale, drawing.Height * scale));

        // Disposing the graphics is what writes the form's content stream. XForm.DrawingFinished
        // reads as the call for that and is not: on PDFsharp 6.2.4 it leaves the form a dictionary
        // with no stream, which draws as nothing.
        using var graphics = XGraphics.FromForm(form);
        SvgClip? clip = null;
        XGraphicsState? clipState = null;
        foreach (var path in drawing.Paths)
        {
            // Paths under one clip-path share its instance, so the clip is set once for the run.
            if (!ReferenceEquals(path.Clip, clip))
            {
                if (clipState != null)
                {
                    graphics.Restore(clipState);
                    clipState = null;
                }

                clip = path.Clip;
                if (clip != null)
                {
                    clipState = graphics.Save();
                    for (var level = clip; level != null; level = level.Parent)
                    {
                        graphics.IntersectClip(Build(level.Figures, level.EvenOdd, drawing, scale));
                    }
                }
            }

            var geometry = Build(path.Figures, path.EvenOdd, drawing, scale);
            if (path.FillColorHex is { } fill)
            {
                graphics.DrawPath(new XSolidBrush(Color(fill, path.FillAlpha * opacity)), geometry);
            }

            if (path.Stroke is { } stroke)
            {
                graphics.DrawPath(
                    new XPen(Color(stroke.ColorHex, stroke.Alpha * opacity), stroke.Width * scale)
                    {
                        LineCap = stroke.Cap switch
                        {
                            SvgLineCap.Round => XLineCap.Round,
                            SvgLineCap.Square => XLineCap.Square,
                            _ => XLineCap.Flat
                        },
                        LineJoin = stroke.Join switch
                        {
                            SvgLineJoin.Round => XLineJoin.Round,
                            SvgLineJoin.Bevel => XLineJoin.Bevel,
                            _ => XLineJoin.Miter
                        },
                        MiterLimit = stroke.MiterLimit
                    },
                    geometry);
            }
        }

        if (clipState != null)
        {
            graphics.Restore(clipState);
        }

        return form;
    }

    static XColor Color(string hex, double alpha)
    {
        var rgb = PdfRenderContext.ParseColor(hex);
        return XColor.FromArgb(PdfShapeDrawing.AlphaByte(alpha), rgb.R, rgb.G, rgb.B);
    }

    // A fill closes an open figure by itself in PDF and a stroke leaves it open, so one path
    // serves both.
    static XGraphicsPath Build(IReadOnlyList<SvgFigure> figures, bool evenOdd, SvgDrawing drawing, double scale)
    {
        var path = new XGraphicsPath {FillMode = evenOdd ? XFillMode.Alternate : XFillMode.Winding};
        foreach (var figure in figures)
        {
            path.StartFigure();
            var (currentX, currentY) = Map(figure.StartX, figure.StartY);
            foreach (var segment in figure.Segments)
            {
                var (x, y) = Map(segment.X, segment.Y);
                if (segment.IsCurve)
                {
                    var (control1X, control1Y) = Map(segment.Control1X, segment.Control1Y);
                    var (control2X, control2Y) = Map(segment.Control2X, segment.Control2Y);
                    path.AddBezier(currentX, currentY, control1X, control1Y, control2X, control2Y, x, y);
                }
                else
                {
                    path.AddLine(currentX, currentY, x, y);
                }

                (currentX, currentY) = (x, y);
            }

            if (figure.Closed)
            {
                path.CloseFigure();
            }
        }

        return path;

        (double X, double Y) Map(double x, double y) =>
            ((x - drawing.X) * scale, (y - drawing.Y) * scale);
    }
}
