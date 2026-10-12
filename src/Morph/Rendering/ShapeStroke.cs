/// <summary>
/// The width a shape's outline (<c>a:ln/@w</c>) is stroked at, shared by the three painters.
/// </summary>
static class ShapeStroke
{
    /// <summary>
    /// A preset dash as lengths in the painter's own units: the pattern is declared in multiples of the
    /// line width (<see cref="FloatingShapeElement.LineDashPattern"/>), so each length is that many
    /// stroke widths. Null for a solid stroke, or one too thin to dash.
    /// </summary>
    public static float[]? DashLengths(IReadOnlyList<double>? pattern, double strokeWidth)
    {
        if (pattern is not {Count: >= 2} || strokeWidth <= 0)
        {
            return null;
        }

        var lengths = new float[pattern.Count];
        for (var index = 0; index < pattern.Count; index++)
        {
            lengths[index] = (float) (pattern[index] * strokeWidth);
        }

        return lengths;
    }

    /// <summary>The width of an outline that declares none: DrawingML's default line, 9525 EMU.</summary>
    const double defaultPoints = 0.75;

    /// <summary>
    /// The width of an outline that declares zero. DrawingML reads that as the thinnest line the
    /// device draws rather than as no line; half a point is a pixel at 150 dpi.
    /// </summary>
    const double hairlinePoints = 0.5;

    /// <summary>
    /// The stroke width in points for a declared <paramref name="lineWidthPoints"/>: the width
    /// itself however thin, the hairline for a declared zero, the default for none. A width under
    /// half a point used to be raised to half a point, which drew a quarter-point accent rule at
    /// twice Word's ink (menus/07's frame, 75 outlines across the corpus) and a 0.34pt one at half
    /// as much again (200 outlines).
    /// </summary>
    public static double WidthPoints(double? lineWidthPoints) =>
        lineWidthPoints switch
        {
            null => defaultPoints,
            <= 0 => hairlinePoints,
            _ => lineWidthPoints.Value
        };
}
