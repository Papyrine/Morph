/// <summary>
/// The measurement units OOXML stores lengths in, and the conversions to the points everything
/// downstream of the parsers works in.
/// </summary>
/// <remarks>
/// <para>
/// One home for the constants because they were being re-declared per file: <c>emusPerPoint</c>
/// appeared as a local <c>const</c> in eleven of them (spelled <c>12700</c> in some and
/// <c>914400.0 / 72.0</c> in others — the same number), and <c>twipsPerPoint</c> in the parser
/// alongside sixty hand-written divisions by it.
/// </para>
/// <para>
/// The conversions are extension methods on the OpenXML value types rather than free functions so
/// that the absent-value check travels with them. The shape they replace was
/// <c>if (x?.HasValue == true) { y = double.Parse(x.Value!) / twipsPerPoint; }</c> — four lines, of
/// which three are ceremony.
/// </para>
/// </remarks>
static class OoxmlUnits
{
    /// <summary>EMUs (English Metric Units) per point. 914400 per inch, 72 points per inch.</summary>
    public const double EmusPerPoint = 914400.0 / 72.0;

    /// <summary>
    /// <see cref="EmusPerPoint"/> as a float, for the backends that scale stroke widths in single
    /// precision. Kept as its own constant rather than a cast of the double so those divisions stay
    /// float-for-float: converting the operand up to double and the quotient back can land a ULP
    /// away, which is invisible in a stroke width but not in a rasterised pixel.
    /// </summary>
    public const float EmusPerPointF = 12700f;

    /// <summary>Twips (twentieths of a point) per point.</summary>
    public const double TwipsPerPoint = 20.0;

    /// <summary>Converts EMUs to points.</summary>
    public static double EmuToPoints(this long emus) => emus / EmusPerPoint;

    /// <summary>Converts EMUs (as double) to points. Used when EMU values have been scaled.</summary>
    public static double EmuToPoints(this double emus) => emus / EmusPerPoint;

    /// <summary>Converts twips to points.</summary>
    public static double TwipsToPoints(this double twips) => twips / TwipsPerPoint;

    /// <summary>Converts twips to points.</summary>
    public static double TwipsToPoints(this int twips) => twips / TwipsPerPoint;

    /// <summary>
    /// Converts a twip-valued attribute to points, or null when the attribute is absent.
    /// </summary>
    /// <remarks>
    /// The OOXML twip attributes that arrive as <see cref="StringValue"/> (<c>w:spacing/@w:before</c>,
    /// <c>w:ind/@w:left</c> and the rest) are integers in the schema, so this parses rather than
    /// try-parses: a value present but unreadable is a malformed document, and swallowing it would
    /// silently substitute the property's default. That is the behaviour of the hand-written
    /// <c>double.Parse</c> calls this replaces, kept deliberately. The invariant culture is
    /// explicit here where those relied on the ambient one.
    /// </remarks>
    public static double? TwipsToPoints(this StringValue? value) =>
        value?.HasValue == true
            ? double.Parse(value.Value!, CultureInfo.InvariantCulture) / TwipsPerPoint
            : null;

    /// <summary>Converts a twip-valued attribute to points, or null when absent.</summary>
    public static double? TwipsToPoints(this Int32Value? value) =>
        value?.HasValue == true ? value.Value / TwipsPerPoint : null;

    /// <summary>Converts a twip-valued attribute to points, or null when absent.</summary>
    public static double? TwipsToPoints(this UInt32Value? value) =>
        value?.HasValue == true ? value.Value / TwipsPerPoint : null;

    /// <summary>Converts half-points (used by w:sz, w:kern, w:position) to points.</summary>
    public static double HalfPointsToPoints(this double halfPoints) => halfPoints / 2.0;

    /// <summary>Converts half-points (used by w:sz, w:kern, w:position) to points.</summary>
    public static double HalfPointsToPoints(this int halfPoints) => halfPoints / 2.0;

    /// <summary>Converts half-points (used by w:sz, w:kern, w:position) to points.</summary>
    public static double HalfPointsToPoints(this uint halfPoints) => halfPoints / 2.0;
}
