/// <summary>
/// The colour transforms declared on an OOXML colour, and the one implementation that applies them.
/// </summary>
/// <remarks>
/// <para>
/// OOXML carries two unrelated families of colour transform, and they were previously conflated
/// into one pair of <c>Shade</c>/<c>Tint</c> byte fields with two different models applied to them
/// depending on which parser reached the colour first — a literal <c>a:srgbClr</c> went through an
/// sRGB-space blend, the identical transform on an <c>a:schemeClr</c> went through HSL luminance.
/// Word applies neither of those to <c>a:shade</c>.
/// </para>
/// <para>
/// Measured against Word over 98 rendered swatches (six probe fixtures, two base colours each,
/// three or more magnitudes per axis — the amplification rule in <c>CLAUDE.md</c>):
/// </para>
/// <list type="table">
/// <item>
///   <term>DrawingML <c>a:shade</c> / <c>a:tint</c></term>
///   <description>LINEAR LIGHT. <c>lin' = lin · f</c> for shade, <c>lin' = lin · f + (1 − f)</c>
///   for tint, where <c>f</c> is the full-precision fraction. Exact on all 24 swatches; the
///   sRGB blend was out by up to 127 per channel and HSL luminance by up to 69.</description>
/// </item>
/// <item>
///   <term>DrawingML <c>a:lumMod</c>/<c>a:lumOff</c>/<c>a:satMod</c>/<c>a:satOff</c></term>
///   <description>HSL, saturation UNCLAMPED (see <see cref="HexColor.HslToRgb"/>).</description>
/// </item>
/// <item>
///   <term>WordprocessingML <c>w:themeShade</c> / <c>w:themeTint</c></term>
///   <description>HSL LUMINANCE on the 0-255 byte: <c>L' = L·(S/255)</c> for shade,
///   <c>L' = L·(T/255) + (255−T)/255</c> for tint. Exact to within one LSB on all 12 measured
///   bands; linear light was out by up to 62. This is a genuinely different transform from
///   <c>a:shade</c>, not a different encoding of it.</description>
/// </item>
/// <item>
///   <term>Composition</term>
///   <description>DOCUMENT ORDER. Word applies each child as it is read, so
///   <c>lumMod</c>-then-<c>shade</c> and <c>shade</c>-then-<c>lumMod</c> give different colours
///   (measured 142748 vs 182948 on accent1). Any fixed ordering mispredicts one of the two by up
///   to 94 per channel; document order fits both.</description>
/// </item>
/// </list>
/// <para>
/// The residual against Word is a single LSB on some byte-form theme shades, consistent with a
/// rounding difference in the HSL round trip. Neither <c>Math.Round</c> nor truncation clears it
/// on every sample, and one channel step is below anything the fidelity comparison resolves.
/// </para>
/// </remarks>
sealed record ColorTransforms
{
    /// <summary>
    /// WordprocessingML <c>w:themeShade</c> (0-255, darkens). Applied as HSL luminance scaling.
    /// </summary>
    public byte? ThemeShade { get; init; }

    /// <summary>
    /// WordprocessingML <c>w:themeTint</c> (0-255, lightens). Applied as HSL luminance scaling
    /// with an offset.
    /// </summary>
    public byte? ThemeTint { get; init; }

    /// <summary>
    /// The DrawingML transform children in document order. Order is load-bearing — see the
    /// composition row of the model table above.
    /// </summary>
    public IReadOnlyList<ColorTransform> Operations { get; init; } = [];

    /// <summary>Returns true if any transform is specified.</summary>
    public bool HasTransforms =>
        ThemeShade.HasValue ||
        ThemeTint.HasValue ||
        Operations.Count > 0;

    /// <summary>Returns true if any transform would actually move the colour.</summary>
    bool HasEffect =>
        ThemeShade is > 0 ||
        ThemeTint.HasValue ||
        Operations.Any(_ => !_.IsIdentity);

    /// <summary>
    /// Applies every declared transform to a six-digit <c>RRGGBB</c> colour, returning the input
    /// unchanged when it cannot be parsed or when nothing is declared.
    /// </summary>
    public string ApplyTo(string hexColor)
    {
        if (!HasEffect ||
            !hexColor.TryParse(out var r, out var g, out var b))
        {
            return hexColor;
        }

        // Consecutive HSL operations accumulate in HSL rather than round-tripping through 8-bit RGB
        // between each one: lumMod immediately followed by lumOff is the commonest sequence in the
        // corpus by a wide margin, and quantising between the two shifts the result by an LSB
        // against the single pass this replaced. Only a shade/tint — which is defined over linear
        // light, a different space — forces the conversion back.
        var inHsl = false;
        double h = 0, s = 0, l = 0;

        foreach (var operation in Operations)
        {
            if (operation.IsIdentity)
            {
                continue;
            }

            if (operation.Kind is ColorTransformKind.Shade or ColorTransformKind.Tint)
            {
                if (inHsl)
                {
                    HexColor.HslToRgb(h, s, Math.Clamp(l, 0.0, 1.0), out r, out g, out b);
                    inHsl = false;
                }

                var scale = operation.Value;
                ApplyInLinearLight(scale, operation.Kind == ColorTransformKind.Tint ? 1 - scale : 0, ref r, ref g, ref b);
                continue;
            }

            if (!inHsl)
            {
                HexColor.RgbToHsl(r, g, b, out h, out s, out l);
                inHsl = true;
            }

            switch (operation.Kind)
            {
                case ColorTransformKind.LumMod:
                    l *= operation.Value;
                    break;
                case ColorTransformKind.LumOff:
                    l += operation.Value;
                    break;
                case ColorTransformKind.SatMod:
                    s *= operation.Value;
                    break;
                case ColorTransformKind.SatOff:
                    s += operation.Value;
                    break;
            }
        }

        if (inHsl)
        {
            // Luminance is clamped, saturation is not: an out-of-range luminance saturates to black
            // or white either way (measured identical on 18 swatches), while clamping saturation
            // demonstrably diverges from Word.
            HexColor.HslToRgb(h, s, Math.Clamp(l, 0.0, 1.0), out r, out g, out b);
        }

        // The byte forms never co-occur with the DrawingML children — w:themeShade lives on a
        // w:color/w:shd, a:shade on a DrawingML colour — so their position in the chain is not
        // observable, and folding them into a single luminance pass keeps the round trips down.
        ApplyThemeShadeTint(ref r, ref g, ref b);

        return HexColor.ToHex(r, g, b);
    }

    static void ApplyInLinearLight(double scale, double offset, ref byte r, ref byte g, ref byte b)
    {
        r = HexColor.FromLinear(HexColor.ToLinear(r) * scale + offset);
        g = HexColor.FromLinear(HexColor.ToLinear(g) * scale + offset);
        b = HexColor.FromLinear(HexColor.ToLinear(b) * scale + offset);
    }

    void ApplyThemeShadeTint(ref byte r, ref byte g, ref byte b)
    {
        var lumMod = 1.0;
        var lumOff = 0.0;

        // A byte shade of 0 is the absent-value sentinel rather than "scale luminance to zero" —
        // a literal w:themeShade="00" never occurs.
        if (ThemeShade is > 0 and { } shade)
        {
            lumMod *= shade / 255.0;
        }

        if (ThemeTint is { } tint)
        {
            lumMod *= tint / 255.0;
            lumOff += (255 - tint) / 255.0;
        }

        // ReSharper disable once CompareOfFloatsByEqualityOperator — exact 1.0/0.0 means "no-op".
        if (lumMod == 1.0 && lumOff == 0.0)
        {
            return;
        }

        HexColor.RgbToHsl(r, g, b, out var h, out var s, out var l);
        HexColor.HslToRgb(h, s, Math.Clamp(l * lumMod + lumOff, 0.0, 1.0), out r, out g, out b);
    }
}
