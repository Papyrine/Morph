/// <summary>
/// One step of an <see cref="SvgFigure"/>: a straight line to (<see cref="X"/>, <see cref="Y"/>), or —
/// when <see cref="IsCurve"/> — a cubic Bézier to that point through the two control points. Those
/// are the only two kinds a reader hands on: horizontal and vertical lines, smooth and quadratic
/// curves, elliptical arcs and the basic shapes are all rewritten as these, so a painter needs only
/// the two calls every vector API has.
/// </summary>
readonly record struct SvgSegment(double Control1X, double Control1Y, double Control2X, double Control2Y, double X, double Y, bool IsCurve)
{
    public static SvgSegment Line(double x, double y) =>
        new(0, 0, 0, 0, x, y, false);

    public static SvgSegment Curve(double control1X, double control1Y, double control2X, double control2Y, double x, double y) =>
        new(control1X, control1Y, control2X, control2Y, x, y, true);
}
