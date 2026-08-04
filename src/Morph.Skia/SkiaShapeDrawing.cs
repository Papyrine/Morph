/// <summary>
/// Shape-drawing primitives shared by <see cref="SkiaPainter"/>: group-shape colour, contour and
/// picture drawing, and custGeom polygon paths. These lived on the production
/// <c>TextRenderer</c>/<c>SkiaPageRenderer</c> (internal statics the painter reused so the two paths
/// stayed pixel-identical) and moved here verbatim when the production raster path was deleted.
/// </summary>
static class SkiaShapeDrawing
{
    internal static SKColor ParseColor(string hex, double alpha) =>
        SkiaRenderContext.ParseColor(hex)
            .WithAlpha((byte) Math.Round(Math.Clamp(alpha, 0, 1) * 255));

    /// <summary>
    /// The shape's <see cref="GroupShape.Subpaths"/> contours scaled into <paramref name="rect"/>
    /// with the flip flags applied, or null for primitive-geometry shapes. Even-odd fill keeps
    /// ring shapes (frame) hollow.
    /// </summary>
    internal static SKPath? BuildGroupShapePath(GroupShape shape, SKRect rect)
    {
        if (shape.Subpaths == null)
        {
            return null;
        }

        var path = new SKPath {FillType = SKPathFillType.EvenOdd};
        foreach (var contour in shape.Subpaths)
        {
            if (contour.Count < 3)
            {
                continue;
            }

            for (var index = 0; index < contour.Count; index++)
            {
                var (pointX, pointY) = contour[index];
                var unitX = shape.FlipHorizontal ? 1 - pointX : pointX;
                var unitY = shape.FlipVertical ? 1 - pointY : pointY;
                var localX = rect.Left + (float) unitX * rect.Width;
                var localY = rect.Top + (float) unitY * rect.Height;
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

        return path;
    }

    internal static void DrawGeometry(SKCanvas canvas, SKRect rect, bool isEllipse, SKPaint paint)
    {
        if (isEllipse)
        {
            canvas.DrawOval(rect.MidX, rect.MidY, rect.Width / 2, rect.Height / 2, paint);
        }
        else
        {
            canvas.DrawRect(rect, paint);
        }
    }

    /// <summary>
    /// Draws a group's <c>pic:pic</c> into its child rectangle through the SVG/crop/ellipse-clip
    /// path, so a group's picture members render identically wherever the group is painted.
    /// </summary>
    internal static void RenderGroupPicture(SkiaRenderContext context, SKCanvas canvas, GroupShape shape, SKRect destRect, bool clipToEllipse)
    {
        var imageData = shape.ImageData!;

        canvas.Save();
        if (clipToEllipse)
        {
            // Word crops the picture to its pic:spPr geometry — the circular photos on menu
            // templates.
            using var clip = new SKPath();
            clip.AddOval(destRect);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        }

        var crop = shape.ImageCrop is {IsCropped: true} cropped ? cropped : null;

        if (shape.ImageContentType == "image/svg+xml")
        {
            // Padding (negative srcRect) shrinks the picture into Expand's sub-rectangle; the SVG
            // rasterizer's own crop math only handles positive source cropping.
            var svgCrop = crop is {HasPadding: true} ? null : crop;
            var svgBox = crop is {HasPadding: true} paddingCrop
                ? paddingCrop.Expand(destRect.Left, destRect.Top, destRect.Width, destRect.Height)
                : (destRect.Left, destRect.Top, destRect.Width, destRect.Height);

            // A crop moves the source origin off the picture's CullRect corner, so the rasterizer
            // has to translate that corner to the bitmap origin — which is what originAdjusted does.
            // Uncropped, the icons' CullRect already starts at 0 and the two agree.
            var bitmap = context.GetSvgRaster(imageData, (float) svgBox.Width, (float) svgBox.Height, svgCrop, originAdjusted: svgCrop != null);
            if (bitmap != null)
            {
                canvas.DrawBitmap(bitmap, (float) svgBox.X, (float) svgBox.Y);
            }
        }
        else if (context.GetBitmap(imageData) is { } skImage)
        {
            if (crop is {HasPadding: true})
            {
                var (paddedX, paddedY, paddedWidth, paddedHeight) = crop.Expand(destRect.Left, destRect.Top, destRect.Width, destRect.Height);
                canvas.Save();
                canvas.ClipRect(destRect);
                canvas.DrawBitmap(skImage, new SKRect((float) paddedX, (float) paddedY, (float) (paddedX + paddedWidth), (float) (paddedY + paddedHeight)));
                canvas.Restore();
            }
            else if (crop != null)
            {
                var source = new SKRect(
                    (float) (crop.Left * skImage.Width),
                    (float) (crop.Top * skImage.Height),
                    (float) ((1 - crop.Right) * skImage.Width),
                    (float) ((1 - crop.Bottom) * skImage.Height));
                canvas.DrawBitmap(skImage, source, destRect);
            }
            else
            {
                canvas.DrawBitmap(skImage, destRect);
            }
        }

        canvas.Restore();
    }

    internal static SKPath BuildPolygonPath(FloatingShapeElement shape, float x, float y, float width, float height)
    {
        var path = new SKPath();
        // Each sub-path is its own closed contour. SKPath's default Winding (nonzero) fill type
        // matches DrawingML's default custGeom fill, so oppositely-wound nested contours read as
        // holes instead of being fused into one polygon by connector lines.
        foreach (var contour in shape.Subpaths!)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                var (px, py) = contour[i];
                // Apply flips around the unit-square center, then scale into the bounding box.
                var ux = shape.FlipHorizontal ? 1 - px : px;
                var uy = shape.FlipVertical ? 1 - py : py;
                var localX = (float) (ux * width);
                var localY = (float) (uy * height);
                if (i == 0)
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

        // Translate so (0,0) sits at the bbox top-left, then rotate around the bbox center.
        var matrix = SKMatrix.CreateTranslation(x, y);
        if (shape.RotationDegrees != 0)
        {
            matrix = SKMatrix.Concat(
                matrix,
                SKMatrix.CreateRotationDegrees((float) shape.RotationDegrees, width / 2, height / 2));
        }
        path.Transform(matrix);
        return path;
    }
}
