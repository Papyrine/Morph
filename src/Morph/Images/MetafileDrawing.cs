/// <summary>
/// What a Windows metafile (EMF or WMF) draws, read into shapes and text in the unit square of the
/// picture's frame: x from its left edge to its right, y from its top to its bottom. A text's size and a
/// line's width are fractions of the frame's height, so the drawing scales with whatever box it is put in.
///
/// It is what <see cref="MetafileReader"/> makes of a legacy OLE object's preview — PowerPoint keeps an
/// MS Graph chart only as such a picture — and is drawn by turning each item into an ordinary floating
/// shape or text box (<c>SlideShapeParser.ParseOle</c>), so no painter needs to know about metafiles.
/// </summary>
sealed record MetafileDrawing(IReadOnlyList<MetafileItem> Items);

abstract record MetafileItem;

/// <summary>
/// One or more figures drawn together (a polygon, a polyline, a rectangle, a polypolygon's parts): each
/// figure a list of points, filled when <see cref="FillHex"/> is set (figures closed, nonzero winding)
/// and stroked when <see cref="LineHex"/> is.
/// </summary>
sealed record MetafilePath(IReadOnlyList<IReadOnlyList<(double X, double Y)>> Figures, bool Closed, string? FillHex, string? LineHex, double LineWidth) : MetafileItem;

/// <summary>
/// A run of text drawn at a reference point, aligned to it as <c>SetTextAlign</c> said: horizontally
/// left, centre or right, vertically top, baseline or bottom. <see cref="Degrees"/> turns it
/// anticlockwise about the point (the font's escapement).
/// </summary>
sealed record MetafileText(
    string Text,
    double X,
    double Y,
    string FontFamily,
    double Size,
    bool Bold,
    bool Italic,
    bool Underline,
    string ColorHex,
    MetafileTextAlign Horizontal,
    MetafileTextBaseline Vertical,
    double Degrees) : MetafileItem;

enum MetafileTextAlign
{
    Left,
    Centre,
    Right
}

enum MetafileTextBaseline
{
    Top,
    Baseline,
    Bottom
}
