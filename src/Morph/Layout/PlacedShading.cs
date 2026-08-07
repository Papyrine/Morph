/// <summary>
/// A paragraph-shading rectangle (<c>w:shd</c> on a paragraph): a solid fill spanning the paragraph's
/// column box behind one of its lines, in points from the page's top-left. One is emitted per line so the
/// band tiles continuously and splits naturally where the paragraph breaks across a column or page. A
/// painter draws it before the line's text. Run-level highlight is separate (it sits on the run).
/// </summary>
sealed record PlacedShading(
    float X,
    float Y,
    float Width,
    float Height,
    string ColorHex) :
    PlacedItem(X, Y, Width, Height);
