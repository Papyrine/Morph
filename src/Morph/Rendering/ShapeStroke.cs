/// <summary>
/// The width a shape's outline (<c>a:ln/@w</c>) is stroked at, shared by the three painters.
/// </summary>
static class ShapeStroke
{
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
