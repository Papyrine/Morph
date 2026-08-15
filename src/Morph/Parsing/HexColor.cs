/// <summary>
/// The colour-space primitives the OOXML colour transforms are defined over: six-digit hex
/// parsing, the HSL round trip, and the sRGB ⇄ linear-light round trip.
///
/// These were duplicated verbatim between <see cref="ThemeColors"/> and <c>ShapeParser</c> —
/// two copies of <c>RgbToHsl</c>/<c>HslToRgb</c>/<c>TryParseHexColor</c> that had already drifted
/// into applying different models to the same OOXML construct. One copy, here.
/// </summary>
static class HexColor
{
    /// <summary>
    /// Parses a six-digit <c>RRGGBB</c> string. Anything else — a shorter string, an eight-digit
    /// value with alpha, <c>auto</c> — fails rather than guessing.
    /// </summary>
    public static bool TryParse(this string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (hex.Length != 6)
        {
            return false;
        }

        return byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out r) &&
               byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out g) &&
               byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out b);
    }

    /// <summary>Formats a byte triple back to six-digit <c>RRGGBB</c>.</summary>
    public static string ToHex(byte r, byte g, byte b) => $"{r:X2}{g:X2}{b:X2}";

    /// <summary>
    /// Converts RGB to HSL. Hue is turns (0..1), not degrees.
    /// </summary>
    public static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;

        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;

        l = (max + min) / 2.0;

        if (delta == 0)
        {
            h = 0;
            s = 0;
        }
        else
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == rd)
            {
                h = ((gd - bd) / delta + (gd < bd ? 6 : 0)) / 6.0;
            }
            else if (max == gd)
            {
                h = ((bd - rd) / delta + 2) / 6.0;
            }
            else
            {
                h = ((rd - gd) / delta + 4) / 6.0;
            }
        }
    }

    /// <summary>
    /// Converts HSL back to RGB. <paramref name="s"/> is deliberately NOT clamped to 0..1 by the
    /// caller: Word lets <c>a:satMod</c> drive saturation past 1 and clips at the RGB byte instead,
    /// which is what turns 4472C4 at <c>satMod 400%</c> into 003CFF rather than parking it at the
    /// fully-saturated 0961FF a clamp would give. Every out-of-range value is absorbed by the final
    /// byte clamp here. Measured against Word over 24 swatches, unclamped is exact and clamped is
    /// out by up to 51 per channel — see <c>docs/word-features.md</c>.
    /// </summary>
    public static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double rd, gd, bd;

        if (s == 0)
        {
            rd = gd = bd = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;

            rd = HueToRgb(p, q, h + 1.0 / 3.0);
            gd = HueToRgb(p, q, h);
            bd = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        r = ToByte(rd);
        g = ToByte(gd);
        b = ToByte(bd);
    }

    static byte ToByte(double value) =>
        (byte) Math.Clamp(Math.Round(value * 255), 0, 255);

    static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6.0)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 1.0 / 2.0)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + (q - p) * (2.0 / 3.0 - t) * 6;
        }

        return p;
    }

    /// <summary>
    /// Decodes one sRGB channel byte to linear light, per IEC 61966-2-1. <c>a:shade</c> and
    /// <c>a:tint</c> are defined over linear light, not over the encoded byte.
    /// </summary>
    public static double ToLinear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>Encodes a linear-light value back to an sRGB channel byte.</summary>
    public static byte FromLinear(double linear)
    {
        linear = Math.Clamp(linear, 0.0, 1.0);
        var encoded = linear <= 0.0031308
            ? linear * 12.92
            : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        return ToByte(encoded);
    }
}
