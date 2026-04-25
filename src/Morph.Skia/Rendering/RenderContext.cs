using Morph;

/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext(
    PageSettings pageSettings,
    int dpi,
    CompatibilitySettings? compatibility = null,
    double fontWidthScale = 1.0,
    Func<string, string?>? fontFallback = null,
    string? fontDirectory = null,
    bool? deterministicRendering = null) :
    RenderContextBase(
        pageSettings,
        dpi,
        compatibility,
        fontWidthScale,
        fontFallback,
        fontDirectory,
        deterministicRendering),
    IDisposable
{
    Dictionary<(string, int, SKFontStyleSlant), SKTypeface> typefaceCache = [];

    static Lazy<FontFileCache> cloudFontsCache = new(() => new(FontCacheLoader.GetCloudFontFiles(), ReadFamilyNames));
    static Lazy<FontFileCache> officeFontsCache = new(() => new(FontCacheLoader.GetOfficeFontFiles(), ReadFamilyNames));
    static Lazy<FontFileCache> userFontsCache = new(() => new(FontCacheLoader.GetUserFontFiles(), ReadFamilyNames));
    static Lazy<FontFileCache> systemFontsCache = new(() => new(FontCacheLoader.GetSystemFontFiles(), ReadFamilyNames));

    static readonly ConcurrentDictionary<string, FontFileCache> directoryCaches = new(StringComparer.OrdinalIgnoreCase);

    static FontFileCache GetDirectoryCache(string fontDirectory) =>
        directoryCaches.GetOrAdd(
            Path.GetFullPath(fontDirectory),
            path => new(FontCacheLoader.EnumerateFontFilesInDirectory(path, recursive: true), ReadFamilyNames));

    static IEnumerable<string> ReadFamilyNames(string fontFile)
    {
        if (fontFile.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
        {
            var index = 0;
            while (true)
            {
                using var tf = SKTypeface.FromFile(fontFile, index);
                if (tf == null)
                {
                    yield break;
                }

                if (!string.IsNullOrEmpty(tf.FamilyName))
                {
                    yield return tf.FamilyName;
                }

                index++;
            }
        }
        else
        {
            using var tf = SKTypeface.FromFile(fontFile);
            if (tf?.FamilyName is { Length: > 0 } name)
            {
                yield return name;
            }
        }
    }

    public SKTypeface GetTypeface(string fontFamily, bool bold, bool italic)
    {
        var style = SKFontStyle.Normal;
        if (bold && italic)
        {
            style = SKFontStyle.BoldItalic;
        }
        else if (bold)
        {
            style = SKFontStyle.Bold;
        }
        else if (italic)
        {
            style = SKFontStyle.Italic;
        }

        var candidates = FontHelpers.GetCandidateNames(fontFamily, bold);
        var key = (candidates.Effective, style.Weight, style.Slant);

        if (!typefaceCache.TryGetValue(key, out var typeface))
        {
            if (FontDirectory != null)
            {
                typeface = ResolveFromDirectory(candidates, style);

                if (typeface == null)
                {
                    var fallbackFont = FontHelpers.FindFallback(candidates) ?? FontFallback?.Invoke(fontFamily);
                    if (fallbackFont != null)
                    {
                        typeface = ResolveFromDirectory(FontHelpers.GetCandidateNames(fallbackFont, bold), style);
                    }
                }

                if (typeface == null)
                {
                    throw new InvalidOperationException($"Font '{fontFamily}' not found in '{FontDirectory}'.");
                }
            }
            else
            {
                // Try each candidate name against system fonts
                typeface = TryResolveFromSystem(candidates, style);

                if (typeface == null)
                {
                    // Merge all font caches so all style variants are available
                    // (e.g. Regular from user fonts + Italic from cloud)
                    var mergedFiles = GetMergedFontFiles(candidates,
                        userFontsCache.Value, officeFontsCache.Value, cloudFontsCache.Value, systemFontsCache.Value);
                    if (mergedFiles != null)
                    {
                        typeface = FindBestMatch(mergedFiles, style);
                    }
                }

                if (typeface == null)
                {
                    var fallbackFont = FontHelpers.FindFallback(candidates) ?? FontFallback?.Invoke(fontFamily);
                    if (fallbackFont == null)
                    {
                        throw new InvalidOperationException($"Font '{fontFamily}' not found. Checked:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", FontCacheLoader.GetSearchedPaths())}");
                    }

                    var fallbackTypeface = SKTypeface.FromFamilyName(fallbackFont, style);
                    if (fallbackTypeface.FamilyName.Equals(fallbackFont, StringComparison.OrdinalIgnoreCase))
                    {
                        typeface = fallbackTypeface;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Font '{fontFamily}' not found and fallback '{fallbackFont}' also not available.");
                    }
                }
            }

            typefaceCache[key] = typeface;
        }

        return typeface;
    }

    SKTypeface? ResolveFromDirectory(FontNameCandidates candidates, SKFontStyle style)
    {
        var cache = GetDirectoryCache(FontDirectory!);
        if (cache.TryGet(candidates, out var files))
        {
            return FindBestMatch(files, style);
        }

        return null;
    }

    static SKTypeface? TryResolveFromSystem(FontNameCandidates candidates, SKFontStyle style)
    {
        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            var typeface = SKTypeface.FromFamilyName(name, style);
            if (typeface.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase)
                || typeface.FamilyName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                // Verify the returned typeface actually matches the requested style.
                // System fonts may return a regular face when italic isn't available,
                // causing us to miss italic variants in the cloud/Office font caches.
                var wantItalic = style.Slant != SKFontStyleSlant.Upright;
                var gotItalic = typeface.FontStyle.Slant != SKFontStyleSlant.Upright;
                if (wantItalic != gotItalic)
                {
                    typeface.Dispose();
                    continue;
                }

                return typeface;
            }
        }

        return null;
    }

    static string[]? GetMergedFontFiles(FontNameCandidates candidates, params FontFileCache[] caches)
    {
        List<string>? merged = null;
        foreach (var cache in caches)
        {
            if (cache.TryGet(candidates, out var files))
            {
                merged ??= [];
                merged.AddRange(files);
            }
        }

        return merged?.ToArray();
    }

    static SKTypeface? FindBestMatch(string[] fontFiles, SKFontStyle style)
    {
        SKTypeface? bestMatch = null;
        var bestScore = -1;

        foreach (var fontFile in fontFiles)
        {
            try
            {
                var tf = SKTypeface.FromFile(fontFile);
                if (tf == null)
                {
                    continue;
                }

                var score = ScoreTypeface(tf, style);

                if (score > bestScore)
                {
                    bestMatch?.Dispose();
                    bestMatch = tf;
                    bestScore = score;
                }
                else
                {
                    tf.Dispose();
                }
            }
            catch
            {
                // Ignore individual font load errors
            }
        }

        return bestMatch;
    }

    static int ScoreTypeface(SKTypeface tf, SKFontStyle style)
    {
        var score = 0;
        var isBold = tf.FontStyle.Weight >= 600;
        var isItalic = tf.FontStyle.Slant != SKFontStyleSlant.Upright;
        var isCondensed = tf.FontStyle.Width <= (int) SKFontStyleWidth.SemiCondensed;
        var isExtended = tf.FontStyle.Width >= (int) SKFontStyleWidth.SemiExpanded;

        var wantBold = style.Weight >= 600;
        var wantItalic = style.Slant != SKFontStyleSlant.Upright;
        var wantCondensed = style.Width <= (int) SKFontStyleWidth.SemiCondensed;
        var wantExtended = style.Width >= (int) SKFontStyleWidth.SemiExpanded;

        if (isCondensed == wantCondensed &&
            isExtended == wantExtended)
        {
            score += 4;
        }

        if (isBold == wantBold)
        {
            score += 2;
        }

        if (isItalic == wantItalic)
        {
            score += 1;
        }

        if (!wantBold &&
            tf.FontStyle.Weight is >= 400 and <= 500)
        {
            score += 1;
        }

        return score;
    }

    public SKFont CreateFont(RunProperties props)
    {
        var typeface = GetTypeface(props.FontFamily, props.Bold, props.Italic);
        var fontSize = (float) props.FontSizePoints;

        // Subscript and superscript use reduced font size (approximately 58% per OpenXML convention)
        if (props.VerticalAlignment != VerticalRunAlignment.Baseline)
        {
            fontSize *= 0.58f;
        }

        var font = new SKFont(typeface, fontSize * Scale);
        ApplyRenderingMode(font);
        return font;
    }

    public static SKPaint CreateTextPaint(RunProperties props) =>
        new()
        {
            IsAntialias = true,
            Color = ParseColor(props.ColorHex)
        };

    /// <summary>
    /// Creates an SKFont with consistent rendering properties from a typeface and font size.
    /// </summary>
    public SKFont CreateFontFromTypeface(SKTypeface typeface, float fontSizePoints)
    {
        var font = new SKFont(typeface, fontSizePoints * Scale);
        ApplyRenderingMode(font);
        return font;
    }

    /// <summary>
    /// Applies hinting / subpixel / edging settings. When deterministic rendering is enabled
    /// (via <see cref="ConversionOptions.DeterministicRendering"/> or the
    /// <see cref="DefaultFontSettings.DeterministicRendering"/> static fallback), falls back to
    /// integer-positioned greyscale anti-aliasing so output is identical across machines;
    /// otherwise uses the platform's full-fidelity subpixel LCD rendering.
    /// </summary>
    void ApplyRenderingMode(SKFont font)
    {
        if (DeterministicRendering)
        {
            font.Subpixel = false;
            font.Edging = SKFontEdging.Antialias;
            font.Hinting = SKFontHinting.None;
        }
        else
        {
            font.Subpixel = true;
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Hinting = SKFontHinting.Normal;
        }
    }

    static SKColor ParseColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) || hexColor == "auto")
        {
            return SKColors.Black;
        }

        if (hexColor.Length == 6 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
        {
            return new(
                (byte) ((rgb >> 16) & 0xFF),
                (byte) ((rgb >> 8) & 0xFF),
                (byte) (rgb & 0xFF)
            );
        }

        if (hexColor.Length == 8 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var argb))
        {
            return new(
                (byte) ((argb >> 16) & 0xFF),
                (byte) ((argb >> 8) & 0xFF),
                (byte) (argb & 0xFF),
                (byte) ((argb >> 24) & 0xFF)
            );
        }

        return SKColors.Black;
    }

    public void Dispose()
    {
        foreach (var typeface in typefaceCache.Values)
        {
            typeface.Dispose();
        }

        typefaceCache.Clear();
    }
}
