/// <summary>
/// Collects the figures of one shape as they are drawn, in the pen-moving terms path data is
/// written in. A figure that never drew anything — a move followed by another move — is dropped,
/// and a segment drawn straight after a close starts a new figure where the closed one began, as
/// SVG has it.
/// </summary>
sealed class SvgFigureBuilder
{
    // The 4/3·tan(π/8) that puts a cubic's control points where they best fit a quarter circle.
    const double quarterArc = 0.5522847498307936;

    readonly List<SvgFigure> figures = [];
    List<SvgSegment>? segments;
    double startX;
    double startY;

    public double X { get; private set; }

    public double Y { get; private set; }

    /// <summary>The segments drawn so far, across every figure.</summary>
    public int SegmentCount { get; private set; }

    public void MoveTo(double x, double y)
    {
        Flush(closed: false);
        startX = X = x;
        startY = Y = y;
    }

    public void LineTo(double x, double y)
    {
        Add(SvgSegment.Line(x, y));
        X = x;
        Y = y;
    }

    public void CurveTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
    {
        Add(SvgSegment.Curve(control1X, control1Y, control2X, control2Y, x, y));
        X = x;
        Y = y;
    }

    public void Close()
    {
        Flush(closed: true);
        X = startX;
        Y = startY;
    }

    /// <summary>
    /// A quadratic Bézier, raised to the cubic that traces the same curve: each control point two
    /// thirds of the way from its end towards the quadratic's one.
    /// </summary>
    public void QuadraticTo(double controlX, double controlY, double x, double y) =>
        CurveTo(
            X + 2.0 / 3 * (controlX - X),
            Y + 2.0 / 3 * (controlY - Y),
            x + 2.0 / 3 * (controlX - x),
            y + 2.0 / 3 * (controlY - y),
            x,
            y);

    /// <summary>
    /// An elliptical arc from the current point, in path data's endpoint form, as cubics of a
    /// quarter turn or less each. The conversion to a centre and two angles is the one the SVG
    /// specification gives in its implementation notes, including its two repairs: radii too
    /// small to reach the end point are scaled up until they do, and an arc with a zero radius is
    /// a straight line.
    /// </summary>
    public void ArcTo(double radiusX, double radiusY, double rotationDegrees, bool largeArc, bool sweep, double x, double y)
    {
        if (X == x && Y == y)
        {
            return;
        }

        radiusX = Math.Abs(radiusX);
        radiusY = Math.Abs(radiusY);
        if (radiusX == 0 || radiusY == 0)
        {
            LineTo(x, y);
            return;
        }

        var rotation = rotationDegrees * Math.PI / 180;
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);

        // The start point in the ellipse's own frame, with the midpoint of the chord at the origin.
        var halfX = (X - x) / 2;
        var halfY = (Y - y) / 2;
        var localX = cos * halfX + sin * halfY;
        var localY = -sin * halfX + cos * halfY;

        var reach = localX * localX / (radiusX * radiusX) + localY * localY / (radiusY * radiusY);
        if (reach > 1)
        {
            radiusX *= Math.Sqrt(reach);
            radiusY *= Math.Sqrt(reach);
        }

        var numerator = radiusX * radiusX * radiusY * radiusY - radiusX * radiusX * localY * localY - radiusY * radiusY * localX * localX;
        var denominator = radiusX * radiusX * localY * localY + radiusY * radiusY * localX * localX;
        var factor = Math.Sqrt(Math.Max(0, numerator / denominator));
        if (largeArc == sweep)
        {
            factor = -factor;
        }

        var localCentreX = factor * radiusX * localY / radiusY;
        var localCentreY = -factor * radiusY * localX / radiusX;
        var centreX = cos * localCentreX - sin * localCentreY + (X + x) / 2;
        var centreY = sin * localCentreX + cos * localCentreY + (Y + y) / 2;

        var startAngle = Math.Atan2((localY - localCentreY) / radiusY, (localX - localCentreX) / radiusX);
        var endAngle = Math.Atan2((-localY - localCentreY) / radiusY, (-localX - localCentreX) / radiusX);
        var turn = endAngle - startAngle;
        if (sweep && turn < 0)
        {
            turn += 2 * Math.PI;
        }
        else if (!sweep && turn > 0)
        {
            turn -= 2 * Math.PI;
        }

        var pieces = Math.Max(1, (int) Math.Ceiling(Math.Abs(turn) / (Math.PI / 2) - 1e-9));
        var step = turn / pieces;
        var handle = 4.0 / 3 * Math.Tan(step / 4);
        for (var piece = 0; piece < pieces; piece++)
        {
            var from = startAngle + piece * step;
            var to = from + step;
            var (control1X, control1Y) = OnEllipse(Math.Cos(from) - handle * Math.Sin(from), Math.Sin(from) + handle * Math.Cos(from));
            var (control2X, control2Y) = OnEllipse(Math.Cos(to) + handle * Math.Sin(to), Math.Sin(to) - handle * Math.Cos(to));

            // The last piece ends on the point the data names, not on where the arithmetic lands.
            var (endX, endY) = piece == pieces - 1 ? (x, y) : OnEllipse(Math.Cos(to), Math.Sin(to));
            CurveTo(control1X, control1Y, control2X, control2Y, endX, endY);
        }

        (double X, double Y) OnEllipse(double unitX, double unitY) =>
            (centreX + radiusX * unitX * cos - radiusY * unitY * sin, centreY + radiusX * unitX * sin + radiusY * unitY * cos);
    }

    /// <summary>A whole ellipse as one closed figure of four quarter-turn cubics.</summary>
    public void Ellipse(double centreX, double centreY, double radiusX, double radiusY)
    {
        var handleX = radiusX * quarterArc;
        var handleY = radiusY * quarterArc;
        MoveTo(centreX + radiusX, centreY);
        CurveTo(centreX + radiusX, centreY + handleY, centreX + handleX, centreY + radiusY, centreX, centreY + radiusY);
        CurveTo(centreX - handleX, centreY + radiusY, centreX - radiusX, centreY + handleY, centreX - radiusX, centreY);
        CurveTo(centreX - radiusX, centreY - handleY, centreX - handleX, centreY - radiusY, centreX, centreY - radiusY);
        CurveTo(centreX + handleX, centreY - radiusY, centreX + radiusX, centreY - handleY, centreX + radiusX, centreY);
        Close();
    }

    /// <summary>
    /// A rectangle as one closed figure, its corners rounded by <paramref name="radiusX"/> and
    /// <paramref name="radiusY"/> when both are above zero.
    /// </summary>
    public void Rectangle(double x, double y, double width, double height, double radiusX, double radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            MoveTo(x, y);
            LineTo(x + width, y);
            LineTo(x + width, y + height);
            LineTo(x, y + height);
            Close();
            return;
        }

        var handleX = radiusX * (1 - quarterArc);
        var handleY = radiusY * (1 - quarterArc);
        var right = x + width;
        var bottom = y + height;
        MoveTo(x + radiusX, y);
        LineTo(right - radiusX, y);
        CurveTo(right - handleX, y, right, y + handleY, right, y + radiusY);
        LineTo(right, bottom - radiusY);
        CurveTo(right, bottom - handleY, right - handleX, bottom, right - radiusX, bottom);
        LineTo(x + radiusX, bottom);
        CurveTo(x + handleX, bottom, x, bottom - handleY, x, bottom - radiusY);
        LineTo(x, y + radiusY);
        CurveTo(x, y + handleY, x + handleX, y, x + radiusX, y);
        Close();
    }

    public List<SvgFigure> Build()
    {
        Flush(closed: false);
        return figures;
    }

    void Add(SvgSegment segment)
    {
        segments ??= [];
        segments.Add(segment);
        SegmentCount++;
    }

    void Flush(bool closed)
    {
        if (segments != null)
        {
            figures.Add(new(startX, startY, segments, closed));
            segments = null;
        }
    }
}
