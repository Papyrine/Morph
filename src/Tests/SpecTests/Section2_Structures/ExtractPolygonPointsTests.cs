using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

public class ExtractPolygonPointsTests
{
    static WPS.ShapeProperties ShapeWithPath(long pathW, long pathH, params OpenXmlElement[] segments)
    {
        var path = new A.Path
        {
            Width = (uint) pathW,
            Height = (uint) pathH
        };
        foreach (var seg in segments)
        {
            path.AppendChild(seg);
        }

        var custGeom = new A.CustomGeometry();
        custGeom.AppendChild(new A.PathList(path));

        var shapeProps = new WPS.ShapeProperties();
        shapeProps.AppendChild(custGeom);
        return shapeProps;
    }

    static A.MoveTo Move(long x, long y) =>
    [
        with(new A.Point
        {
            X = x.ToString(),
            Y = y.ToString()
        })
    ];

    static A.LineTo Line(long x, long y) =>
    [
        with(new A.Point
        {
            X = x.ToString(),
            Y = y.ToString()
        })
    ];

    static A.CubicBezierCurveTo Cubic(long c1x, long c1y, long c2x, long c2y, long ex, long ey)
    {
        var c = new A.CubicBezierCurveTo();
        c.AppendChild(new A.Point {X = c1x.ToString(), Y = c1y.ToString()});
        c.AppendChild(new A.Point {X = c2x.ToString(), Y = c2y.ToString()});
        c.AppendChild(new A.Point {X = ex.ToString(), Y = ey.ToString()});
        return c;
    }

    static A.QuadraticBezierCurveTo Quad(long c1x, long c1y, long ex, long ey)
    {
        var q = new A.QuadraticBezierCurveTo();
        q.AppendChild(new A.Point {X = c1x.ToString(), Y = c1y.ToString()});
        q.AppendChild(new A.Point {X = ex.ToString(), Y = ey.ToString()});
        return q;
    }

    [Test]
    public async Task LinePolygon_NormalizesToUnitSquare()
    {
        // Triangle: (0,0) → (100,0) → (50,200) → close
        var props = ShapeWithPath(100, 200, Move(0, 0), Line(100, 0), Line(50, 200));

        var pts = ShapeParser.ExtractPolygonPoints(props);

        await Assert.That(pts).IsNotNull();
        await Assert.That(pts!.Count).IsEqualTo(3);
        await Assert.That(pts[0]).IsEqualTo((0d, 0d));
        await Assert.That(pts[1]).IsEqualTo((1d, 0d));
        await Assert.That(pts[2]).IsEqualTo((0.5d, 1d));
    }

    [Test]
    public async Task CubicBezier_FlattensIntoMultipleSegments()
    {
        // Quarter-arc-ish from (0,1) up over to (1,0) using two control handles.
        // Path declares w=h=1000 so output coordinates land in the unit square.
        var props = ShapeWithPath(1000, 1000,
            Move(0, 1000),
            Cubic(0, 0, 1000, 0, 1000, 0));

        var pts = ShapeParser.ExtractPolygonPoints(props);

        await Assert.That(pts).IsNotNull();
        // 1 moveTo point + 12 flattened bezier points (segment count is the contract).
        await Assert.That(pts!.Count).IsEqualTo(13);

        // First point is the moveTo.
        await Assert.That(pts[0]).IsEqualTo((0d, 1d));
        // Last point lands on the bezier endpoint.
        await Assert.That(pts[^1]).IsEqualTo((1d, 0d));

        // Mid-curve sample should bow toward the (0,0) corner because both control points
        // sit there. A straight line from (0,1) to (1,0) would put the midpoint at y=0.5;
        // the curve pulling toward y=0 confirms the de Casteljau weights are applied.
        var mid = pts[6];
        await Assert.That(mid.X).IsEqualTo(0.5).Within(0.05);
        await Assert.That(mid.Y).IsLessThan(0.3);
    }

    [Test]
    public async Task QuadraticBezier_FlattensIntoMultipleSegments()
    {
        // Symmetric arch with control point above the chord.
        var props = ShapeWithPath(1000, 1000,
            Move(0, 1000),
            Quad(500, 0, 1000, 1000));

        var pts = ShapeParser.ExtractPolygonPoints(props);

        await Assert.That(pts).IsNotNull();
        await Assert.That(pts!.Count).IsEqualTo(13);
        await Assert.That(pts[^1]).IsEqualTo((1d, 1d));

        // Mid-curve sample should sit above the chord midpoint (smaller Y) — a sanity check
        // that the quadratic weights are applied, not a straight line from start to end.
        var mid = pts[6];
        await Assert.That(mid.X).IsEqualTo(0.5).Within(0.01);
        await Assert.That(mid.Y).IsLessThan(0.6);
    }

    [Test]
    public async Task ArcTo_FallsBackToBoundingRect()
    {
        // ArcTo isn't supported by the flattener — the parser should return null so callers
        // render the bounding rectangle instead.
        var path = new A.Path
        {
            Width = 1000U,
            Height = 1000U
        };
        path.AppendChild(Move(0, 0));
        path.AppendChild(new A.ArcTo
        {
            WidthRadius = "500",
            HeightRadius = "500",
            StartAngle = "0",
            SwingAngle = "5400000"
        });

        var custGeom = new A.CustomGeometry();
        custGeom.AppendChild(new A.PathList(path));
        var props = new WPS.ShapeProperties();
        props.AppendChild(custGeom);

        var pts = ShapeParser.ExtractPolygonPoints(props);

        await Assert.That(pts).IsNull();
    }

    [Test]
    public async Task MissingCustomGeometry_ReturnsNull()
    {
        var props = new WPS.ShapeProperties();

        var pts = ShapeParser.ExtractPolygonPoints(props);

        await Assert.That(pts).IsNull();
    }
}
