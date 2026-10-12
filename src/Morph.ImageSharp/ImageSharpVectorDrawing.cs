/// <summary>
/// Rasterises an <see cref="SvgDrawing"/> — a picture's SVG as the core reader resolved it — with
/// ImageSharp.Drawing paths, into a bitmap of exactly the pixels the picture's box covers. The
/// result takes the decoded fallback's place at the head of
/// <see cref="ImageSharpRenderContext"/>'s picture pipeline, so the effects, flips, rotation and
/// outline cut that follow are the ones a raster picture gets. An <c>a:srcRect</c> crop is the one
/// step done here rather than there: the cropped part of the drawing is laid over the whole bitmap,
/// so a crop costs no resolution.
/// </summary>
static class ImageSharpVectorDrawing
{
    static readonly DrawingOptions nonzeroFill = new() {IntersectionRule = IntersectionRule.NonZero};
    static readonly DrawingOptions evenOddFill = new() {IntersectionRule = IntersectionRule.EvenOdd};

    public static Image<Rgba32> Rasterize(SvgDrawing drawing, int width, int height, ImageCrop? crop)
    {
        var image = new Image<Rgba32>(width, height);
        var placement = drawing.Place(0, 0, width, height, crop);
        if (!placement.IsVisible)
        {
            return image;
        }

        // Paths under one clip-path share its instance, so the drawing falls into runs: unclipped
        // ones are drawn straight in, and a clipped one on a layer of its own that is cut to the
        // clip before it is laid over.
        var paths = drawing.Paths;
        var start = 0;
        while (start < paths.Count)
        {
            var clip = paths[start].Clip;
            var end = start + 1;
            while (end < paths.Count && ReferenceEquals(paths[end].Clip, clip))
            {
                end++;
            }

            if (clip == null)
            {
                Paint(image, paths, start, end, placement);
            }
            else
            {
                using var layer = new Image<Rgba32>(width, height);
                Paint(layer, paths, start, end, placement);
                for (var level = clip; level != null; level = level.Parent)
                {
                    Cut(layer, level, placement);
                }

                image.Mutate(_ => _.DrawImage(layer, 1f));
            }

            start = end;
        }

        return image;
    }

    static void Paint(Image<Rgba32> target, IReadOnlyList<SvgPath> paths, int start, int end, SvgPlacement placement)
    {
        using var canvas = target.Frames.RootFrame.CreateCanvas(Configuration.Default, new());

        // The bitmap's own edge is the picture's box. The drawing's frame only needs a clip of its
        // own when a negative crop has shrunk it to sit inside the box.
        var framed = placement.Left > 0 || placement.Top > 0 || placement.Right < target.Width || placement.Bottom < target.Height;
        if (framed)
        {
            canvas.Save();
            canvas.Clip(new RectanglePolygon((float) placement.Left, (float) placement.Top, (float) (placement.Right - placement.Left), (float) (placement.Bottom - placement.Top)));
        }

        for (var index = start; index < end; index++)
        {
            var path = paths[index];
            if (path.FillColorHex is { } fill)
            {
                // A fill closes every figure; a stroke, built apart below, leaves an open one open.
                canvas.Save(path.EvenOdd ? evenOddFill : nonzeroFill);
                canvas.Fill(new SolidBrush(ImageSharpShapeDrawing.ParseColor(fill, path.FillAlpha)), Build(path.Figures, placement, closeAll: true));
                canvas.Restore();
            }

            if (path.Stroke is { } stroke)
            {
                var pen = new SolidPen(new PenOptions(ImageSharpShapeDrawing.ParseColor(stroke.ColorHex, stroke.Alpha), (float) (stroke.Width * placement.StrokeScale))
                {
                    StrokeOptions = new()
                    {
                        LineCap = stroke.Cap switch
                        {
                            SvgLineCap.Round => LineCap.Round,
                            SvgLineCap.Square => LineCap.Square,
                            _ => LineCap.Butt
                        },

                        // SVG's miter gives way to a bevel past the limit, which is MiterRevert here;
                        // plain Miter would square the spike off instead.
                        LineJoin = stroke.Join switch
                        {
                            SvgLineJoin.Round => LineJoin.Round,
                            SvgLineJoin.Bevel => LineJoin.Bevel,
                            _ => LineJoin.MiterRevert
                        },
                        MiterLimit = stroke.MiterLimit
                    }
                });
                canvas.Draw(pen, Build(path.Figures, placement, closeAll: false));
            }
        }

        if (framed)
        {
            canvas.Restore();
        }
    }

    // Cuts a layer to a clip region by its coverage: the region is filled into a mask under its own
    // rule and each pixel's alpha scaled by the mask's. DrawingCanvas.Clip is not used because it
    // reads every clip path as nonzero whatever the options say (probed on ImageSharp.Drawing
    // 3.1.2: a pentagram clipped under EvenOdd kept its centre), and a clip-rule of evenodd is
    // something these documents do declare.
    static void Cut(Image<Rgba32> layer, SvgClip clip, SvgPlacement placement)
    {
        using var mask = new Image<Rgba32>(layer.Width, layer.Height);
        using (var canvas = mask.Frames.RootFrame.CreateCanvas(Configuration.Default, new()))
        {
            canvas.Save(clip.EvenOdd ? evenOddFill : nonzeroFill);
            canvas.Fill(new SolidBrush(Color.White), Build(clip.Figures, placement, closeAll: true));
            canvas.Restore();
        }

        layer.ProcessPixelRows(
            mask,
            (pixels, coverage) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    var covered = coverage.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x].A = (byte) (row[x].A * covered[x].A / 255);
                    }
                }
            });
    }

    static IPath Build(IReadOnlyList<SvgFigure> figures, SvgPlacement placement, bool closeAll)
    {
        var builder = new PathBuilder();
        foreach (var figure in figures)
        {
            builder.MoveTo(Map(figure.StartX, figure.StartY));
            foreach (var segment in figure.Segments)
            {
                if (segment.IsCurve)
                {
                    builder.CubicBezierTo(Map(segment.Control1X, segment.Control1Y), Map(segment.Control2X, segment.Control2Y), Map(segment.X, segment.Y));
                }
                else
                {
                    builder.LineTo(Map(segment.X, segment.Y));
                }
            }

            if (closeAll || figure.Closed)
            {
                builder.CloseFigure();
            }
        }

        return builder.Build();

        Vector2 Map(double x, double y) =>
            new((float) (x * placement.ScaleX + placement.OffsetX), (float) (y * placement.ScaleY + placement.OffsetY));
    }
}
