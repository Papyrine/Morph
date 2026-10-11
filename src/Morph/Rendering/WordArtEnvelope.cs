/// <summary>
/// The geometry of the DrawingML envelope text warps (<c>a:prstTxWarp</c>), shared by the Skia and
/// ImageSharp drawers.
/// </summary>
/// <remarks>
/// <para>
/// An envelope warp is two guide paths, a top and a bottom, drawn in the shape's text rect by the
/// formulas of the format's <c>presetTextWarpDefinitions</c>. Word stretches the text between them:
/// the INK box of the laid-out line — its tightest bounding box, whatever the font size, and less
/// any leading or trailing spaces — is mapped onto the envelope, a point at the fraction
/// <c>u</c> across the ink and <c>v</c> down it landing <c>v</c> of the way from the top path to the
/// bottom path at the fraction <c>u</c> of the rect's width. So a line of capitals, a line of
/// lower-case x and a line of descenders all fill the same envelope edge to edge.
/// </para>
/// <para>
/// Word-probed 2026-10-11 (<c>_probe_warp_a</c> / <c>_probe_warp_b</c>: 400x100pt boxes with a
/// hairline outline, black Arial under each preset, ink read against the box per column). Under
/// <c>textPlain</c> "HHHH", "xxxx", "gggg" and "Hxgy" each span 0.001..1.001 of the width and
/// 0.005..1.003 of the height, at 36pt and at 12pt alike, and with the default insets the same
/// text spans 0.019..0.983 by 0.038..0.965 — the text rect. Read at every eighth of the width,
/// the top and bottom edges of a row of capitals follow the formulas below to within the 1%
/// the sampling window allows, for slant, triangle, wave, inflate, deflate and both cans (whose
/// arcs are the ellipse of the definition taken by x, not by arc length).
/// </para>
/// <para>
/// Pure arithmetic with no backend types in the signatures, which is what lets both drawers call
/// it: the scenario suite compares them independently against the same Word reference, so a
/// constant that drifted in one copy would read as a rasterisation difference.
/// </para>
/// </remarks>
static class WordArtEnvelope
{
    /// <summary>How finely an edge that runs the whole width of the ink is cut before it is bent.</summary>
    const int piecesAcross = 256;

    /// <summary>Whether <paramref name="transform"/> is one of the envelope warps this class maps.</summary>
    public static bool Covers(WordArtTransform transform) =>
        transform is WordArtTransform.Plain or WordArtTransform.Stop
            or WordArtTransform.Triangle or WordArtTransform.TriangleInverted
            or WordArtTransform.ChevronUp or WordArtTransform.ChevronDown
            or WordArtTransform.Wave or WordArtTransform.WaveInverted
            or WordArtTransform.DoubleWave or WordArtTransform.DoubleWaveInverted
            or WordArtTransform.Inflate or WordArtTransform.Deflate
            or WordArtTransform.InflateTop or WordArtTransform.InflateBottom
            or WordArtTransform.DeflateTop or WordArtTransform.DeflateBottom
            or WordArtTransform.CanUp or WordArtTransform.CanDown
            or WordArtTransform.FadeRight or WordArtTransform.FadeLeft
            or WordArtTransform.FadeUp or WordArtTransform.FadeDown
            or WordArtTransform.SlantUp or WordArtTransform.SlantDown
            or WordArtTransform.CurveUp or WordArtTransform.CurveDown
            or WordArtTransform.CascadeUp or WordArtTransform.CascadeDown;

    /// <summary>
    /// Bends the outline of a line of text into <paramref name="transform"/>'s envelope, drawn in the
    /// rect at (<paramref name="x"/>, <paramref name="y"/>). The contours are closed polylines in any
    /// one coordinate space — only their place within their own bounding box matters. Returns nothing
    /// for an outline with no extent.
    /// </summary>
    public static List<List<(float X, float Y)>> Warp(
        IReadOnlyList<IReadOnlyList<(float X, float Y)>> contours,
        WordArtTransform transform,
        double? adjust,
        float x, float y, float width, float height)
    {
        var warped = new List<List<(float X, float Y)>>(contours.Count);
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach (var contour in contours)
        {
            foreach (var point in contour)
            {
                left = Math.Min(left, point.X);
                right = Math.Max(right, point.X);
                top = Math.Min(top, point.Y);
                bottom = Math.Max(bottom, point.Y);
            }
        }

        var inkWidth = right - left;
        var inkHeight = bottom - top;
        if (inkWidth <= 0 || inkHeight <= 0 || width <= 0 || height <= 0)
        {
            return warped;
        }

        var shortSide = Math.Min(width, height) / (double) height;

        (float X, float Y) Map((float X, float Y) point)
        {
            var across = (point.X - left) / (double) inkWidth;
            var down = (point.Y - top) / (double) inkHeight;
            var (mappedX, mappedY) = Point(across, down, transform, adjust, shortSide);
            return ((float) (x + mappedX * width), (float) (y + mappedY * height));
        }

        foreach (var contour in contours)
        {
            if (contour.Count == 0)
            {
                continue;
            }

            var bent = new List<(float X, float Y)>(contour.Count * 2);
            for (var i = 0; i < contour.Count; i++)
            {
                var from = contour[i];
                var to = contour[(i + 1) % contour.Count];
                bent.Add(Map(from));

                // An edge that travels across the text is bent by the envelope; one that stands
                // upright stays straight, since a column of the ink maps to a straight line.
                var pieces = (int) Math.Ceiling(Math.Abs(to.X - from.X) / inkWidth * piecesAcross);
                for (var piece = 1; piece < pieces; piece++)
                {
                    var along = (float) piece / pieces;
                    bent.Add(Map((from.X + (to.X - from.X) * along, from.Y + (to.Y - from.Y) * along)));
                }
            }

            warped.Add(bent);
        }

        return warped;
    }

    /// <summary>
    /// Where the point <paramref name="across"/> of the way along the ink and <paramref name="down"/>
    /// of the way down it lands, as fractions of the text rect's width and height.
    /// <paramref name="shortSide"/> is the rect's shorter side over its height, which the deflating
    /// warps measure their depth in.
    /// </summary>
    public static (double X, double Y) Point(double across, double down, WordArtTransform transform, double? adjust, double shortSide)
    {
        var (topX, topY, bottomX, bottomY) = At(across, transform, adjust, shortSide);
        return (topX + (bottomX - topX) * down, topY + (bottomY - topY) * down);
    }

    /// <summary>
    /// The envelope's top and bottom guide points at the fraction <paramref name="across"/> of the
    /// text rect, as fractions of its width and height. Each case is the preset's own formula, with
    /// its adjust value pinned to the range the definition gives it.
    /// </summary>
    static (double TopX, double TopY, double BottomX, double BottomY) At(double across, WordArtTransform transform, double? adjust, double shortSide)
    {
        double Adjust(double fallback, double minimum, double maximum) =>
            Math.Clamp(adjust ?? fallback, minimum, maximum);

        // 0 at the rect's two ends and 1 at its middle, in a straight line.
        var tent = 1 - Math.Abs(1 - 2 * across);
        // The same, on the half ellipse the can warps arc along.
        var arc = Math.Sqrt(Math.Max(0, 1 - (2 * across - 1) * (2 * across - 1)));
        // A quadratic or symmetric cubic from end to end: 0 at the ends, 1 at the middle.
        var bow = 4 * across * (1 - across);

        double topY = 0;
        double bottomY = 1;
        var topX = across;
        var bottomX = across;
        switch (transform)
        {
            case WordArtTransform.Stop:
            {
                var depth = Adjust(0.25, 0.14286, 0.5);
                var corner = depth * Math.Max(0, Math.Max(1 - 3 * across, 3 * across - 2));
                topY = corner;
                bottomY = 1 - corner;
                break;
            }
            case WordArtTransform.Triangle:
                topY = Adjust(0.5, 0, 1) * (1 - tent);
                break;
            case WordArtTransform.TriangleInverted:
            {
                var edge = Adjust(0.5, 0, 1);
                bottomY = edge + (1 - edge) * tent;
                break;
            }
            case WordArtTransform.ChevronUp:
            {
                var rise = Adjust(0.25, 0, 0.5);
                topY = rise * (1 - tent);
                bottomY = 1 - rise * tent;
                break;
            }
            case WordArtTransform.ChevronDown:
            {
                var edge = Adjust(0.75, 0.5, 1);
                topY = (1 - edge) * tent;
                bottomY = edge + (1 - edge) * tent;
                break;
            }
            case WordArtTransform.Wave:
            case WordArtTransform.WaveInverted:
            {
                var edge = Adjust(0.125, 0, 0.2);
                var swing = Swing(across) * edge * 10 / 3;
                topY = edge + (transform == WordArtTransform.Wave ? swing : -swing);
                bottomY = topY + 1 - 2 * edge;
                break;
            }
            case WordArtTransform.DoubleWave:
            case WordArtTransform.DoubleWaveInverted:
            {
                var edge = Adjust(0.0625, 0, 0.125);
                var half = across < 0.5 ? across * 2 : across * 2 - 1;
                var swing = Swing(half) * edge * 10 / 3;
                topY = edge + (transform == WordArtTransform.DoubleWave ? swing : -swing);
                bottomY = topY + 1 - 2 * edge;
                break;
            }
            case WordArtTransform.Inflate:
            {
                var depth = Adjust(0.1875, 0, 0.2);
                topY = depth * (1 - bow);
                bottomY = 1 - topY;
                break;
            }
            case WordArtTransform.Deflate:
            {
                var depth = Adjust(0.1875, 0, 0.375) * shortSide;
                topY = depth * bow;
                bottomY = 1 - topY;
                break;
            }
            case WordArtTransform.InflateTop:
                topY = Adjust(0.4, 0, 0.5) * (1 - bow / 2);
                break;
            case WordArtTransform.InflateBottom:
            {
                var edge = Adjust(0.6, 0.6, 1);
                bottomY = edge + (1 - edge) * bow / 2;
                break;
            }
            case WordArtTransform.DeflateTop:
                topY = Adjust(0.5, 0, 0.9375) * bow;
                break;
            case WordArtTransform.DeflateBottom:
                bottomY = 1 - (1 - Adjust(0.5, 0.0625, 1) * shortSide) * bow;
                break;
            case WordArtTransform.CanUp:
            {
                var depth = 1 - Adjust(0.85714, 0.66667, 1);
                topY = depth * (1 - arc);
                bottomY = 1 - depth * arc;
                break;
            }
            case WordArtTransform.CanDown:
            {
                var depth = Adjust(0.14286, 0, 0.33333);
                topY = depth * arc;
                bottomY = 1 - depth + depth * arc;
                break;
            }
            case WordArtTransform.FadeRight:
            {
                var depth = Adjust(0.33333, 0, 0.49999);
                topY = depth * across;
                bottomY = 1 - topY;
                break;
            }
            case WordArtTransform.FadeLeft:
            {
                var depth = Adjust(0.33333, 0, 0.49999);
                topY = depth * (1 - across);
                bottomY = 1 - topY;
                break;
            }
            case WordArtTransform.FadeUp:
            {
                var inset = Adjust(0.33333, 0, 0.49999);
                topX = inset + across * (1 - 2 * inset);
                break;
            }
            case WordArtTransform.FadeDown:
            {
                var inset = Adjust(0.33333, 0, 0.49999);
                bottomX = inset + across * (1 - 2 * inset);
                break;
            }
            case WordArtTransform.SlantUp:
            {
                var rise = Adjust(0.55555, 0, 0.71431);
                topY = rise * (1 - across);
                bottomY = 1 - rise * across;
                break;
            }
            case WordArtTransform.SlantDown:
            {
                var edge = Adjust(0.44445, 0.28569, 1);
                topY = (1 - edge) * across;
                bottomY = edge + (1 - edge) * across;
                break;
            }
            case WordArtTransform.CurveUp:
            case WordArtTransform.CurveDown:
            {
                var depth = Adjust(0.45977, 0, 0.56338);
                topY = transform == WordArtTransform.CurveUp
                    ? Cubic(depth, depth * 1.25, depth * 0.75, 0, across)
                    : Cubic(0, depth * 0.75, depth * 1.25, depth, across);
                bottomY = Cubic(1 - depth * 0.375, 1 + depth * 0.125, 1 + depth * 0.125, 1 - depth * 0.375, across);
                break;
            }
            case WordArtTransform.CascadeUp:
            {
                var edge = Adjust(0.44444, 0.2857, 1);
                topY = (1 - edge) / 4 * (1 - across);
                bottomY = 1 - (1 - edge) * across;
                break;
            }
            case WordArtTransform.CascadeDown:
            {
                var edge = Adjust(0.44444, 0.2857, 1);
                topY = (1 - edge) / 4 * across;
                bottomY = edge + (1 - edge) * across;
                break;
            }
        }

        return (topX, topY, bottomX, bottomY);
    }

    /// <summary>
    /// The wave's cubic about its own ends: control points one unit up at a third of the way and one
    /// unit down at two thirds, so it rises first and dips second, by 0.289 of a unit at most.
    /// </summary>
    static double Swing(double along) =>
        3 * along * (1 - along) * (2 * along - 1);

    static double Cubic(double start, double first, double second, double end, double along)
    {
        var rest = 1 - along;
        return rest * rest * rest * start
               + 3 * rest * rest * along * first
               + 3 * rest * along * along * second
               + along * along * along * end;
    }
}
