/// <summary>
/// One image placed on a page: its box in points from the page's top-left and the drawable
/// <see cref="Data"/> (raster bytes; an SVG's raster fallback). An inline image is carried on its
/// <see cref="PlacedLine"/> with its bottom on the text baseline; page/margin-anchored floating images sit
/// directly on the page. A painter decodes the bytes and draws them into the box, applying the DrawingML
/// transforms a floating image can carry: rotation and flip about the box centre, a source-rectangle
/// <see cref="Crop"/>, and an ellipse or freeform clip. The transform fields default to none, so a plain
/// image (a header background, most inline images) constructs with the five-argument form.
///
/// An inline <see cref="ShapeGroup"/> (a grouped drawing embedded in a run) rides the same inline-image
/// carrier: <see cref="Data"/> is then null and the painter draws the group's child shapes scaled into the
/// box instead of decoding bytes. Floating images never set it.
/// </summary>
sealed record PlacedImage(
    float X,
    float Y,
    float Width,
    float Height,
    byte[]? Data,
    double RotationDegrees = 0,
    bool FlipHorizontal = false,
    bool FlipVertical = false,
    bool ClipToEllipse = false,
    IReadOnlyList<IReadOnlyList<(double X, double Y)>>? ClipSubpaths = null,
    ImageCrop? Crop = null,
    InlineShapeGroup? ShapeGroup = null) : PlacedItem(X, Y, Width, Height);
