/// <summary>
/// The DrawingML colour transform children (<c>a:shade</c>, <c>a:tint</c>, <c>a:lumMod</c>,
/// <c>a:lumOff</c>, <c>a:satMod</c>, <c>a:satOff</c>).
/// </summary>
/// <remarks>
/// Shade/tint and the lum/sat family are NOT the same kind of operation, which is why they are
/// applied in different colour spaces by <see cref="ColorTransforms.ApplyTo"/> — see the model
/// table there.
/// </remarks>
enum ColorTransformKind
{
    /// <summary>Scales linear light toward black.</summary>
    Shade,

    /// <summary>Blends linear light toward white.</summary>
    Tint,

    /// <summary>Scales HSL luminance.</summary>
    LumMod,

    /// <summary>Offsets HSL luminance.</summary>
    LumOff,

    /// <summary>Scales HSL saturation.</summary>
    SatMod,

    /// <summary>Offsets HSL saturation.</summary>
    SatOff
}

/// <summary>
/// One DrawingML colour transform. <paramref name="Value"/> is a fraction, already divided out of
/// the OOXML thousandths-of-a-percent encoding: <c>val="50000"</c> arrives here as <c>0.5</c>.
/// </summary>
/// <remarks>
/// Full precision on purpose. The value used to be squeezed through a 0-255 byte on the way in,
/// which quantised <c>val="50000"</c> to 127/255 = 0.498 and lost the distinction between, say,
/// <c>104999</c> and <c>105000</c> entirely.
/// </remarks>
readonly record struct ColorTransform(ColorTransformKind Kind, double Value)
{
    /// <summary>Builds a transform from the raw OOXML value (thousandths of a percent).</summary>
    public static ColorTransform FromOoxml(ColorTransformKind kind, int ooxmlValue) =>
        new(kind, ooxmlValue / 100_000.0);

    /// <summary>
    /// True when this transform leaves the colour alone — a modulation of 100% or an offset of 0.
    /// </summary>
    /// <remarks>
    /// Worth detecting rather than applying: Word writes plenty of them (the corpus carries 250
    /// <c>a:shade val="100000"</c> and 36 <c>a:satMod val="100000"</c>), and applying one is not
    /// free. Both spaces quantise back to a byte per channel, so a nominal no-op that takes the
    /// round trip can still shift a channel by one — which is exactly what it did across 176
    /// corpus pages before this guard existed.
    /// </remarks>
    // ReSharper disable once CompareOfFloatsByEqualityOperator — the value is an exact quotient of
    // integers, so the identity cases land on exactly 1.0 and 0.0.
    public bool IsIdentity => Kind switch
    {
        ColorTransformKind.Shade or
            ColorTransformKind.Tint or
            ColorTransformKind.LumMod or
            ColorTransformKind.SatMod => Value == 1.0,
        _ => Value == 0.0
    };
}
