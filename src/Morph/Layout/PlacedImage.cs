/// <summary>
/// One image placed on a page: its box in points from the page's top-left and the drawable
/// <see cref="Data"/> (raster bytes; an SVG's raster fallback). An inline image is carried on its
/// <see cref="PlacedLine"/> with its bottom on the text baseline; page/margin-anchored floating images
/// (a later slice) would sit directly on the page. A painter decodes the bytes and draws them into the
/// box. Rotation, flip and crop are later slices.
/// </summary>
sealed record PlacedImage(
    float X,
    float Y,
    float Width,
    float Height,
    byte[] Data) : PlacedItem(X, Y, Width, Height);
