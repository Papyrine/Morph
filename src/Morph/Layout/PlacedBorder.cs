/// <summary>
/// A paragraph-border box (<c>w:pBdr</c>): the four edges a painter may stroke around a paragraph's
/// content box, in points from the page's top-left. The box already includes each edge's space (the gap
/// Word leaves between the text and the border). One is emitted per paragraph that does not break across a
/// column or page; the between-border collapse of consecutive same-bordered paragraphs, and per-fragment
/// borders for a paragraph that does break, are later slices.
/// </summary>
sealed record PlacedBorder(
    float X,
    float Y,
    float Width,
    float Height,
    CellBorders Borders) : PlacedItem(X, Y, Width, Height);
