/// <summary>
/// The one format code that follows the reader's culture. Built-in format 14 is Excel's system short
/// date: it is stored as <c>m/d/yyyy</c> (and a chart caches it so), but Office shows it in the order
/// the machine's region gives its dates — PowerPoint on an en-AU machine draws a 1 May 2002 category
/// as <c>1/05/2002</c> (chart-area). Every other code is fixed, and with the invariant culture this one
/// is too, so output stays the same on every machine unless a culture is asked for.
/// </summary>
static partial class NumberFormat
{
    const string systemShortDate = "m/d/yyyy";

    /// <summary>
    /// The code to format with under <paramref name="culture"/>: the culture's short-date pattern in
    /// place of built-in format 14, and any other code as it is.
    /// </summary>
    [return: NotNullIfNotNull(nameof(code))]
    public static string? Localize(string? code, CultureInfo culture)
    {
        if (code == null ||
            Equals(culture, CultureInfo.InvariantCulture) ||
            !string.Equals(code, systemShortDate, StringComparison.OrdinalIgnoreCase))
        {
            return code;
        }

        return ShortDateCode(culture.DateTimeFormat);
    }

    // A .NET short-date pattern as an Excel format code: the day, month and year runs carry over (a
    // month's M becoming m), '/' becomes the culture's own date separator, and anything else — quoted
    // text, other letters — is kept as quoted literal text.
    static string ShortDateCode(DateTimeFormatInfo format)
    {
        var pattern = format.ShortDatePattern;
        var code = new StringBuilder();
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            switch (character)
            {
                case 'd' or 'M' or 'y':
                    var run = 1;
                    while (index + run < pattern.Length && pattern[index + run] == character)
                    {
                        run++;
                    }

                    code.Append(char.ToLowerInvariant(character), run);
                    index += run - 1;
                    break;
                case '/':
                    Literal(code, format.DateSeparator);
                    break;
                case '\'' or '"':
                    var end = pattern.IndexOf(character, index + 1);
                    if (end < 0)
                    {
                        end = pattern.Length;
                    }

                    Literal(code, pattern[(index + 1)..end]);
                    index = end;
                    break;
                case '\\' when index + 1 < pattern.Length:
                    Literal(code, pattern[++index].ToString());
                    break;
                default:
                    Literal(code, character.ToString());
                    break;
            }
        }

        return code.ToString();
    }

    // Text a date code shows as it is: the separators Excel's date codes write bare stay bare, and
    // anything else is quoted, so a letter in it is never read as a token.
    static void Literal(StringBuilder code, string text)
    {
        if (text.All(_ => _ is '/' or '-' or '.' or ' ' or ','))
        {
            code.Append(text);
            return;
        }

        code.Append('"').Append(text.Replace("\"", "")).Append('"');
    }
}
