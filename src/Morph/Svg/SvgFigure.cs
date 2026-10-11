/// <summary>
/// One contour of an <see cref="SvgPath"/>: the point it starts at, the segments that follow, and
/// whether it runs back to its start. An open figure strokes without the closing segment and still
/// fills as though it were closed, as SVG has it.
/// </summary>
sealed record SvgFigure(double StartX, double StartY, IReadOnlyList<SvgSegment> Segments, bool Closed);
