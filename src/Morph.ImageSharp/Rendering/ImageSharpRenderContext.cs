/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class ImageSharpRenderContext(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility = null, double fontWidthScale = 1.0, Func<string, string?>? fontFallback = null, string? fontDirectory = null, bool? deterministicRendering = null)
    : RenderContextBase(pageSettings, dpi, compatibility, fontWidthScale, fontFallback, fontDirectory, deterministicRendering),
        IDisposable
{
    // Pre-seeded with the embedded Aptos family so the (name, style) lookup at the top of
    // GetFontFamily hits immediately — no LoadFilesIntoSharedCollection or system-fonts
    // walk for the common bold/italic combinations of the default font.
    Dictionary<(string, FontStyle), FontFamily> fontFamilyCache = SeedFontFamilyCache();

    static Dictionary<(string, FontStyle), FontFamily> SeedFontFamilyCache()
    {
        var cache = new Dictionary<(string, FontStyle), FontFamily>();
        foreach (var family in embeddedFontCollection.Families)
        {
            foreach (var style in new[] {FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.BoldItalic})
            {
                cache[(family.Name, style)] = family;
            }
        }
        return cache;
    }

    // Shared font collection for fonts loaded from file (cloud, Office, user caches)
    FontCollection sharedFontCollection = new();

    /// <summary>
    /// Index of every font file Morph can discover on the host (user, Office, M365 cloud,
    /// system) in a single name-keyed cache. See <see cref="FontCacheLoader.GetAllFontFiles"/>
    /// for the merge ordering.
    /// </summary>
    static readonly FontFileCache allFontsCache = new(FontCacheLoader.GetAllFontFiles(), OpenTypeReader.ReadFaces);

    /// <summary>
    /// FontCollection populated from <see cref="EmbeddedFonts"/> — the Aptos faces shipped
    /// inside <c>Morph.dll</c>. Held statically so the streams are parsed once per
    /// process; consulted as the last resort during family resolution.
    /// </summary>
    static readonly FontCollection embeddedFontCollection = LoadEmbeddedFontCollection();

    static FontCollection LoadEmbeddedFontCollection()
    {
        var collection = new FontCollection();
        foreach (var stream in EmbeddedFonts.OpenStreams())
        {
            using (stream)
            {
                collection.Add(stream);
            }
        }
        return collection;
    }

    static readonly ConcurrentDictionary<string, FontFileCache> directoryCaches = new(StringComparer.OrdinalIgnoreCase);

    static FontFileCache GetDirectoryCache(string fontDirectory) =>
        directoryCaches.GetOrAdd(
            System.IO.Path.GetFullPath(fontDirectory),
            path => new(FontCacheLoader.EnumerateFontFilesInDirectory(path, recursive: true), OpenTypeReader.ReadFaces));

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

            // Load every matching face from the merged host cache into the shared
            // collection so all style variants are available (e.g. Regular from user fonts
            // + Italic from cloud, both indexed in allFontsCache).
            LoadFilesIntoSharedCollection(allFontsCache, candidates);

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
        if (!cache.TryGet(candidates, out var faces))
        {
            return;
        }

        // Dedupe by file path: a single file may surface multiple faces (e.g. a .ttc with
        // many fonts, or a .ttf indexed under several names), but we only need to load
        // each path once — ImageSharp's FontCollection will surface every face inside.
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var face in faces)
        {
            if (!loaded.Add(face.Path))
            {
                continue;
            }

            try
            {
                if (face.Path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                {
                    sharedFontCollection.AddCollection(face.Path);
                }
                else
                {
                    sharedFontCollection.Add(face.Path);
                }
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
    public static float MeasureText(Font font, string text, KerningMode kerning = KerningMode.Standard)
    {
        var options = new TextOptions(font)
        {
            Dpi = 72,
            KerningMode = kerning
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
