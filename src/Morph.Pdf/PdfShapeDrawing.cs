/// <summary>
/// Shape-drawing primitives shared by <see cref="PdfPainter"/>: path construction (rectangles, ellipses,
/// lines, group-shape contours and custGeom polygons), filling and stroking, group pictures and
/// linear-gradient paints — the raster analogue is <c>SkiaShapeDrawing</c>/<c>ImageSharpShapeDrawing</c>.
/// </summary>
static class PdfShapeDrawing
{
    // The control-point distance of a cubic Bézier quarter circle, as a fraction of the radius.
    const double kappa = 0.5522847498307936;

    public static PdfPath Rectangle(double x, double y, double width, double height)
    {
        using var builder = new PathBuilder();
        builder.MoveTo((float) x, (float) y)
            .LineTo((float) (x + width), (float) y)
            .LineTo((float) (x + width), (float) (y + height))
            .LineTo((float) x, (float) (y + height))
            .Close();
        return builder.Build();
    }

    public static PdfPath Ellipse(double x, double y, double width, double height)
    {
        using var builder = new PathBuilder();
        AddEllipse(builder, x, y, width, height);
        return builder.Build();
    }

    public static void AddEllipse(PathBuilder builder, double x, double y, double width, double height)
    {
        var radiusX = width / 2;
        var radiusY = height / 2;
        var centerX = x + radiusX;
        var centerY = y + radiusY;
        var controlX = radiusX * kappa;
        var controlY = radiusY * kappa;
        builder.MoveTo((float) (centerX + radiusX), (float) centerY)
            .CubicTo((float) (centerX + radiusX), (float) (centerY + controlY), (float) (centerX + controlX), (float) (centerY + radiusY), (float) centerX, (float) (centerY + radiusY))
            .CubicTo((float) (centerX - controlX), (float) (centerY + radiusY), (float) (centerX - radiusX), (float) (centerY + controlY), (float) (centerX - radiusX), (float) centerY)
            .CubicTo((float) (centerX - radiusX), (float) (centerY - controlY), (float) (centerX - controlX), (float) (centerY - radiusY), (float) centerX, (float) (centerY - radiusY))
            .CubicTo((float) (centerX + controlX), (float) (centerY - radiusY), (float) (centerX + radiusX), (float) (centerY - controlY), (float) (centerX + radiusX), (float) centerY)
            .Close();
    }

    public static PdfPath Line(double x1, double y1, double x2, double y2)
    {
        using var builder = new PathBuilder();
        builder.MoveTo((float) x1, (float) y1)
            .LineTo((float) x2, (float) y2);
        return builder.Build();
    }

    public static PdfPath? Polyline(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2)
        {
            return null;
        }

        using var builder = new PathBuilder();
        builder.MoveTo((float) points[0].X, (float) points[0].Y);
        for (var index = 1; index < points.Count; index++)
        {
            builder.LineTo((float) points[index].X, (float) points[index].Y);
        }

        return builder.Build();
    }

    // Adds one contour; a fill closes an open figure by itself, so only a stroke cares about close.
    static void AddContour(PathBuilder builder, IReadOnlyList<(double X, double Y)> points, bool close)
    {
        builder.MoveTo((float) points[0].X, (float) points[0].Y);
        for (var index = 1; index < points.Count; index++)
        {
            builder.LineTo((float) points[index].X, (float) points[index].Y);
        }

        if (close)
        {
            builder.Close();
        }
    }

    /// <summary>Fills <paramref name="path"/>, then disposes it.</summary>
    public static void Fill(Surface surface, PdfPath path, Fill fill)
    {
        using (path)
        {
            surface.SetStroke(null)
                .SetFill(fill)
                .DrawPath(path);
        }
    }

    /// <summary>Strokes <paramref name="path"/>, then disposes it.</summary>
    public static void Stroke(Surface surface, PdfPath path, Stroke stroke)
    {
        using (path)
        {
            surface.SetFill(null)
                .SetStroke(stroke)
                .DrawPath(path);
        }

        surface.SetStroke(null);
    }

    public static void FillRectangle(PdfRenderContext context, Surface surface, PdfColor color, double x, double y, double width, double height) =>
        Fill(surface, Rectangle(x, y, width, height), context.GetFill(color));

    public static void StrokeLine(Surface surface, Stroke stroke, double x1, double y1, double x2, double y2) =>
        Stroke(surface, Line(x1, y1, x2, y2), stroke);

    public static Layer ClipRectangle(Surface surface, double x, double y, double width, double height)
    {
        using var path = Rectangle(x, y, width, height);
        return surface.PushClip(path);
    }

    public static Layer RotateAt(Surface surface, double degrees, double centerX, double centerY) =>
        surface.PushTransform(Matrix3x2.CreateRotation((float) (degrees * Math.PI / 180), new((float) centerX, (float) centerY)));

    /// <summary>
    /// The shape's <see cref="GroupShape.Subpaths"/> contours scaled into the given box with the
    /// flip flags applied, or null for primitive-geometry shapes. Filled even-odd, which keeps ring
    /// shapes (frame) hollow. <paramref name="close"/> false leaves each contour an open figure — the
    /// stroke of an <see cref="GroupShape.OpenOutline"/>.
    /// </summary>
    internal static PdfPath? BuildGroupShapePath(GroupShape shape, double x, double y, double width, double height, bool close = true)
    {
        if (shape.Subpaths == null)
        {
            return null;
        }

        using var builder = new PathBuilder();
        var any = false;
        foreach (var contour in shape.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            var points = new (double X, double Y)[contour.Count];
            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                points[index] = (x + unitX * width, y + unitY * height);
            }

            AddContour(builder, points, close);
            any = true;
        }

        if (!any)
        {
            return null;
        }

        return builder.Build();
    }

    internal static void DrawGroupPicture(PdfRenderContext context, Surface surface, GroupShape shape, double x, double y, double width, double height, bool clipToEllipse)
    {
        // An SVG icon is placed as a graphic of the paths the core reader made of it; one the reader
        // gave up falls back to the raster blip the parser kept behind it.
        var vector = SvgDrawing.For(shape.ImageData, shape.ImageContentType);
        var data = shape.ImageContentType == "image/svg+xml"
            ? shape.ImageRasterFallbackData
            : shape.ImageData;
        if (vector == null && data == null)
        {
            return;
        }

        var picture = vector != null
            ? PdfPicture.Of(context, vector)
            : PdfPicture.Of(context.GetImage(data!));
        if (picture == null)
        {
            // Undecodable raster: drop the picture and keep the rest of the group.
            return;
        }

        // An a:srcRect crop is drawn by enlarging the picture so its visible sub-rectangle covers
        // the shape's box, then clipping back to that box.
        var image = shape.ImageCrop?.Expand(x, y, width, height) ?? (x, y, width, height);
        var cropped = image != (x, y, width, height);

        if (clipToEllipse)
        {
            // Word crops the picture to its pic:spPr geometry — the circular photos on menu
            // templates.
            using var clip = Ellipse(x, y, width, height);
            using (surface.PushClip(clip))
            {
                picture.Draw(surface, image.X, image.Y, image.Width, image.Height);
            }
        }
        else if (cropped)
        {
            using (ClipRectangle(surface, x, y, width, height))
            {
                picture.Draw(surface, image.X, image.Y, image.Width, image.Height);
            }
        }
        else
        {
            picture.Draw(surface, image.X, image.Y, image.Width, image.Height);
        }
    }

    internal static Stroke StrokePen(PdfRenderContext context, GroupShape shape, double widthPoints, LineCap? cap = null)
    {
        var width = Math.Max(0.4, widthPoints);
        var color = PdfRenderContext.ParseColor(shape.ColorHex).WithAlpha(shape.LineAlpha);
        // Square end caps extend each line half a stroke-width past its endpoints — that's how
        // Word's icon-style arrow glyphs (perpendicular connector lines) fuse into a clean L
        // corner. The raster backends stroke with square caps too; without it the PDF leaves
        // notches and slivers where the assembled arrow pieces meet.
        return Dashed(context.GetStroke(color, width, cap ?? LineCap.Square), shape.LineDashPattern);
    }

    /// <summary>
    /// Gives a stroke its preset dash (<c>a:prstDash</c>). The pattern is declared in multiples of the
    /// stroke's width; Krilla takes it in points. A dashed stroke ends each dash flat.
    /// </summary>
    internal static Stroke Dashed(Stroke stroke, IReadOnlyList<double>? pattern)
    {
        if (pattern is not {Count: >= 2})
        {
            return stroke;
        }

        return stroke with
        {
            DashArray = pattern.Select(_ => (float) (_ * stroke.Width)).ToArray(),
            LineCap = LineCap.Butt
        };
    }

    // Builds a path from custom geometry: each sub-path is its own closed contour, filled with
    // nonzero winding so oppositely-wound nested contours read as holes (matching DrawingML's
    // default custGeom fill) rather than fusing into one polygon.
    internal static PdfPath BuildShapePath(FloatingShapeElement shape, double x, double y, double width, double height)
    {
        using var builder = new PathBuilder();

        // Flip in the unit square, scale into the bounding box, then rotate around its centre —
        // matching the Skia/ImageSharp path transform so rotated custom geometry lines up.
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var radians = shape.RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        foreach (var contour in shape.Subpaths!)
        {
            if (contour.Count == 0)
            {
                continue;
            }

            var points = new (double X, double Y)[contour.Count];
            for (var i = 0; i < contour.Count; i++)
            {
                var (pointX, pointY) = contour[i];
                var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                var absoluteX = x + unitX * width;
                var absoluteY = y + unitY * height;
                if (shape.RotationDegrees != 0)
                {
                    var deltaX = absoluteX - centerX;
                    var deltaY = absoluteY - centerY;
                    absoluteX = centerX + deltaX * cos - deltaY * sin;
                    absoluteY = centerY + deltaX * sin + deltaY * cos;
                }

                points[i] = (absoluteX, absoluteY);
            }

            AddContour(builder, points, close: true);
        }

        return builder.Build();
    }

    // Linear gradient mirroring the Skia/ImageSharp backends: angle 0° points along +X, clockwise
    // positive (OOXML a:lin/@ang), projected onto the bounding box as start/end points.
    internal static Paint BuildGradientPaint(GradientFill gradient, double x, double y, double width, double height)
    {
        var radians = gradient.DirectionDegrees * Math.PI / 180.0;
        var directionX = Math.Cos(radians);
        var directionY = Math.Sin(radians);
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var halfDiagonal = Math.Sqrt(width * width + height * height) / 2;
        var start = PdfRenderContext.ParseColor(gradient.StartColorHex);
        var end = PdfRenderContext.ParseColor(gradient.EndColorHex);
        return Paint.LinearGradient(
            (float) (centerX - directionX * halfDiagonal),
            (float) (centerY - directionY * halfDiagonal),
            (float) (centerX + directionX * halfDiagonal),
            (float) (centerY + directionY * halfDiagonal),
            [
                new(0, Color.Rgb(start.R, start.G, start.B), (float) Math.Clamp(gradient.StartAlpha, 0, 1)),
                new(1, Color.Rgb(end.R, end.G, end.B), (float) Math.Clamp(gradient.EndAlpha, 0, 1))
            ]);
    }
}

/// <summary>
/// A picture ready to place: a decoded bitmap, or a graphic of an SVG's paths in the drawing's own
/// units. Either is drawn into a box by the same call, so crop, tile, rotation, flip and clip carry
/// over untouched.
/// </summary>
sealed class PdfPicture
{
    PdfImage? image;
    Graphic? graphic;
    double graphicWidth;
    double graphicHeight;

    public static PdfPicture? Of(PdfImage? image)
    {
        if (image == null)
        {
            return null;
        }

        return new()
        {
            image = image
        };
    }

    public static PdfPicture Of(PdfRenderContext context, SvgDrawing vector, ImageRecolor? recolor = null, double opacity = 1) =>
        new()
        {
            graphic = context.GetVectorGraphic(vector, recolor, opacity),
            graphicWidth = vector.Width,
            graphicHeight = vector.Height
        };

    public void Draw(Surface surface, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (image != null)
        {
            surface.DrawImage(image, Krilla.Rectangle.FromSize((float) x, (float) y, (float) width, (float) height));
            return;
        }

        var transform = Matrix3x2.CreateScale((float) (width / graphicWidth), (float) (height / graphicHeight)) *
                        Matrix3x2.CreateTranslation((float) x, (float) y);
        using (surface.PushTransform(transform))
        {
            surface.DrawGraphic(graphic!);
        }
    }
}
