/// <summary>
/// An inline box on a wrapped line as the canonical measurer lays it out — either an image (drawable
/// <see cref="Data"/>: raster bytes or an SVG's raster fallback) or an inline <see cref="ShapeGroup"/>
/// (grouped drawing painted from its child shapes; then <see cref="Data"/> is null). Display
/// <see cref="Width"/>/<see cref="Height"/> in points, <see cref="X"/> offset from the line's left edge.
/// The box is unbreakable in the flow — its width counts toward the line width and its height can grow the
/// line. Positioned with its bottom on the text baseline by the fragmenter.
/// </summary>
readonly record struct LaidOutImage(float X, float Width, float Height, byte[]? Data, double RotationDegrees = 0, bool FlipHorizontal = false, bool FlipVertical = false, ImageCrop? Crop = null, InlineShapeGroup? ShapeGroup = null);
