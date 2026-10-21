/// <summary>
/// One image placed on a page: its box in points from the page's top-left and the drawable
/// <see cref="Data"/> (raster bytes; an SVG's raster fallback). An inline image is carried on its
/// <see cref="PlacedLine"/> with its bottom on the text baseline; page/margin-anchored floating images sit
/// directly on the page. A painter decodes the bytes and draws them into the box, applying the DrawingML
/// transforms a floating image can carry: rotation and flip about the box centre, a source-rectangle
/// <see cref="Crop"/>, and an ellipse or freeform clip. The transform fields default to none, so a plain
/// image (a header background, most inline images) constructs with the five-argument form. A
/// <see cref="Recolor"/> is Word's Recolor gallery transform (duotone, greyscale, washout), which the
/// painter applies as a colour filter over whatever it draws, and <see cref="Opacity"/> is the blip's
/// constant transparency (<c>a:alphaModFix</c>), which it composites with. A <see cref="Tile"/> is a
/// picture fill that repeats: the painter lays the picture edge to edge at the tile's size from the
/// tile's origin, cut to the box and to the clip, instead of stretching it over the box.
///
/// A picture stored as SVG carries its <see cref="Vector"/> as well, whenever the reader could take the
/// whole document (<see cref="SvgDrawing.For"/>): the painter then draws that into the box with its own
/// path calls, through the same crop, turn and clip, and leaves <see cref="Data"/> — the PNG Office
/// saved beside the SVG, and absent from a file that holds only the SVG — undecoded. Null for a raster
/// picture and for an SVG the reader gave up, which is drawn from <see cref="Data"/> as before.
///
/// An inline <see cref="ShapeGroup"/> (a grouped drawing embedded in a run) rides the same inline-image
/// carrier: <see cref="Data"/> is then null and the painter draws the group's child shapes scaled into the
/// box instead of decoding bytes. Floating images never set it.
///
/// An inline chart rides it too, already drawn: <see cref="Composed"/> holds the chart's shapes and
/// laid-out labels at their places on the page (<c>ChartComposer</c>), and the painter paints those
/// items in place of anything else.
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
    InlineShapeGroup? ShapeGroup = null,
    ImageRecolor? Recolor = null,
    double Opacity = 1,
    ImageOutline? Outline = null,
    ImageTile? Tile = null,
    SvgDrawing? Vector = null,
    IReadOnlyList<PlacedItem>? Composed = null) : PlacedItem(X, Y, Width, Height);
