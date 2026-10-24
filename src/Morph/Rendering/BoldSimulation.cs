/// <summary>
/// Word's bold for a family that has no bold face. It keeps the face the run names and emboldens it:
/// its fixed-format output marks the run with the XPS bold simulation, strokes thicken, and each glyph
/// is drawn a pixel of Word's layout grid wider.
/// </summary>
/// <remarks>
/// Word-probed (<c>_probe_synbold</c>, <c>_probe_synwrap</c>): twenty-three families set regular and bold
/// at 10, 20 and 40pt in compatibility modes 12 and 15.
/// <list type="bullet">
/// <item>Every family with no bold face of its own draws bold in its regular face with the simulation:
/// Franklin Gothic Book, Medium and Demi, Calibri Light, Lato Light, Source Sans Pro Light, Impact,
/// Baskerville Old Face, Arial Rounded MT Bold and the rest, all of OS/2 weight 300 or 400. A face of
/// weight 900 (Arial Black) is drawn as it is.</item>
/// <item>Strokes grow by 2.5 to 3.1% of the em at 40pt, fifteen faces averaging 2.8% (a horizontal run of
/// ink is 2.1 to 2.7px longer at 150 dpi), and by a pixel at 10 and 20pt, where that fraction is less
/// than one.</item>
/// <item>A drawn glyph is 0.6pt wider, one pixel at 120 dpi, at every size: a right-aligned line of
/// seventeen glyphs starts 10.2pt further left at 10pt and at 40pt alike.</item>
/// <item>A line is BROKEN on the regular advances. A bold sentence wraps at the same measure as the
/// regular one to the pixel (556px for 555.3px of text), so the widening is how a line is drawn and
/// never what it is laid out on. A line that would then overrun has its spaces narrowed.</item>
/// <item>A variable font is not simulated. Bahnschrift's bold is drawn as a real instance of its
/// weight axis, at the regular instance's advances: the line starts where the regular line does, and
/// its strokes are 5.0 and 5.3% of the em thicker at 40 and 20pt.</item>
/// </list>
/// </remarks>
static class BoldSimulation
{
    /// <summary>How much thicker the simulation draws a stroke, as a fraction of the em.</summary>
    const double simulatedGrowth = 0.027;

    /// <summary>How much thicker a variable font's bold instance is than its regular one, as a fraction of the em.</summary>
    const double instanceGrowth = 0.05;

    /// <summary>What a simulated glyph gains in advance: one pixel of Word's 120-dpi layout grid.</summary>
    public const double AdvancePoints = 0.6;

    /// <summary>The weight below which a face asked for bold is emboldened rather than drawn as it is.</summary>
    const int emboldenedBelow = 600;

    /// <summary>
    /// Whether a bold request is emboldened at all: the face it resolved is lighter than a bold one. A
    /// face that declares no weight is taken as it comes.
    /// </summary>
    public static bool Applies(bool bold, int faceWeight) =>
        bold &&
        faceWeight is > 0 and < emboldenedBelow;

    /// <summary>How a run asking for <paramref name="bold"/> is drawn in the face it resolved.</summary>
    public static SyntheticBold For(bool bold, FontMetrics face)
    {
        if (!Applies(bold, face.Weight))
        {
            return SyntheticBold.None;
        }

        return face.IsVariable ? SyntheticBold.VariableInstance : SyntheticBold.Simulated;
    }

    /// <summary>
    /// The width of the stroke laid over the filled glyphs, in points: the growth of a stem, half of it on
    /// each side of the outline, and never less than <paramref name="pixelPoints"/>, one pixel of the
    /// raster being drawn.
    /// </summary>
    public static double StrokeWidthPoints(SyntheticBold kind, double sizePoints, double pixelPoints) =>
        Math.Max(sizePoints * (kind == SyntheticBold.VariableInstance ? instanceGrowth : simulatedGrowth), pixelPoints);
}
