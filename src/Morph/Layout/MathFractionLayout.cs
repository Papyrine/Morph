/// <summary>
/// Where Word sets a built-up fraction's levels and bar about the baseline of the text it stands in, in
/// points. Word-probed 2026-10-28 (<c>_probe_math</c>: inline fractions of 12pt and 24pt math, "1" and
/// "a+b" over "2", glyph origins and the bar's path off the XPS): the bar's top stood 3.6pt above the
/// baseline and was 0.6pt thick at both sizes, the numerator's baseline 7.2pt above the baseline at both,
/// and the denominator's baseline 6.0pt below it at 12pt and 9.6pt at 24pt. Read at two sizes only, so
/// between and beyond them this is the straight line through what was measured.
/// </summary>
static class MathFractionLayout
{
    public const float BarTop = 3.6f;

    public const float BarThickness = 0.6f;

    public const float NumeratorBaseline = 7.2f;

    /// <summary>How far above its baseline a numerator's box reaches, as a fraction of its size: Word set
    /// the 12pt line's baseline 14.47pt under the line's top, 7.2 to the numerator's baseline and 0.83 of
    /// its 8.76pt from there. Cambria's own ascent (0.97) put the line 1.2pt low. Read at 12pt only.</summary>
    public const float NumeratorAscent = 0.83f;

    public static float DenominatorBaseline(double sizePoints) => (float) (2.4 + 0.3 * sizePoints);
}

/// <summary>
/// A built-up fraction as the measurer laid it out: each level's width, and how far the box reaches above
/// and below the line's baseline (<see cref="MathFractionLayout"/>). The fragmenter composes the levels
/// and the bar into the box.
/// </summary>
sealed record LaidOutFraction(MathFraction Source, float NumeratorWidth, float DenominatorWidth, float Above, float Below);
