/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility = null, double fontWidthScale = 1.0, Func<string, string?>? fontFallback = null, string? fontDirectory = null, bool? deterministicRendering = null)
    : RenderContextBase(pageSettings, dpi, compatibility, fontWidthScale, fontFallback, fontDirectory, deterministicRendering),
        IDisposable
{
    Dictionary<(string, FontStyle), FontFamily> fontFamilyCache = [];

    // Shared font collection for fonts loaded from file (cloud, Office, user caches)
    FontCollection sharedFontCollection = new();

    static Lazy<FontFileCache> cloudFontsCache = new(() => new(FontCacheLoader.GetCloudFontFiles(), ReadFamilyName));
    static Lazy<FontFileCache> officeFontsCache = new(() => new(FontCacheLoader.GetOfficeFontFiles(), ReadFamilyName));
    static Lazy<FontFileCache> userFontsCache = new(() => new(FontCacheLoader.GetUserFontFiles(), ReadFamilyName));
    static Lazy<FontFileCache> systemFontsCache = new(() => new(FontCacheLoader.GetSystemFontFiles(), ReadFamilyName));

    static readonly ConcurrentDictionary<string, FontFileCache> directoryCaches = new(StringComparer.OrdinalIgnoreCase);

    static FontFileCache GetDirectoryCache(string fontDirectory) =>
        directoryCaches.GetOrAdd(
            System.IO.Path.GetFullPath(fontDirectory),
            path => new(FontCacheLoader.EnumerateFontFilesInDirectory(path, recursive: true), ReadFamilyName));

    static string ReadFamilyName(string fontFile) => new FontCollection().Add(fontFile).Name;

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
            if (FontDirectory != null)
            {
                var directoryCache = GetDirectoryCache(FontDirectory);
                LoadFilesIntoSharedCollection(directoryCache, candidates);

                if (TryResolveFromSharedCollection(candidates, style, out resolvedFamily) ||
                    TryResolveFromSharedCollection(candidates, requireStyle: null, out resolvedFamily))
                {
                    fontFamilyCache[key] = resolvedFamily;
                    return resolvedFamily;
                }

                var directoryFallbackFont = FontHelpers.FindFallback(candidates) ?? FontFallback?.Invoke(fontFamily);
                if (directoryFallbackFont != null)
                {
                    var fallbackCandidates = FontHelpers.GetCandidateNames(directoryFallbackFont, bold);
                    LoadFilesIntoSharedCollection(directoryCache, fallbackCandidates);

                    if (TryResolveFromSharedCollection(fallbackCandidates, style, out resolvedFamily) ||
                        TryResolveFromSharedCollection(fallbackCandidates, requireStyle: null, out resolvedFamily))
                    {
                        fontFamilyCache[key] = resolvedFamily;
                        return resolvedFamily;
                    }
                }

                throw new InvalidOperationException($"Font '{fontFamily}' not found in '{FontDirectory}'.");
            }

            // Load from all font caches into the shared collection so all style
            // variants are available (e.g. Regular from user fonts + Italic from cloud)
            LoadFilesIntoSharedCollection(userFontsCache.Value, candidates);
            LoadFilesIntoSharedCollection(officeFontsCache.Value, candidates);
            LoadFilesIntoSharedCollection(cloudFontsCache.Value, candidates);
            LoadFilesIntoSharedCollection(systemFontsCache.Value, candidates);

            // Prefer a family that has the exact style, but accept any matching-name family
            // if an exact variant isn't available. The caller will downgrade the requested
            // style in GetFont so we still render with the closest available variant.
            if (TryResolveFromKnownSources(candidates, style, out resolvedFamily) ||
                TryResolveFromKnownSources(candidates, requireStyle: null, out resolvedFamily))
            {
                fontFamilyCache[key] = resolvedFamily;
                return resolvedFamily;
            }

            var fallbackFont = FontHelpers.FindFallback(candidates) ?? FontFallback?.Invoke(fontFamily);
            if (fallbackFont == null)
            {
                throw new InvalidOperationException($"Font '{fontFamily}' not found. Checked:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", FontCacheLoader.GetSearchedPaths())}");
            }

            if (!SystemFonts.TryGet(fallbackFont, out resolvedFamily) &&
                !sharedFontCollection.TryGet(fallbackFont, out resolvedFamily))
            {
                throw new InvalidOperationException($"Font '{fontFamily}' not found and fallback '{fallbackFont}' also not available.");
            }

            fontFamilyCache[key] = resolvedFamily;
        }

        return resolvedFamily;
    }

    bool TryResolveFromKnownSources(FontNameCandidates candidates, FontStyle? requireStyle, out FontFamily resolved)
    {
        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            // Prefer shared collection (has fonts from all caches with proper style variants)
            if (sharedFontCollection.TryGet(name, out resolved) &&
                (requireStyle == null || resolved.TryGetMetrics(requireStyle.Value, out _)))
            {
                return true;
            }

            // Fall back to system fonts
            if (SystemFonts.TryGet(name, out resolved) &&
                (requireStyle == null || resolved.TryGetMetrics(requireStyle.Value, out _)))
            {
                return true;
            }
        }

        resolved = default;
        return false;
    }

    bool TryResolveFromSharedCollection(FontNameCandidates candidates, FontStyle? requireStyle, out FontFamily resolved)
    {
        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            if (sharedFontCollection.TryGet(name, out resolved) &&
                (requireStyle == null || resolved.TryGetMetrics(requireStyle.Value, out _)))
            {
                return true;
            }
        }

        resolved = default;
        return false;
    }

    void LoadFilesIntoSharedCollection(FontFileCache cache, FontNameCandidates candidates)
    {
        if (!cache.TryGet(candidates, out var fontFiles))
        {
            return;
        }

        foreach (var fontFile in fontFiles)
        {
            try
            {
                sharedFontCollection.Add(fontFile);
            }
            catch
            {
                // Ignore individual font load errors
            }
        }
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

        return family.CreateFont(fontSize, PickAvailableStyle(family, style));
    }

    /// <summary>
    /// Returns the closest available style from the given family. Prefers the requested
    /// style, then drops italic, then drops bold, finally returning Regular.
    /// </summary>
    static FontStyle PickAvailableStyle(FontFamily family, FontStyle requested)
    {
        if (family.TryGetMetrics(requested, out _))
        {
            return requested;
        }

        // Ordered fallback attempts per requested style
        var fallbackOrder = requested switch
        {
            FontStyle.BoldItalic => new[] {FontStyle.Bold, FontStyle.Italic, FontStyle.Regular},
            FontStyle.Bold => new[] {FontStyle.BoldItalic, FontStyle.Regular, FontStyle.Italic},
            FontStyle.Italic => new[] {FontStyle.BoldItalic, FontStyle.Regular, FontStyle.Bold},
            _ => new[] {FontStyle.Bold, FontStyle.Italic, FontStyle.BoldItalic}
        };
        foreach (var candidate in fallbackOrder)
        {
            if (family.TryGetMetrics(candidate, out _))
            {
                return candidate;
            }
        }

        return requested;
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

        return family.CreateFont(sizePoints, PickAvailableStyle(family, style));
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

        if (hexColor.Length == 6 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromRgb(
                (byte) ((rgb >> 16) & 0xFF),
                (byte) ((rgb >> 8) & 0xFF),
                (byte) (rgb & 0xFF)
            );
        }

        if (hexColor.Length == 8 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var argb))
        {
            return Color.FromRgba(
                (byte) ((argb >> 16) & 0xFF),
                (byte) ((argb >> 8) & 0xFF),
                (byte) (argb & 0xFF),
                (byte) ((argb >> 24) & 0xFF)
            );
        }

        return Color.Black;
    }

    public void Dispose() =>
        fontFamilyCache.Clear();
}
