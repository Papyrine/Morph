/// <summary>
/// PowerPoint's gradient blend, laid out as stops the painters draw linearly.
/// </summary>
/// <remarks>
/// PowerPoint blends a gradient's stops in gamma 2.2 rather than in sRGB values, and eases the position
/// between two stops along an S-curve, t^e / (t^e + (1 − t)^e) with e = 1.58. Probed in PowerPoint 16
/// (2026-11-22): left-to-right two-stop gradients over 12 inches, blue to orange, red to green, black to
/// white and the gradient deck's accent2 to accent4, sampled at 39 points each. The midpoints match the
/// gamma 2.2 blend to the level in every channel — the deck's 2D3BFF to FF8C2A gives (188, 109, 188),
/// where an sRGB blend gives (150, 100, 148) — and the eased blend fits every sample with a worst
/// channel error of 8 levels (RMS 1.8), where the sRGB blend strays by up to 60. Read without it,
/// gradient's frames ran blue to orange through a muddy brown where PowerPoint runs them through pink.
/// </remarks>
static class SlideGradient
{
    const double gamma = 2.2;

    const double easing = 1.58;

    /// <summary>Stops laid between each pair of the gradient's own, so a linear draw follows the blend.</summary>
    const int stepsPerSegment = 16;

    /// <summary>The gradient as PowerPoint draws it, in enough stops to draw linearly.</summary>
    public static GradientFill? AsDrawn(GradientFill? gradient)
    {
        if (gradient == null)
        {
            return null;
        }

        var stops = gradient.AllStops;
        var drawn = new List<GradientStop>(stops.Count * stepsPerSegment);
        for (var index = 0; index + 1 < stops.Count; index++)
        {
            var from = stops[index];
            var to = stops[index + 1];
            for (var step = 0; step < stepsPerSegment; step++)
            {
                var t = (double) step / stepsPerSegment;
                drawn.Add(Blend(from, to, t));
            }
        }

        drawn.Add(stops[^1]);
        return gradient with
        {
            Stops = drawn
        };
    }

    /// <summary>The colour halfway between two colours, as PowerPoint blends them.</summary>
    public static string Midpoint(string firstHex, string secondHex) =>
        Blend(new(0, firstHex, 1), new(1, secondHex, 1), 0.5).ColorHex;

    static GradientStop Blend(GradientStop from, GradientStop to, double t)
    {
        var eased = Ease(t);
        var color = string.Concat(
            Enumerable.Range(0, 3).Select(channel =>
            {
                var first = Math.Pow(Channel(from.ColorHex, channel) / 255.0, gamma);
                var second = Math.Pow(Channel(to.ColorHex, channel) / 255.0, gamma);
                var value = Math.Pow((1 - eased) * first + eased * second, 1 / gamma);
                return ((int) Math.Round(Math.Clamp(value, 0, 1) * 255)).ToString("X2", CultureInfo.InvariantCulture);
            }));
        return new(
            from.Position + (to.Position - from.Position) * t,
            color,
            from.Alpha + (to.Alpha - from.Alpha) * eased);
    }

    static double Ease(double t)
    {
        if (t <= 0)
        {
            return 0;
        }

        if (t >= 1)
        {
            return 1;
        }

        var rising = Math.Pow(t, easing);
        return rising / (rising + Math.Pow(1 - t, easing));
    }

    static int Channel(string hex, int channel) =>
        hex.Length >= 6 &&
        int.TryParse(hex.AsSpan(channel * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
