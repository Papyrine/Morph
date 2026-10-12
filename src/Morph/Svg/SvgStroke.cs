/// <summary>
/// The outline an <see cref="SvgPath"/> is stroked with. <see cref="Width"/> is in the drawing's own
/// units, already scaled by whatever transforms stood over the shape, so a painter scales it once
/// more by its own mapping of the drawing onto the page and nothing else.
/// </summary>
sealed record SvgStroke(string ColorHex, double Alpha, double Width, SvgLineCap Cap, SvgLineJoin Join, double MiterLimit);
