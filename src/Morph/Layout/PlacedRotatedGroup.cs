/// <summary>
/// A run of placed items laid out in an UNROTATED box (<see cref="PlacedItem.X"/>, <see cref="PlacedItem.Y"/>,
/// <see cref="PlacedItem.Width"/>, <see cref="PlacedItem.Height"/> — the box the items were placed in) that a
/// painter draws rotated by <see cref="RotationDegrees"/> about that box's centre. A table cell with
/// <c>w:textDirection</c> lays its content out in a box of the cell's height by its width, centred on the
/// cell, and rotates it into place (−90 for <c>btLr</c>, reading bottom to top; +90 for <c>tbRl</c>); a
/// floating text box with an <c>a:xfrm</c> rotation lays out in its own box and rotates with its chrome.
/// The cell direction was an engine-flip orphan — parsed onto <see cref="TableCellProperties.TextDirection"/>
/// and drawn by the deleted production renderers, never by the engine — and a text box's rotation only
/// reached its chrome, so labels/06's "ADMIT ONE" stubs lay flat across their ticket seams.
/// </summary>
sealed record PlacedRotatedGroup(
    float X,
    float Y,
    float Width,
    float Height,
    IReadOnlyList<PlacedItem> Items,
    double RotationDegrees) : PlacedItem(X, Y, Width, Height);
