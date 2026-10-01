/// <summary>
/// The geometry of Word's <c>textArchUp</c> / <c>textArchDown</c> warps, shared by the Skia and
/// ImageSharp drawers so the two place every glyph identically.
/// </summary>
/// <remarks>
/// <para>
/// Settled by Word probes read through the XPS (<c>_probe_arch</c>, 2026-10-02: 32 boxes varying the
/// box width and height, the font size, the face — Impact and Arial — the text length and the
/// alignment), whose <c>Glyphs</c> carry each glyph's em size, baseline origin and rotation:
/// </para>
/// <list type="bullet">
/// <item>The text is drawn SMALLER than declared, by <see cref="Scale"/>: the text rect width over
/// that width plus twice the text's ink height. 36pt Impact in a 432pt box is drawn at 31.25pt;
/// within 0.05pt of every probe. The box height plays no part — a 54pt, 108pt and 216pt box draw the
/// same size.</item>
/// <item>The path is the half ellipse inscribed in the text rect (the box less its <c>bodyPr</c>
/// insets), scaled by that same factor about the rect's centre — the upper half for an arch up, the
/// lower for an arch down, both read left to right.</item>
/// <item>The paragraph is laid along the path by arc length, as if the path were its line: centred
/// text sits about the apex, left-aligned text starts at the path's left end.</item>
/// <item>Each glyph is upright to the path's tangent at its centre, its baseline
/// <see cref="BaselineDepthEm"/> below the path in the glyph's own frame — inside the arch for an
/// arch up, outside it for an arch down. That depth is the font's <c>usWinAscent − sTypoAscender</c>:
/// 0.218 em on Impact (fitted 0.223), 0.177 em on Arial Bold (fitted 0.178).</item>
/// </list>
/// <para>
/// The glyph tops therefore stand above the box on an arch up — Word draws 36pt Impact 7.5pt above a
/// 432x108 box — which is why the raster page carries padding. The earlier chord-sagitta circle fitted
/// to the text width drew the text at its declared size on a far tighter curve, its baseline on the
/// box top.
/// </para>
/// </remarks>
static class WordArtArch
{
    /// <summary>
    /// The factor Word draws the arched text at, against its declared size: the text rect width over
    /// that width plus twice the ink height (both in the same unit, the ink height at the declared size).
    /// </summary>
    public static float Scale(float textRectWidth, float inkHeight) =>
        textRectWidth > 0 && inkHeight > 0 ? textRectWidth / (textRectWidth + 2 * inkHeight) : 1;

    /// <summary>
    /// How far the baseline sits below the path, as a fraction of the drawn em: the font's
    /// <c>usWinAscent − sTypoAscender</c>. Zero for a font that declares no <c>OS/2</c> table, which
    /// leaves the baseline on the path.
    /// </summary>
    public static float BaselineDepthEm(FontMetrics? metrics) =>
        metrics is {WinAscent: > 0, TypoAscender: > 0, UnitsPerEm: > 0}
            ? (float) (metrics.WinAscent - metrics.TypoAscender) / metrics.UnitsPerEm
            : 0;

    /// <summary>
    /// The text split into the units each placed separately — text elements, so a surrogate pair or a
    /// base letter with its combining marks turns as one.
    /// </summary>
    public static IReadOnlyList<string> Glyphs(string text)
    {
        var glyphs = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            glyphs.Add(enumerator.GetTextElement());
        }

        return glyphs;
    }

    /// <summary>
    /// The baseline origin and rotation of each glyph, for glyph advances already at the drawn size.
    /// The text rect is given in the drawing unit (left, top, width, height); <paramref name="scale"/>
    /// is <see cref="Scale"/> and <paramref name="baselineDepth"/> is <see cref="BaselineDepthEm"/>
    /// times the drawn em. The angle is in degrees, clockwise in a y-down space.
    /// </summary>
    public static IReadOnlyList<(float X, float Y, float AngleDegrees)> Place(
        IReadOnlyList<float> advances,
        float left, float top, float width, float height,
        float scale,
        float baselineDepth,
        TextAlignment alignment,
        bool down)
    {
        var semiWidth = scale * width / 2;
        var semiHeight = scale * height / 2;
        var centreX = left + width / 2;
        var centreY = top + height / 2;

        // The parametric angle runs from the left end (pi) to the right end: through the top
        // (pi -> 0) for an arch up, through the bottom (pi -> 2pi) for an arch down.
        const int segments = 720;
        var angles = new double[segments + 1];
        var lengths = new double[segments + 1];
        (double X, double Y) Point(double angle) =>
            (centreX + semiWidth * Math.Cos(angle), centreY - semiHeight * Math.Sin(angle));

        var previous = Point(Math.PI);
        angles[0] = Math.PI;
        for (var i = 1; i <= segments; i++)
        {
            var fraction = (double) i / segments;
            angles[i] = down ? Math.PI + Math.PI * fraction : Math.PI - Math.PI * fraction;
            var point = Point(angles[i]);
            lengths[i] = lengths[i - 1] + Math.Sqrt((point.X - previous.X) * (point.X - previous.X) + (point.Y - previous.Y) * (point.Y - previous.Y));
            previous = point;
        }

        var pathLength = lengths[segments];
        var textLength = advances.Sum();
        var start = alignment switch
        {
            TextAlignment.Center => (pathLength - textLength) / 2,
            TextAlignment.Right => pathLength - textLength,
            _ => 0
        };

        var placements = new List<(float X, float Y, float AngleDegrees)>(advances.Count);
        var cursor = start;
        foreach (var advance in advances)
        {
            var (pointX, pointY, tangentX, tangentY) = At(cursor + advance / 2);
            // The glyph's own down: the tangent turned a quarter clockwise in y-down space.
            var baselineX = pointX - tangentY * baselineDepth;
            var baselineY = pointY + tangentX * baselineDepth;
            placements.Add((
                (float) (baselineX - tangentX * advance / 2),
                (float) (baselineY - tangentY * advance / 2),
                (float) (Math.Atan2(tangentY, tangentX) * 180 / Math.PI)));
            cursor += advance;
        }

        return placements;

        // The point at an arc length along the path and the unit direction of travel there. Text that
        // overruns the path carries on along the end tangent.
        (double X, double Y, double TangentX, double TangentY) At(double length)
        {
            var clamped = Math.Clamp(length, 0, pathLength);
            var index = Array.BinarySearch(lengths, clamped);
            if (index < 0)
            {
                index = ~index;
            }

            index = Math.Clamp(index, 1, segments);
            var span = lengths[index] - lengths[index - 1];
            var blend = span > 0 ? (clamped - lengths[index - 1]) / span : 0;
            var angle = angles[index - 1] + (angles[index] - angles[index - 1]) * blend;
            var (pointX, pointY) = Point(angle);

            // d/d(angle) of the point is (-a sin, -b cos); travel runs with the angle on an arch down
            // and against it on an arch up.
            var direction = down ? 1 : -1;
            var tangentX = -semiWidth * Math.Sin(angle) * direction;
            var tangentY = -semiHeight * Math.Cos(angle) * direction;
            var magnitude = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
            if (magnitude > 0)
            {
                tangentX /= magnitude;
                tangentY /= magnitude;
            }
            else
            {
                (tangentX, tangentY) = (1, 0);
            }

            var overrun = length - clamped;
            return (pointX + tangentX * overrun, pointY + tangentY * overrun, tangentX, tangentY);
        }
    }
}
