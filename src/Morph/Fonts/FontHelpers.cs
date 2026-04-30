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
        ["Grandview Display"] = "Grandview",
        ["Cambria Math"] = "Cambria",
    };

    // Common font style suffixes to strip when looking for base family
    // Note: Do NOT include vendor suffixes like " MT", " Pro", " LT", " ITC" as these are part of the font name
    static string[] StyleSuffixes { get; } =
    [
        " Condensed", " Compressed", " Narrow", " Extended", " Wide",
        " UltraBlack", " Black", " Heavy",
        " UltraBold", " ExtraBold", " Demibold", " Bold", " Semibold", " Demi",
        " Medium", " Regular", " Book",
        " UltraLight", " ExtraLight", " Semilight", " Light", " Thin", " Hairline",
        " Italic", " Oblique", " Cond"
    ];

    // Weight suffixes that are "medium-weight" - when Bold is requested on these fonts,
    // we should look for the Bold variant of the base family instead
    static string[] mediumWeightSuffixes =
    [
        " Semibold", " Demi", " Medium", " Regular", " Book"
    ];

    /// <summary>
    /// Maps weight-name suffixes to OS/2 <c>usWeightClass</c> values, so a request for
    /// <c>"Segoe UI Semilight"</c> can be scored against face metadata as weight 350
    /// rather than the generic 400/700 derived from the bold flag alone.
    /// </summary>
    static readonly Dictionary<string, int> weightFromSuffix = new(StringComparer.OrdinalIgnoreCase)
    {
        [" Hairline"] = 100,
        [" Thin"] = 100,
        [" UltraLight"] = 200,
        [" ExtraLight"] = 200,
        [" Light"] = 300,
        [" Semilight"] = 350,
        [" Regular"] = 400,
        [" Book"] = 400,
        [" Medium"] = 500,
        [" Demi"] = 600,
        [" Demibold"] = 600,
        [" Semibold"] = 600,
        [" Bold"] = 700,
        [" UltraBold"] = 800,
        [" ExtraBold"] = 800,
        [" Heavy"] = 900,
        [" Black"] = 900,
        [" UltraBlack"] = 950,
    };

    /// <summary>
    /// Inspects a font family name for a trailing weight word and returns the corresponding
    /// OS/2 weight class, or <c>null</c> when no recognised suffix is present.
    /// </summary>
    internal static int? InferWeightFromName(string fontFamily)
    {
        foreach (var (suffix, weight) in weightFromSuffix)
        {
            if (fontFamily.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return weight;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the OS/2 weight class to score against when resolving a font, derived from
    /// the requested name's suffix when present, otherwise from the bold flag.
    /// </summary>
    internal static int ResolveTargetWeight(string fontFamily, bool bold) =>
        InferWeightFromName(fontFamily) ?? (bold ? 700 : 400);

    /// <summary>
    /// Score a face against the requested weight/italic/width. Lower is closer; the
    /// resolver picks the face with the smallest score.
    /// </summary>
    internal static int ScoreFace(FontFace face, int targetWeight, bool targetItalic, int targetWidth = 5)
    {
        // Italic mismatch dominates: we'd rather take a regular face at the wrong weight
        // than an italic face at the right weight when an upright was requested.
        var italicPenalty = face.Italic == targetItalic ? 0 : 10_000;

        // Width mismatch is significant but secondary to italic.
        var widthPenalty = Math.Abs(face.Width - targetWidth) * 1_000;

        // Weight is a smooth distance so 350 vs 400 (50) beats 350 vs 700 (350).
        var weightPenalty = Math.Abs(face.Weight - targetWeight);

        return italicPenalty + widthPenalty + weightPenalty;
    }

    /// <summary>
    /// Picks the face whose metrics best match the requested weight/italic/width from a
    /// list returned by <see cref="FontFileCache.TryGet(string, out FontFace[])"/>.
    /// </summary>
    internal static FontFace? PickBestFace(IEnumerable<FontFace> faces, int targetWeight, bool targetItalic, int targetWidth = 5)
    {
        FontFace? best = null;
        var bestScore = int.MaxValue;
        foreach (var face in faces)
        {
            var score = ScoreFace(face, targetWeight, targetItalic, targetWidth);
            if (score < bestScore)
            {
                best = face;
                bestScore = score;
            }
        }

        return best;
    }

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
    static readonly string[] boldKeywords = ["Bold", "Black", "Heavy", "Medium", "Demi", "Semibold"];

    internal static bool ImpliesBold(string fontFamily)
    {
        foreach (var keyword in boldKeywords)
        {
            if (fontFamily.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes the candidate font family names to try when resolving a font.
    /// Returns distinct names in priority order: effectiveFontFamily, original fontFamily, stripped name.
    /// </summary>
    internal static FontNameCandidates GetCandidateNames(string fontFamily, bool bold)
    {
        var effectiveFontFamily = fontFamily;
        if (bold &&
            HasMediumWeightSuffix(fontFamily))
        {
            var baseName = StripWeightSuffixes(fontFamily);
            if (!string.IsNullOrEmpty(baseName) &&
                baseName != fontFamily)
            {
                effectiveFontFamily = baseName;
            }
        }

        var strippedName = StripWeightSuffixes(fontFamily);
        if (string.IsNullOrEmpty(strippedName) ||
            strippedName == fontFamily ||
            strippedName == effectiveFontFamily)
        {
            strippedName = null;
        }

        return new(effectiveFontFamily, fontFamily, strippedName);
    }

    /// <summary>
    /// Finds a fallback font name for any of the candidate names.
    /// Returns null if no fallback is configured.
    /// </summary>
    internal static string? FindFallback(FontNameCandidates candidates)
    {
        if (FontFallbacks.TryGetValue(candidates.Effective, out var fallback))
        {
            return fallback;
        }

        if (candidates.Effective != candidates.Original &&
            FontFallbacks.TryGetValue(candidates.Original, out fallback))
        {
            return fallback;
        }

        if (candidates.Stripped != null &&
            FontFallbacks.TryGetValue(candidates.Stripped, out fallback))
        {
            return fallback;
        }

        return null;
    }
}
