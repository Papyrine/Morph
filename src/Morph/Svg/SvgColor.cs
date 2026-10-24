/// <summary>
/// Reads a colour as SVG's presentation attributes and CSS write one: <c>#rgb</c> and
/// <c>#rrggbb</c> with or without an alpha digit pair, <c>rgb()</c> and <c>rgba()</c> in numbers or
/// percentages, a named colour, or <c>transparent</c>. The result is the six-digit hex and separate
/// alpha the rest of the model carries a colour as.
/// </summary>
static class SvgColor
{
    public static bool TryParse(string text, out string hex, out double alpha)
    {
        hex = "000000";
        alpha = 1;
        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            return TryParseHex(value.AsSpan(1), out hex, out alpha);
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseFunction(value, out hex, out alpha);
        }

        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            alpha = 0;
            return true;
        }

        // The named colours are CSS's, which the HTML parser already holds the whole table of.
        if (HtmlParser.NormalizeColor(value) is { } named)
        {
            hex = named;
            return true;
        }

        return false;
    }

    static bool TryParseHex(CharSpan digits, out string hex, out double alpha)
    {
        hex = "000000";
        alpha = 1;
        foreach (var digit in digits)
        {
            if (!char.IsAsciiHexDigit(digit))
            {
                return false;
            }
        }

        // The short forms double each digit: #1af is #11aaff.
        Span<char> full = stackalloc char[8];
        switch (digits.Length)
        {
            case 3 or 4:
                for (var index = 0; index < digits.Length; index++)
                {
                    full[2 * index] = full[2 * index + 1] = digits[index];
                }

                full = full[..(2 * digits.Length)];
                break;
            case 6 or 8:
                digits.CopyTo(full);
                full = full[..digits.Length];
                break;
            default:
                return false;
        }

        hex = full[..6].ToString().ToUpperInvariant();
        if (full.Length == 8)
        {
            alpha = int.Parse(full[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        }

        return true;
    }

    // rgb(255, 0, 0), rgb(100% 0% 0%), rgba(255, 0, 0, 0.5) and rgb(255 0 0 / 50%): three channels
    // as numbers out of 255 or as percentages, then an optional alpha as a fraction or a percentage.
    static bool TryParseFunction(string value, out string hex, out double alpha)
    {
        hex = "000000";
        alpha = 1;
        var open = value.IndexOf('(');
        if (open < 0 || !value.EndsWith(')'))
        {
            return false;
        }

        var name = value.AsSpan(0, open).Trim();
        if (!name.Equals("rgb", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("rgba", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = value[(open + 1)..^1].Split([',', ' ', '/', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is not (3 or 4))
        {
            return false;
        }

        Span<byte> channels = stackalloc byte[3];
        for (var index = 0; index < 3; index++)
        {
            if (!TryParsePart(parts[index], 255, out var channel))
            {
                return false;
            }

            channels[index] = (byte) Math.Round(Math.Clamp(channel, 0, 255));
        }

        if (parts.Length == 4)
        {
            if (!TryParsePart(parts[3], 1, out alpha))
            {
                return false;
            }

            alpha = Math.Clamp(alpha, 0, 1);
        }

        hex = HexColor.ToHex(channels[0], channels[1], channels[2]);
        return true;
    }

    // A plain number as it stands, or a percentage of the given whole.
    static bool TryParsePart(string part, double whole, out double value)
    {
        var percent = part.EndsWith('%');
        var number = percent ? part.AsSpan(0, part.Length - 1) : part.AsSpan();
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
        {
            return false;
        }

        if (percent)
        {
            value = value / 100 * whole;
        }

        return true;
    }
}
