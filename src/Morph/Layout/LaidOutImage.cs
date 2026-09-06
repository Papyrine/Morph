/// <summary>
/// An inline box on a wrapped line as the canonical measurer lays it out — either an image (drawable
/// <see cref="Data"/>: raster bytes or an SVG's raster fallback) or an inline <see cref="ShapeGroup"/>
/// (grouped drawing painted from its child shapes; then <see cref="Data"/> is null). Display
/// <see cref="Width"/>/<see cref="Height"/> in points are the picture's extent — the drawn size —
/// and <see cref="X"/> the offset of its layout box from the line's left edge. The layout box is the
/// extent grown by <see cref="EffectExtent"/> on every side (<see cref="BoxWidth"/>,
/// <see cref="BoxHeight"/>): that box is what the line reserves and what sits on the baseline, with
/// the picture drawn inside it at the left/top edge offsets. The box is unbreakable in the flow — its
/// width counts toward the line width and its height can grow the line. Positioned with its bottom on
/// the text baseline by the fragmenter.
/// </summary>
readonly record struct LaidOutImage(float X, float Width, float Height, byte[]? Data, double RotationDegrees = 0, bool FlipHorizontal = false, bool FlipVertical = false, ImageCrop? Crop = null, InlineShapeGroup? ShapeGroup = null, ImageRecolor? Recolor = null, double Opacity = 1, ImageOutline? Outline = null, ImageEffectExtent? EffectExtent = null)
{
    public float BoxWidth => Width + (float) ((EffectExtent?.Left ?? 0) + (EffectExtent?.Right ?? 0));

    public float BoxHeight => Height + (float) ((EffectExtent?.Top ?? 0) + (EffectExtent?.Bottom ?? 0));
}
