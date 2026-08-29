/// <summary>
/// A Word "Recolor" transform resolved to the channel weights a painter applies (a:duotone,
/// a:grayscl, a:lum). Carried on the laid-out image so every backend recolours from one recipe:
/// the raster painters apply <see cref="Rows"/> as a draw-time colour filter, and the PDF backend —
/// which has no pixel pipeline of its own — bakes it into the bytes it embeds through
/// <see cref="IImageEffects"/>.
/// </summary>
/// <remarks>
/// The effect is parsed onto <see cref="ImageElement"/>, <see cref="FloatingImageElement"/> and the
/// inline-image fields of <see cref="Run"/>; this is where those three become one value the layout
/// tree can carry. <see cref="For"/> returns null for <see cref="BlipColorEffect.None"/>, so a plain
/// picture keeps a null <c>Recolor</c> and every painter's untouched fast path.
/// </remarks>
sealed record ImageRecolor(BlipColorEffect Effect, string? DarkHex, string? LightHex)
{
    /// <summary>
    /// The recolour for an effect and its duotone endpoints, or null when there is nothing to apply.
    /// </summary>
    public static ImageRecolor? For(BlipColorEffect effect, string? darkHex, string? lightHex) =>
        effect == BlipColorEffect.None ? null : new(effect, darkHex, lightHex);

    /// <summary>
    /// One output channel: the weights applied to the source red, green and blue, plus a constant.
    /// All four are in 0-1 space, and alpha always passes through untouched.
    /// </summary>
    public readonly record struct Row(float Red, float Green, float Blue, float Offset);

    /// <summary>
    /// The transform as its three output rows. Backends differ on matrix layout — Skia's colour
    /// matrix is row-major over output channels, ImageSharp's <c>ColorMatrix</c> is its transpose —
    /// so the recipe is stated as rows rather than as a flat array that invites a silent
    /// transposition.
    /// </summary>
    public (Row Red, Row Green, Row Blue) Rows()
    {
        switch (Effect)
        {
            case BlipColorEffect.Duotone when DarkHex != null || LightHex != null:
            {
                // a:duotone maps luminance onto a dark→light ramp: out_c = dark_c + L*(light_c − dark_c),
                // which is the luminance row scaled by the channel's span and biased by its dark end.
                // Word's Recolor gallery pairs a dark colour with white; letters/02 pairs black with a
                // tinted accent instead, so neither end can be assumed.
                var (darkRed, darkGreen, darkBlue) = Channels(DarkHex, 0);
                var (lightRed, lightGreen, lightBlue) = Channels(LightHex, 1);
                return (
                    Luminance(lightRed - darkRed, darkRed),
                    Luminance(lightGreen - darkGreen, darkGreen),
                    Luminance(lightBlue - darkBlue, darkBlue));
            }

            // a:grayscl, and a duotone whose colours both failed to resolve, are the plain ramp from
            // black to white — the luminance row with no scale or bias.
            case BlipColorEffect.Grayscale:
            case BlipColorEffect.Duotone:
                return (Luminance(1, 0), Luminance(1, 0), Luminance(1, 0));

            // Word's washout is brightness +70% then contrast −50%, which composes to a single
            // affine ramp per channel: ((c × 1.7) − 0.5) × 0.5 + 0.5 = c × 0.85 + 0.25.
            case BlipColorEffect.Washout:
                return (new(0.85f, 0, 0, 0.25f), new(0, 0.85f, 0, 0.25f), new(0, 0, 0.85f, 0.25f));

            default:
                return (new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0));
        }
    }

    // Rec. 709 luminance, scaled and biased onto one output channel. These are the coefficients
    // ImageSharp's Grayscale() uses by default, which is the model the duotone was written against.
    static Row Luminance(float scale, float offset) =>
        new(0.2126f * scale, 0.7152f * scale, 0.0722f * scale, offset);

    // A duotone endpoint in 0-1 channels. An unresolved or unparseable colour falls back to the
    // ramp's natural end — black for the dark end, white for the light — so a half-resolved duotone
    // still renders as a ramp rather than collapsing to a flat fill.
    static (float Red, float Green, float Blue) Channels(string? hex, float fallback) =>
        hex != null && hex.TryParse(out var red, out var green, out var blue)
            ? (red / 255f, green / 255f, blue / 255f)
            : (fallback, fallback, fallback);
}
