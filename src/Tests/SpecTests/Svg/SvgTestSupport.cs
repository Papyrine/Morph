/// <summary>
/// Shared by the SVG reader's tests: reading markup, and writing figures back out in a form short
/// enough to assert whole — <c>M0,0 L10,0 C1,2 3,4 5,6 Z</c>, every curve already a cubic.
/// </summary>
static class SvgTestSupport
{
    public const string Namespace = "xmlns=\"http://www.w3.org/2000/svg\"";

    public static SvgReadResult Read(string markup) =>
        SvgReader.Read(Encoding.UTF8.GetBytes(markup));

    /// <summary>The drawing of a document the reader is expected to take.</summary>
    public static SvgDrawing Draw(string markup)
    {
        var result = Read(markup);
        return result.Drawing ?? throw new($"The reader gave the document up: {result.Unsupported}");
    }

    /// <summary>A 100 by 100 document around some content.</summary>
    public static SvgDrawing DrawBody(string body) =>
        Draw($"<svg {Namespace} viewBox=\"0 0 100 100\">{body}</svg>");

    /// <summary>What the reader names as beyond it in a 100 by 100 document around some content.</summary>
    public static string? Unsupported(string body) =>
        Read($"<svg {Namespace} viewBox=\"0 0 100 100\">{body}</svg>").Unsupported;

    public static List<SvgFigure> Path(string data)
    {
        var builder = new SvgFigureBuilder();
        if (!SvgPathData.TryParse(data, builder))
        {
            throw new($"Path data did not parse: {data}");
        }

        return builder.Build();
    }

    public static string Describe(IEnumerable<SvgFigure> figures) =>
        string.Join(
            ' ',
            figures.Select(_ =>
                $"M{Number(_.StartX)},{Number(_.StartY)}" +
                string.Concat(_.Segments.Select(segment => segment.IsCurve
                    ? $" C{Number(segment.Control1X)},{Number(segment.Control1Y)} {Number(segment.Control2X)},{Number(segment.Control2Y)} {Number(segment.X)},{Number(segment.Y)}"
                    : $" L{Number(segment.X)},{Number(segment.Y)}")) +
                (_.Closed ? " Z" : "")));

    // Three decimals, and no negative zero to trip a comparison up.
    static string Number(double value) =>
        (Math.Round(value, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture);
}
