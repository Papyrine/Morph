/// <summary>
/// Placed items laid out on a page of their own, drawn shrunk by <see cref="Scale"/> with that page's
/// top-left at (<see cref="PlacedItem.X"/>, <see cref="PlacedItem.Y"/>): a point (x, y) of the items lands
/// at (X + x × Scale, Y + y × Scale). <see cref="PlacedItem.Width"/> and <see cref="PlacedItem.Height"/>
/// are the shrunk page's. Line widths, font sizes and everything else scale with it.
///
/// <para>This is how a page is drawn with its comment markup (<see cref="CommentMarkup"/>): Word lays the
/// page out at its own size, puts the markup pane beside it, and prints the two shrunk to the paper.</para>
/// </summary>
sealed record PlacedScaledGroup(
    float X,
    float Y,
    float Width,
    float Height,
    IReadOnlyList<PlacedItem> Items,
    float Scale) : PlacedItem(X, Y, Width, Height);
