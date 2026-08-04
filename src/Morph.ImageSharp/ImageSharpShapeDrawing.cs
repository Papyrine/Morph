/// <summary>
/// Shape-drawing primitives shared by <see cref="ImageSharpPainter"/>: group-shape colour, contour
/// and picture drawing, custGeom/preset paths and the shape rotation transform. These lived on the
/// production <c>TextRenderer</c>/<c>ImageSharpPageRenderer</c> (internal statics the painter reused
/// so the two paths stayed pixel-identical) and moved here verbatim when the production raster path
/// was deleted.
/// </summary>
static class ImageSharpShapeDrawing
{
    internal static Color ParseColor(string hex, double alpha)
    {
        var color = ImageSharpRenderContext.ParseColor(hex);
        var clamped = Math.Clamp(alpha, 0, 1);
        if (clamped >= 1)
        {
            return color;
        }

        var pixel = color.ToPixel<Rgba32>();
        pixel.A = (byte) Math.Round(clamped * 255);
        return Color.FromPixel(pixel);
    }

    /// <summary>
    /// The shape's <see cref="GroupShape.Subpaths"/> contours scaled into the given box with the
    /// flip flags applied, or null for primitive-geometry shapes. Multiple contours combine into a
    /// <see cref="ComplexPolygon"/>, whose even-odd intersection keeps ring shapes (frame) hollow.
    /// </summary>
    internal static IPath? BuildGroupShapePath(GroupShape shape, float x, float y, float width, float height)
    {
        if (shape.Subpaths == null)
        {
            return null;
        }

        var polygons = new List<IPath>();
        foreach (var contour in shape.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            var points = new PointF[contour.Count];
            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                points[index] = new(x + (float) unitX * width, y + (float) unitY * height);
            }

            polygons.Add(new Polygon(new LinearLineSegment(points)));
        }

        return polygons.Count switch
        {
            0 => null,
            1 => polygons[0],
            _ => new ComplexPolygon(polygons.ToArray())
        };
    }

    /// <summary>
    /// Draws a group's <c>pic:pic</c> into its child rectangle. <paramref name="clip"/> is the
    /// shape's path when Word crops the picture to something other than its bounding box (the
    /// circular photos on menu templates), null for a plain <c>rect</c> picture.
    /// </summary>
    internal static void RenderGroupPicture(ImageSharpRenderContext context, DrawingCanvas pageCanvas, GroupShape shape, float x, float y, float width, float height, IPath? clip, bool groupRotated)
    {
        // SVG isn't supported here; use the raster fallback the parser kept from the primary
        // <a:blip>, or skip if we don't have one.
        var imageBytes = shape.ImageContentType == "image/svg+xml"
            ? shape.ImageRasterFallbackData
            : shape.ImageData;
        if (imageBytes == null)
        {
            return;
        }

        // DrawingCanvas.Apply doesn't honour a pushed canvas rotation, so under a rotated group an
        // ellipse-clipped photo would sit unrotated. DrawImage does honour it, so draw a standalone
        // pre-clipped bitmap instead: the pushed transform then rotates the circle into place. (A
        // rect picture already goes through DrawImage below and rotates for free.)
        if (groupRotated && clip != null)
        {
            var clipped = context.GetEllipseClippedImage(imageBytes, (int) width, (int) height, shape.ImageCrop);
            if (clipped != null)
            {
                pageCanvas.DrawImage(clipped, new((int) x, (int) y));
            }

            return;
        }

        var img = context.GetProcessedImage(imageBytes, (int) width, (int) height, shape.ImageCrop, BlipColorEffect.None, rotationDegrees: 0);
        if (img == null)
        {
            return;
        }

        if (clip == null)
        {
            pageCanvas.DrawImage(img, new((int) x, (int) y));
            return;
        }

        // Apply() masks its inner context to the path and rebases coordinates on the path's
        // bounding box — which is exactly this picture's rectangle, hence the (0, 0) origin. It
        // defers the draw to canvas-replay time, so the source has to outlive this call: the
        // context's image cache holds it until the whole document is rendered.
        pageCanvas.Apply(clip, _ => _.DrawImage(img, new Point(0, 0), 1f));
    }

    /// <summary>
    /// <see cref="DrawingOptions"/> whose transform rotates by <paramref name="radians"/> around the
    /// pivot. Use with <see cref="DrawingCanvas.Save(DrawingOptions, IPath[])"/> /
    /// <see cref="DrawingCanvas.Restore"/> to render content rotated in geometry space, avoiding
    /// the temp-image + <c>Mutate(_.Rotate(...))</c> + composite round trip.
    /// </summary>
    internal static DrawingOptions BuildRotation(float radians, float pivotX, float pivotY) =>
        new()
        {
            Transform = new(Matrix3x2.CreateRotation(radians, new(pivotX, pivotY)))
        };

    /// <summary>The preset rect/ellipse as an unrotated path (rotation applies via
    /// <see cref="BuildRotation"/> around the box centre at the call sites).</summary>
    internal static IPath BuildPresetPath(FloatingShapeElement shape, float x, float y, float width, float height) =>
        shape.Preset == PresetShape.Ellipse
            ? new EllipsePolygon(x + width / 2, y + height / 2, width, height)
            : new RectanglePolygon(x, y, width, height);

    // custGeom fills use nonzero winding to match SkiaSharp's default and DrawingML — without
    // this ImageSharp's default even-odd rule would punch holes wherever contours overlap.
    internal static readonly DrawingOptions NonzeroFill = new()
    {
        ShapeOptions = new() { IntersectionRule = IntersectionRule.NonZero }
    };

    internal static IPath BuildPath(FloatingShapeElement shape, float x, float y, float width, float height)
    {
        var rotRad = (float) (shape.RotationDegrees * Math.PI / 180.0);
        var cos = (float) Math.Cos(rotRad);
        var sin = (float) Math.Sin(rotRad);
        var halfW = width / 2f;
        var halfH = height / 2f;

        var builder = new PathBuilder();
        foreach (var contour in shape.Subpaths!)
        {
            var transformed = new PointF[contour.Count];
            for (var i = 0; i < contour.Count; i++)
            {
                var (px, py) = contour[i];
                var ux = shape.FlipHorizontal ? 1 - px : px;
                var uy = shape.FlipVertical ? 1 - py : py;
                // Local coords with the bbox center at origin.
                var lx = (float) (ux * width) - halfW;
                var ly = (float) (uy * height) - halfH;
                // Rotate clockwise (image-space y-down): standard 2D rotation matrix.
                var rx = lx * cos - ly * sin;
                var ry = lx * sin + ly * cos;
                transformed[i] = new(x + halfW + rx, y + halfH + ry);
            }
            // Each contour is its own closed figure so disjoint pieces and holes stay separate
            // instead of being fused into one polygon by connector lines.
            builder.AddLines(transformed);
            builder.CloseFigure();
        }
        return builder.Build();
    }
}
