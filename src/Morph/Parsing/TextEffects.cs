/// <summary>
/// Word 2010+ run-level text effects (w14:shadow, w14:textOutline, w14:glow, w14:reflection).
/// We currently capture only presence, not the underlying parameters (colors, blur radius, etc.).
/// </summary>
[Flags]
enum TextEffects
{
    None = 0,
    Shadow = 1 << 0,
    Outline = 1 << 1,
    Glow = 1 << 2,
    Reflection = 1 << 3
}
