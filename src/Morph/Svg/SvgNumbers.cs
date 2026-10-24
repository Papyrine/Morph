/// <summary>
/// The number grammar SVG shares between path data, <c>points</c>, <c>viewBox</c> and transform
/// lists: numbers separated by whitespace, a comma, or nothing at all where the next one's sign or
/// leading point already ends the last (<c>1-2</c> is two numbers, and so is <c>.5.5</c>).
/// </summary>
static class SvgNumbers
{
    /// <summary>Moves past whitespace and at most one comma.</summary>
    public static void SkipSeparator(CharSpan text, ref int position)
    {
        SkipSpace(text, ref position);
        if (position < text.Length &&
            text[position] == ',')
        {
            position++;
            SkipSpace(text, ref position);
        }
    }

    public static void SkipSpace(CharSpan text, ref int position)
    {
        while (position < text.Length &&
               text[position] is ' ' or '\t' or '\r' or '\n' or '\f')
        {
            position++;
        }
    }

    /// <summary>
    /// Reads one number at <paramref name="position"/>, leaving it just past the number. False,
    /// with the position unmoved, when no number starts there.
    /// </summary>
    public static bool TryRead(CharSpan text, ref int position, out double value)
    {
        value = 0;
        var index = position;
        if (index < text.Length &&
            text[index] is '+' or '-')
        {
            index++;
        }

        var digits = 0;
        while (index < text.Length &&
               char.IsAsciiDigit(text[index]))
        {
            index++;
            digits++;
        }

        if (index < text.Length &&
            text[index] == '.')
        {
            index++;
            while (index < text.Length &&
                   char.IsAsciiDigit(text[index]))
            {
                index++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        // An exponent only counts when digits follow it; otherwise the e belongs to whatever comes
        // next (the em of a length, say) and the number ends before it.
        if (index < text.Length &&
            text[index] is 'e' or 'E')
        {
            var exponent = index + 1;
            if (exponent < text.Length &&
                text[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (exponent < text.Length &&
                char.IsAsciiDigit(text[exponent]))
            {
                while (exponent < text.Length &&
                       char.IsAsciiDigit(text[exponent]))
                {
                    exponent++;
                }

                index = exponent;
            }
        }

        if (!double.TryParse(text[position..index], NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            !double.IsFinite(value))
        {
            return false;
        }

        position = index;
        return true;
    }

    /// <summary>
    /// Reads a whole attribute as a list of numbers. Null when anything but numbers and their
    /// separators is in it.
    /// </summary>
    public static List<double>? ReadList(CharSpan text)
    {
        var values = new List<double>();
        var position = 0;
        SkipSpace(text, ref position);
        while (position < text.Length)
        {
            if (!TryRead(text, ref position, out var value))
            {
                return null;
            }

            values.Add(value);
            SkipSeparator(text, ref position);
        }

        return values;
    }
}
