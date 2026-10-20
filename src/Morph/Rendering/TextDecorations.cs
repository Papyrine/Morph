/// <summary>
/// The rules a run's underline and strikethrough draw as, in points, for the painters to stroke.
///
/// Every style is placed against the single underline's band — offset <c>p</c> below the baseline,
/// thickness <c>t</c> — exactly as Word places it against the font's own. Word-probed from the XPS of
/// <c>_probe_underline_48</c> and <c>_probe_underline_24</c> (Arial, 2026-10-21), where every law held at
/// both sizes:
/// * single, words and the light dashed styles fill <c>[p, p + t]</c>; words leaves the spaces bare.
/// * double is two rules of <c>t/2</c> filling <c>[p/2, p + t]</c> — top and bottom of it.
/// * thick and every heavy style fill <c>[p/2, p + t/2]</c>.
/// * dashes are fixed lengths in points, the same at 24pt and 48pt, patterned from the run's start:
///   dotted 1.44 on / 1.44 off, dash 3.96 / 1.92, dashLong 7.92 / 3.96, dotDash 3.0 / .96 / .96 / .96,
///   dotDotDash 2.16 then .72 five times. A heavy style keeps its light style's pattern.
/// * wave is a 2.4pt-period wave whose top is at <c>p/2</c>, 0.06 em from crest to trough, stroked at a
///   fixed width; wavyDouble is two at half that, the second 0.54 of a full wave lower.
/// * dstrike is two rules of the strike's own thickness, each one thickness above and below where the
///   single strike's centre is.
/// </summary>
static class TextDecorations
{
    /// <summary>A horizontal rule centred on <see cref="Y"/>.</summary>
    internal readonly record struct Rule(float X0, float X1, float Y, float Thickness, bool IsStrike);

    /// <summary>A wavy underline as a polyline through <see cref="Points"/>.</summary>
    internal sealed record Wave(IReadOnlyList<(float X, float Y)> Points, float Thickness);

    const float wavePeriod = 2.4f;

    // A wave's stroke does not follow the font: the XPS strokes it 0.24pt (wave, wavyDouble) or 0.96pt
    // (wavyHeavy) at 24pt and at 48pt alike.
    const float lightWave = 0.24f;
    const float heavyWave = 0.96f;

    /// <summary>
    /// Collects the run's underline and strike rules. <paramref name="measure"/> gives the drawn width in
    /// points of a prefix of <paramref name="text"/>, which only the words style asks for.
    /// <paramref name="ascent"/> is the line's ascent, which places the strike.
    /// </summary>
    public static void Collect(RunProperties properties, string text, float x, float width, float baseline, float ascent, Func<string, float> measure, List<Rule> rules, List<Wave> waves)
    {
        var size = (float) properties.FontSizePoints;
        var thickness = Math.Max(0.5f, size / 16);
        if (properties.Underline)
        {
            var offset = size * 0.12f - thickness / 2;
            foreach (var (start, end) in Spans(properties.UnderlineStyle, text, x, width, measure))
            {
                Underline(properties.UnderlineStyle, start, end, baseline, offset, thickness, size, rules, waves);
            }
        }

        if (properties.Strikethrough || properties.DoubleStrikethrough)
        {
            var centre = baseline - ascent * 0.3f;
            if (properties.DoubleStrikethrough)
            {
                rules.Add(new(x, x + width, centre - thickness, thickness, true));
                rules.Add(new(x, x + width, centre + thickness, thickness, true));
            }
            else
            {
                rules.Add(new(x, x + width, centre, thickness, true));
            }
        }
    }

    static void Underline(UnderlineStyle style, float start, float end, float baseline, float offset, float thickness, float size, List<Rule> rules, List<Wave> waves)
    {
        switch (style)
        {
            case UnderlineStyle.Double:
                rules.Add(new(start, end, baseline + offset / 2 + thickness / 4, thickness / 2, false));
                rules.Add(new(start, end, baseline + offset + thickness * 3 / 4, thickness / 2, false));
                return;
            case UnderlineStyle.Wave:
                waves.Add(WaveFrom(start, end, baseline + offset / 2, size * 0.06f, lightWave));
                return;
            case UnderlineStyle.WavyHeavy:
                waves.Add(WaveFrom(start, end, baseline + offset / 2, size * 0.06f, heavyWave));
                return;
            case UnderlineStyle.WavyDouble:
                waves.Add(WaveFrom(start, end, baseline + offset / 2, size * 0.03f, lightWave));
                waves.Add(WaveFrom(start, end, baseline + offset / 2 + size * 0.06f * 0.54f, size * 0.03f, lightWave));
                return;
        }

        float top;
        float bottom;
        if (IsHeavy(style))
        {
            top = offset / 2;
            bottom = offset + thickness / 2;
        }
        else
        {
            top = offset;
            bottom = offset + thickness;
        }

        var y = baseline + (top + bottom) / 2;
        var bandThickness = bottom - top;
        var pattern = DashPattern(style);
        if (pattern == null)
        {
            rules.Add(new(start, end, y, bandThickness, false));
            return;
        }

        var at = start;
        var index = 0;
        while (at < end)
        {
            var length = pattern[index % pattern.Length];
            if (index % 2 == 0)
            {
                rules.Add(new(at, Math.Min(at + length, end), y, bandThickness, false));
            }

            at += length;
            index++;
        }
    }

    static bool IsHeavy(UnderlineStyle style) =>
        style is UnderlineStyle.Thick or
            UnderlineStyle.DottedHeavy or
            UnderlineStyle.DashedHeavy or
            UnderlineStyle.DashLongHeavy or
            UnderlineStyle.DashDotHeavy or
            UnderlineStyle.DashDotDotHeavy;

    // On, off, on, off… in points.
    static float[]? DashPattern(UnderlineStyle style)
    {
        switch (style)
        {
            case UnderlineStyle.Dotted:
            case UnderlineStyle.DottedHeavy:
                return [1.44f, 1.44f];
            case UnderlineStyle.Dash:
            case UnderlineStyle.DashedHeavy:
                return [3.96f, 1.92f];
            case UnderlineStyle.DashLong:
            case UnderlineStyle.DashLongHeavy:
                return [7.92f, 3.96f];
            case UnderlineStyle.DotDash:
            case UnderlineStyle.DashDotHeavy:
                return [3.0f, 0.96f, 0.96f, 0.96f];
            case UnderlineStyle.DotDotDash:
            case UnderlineStyle.DashDotDotHeavy:
                return [2.16f, 0.72f, 0.72f, 0.72f, 0.72f, 0.72f];
        }

        return null;
    }

    // The stretches of the run a rule spans: the whole run, or for the words style each stretch of
    // non-space characters.
    static IEnumerable<(float Start, float End)> Spans(UnderlineStyle style, string text, float x, float width, Func<string, float> measure)
    {
        if (style != UnderlineStyle.Words)
        {
            yield return (x, x + width);
            yield break;
        }

        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index == text.Length)
            {
                yield break;
            }

            var wordStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            yield return (x + measure(text[..wordStart]), x + measure(text[..index]));
        }
    }

    // A wave whose crests sit at top and troughs amplitude below it, sampled finely enough to read as a
    // curve at any DPI the painters draw at.
    static Wave WaveFrom(float start, float end, float top, float amplitude, float thickness)
    {
        const int samplesPerPeriod = 12;
        var step = wavePeriod / samplesPerPeriod;
        var points = new List<(float X, float Y)>();
        for (var at = start; ; at += step)
        {
            var clamped = Math.Min(at, end);
            var phase = (clamped - start) / wavePeriod * 2 * MathF.PI;
            points.Add((clamped, top + amplitude * (1 - MathF.Cos(phase)) / 2));
            if (clamped >= end)
            {
                break;
            }
        }

        return new(points, thickness);
    }
}
