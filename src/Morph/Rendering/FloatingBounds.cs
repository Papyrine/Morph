/// <summary>
/// Resolved position and pixel rectangle of a floating element, computed once from its
/// anchor, offset, and size so backends don't repeat the conversion math.
/// </summary>
readonly record struct FloatingBounds(
    float X,
    float Y,
    float PixelX,
    float PixelY,
    float PixelWidth,
    float PixelHeight);
