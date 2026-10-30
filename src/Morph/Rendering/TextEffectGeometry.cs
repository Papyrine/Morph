/// <summary>
/// What the painters share of a run's Word 2010 text effects: where <c>w14:shadow</c> puts its copy of
/// the glyphs and how opaque it draws it. The outline (<c>w14:textOutline</c>) is a stroke of
/// <see cref="TextOutline.WidthPoints"/> centred on the glyph outlines, over the fill.
/// </summary>
static class TextEffectGeometry
{
    /// <summary>The shadow's offset from the glyphs in points, its distance along its direction (0 is
    /// right, 90 down).</summary>
    public static (double X, double Y) ShadowOffset(TextShadow shadow)
    {
        var radians = shadow.DirectionDegrees * Math.PI / 180;
        return (shadow.DistancePoints * Math.Cos(radians), shadow.DistancePoints * Math.Sin(radians));
    }

    /// <summary>The shadow's alpha, 0 to 255.</summary>
    public static byte ShadowAlpha(TextShadow shadow) =>
        (byte) Math.Round(Math.Clamp(shadow.AlphaPercent, 0, 100) * 2.55);
}
