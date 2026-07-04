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
}
