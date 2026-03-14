namespace WordRender.Rendering;

static class FontHelpers
{
    // Font fallback mappings for fonts that may not be installed
    internal static Dictionary<string, string> FontFallbacks { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Variable fonts to their non-variable equivalents
        ["Segoe UI Variable"] = "Segoe UI",
        ["Segoe UI Variable Display"] = "Segoe UI",
        ["Segoe UI Variable Text"] = "Segoe UI",
        ["Segoe UI Variable Small"] = "Segoe UI",
        // Common premium font fallbacks
        ["Avenir Next LT Pro"] = "Century Gothic",
        ["AvenirNext LT Pro"] = "Century Gothic",
        ["AvenirNext LT Pro Medium"] = "Century Gothic",
        ["Eras Light ITC"] = "Century Gothic",
        ["Eras Medium ITC"] = "Century Gothic",
        ["Sagona"] = "Georgia",
        ["Sagona ExtraLight"] = "Georgia",
        ["Sagona Light"] = "Georgia",
    };

    // Common font style suffixes to strip when looking for base family
    // Note: Do NOT include vendor suffixes like " MT", " Pro", " LT", " ITC" as these are part of the font name
    internal static string[] StyleSuffixes { get; } =
    [
        " Condensed", " Compressed", " Narrow", " Extended", " Wide",
        " Black", " Heavy", " ExtraBold", " Bold", " Semibold", " Demi",
        " Medium", " Regular", " Book", " Light", " Thin", " Hairline",
        " Italic", " Oblique", " Cond"
    ];

    // Weight suffixes that are "medium-weight" - when Bold is requested on these fonts,
    // we should look for the Bold variant of the base family instead
    static string[] mediumWeightSuffixes =
    [
        " Semibold", " Demi", " Medium", " Regular", " Book"
    ];

    internal static bool HasMediumWeightSuffix(string fontFamily)
    {
        foreach (var suffix in mediumWeightSuffixes)
        {
            if (fontFamily.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static string StripWeightSuffixes(string fontFamily)
    {
        var result = fontFamily;
        bool changed;
        do
        {
            changed = false;
            foreach (var suffix in StyleSuffixes)
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[..^suffix.Length];
                    changed = true;
                }
            }
        } while (changed);

        return result.Trim();
    }

    /// <summary>
    /// Determines if a font name implies bold weight (for adjusting style when resolving from caches).
    /// </summary>
    internal static bool ImpliesBold(string fontFamily) =>
        fontFamily.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
        fontFamily.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
        fontFamily.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ||
        fontFamily.Contains("Medium", StringComparison.OrdinalIgnoreCase) ||
        fontFamily.Contains("Demi", StringComparison.OrdinalIgnoreCase) ||
        fontFamily.Contains("Semibold", StringComparison.OrdinalIgnoreCase);
}
