/// <summary>
/// One floating shape placed on a page: its resolved box in points from the page's top-left, and the
/// source <see cref="FloatingShapeElement"/> a painter reads for geometry (preset or freeform subpaths),
/// solid fill, outline, rotation and flip. Behind-text cell floats (a label template's coloured
/// background rectangle and freeform blobs) are the first users; page/margin-anchored body shapes,
/// gradient and image fills, and clipping are later slices.
/// </summary>
sealed record PlacedShape(
    float X,
    float Y,
    float Width,
    float Height,
    FloatingShapeElement Shape) : PlacedItem(X, Y, Width, Height);
