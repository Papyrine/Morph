/// <summary>
/// Clips a flattened group child's contours to its owning group's frame. Word cuts a group's
/// children at the group's extent box; the parse flattens groups into independent floating
/// elements, so without this a child extending past the frame paints where Word shows nothing
/// (labels/07's collage bleeding over the page's white margin, cards/13's stray white boxes).
/// The child box and the frame live in the SAME anchor-relative coordinates, so the clip is pure
/// geometry — each contour (in the shape's 0..1 unit square) is clipped against the frame
/// rectangle transformed into that unit space, via Sutherland–Hodgman.
/// </summary>
static class GroupFrameClipper
{
    /// <summary>
    /// Clips <paramref name="subpaths"/> (unit-square contours of a box at
    /// <paramref name="shapeLeft"/>/<paramref name="shapeTop"/> sized
    /// <paramref name="shapeWidth"/>×<paramref name="shapeHeight"/>) to the frame rectangle in
    /// the same coordinate space. Returns the clipped contours; an empty list when the shape
    /// lies fully outside the frame; or the ORIGINAL list when the shape lies fully inside
    /// (the common case — no allocation).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Clip(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> subpaths,
        double shapeLeft,
        double shapeTop,
        double shapeWidth,
        double shapeHeight,
        double frameLeft,
        double frameTop,
        double frameWidth,
        double frameHeight)
    {
        if (shapeWidth <= 0 || shapeHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
        {
            return subpaths;
        }

        // The frame in the shape's unit space.
        var clipLeft = (frameLeft - shapeLeft) / shapeWidth;
        var clipTop = (frameTop - shapeTop) / shapeHeight;
        var clipRight = clipLeft + frameWidth / shapeWidth;
        var clipBottom = clipTop + frameHeight / shapeHeight;

        // Shape fully inside the frame: nothing to do.
        if (clipLeft <= 0 && clipTop <= 0 && clipRight >= 1 && clipBottom >= 1)
        {
            return subpaths;
        }

        var result = new List<IReadOnlyList<(double X, double Y)>>(subpaths.Count);
        foreach (var contour in subpaths)
        {
            var clipped = ClipContour(contour, clipLeft, clipTop, clipRight, clipBottom);
            if (clipped.Count >= 3)
            {
                result.Add(clipped);
            }
        }

        return result;
    }

    /// <summary>Sutherland–Hodgman polygon clip against an axis-aligned rectangle.</summary>
    static List<(double X, double Y)> ClipContour(
        IReadOnlyList<(double X, double Y)> contour,
        double left,
        double top,
        double right,
        double bottom)
    {
        var points = new List<(double X, double Y)>(contour);
        points = ClipEdge(points, static (p, edge) => p.X >= edge, static (a, b, edge) => IntersectVertical(a, b, edge), left);
        points = ClipEdge(points, static (p, edge) => p.X <= edge, static (a, b, edge) => IntersectVertical(a, b, edge), right);
        points = ClipEdge(points, static (p, edge) => p.Y >= edge, static (a, b, edge) => IntersectHorizontal(a, b, edge), top);
        points = ClipEdge(points, static (p, edge) => p.Y <= edge, static (a, b, edge) => IntersectHorizontal(a, b, edge), bottom);
        return points;
    }

    static List<(double X, double Y)> ClipEdge(
        List<(double X, double Y)> points,
        Func<(double X, double Y), double, bool> inside,
        Func<(double X, double Y), (double X, double Y), double, (double X, double Y)> intersect,
        double edge)
    {
        if (points.Count == 0)
        {
            return points;
        }

        var output = new List<(double X, double Y)>(points.Count + 4);
        var previous = points[^1];
        var previousInside = inside(previous, edge);
        foreach (var current in points)
        {
            var currentInside = inside(current, edge);
            if (currentInside)
            {
                if (!previousInside)
                {
                    output.Add(intersect(previous, current, edge));
                }

                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(intersect(previous, current, edge));
            }

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }

    static (double X, double Y) IntersectVertical((double X, double Y) a, (double X, double Y) b, double x)
    {
        var t = (x - a.X) / (b.X - a.X);
        return (x, a.Y + t * (b.Y - a.Y));
    }

    static (double X, double Y) IntersectHorizontal((double X, double Y) a, (double X, double Y) b, double y)
    {
        var t = (y - a.Y) / (b.Y - a.Y);
        return (a.X + t * (b.X - a.X), y);
    }
}
