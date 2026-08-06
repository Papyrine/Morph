/// <summary>
/// Shape-drawing primitives shared by <see cref="PdfPainter"/>: group-shape contours, pictures, pens
/// and alpha, plus custGeom polygon paths and linear-gradient brushes. These lived on the production
/// <c>PdfTextEngine</c>/<c>PdfPageRenderer</c> (internal statics the painter reused so the two paths
/// stayed identical) and moved here verbatim when the production PDF path was deleted — the raster
/// analogue is <c>SkiaShapeDrawing</c>/<c>ImageSharpShapeDrawing</c>.
/// </summary>
static class PdfShapeDrawing
{
    /// <summary>
    /// The shape's <see cref="GroupShape.Subpaths"/> contours scaled into the given box with the
    /// flip flags applied, or null for primitive-geometry shapes. Alternate (even-odd) fill keeps
    /// ring shapes (frame) hollow.
    /// </summary>
    internal static XGraphicsPath? BuildGroupShapePath(GroupShape shape, double x, double y, double width, double height)
    {
        if (shape.Subpaths == null)
        {
            return null;
        }

        var path = new XGraphicsPath {FillMode = XFillMode.Alternate};
        foreach (var contour in shape.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            var points = new XPoint[contour.Count];
            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                points[index] = new(x + unitX * width, y + unitY * height);
            }

            path.StartFigure();
            path.AddPolygon(points);
            path.CloseFigure();
        }

        return path;
    }

    internal static void DrawGroupPicture(PdfRenderContext context, XGraphics graphics, GroupShape shape, double x, double y, double width, double height, bool clipToEllipse)
    {
        // PDFsharp can't decode SVG, so an icon graphic falls back to the raster blip the parser
        // kept behind it.
        var data = shape.ImageContentType == "image/svg+xml"
            ? shape.ImageRasterFallbackData
            : shape.ImageData;
        if (data == null)
        {
            return;
        }

        // An a:srcRect crop is drawn by enlarging the picture so its visible sub-rectangle covers
        // the shape's box, then clipping back to that box. PDFsharp's source-rectangle overload
        // leaves the unit of the rectangle undocumented; this needs no such API.
        var image = shape.ImageCrop?.Expand(x, y, width, height) ?? (x, y, width, height);
        var cropped = image != (x, y, width, height);

        var state = graphics.Save();
        try
        {
            if (clipToEllipse)
            {
                // Word crops the picture to its pic:spPr geometry — the circular photos on menu
                // templates.
                var clip = new XGraphicsPath();
                clip.AddEllipse(x, y, width, height);
                graphics.IntersectClip(clip);
            }
            else if (cropped)
            {
                graphics.IntersectClip(new XRect(x, y, width, height));
            }

            graphics.DrawImage(context.GetImage(data), image.X, image.Y, image.Width, image.Height);
        }
        catch
        {
            // Undecodable raster (PDFsharp's cross-platform build only does BMP/PNG/JPEG):
            // drop the picture and keep the rest of the group.
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    internal static XPen StrokePen(GroupShape shape, double widthPoints)
    {
        var rgb = PdfRenderContext.ParseColor(shape.ColorHex);
        return new(XColor.FromArgb(AlphaByte(shape.LineAlpha), rgb.R, rgb.G, rgb.B), Math.Max(0.4, widthPoints))
        {
            // Square end caps extend each line half a stroke-width past its endpoints — that's how
            // Word's icon-style arrow glyphs (perpendicular connector lines) fuse into a clean L
            // corner. The raster backends already stroke with square caps; without this the PDF
            // leaves notches and slivers where the assembled arrow pieces meet.
            LineCap = XLineCap.Square
        };
    }

    internal static int AlphaByte(double alpha) =>
        (int) Math.Round(Math.Clamp(alpha, 0, 1) * 255);

    // Builds a path from custom geometry: each sub-path is its own closed contour, filled with
    // nonzero winding so oppositely-wound nested contours read as holes (matching DrawingML's
    // default custGeom fill) rather than fusing into one polygon.
    internal static XGraphicsPath BuildShapePath(FloatingShapeElement shape, double x, double y, double width, double height)
    {
        var path = new XGraphicsPath
        {
            FillMode = XFillMode.Winding
        };

        // Flip in the unit square, scale into the bounding box, then rotate around its centre —
        // matching the Skia/ImageSharp path transform so rotated custom geometry lines up.
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var radians = shape.RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        foreach (var contour in shape.Subpaths!)
        {
            var points = new XPoint[contour.Count];
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

                points[i] = new(absoluteX, absoluteY);
            }

            path.AddPolygon(points);
        }

        return path;
    }

    // Linear gradient mirroring the Skia/ImageSharp backends: angle 0° points along +X, clockwise
    // positive (OOXML a:lin/@ang), projected onto the bounding box as start/end points.
    internal static XLinearGradientBrush BuildGradientBrush(GradientFill gradient, double x, double y, double width, double height)
    {
        var radians = gradient.DirectionDegrees * Math.PI / 180.0;
        var directionX = Math.Cos(radians);
        var directionY = Math.Sin(radians);
        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var halfDiagonal = Math.Sqrt(width * width + height * height) / 2;
        var start = new XPoint(centerX - directionX * halfDiagonal, centerY - directionY * halfDiagonal);
        var end = new XPoint(centerX + directionX * halfDiagonal, centerY + directionY * halfDiagonal);
        return new(start, end, PdfRenderContext.ParseColor(gradient.StartColorHex), PdfRenderContext.ParseColor(gradient.EndColorHex));
    }
}
