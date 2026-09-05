/// <summary>
/// Renders a page number in a PAGE field's <c>\*</c> switch vocabulary — <c>roman</c> / <c>Roman</c>,
/// <c>alphabetic</c> / <c>Alphabetic</c> (upper-case switch names map onto the same two) — which is
/// also how the parser stores a section's <c>w:pgNumType/@w:fmt</c>. Anything else is decimal.
/// </summary>
static class PageNumberFormatting
{
    public static string Format(int number, string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var upper = char.IsUpper(format[0]);
        return format.ToLowerInvariant() switch
        {
            "roman" => upper ? Roman(number) : Roman(number).ToLowerInvariant(),
            "alphabetic" => upper ? Letters(number) : Letters(number).ToLowerInvariant(),
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    static string Roman(int number)
    {
        if (number is <= 0 or > 3999)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var values = (int[]) [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        var symbols = (string[]) ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        for (var index = 0; index < values.Length; index++)
        {
            while (number >= values[index])
            {
                builder.Append(symbols[index]);
                number -= values[index];
            }
        }

        return builder.ToString();
    }

    // Word repeats the letter past 26: 27 is AA, 53 is AAA.
    static string Letters(int number)
    {
        if (number <= 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var cycle = (number - 1) / 26;
        var letter = (char) ('A' + (number - 1) % 26);
        return new(letter, cycle + 1);
    }
}
