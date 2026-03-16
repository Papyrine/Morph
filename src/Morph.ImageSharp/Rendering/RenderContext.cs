using WordRender.Rendering;

/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext : RenderContextBase, IDisposable
{
    Dictionary<string, FontFamily> fontFamilyCache = new();

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

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public RenderContext(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility = null, double fontWidthScale = 1.0)
        : base(pageSettings, dpi, compatibility, fontWidthScale)
    {
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

        // If bold is requested and font name has a medium/semibold weight suffix,
        // try to find the Bold variant of the base family instead
        var effectiveFontFamily = fontFamily;
        if (bold && FontHelpers.HasMediumWeightSuffix(fontFamily))
        {
            var baseName = FontHelpers.StripWeightSuffixes(fontFamily);
            if (!string.IsNullOrEmpty(baseName) && baseName != fontFamily)
            {
                effectiveFontFamily = baseName;
            }
        }

        var key = $"{effectiveFontFamily}_{style}";

        if (!fontFamilyCache.TryGetValue(key, out var resolvedFamily))
        {
            // Try system fonts first
            if (SystemFonts.TryGet(effectiveFontFamily, out resolvedFamily))
            {
                fontFamilyCache[key] = resolvedFamily;
                return resolvedFamily;
            }

            // If we stripped weight suffixes, also try the original family name in system fonts
            if (effectiveFontFamily != fontFamily && SystemFonts.TryGet(fontFamily, out resolvedFamily))
            {
                fontFamilyCache[key] = resolvedFamily;
                return resolvedFamily;
            }

            // Try shared collection (previously loaded from file caches)
            if (sharedFontCollection.TryGet(effectiveFontFamily, out resolvedFamily))
            {
                fontFamilyCache[key] = resolvedFamily;
                return resolvedFamily;
            }

            if (effectiveFontFamily != fontFamily && sharedFontCollection.TryGet(fontFamily, out resolvedFamily))
            {
                fontFamilyCache[key] = resolvedFamily;
                return resolvedFamily;
            }

            // Try user fonts, Office fonts, then cloud cache
            var loaded = TryLoadFromFontCache(userFontsCache.Value, effectiveFontFamily, style)
                         ?? TryLoadFromFontCache(userFontsCache.Value, fontFamily, style);

            if (loaded == null)
            {
                loaded = TryLoadFromFontCache(officeFontsCache.Value, effectiveFontFamily, style)
                         ?? TryLoadFromFontCache(officeFontsCache.Value, fontFamily, style);
            }

            if (loaded == null)
            {
                loaded = TryLoadFromFontCache(cloudFontsCache.Value, effectiveFontFamily, style)
                         ?? TryLoadFromFontCache(cloudFontsCache.Value, fontFamily, style);
            }

            if (loaded != null)
            {
                resolvedFamily = loaded.Value;
            }
            else if (FontHelpers.FontFallbacks.TryGetValue(effectiveFontFamily, out var fallbackFont)
                     || FontHelpers.FontFallbacks.TryGetValue(fontFamily, out fallbackFont))
            {
                // Try known fallback font
                if (SystemFonts.TryGet(fallbackFont, out resolvedFamily))
                {
                    // Found fallback in system fonts
                }
                else if (sharedFontCollection.TryGet(fallbackFont, out resolvedFamily))
                {
                    // Found fallback in shared collection
                }
                else
                {
                    throw new InvalidOperationException($"Font '{fontFamily}' not found and fallback '{fallbackFont}' also not available.");
                }
            }
            else
            {
                throw new InvalidOperationException($"Font '{fontFamily}' not found. Checked system fonts, user fonts, Office fonts, and cloud cache.");
            }

            fontFamilyCache[key] = resolvedFamily;
        }

        return resolvedFamily;
    }

    static string[] styleSuffixes => FontHelpers.StyleSuffixes;

    FontFamily? TryLoadFromFontCache(Dictionary<string, string[]> fontCache, string fontFamily, FontStyle style)
    {
        // Try exact match first
        if (!fontCache.TryGetValue(fontFamily, out var fontFiles))
        {
            // Try stripping style suffixes to find base family
            var baseName = fontFamily;
            foreach (var suffix in styleSuffixes)
            {
                if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName[..^suffix.Length];
                }
            }

            // Also try stripping common multi-word suffixes
            foreach (var suffix in styleSuffixes)
            {
                if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName[..^suffix.Length];
                }
            }

            if (baseName != fontFamily && fontCache.TryGetValue(baseName, out fontFiles))
            {
                // Found base family, adjust style based on original name
                // Determine if the original font name implies bold
                if (FontHelpers.ImpliesBold(fontFamily))
                {
                    // If bold wasn't already requested, add it based on font name
                    if (!style.HasFlag(FontStyle.Bold))
                    {
                        style |= FontStyle.Bold;
                    }
                }
            }
            else
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

        var style = FontStyle.Regular;
        if (props is { Bold: true, Italic: true })
        {
            style = FontStyle.BoldItalic;
        }
        else if (props.Bold)
        {
            style = FontStyle.Bold;
        }
        else if (props.Italic)
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
        if (hexColor.Length == 6)
        {
            if (uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
            {
                return Color.FromRgb(
                    (byte) ((rgb >> 16) & 0xFF),
                    (byte) ((rgb >> 8) & 0xFF),
                    (byte) (rgb & 0xFF)
                );
            }
        }

        return Color.Black;
    }

    public void Dispose() =>
        fontFamilyCache.Clear();
}
