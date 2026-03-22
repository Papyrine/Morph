using System.Diagnostics.CodeAnalysis;
// ReSharper disable UseCollectionExpression

/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
[SuppressMessage("Style", "IDE0028:Simplify collection initialization")]
sealed class RenderContext(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility = null, double fontWidthScale = 1.0, Func<string, string?>? fontFallback = null)
    : RenderContextBase(pageSettings, dpi, compatibility, fontWidthScale, fontFallback),
        IDisposable
{
    Dictionary<(string, FontStyle), FontFamily> fontFamilyCache = [];

    // Shared font collection for fonts loaded from file (cloud, Office, user caches)
    FontCollection sharedFontCollection = new();

    // Cloud fonts cache from Microsoft 365
    static Lazy<Dictionary<string, string[]>> cloudFontsCache = new(() => LoadFontCache(FontCacheLoader.GetCloudFontFiles()));

    // Office private fonts (bundled with Microsoft Office)
    static Lazy<Dictionary<string, string[]>> officeFontsCache = new(() => LoadFontCache(FontCacheLoader.GetOfficeFontFiles()));

    // User-installed fonts (installed without admin rights)
    static Lazy<Dictionary<string, string[]>> userFontsCache = new(() => LoadFontCache(FontCacheLoader.GetUserFontFiles()));

    static Dictionary<string, string[]> LoadFontCache(IEnumerable<string> fontFiles)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var fontFile in fontFiles)
        {
            try
            {
                var collection = new FontCollection();
                var family = collection.Add(fontFile);

                if (!result.TryGetValue(family.Name, out var files))
                {
                    files = new();
                    result[family.Name] = files;
                }

                files.Add(fontFile);
            }
            catch
            {
                // Ignore individual font load errors
            }
        }

        var final = new Dictionary<string, string[]>(result.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in result)
        {
            final[kvp.Key] = kvp.Value.ToArray();
        }

        return final;
    }

    public FontFamily GetFontFamily(string fontFamily, bool bold, bool italic)
    {
        var style = FontStyle.Regular;
        if (bold && italic)
        {
            style = FontStyle.BoldItalic;
        }
        else if (bold)
        {
            style = FontStyle.Bold;
        }
        else if (italic)
        {
            style = FontStyle.Italic;
        }

        var candidates = FontHelpers.GetCandidateNames(fontFamily, bold);
        var key = (candidates.Effective, style);

        if (!fontFamilyCache.TryGetValue(key, out var resolvedFamily))
        {
            // Try system fonts and shared collection with each candidate name
            if (TryResolveFromKnownSources(candidates, out resolvedFamily))
            {
                fontFamilyCache[key] = resolvedFamily;
                return resolvedFamily;
            }

            // Try user fonts, Office fonts, then cloud cache
            var loaded = TryLoadFromAnyCandidateName(userFontsCache.Value, candidates)
                         ?? TryLoadFromAnyCandidateName(officeFontsCache.Value, candidates)
                         ?? TryLoadFromAnyCandidateName(cloudFontsCache.Value, candidates);

            if (loaded != null)
            {
                resolvedFamily = loaded.Value;
            }
            else
            {
                var fallbackFont = FontHelpers.FindFallback(candidates) ?? FontFallback?.Invoke(fontFamily);
                if (fallbackFont == null)
                {
                    throw new InvalidOperationException($"Font '{fontFamily}' not found. Checked system fonts, user fonts, Office fonts, and cloud cache.");
                }

                if (!SystemFonts.TryGet(fallbackFont, out resolvedFamily) &&
                    !sharedFontCollection.TryGet(fallbackFont, out resolvedFamily))
                {
                    throw new InvalidOperationException($"Font '{fontFamily}' not found and fallback '{fallbackFont}' also not available.");
                }
            }

            fontFamilyCache[key] = resolvedFamily;
        }

        return resolvedFamily;
    }

    bool TryResolveFromKnownSources(FontNameCandidates candidates, out FontFamily resolved)
    {
        foreach (var name in CandidateNames(candidates))
        {
            if (SystemFonts.TryGet(name, out resolved) || sharedFontCollection.TryGet(name, out resolved))
            {
                return true;
            }
        }

        resolved = default;
        return false;
    }

    FontFamily? TryLoadFromAnyCandidateName(Dictionary<string, string[]> fontCache, FontNameCandidates candidates)
    {
        foreach (var name in CandidateNames(candidates))
        {
            var result = TryLoadFromFontCache(fontCache, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    static IEnumerable<string> CandidateNames(FontNameCandidates candidates)
    {
        yield return candidates.Effective;
        if (candidates.Original != candidates.Effective)
        {
            yield return candidates.Original;
        }

        if (candidates.Stripped != null)
        {
            yield return candidates.Stripped;
        }
    }

    FontFamily? TryLoadFromFontCache(Dictionary<string, string[]> fontCache, string fontFamily)
    {
        // Try exact match first
        if (!fontCache.TryGetValue(fontFamily, out var fontFiles))
        {
            // Try stripping style suffixes to find base family
            var baseName = FontHelpers.StripWeightSuffixes(fontFamily);

            if (baseName == fontFamily ||
                !fontCache.TryGetValue(baseName, out fontFiles))
            {
                return null;
            }
        }

        // Load all font files into the shared collection and find the best match
        FontFamily? bestFamily = null;

        foreach (var fontFile in fontFiles)
        {
            try
            {
                var family = sharedFontCollection.Add(fontFile);
                bestFamily = family;
            }
            catch
            {
                // Ignore individual font load errors
            }
        }

        if (bestFamily != null && sharedFontCollection.TryGet(bestFamily.Value.Name, out var resolved))
        {
            return resolved;
        }

        return bestFamily;
    }

    public Font GetFont(RunProperties props)
    {
        var family = GetFontFamily(props.FontFamily, props.Bold, props.Italic);
        var fontSize = (float) props.FontSizePoints;

        // Subscript and superscript use reduced font size (approximately 58% per OpenXML convention)
        if (props.VerticalAlignment != VerticalRunAlignment.Baseline)
        {
            fontSize *= 0.58f;
        }

        var bold = props.Bold || FontHelpers.ImpliesBold(props.FontFamily);
        var italic = props.Italic;

        var style = FontStyle.Regular;
        if (bold && italic)
        {
            style = FontStyle.BoldItalic;
        }
        else if (bold)
        {
            style = FontStyle.Bold;
        }
        else if (italic)
        {
            style = FontStyle.Italic;
        }

        return family.CreateFont(fontSize, style);
    }

    /// <summary>
    /// Creates a Font for a given font family name and size in points.
    /// </summary>
    public Font GetFontForFamily(string fontFamily, float sizePoints, bool bold, bool italic)
    {
        var family = GetFontFamily(fontFamily, bold, italic);

        var effectiveBold = bold || FontHelpers.ImpliesBold(fontFamily);

        var style = FontStyle.Regular;
        if (effectiveBold && italic)
        {
            style = FontStyle.BoldItalic;
        }
        else if (effectiveBold)
        {
            style = FontStyle.Bold;
        }
        else if (italic)
        {
            style = FontStyle.Italic;
        }

        return family.CreateFont(sizePoints, style);
    }

    /// <summary>
    /// Measures text width in points. Uses DPI=72 so pixels equal points.
    /// </summary>
    public static float MeasureText(Font font, string text)
    {
        var options = new TextOptions(font)
        {
            Dpi = 72
        };

        var advance = TextMeasurer.MeasureAdvance(text, options);
        return advance.Width;
    }

    /// <summary>
    /// Gets font height and baseline metrics in points.
    /// </summary>
    public static (float Height, float Baseline) GetFontMetrics(Font font)
    {
        var metrics = font.FontMetrics;
        var unitsPerEm = metrics.UnitsPerEm;
        var pointSize = font.Size;

        // Ascender is positive in design units
        var ascent = metrics.HorizontalMetrics.Ascender * pointSize / unitsPerEm;

        // Descender is negative in design units, we want positive value
        var descent = Math.Abs(metrics.HorizontalMetrics.Descender) * pointSize / unitsPerEm;

        var height = ascent + descent;

        return (height, ascent);
    }

    public static Color ParseColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor == "auto")
        {
            return Color.Black;
        }

        // Handle colors like "000000" (6 chars) or "FF000000" (8 chars with alpha)
        if (hexColor.Length == 6 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromRgb(
                (byte) ((rgb >> 16) & 0xFF),
                (byte) ((rgb >> 8) & 0xFF),
                (byte) (rgb & 0xFF)
            );
        }

        return Color.Black;
    }

    public void Dispose() =>
        fontFamilyCache.Clear();
}
