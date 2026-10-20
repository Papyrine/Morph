/// <summary>
/// Extension helpers for <see cref="string"/>.
/// </summary>
static class StringExtensions
{
    /// <summary>
    /// Trims leading and trailing whitespace over a span and yields the result only when it is
    /// non-empty, so whitespace-only (or empty/null) input allocates nothing — unlike
    /// <c>value.Trim()</c> followed by an emptiness check, which allocates the trimmed string even
    /// when it is about to be discarded. When <paramref name="value"/> has no surrounding
    /// whitespace the original instance is reused rather than re-allocated.
    /// </summary>
    /// <returns><c>true</c> with the trimmed string in <paramref name="trimmed"/> when non-empty;
    /// otherwise <c>false</c> with <paramref name="trimmed"/> set to <c>null</c>.</returns>
    public static bool TryTrim(this string? value, [NotNullWhen(true)] out string? trimmed)
    {
        if (value != null)
        {
            var span = value.AsSpan().Trim();
            if (!span.IsEmpty)
            {
                trimmed = span.Length == value.Length ? value : span.ToString();
                return true;
            }
        }

        trimmed = null;
        return false;
    }

    // Escaped, not literal: C# counts U+2028/U+2029 as line terminators in source, so a literal
    // one inside a char literal ends the line mid-token.
    const char lineSeparator = '\u2028';
    const char paragraphSeparator = '\u2029';

    /// <summary>
    /// Replaces U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR with an ordinary space.
    /// Returns the original instance when neither is present, so the overwhelmingly common case
    /// allocates nothing.
    /// </summary>
    /// <remarks>
    /// Text faces carry no glyph for either character, so passing them through to a backend draws a
    /// missing-glyph box — <c>business-plans/01</c> rendered two of them. Word draws nothing.
    ///
    /// A space rather than a break: despite the Unicode names, and despite UAX #14 classing both as
    /// mandatory breaks, Word does NOT break a line on either one. A probe rendering
    /// <c>LINESEPAAA</c> U+2028 <c>LINESEPBBB</c> through Word keeps both words on one line with a
    /// blank gap between them, while a <c>w:br</c> control in the same document does split. U+2029
    /// behaves the same way. Word's own line break is <c>w:br</c>; a literal separator in
    /// <c>w:t</c> is stray content from a converter or a paste, and Word treats it as blank.
    ///
    /// Known residual: Word advances ~2.4 space widths for U+2028 (measured 18px against 8px for
    /// one space, Calibri 16pt at 150dpi), where this substitutes exactly one. Matching that would
    /// mean inventing a width no specification gives, for a character the corpus only ever carries
    /// at paragraph end where the advance is invisible.
    ///
    /// A literal line feed or carriage return in <c>w:t</c> is the same case, and Word sets it as a
    /// space too: complex_tables carries <c>COMPLEX</c> LF <c>MERGE</c> and <c>15-16</c> LF
    /// <c>Event</c> in single <c>w:t</c> elements, and Word's page has each on one line with a word
    /// space between. Left in the text they read as the engine's own mark for a <c>w:br</c> and broke
    /// the line there.
    /// </remarks>
    public static string ReplaceSeparatorsWithSpace(this string value)
    {
        var index = value.AsSpan().IndexOfAny(separators);
        if (index < 0)
        {
            return value;
        }

        return string.Create(
            value.Length,
            (Value: value, Index: index),
            (span, state) =>
            {
                state.Value.CopyTo(span);
                for (var i = state.Index; i < span.Length; i++)
                {
                    if (span[i] is lineSeparator or paragraphSeparator or '\n' or '\r')
                    {
                        span[i] = ' ';
                    }
                }
            });
    }

    static readonly System.Buffers.SearchValues<char> separators = System.Buffers.SearchValues.Create(lineSeparator, paragraphSeparator, '\n', '\r');
}
