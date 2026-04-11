/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext : RenderContextBase, IDisposable
{
    Dictionary<(string, int, SKFontStyleSlant), SKTypeface> typefaceCache = [];

    // Cloud fonts cache from Microsoft 365
    static Lazy<Dictionary<string, string[]>> cloudFontsCache = new(() => LoadFontCache(FontCacheLoader.GetCloudFontFiles()));

    // Office private fonts (bundled with Microsoft Office)
    static Lazy<Dictionary<string, string[]>> officeFontsCache = new(() => LoadFontCache(FontCacheLoader.GetOfficeFontFiles()));

    // User-installed fonts (installed without admin rights)
    static Lazy<Dictionary<string, string[]>> userFontsCache = new(() => LoadFontCache(FontCacheLoader.GetUserFontFiles()));

    static Dictionary<string, string[]> LoadFontCache(IEnumerable<string> fontFiles)
    {
        var temp = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var fontFile in fontFiles)
        {
            using var tf = SKTypeface.FromFile(fontFile);
            if (tf == null)
            {
                continue;
            }

            if (!temp.TryGetValue(tf.FamilyName, out var files))
            {
                files = [];
                temp[tf.FamilyName] = files;
            }

            files.Add(fontFile);
        }

        var result = new Dictionary<string, string[]>(temp.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in temp)
        {
            result[kvp.Key] = kvp.Value.ToArray();
        }

        return result;
    }

    public RenderContext(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility = null, double fontWidthScale = 1.0, Func<string, string?>? fontFallback = null)
        : base(pageSettings, dpi, compatibility, fontWidthScale, fontFallback)
    {
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
            // Try each candidate name against system fonts
            typeface = TryResolveFromSystem(candidates, style);

            if (typeface == null)
            {
                // Merge all font caches so all style variants are available
                // (e.g. Regular from user fonts + Italic from cloud)
                var mergedFiles = GetMergedFontFiles(candidates,
                    userFontsCache.Value, officeFontsCache.Value, cloudFontsCache.Value);
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
                    throw new InvalidOperationException($"Font '{fontFamily}' not found. Checked system fonts, user fonts, Office fonts, and cloud cache.");
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

            typefaceCache[key] = typeface;
        }

        return typeface;
    }

    static SKTypeface? TryResolveFromSystem(FontNameCandidates candidates, SKFontStyle style)
    {
        foreach (var name in CandidateNames(candidates))
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

    static string[]? GetMergedFontFiles(FontNameCandidates candidates, params Dictionary<string, string[]>[] caches)
    {
        List<string>? merged = null;
        foreach (var cache in caches)
        {
            foreach (var name in CandidateNames(candidates))
            {
                if (cache.TryGetValue(name, out var files))
                {
                    merged ??= [];
                    merged.AddRange(files);
                }
                else
                {
                    var baseName = FontHelpers.StripWeightSuffixes(name);
                    if (baseName != name && cache.TryGetValue(baseName, out files))
                    {
                        merged ??= [];
                        merged.AddRange(files);
                    }
                }
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

        if (isCondensed == wantCondensed && isExtended == wantExtended)
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

        if (!wantBold && tf.FontStyle.Weight is >= 400 and <= 500)
        {
            score += 1;
        }

        return score;
    }

    static SKTypeface? TryLoadFromAnyCandidateName(Dictionary<string, string[]> fontCache, FontNameCandidates candidates, SKFontStyle style)
    {
        foreach (var name in CandidateNames(candidates))
        {
            var result = TryLoadFromFontCache(fontCache, name, style);
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

    static SKTypeface? TryLoadFromFontCache(Dictionary<string, string[]> fontCache, string fontFamily, SKFontStyle style)
    {
        // Try exact match first
        if (!fontCache.TryGetValue(fontFamily, out var fontFiles))
        {
            // Try stripping style suffixes to find base family
            var baseName = FontHelpers.StripWeightSuffixes(fontFamily);

            if (baseName != fontFamily && fontCache.TryGetValue(baseName, out fontFiles))
            {
                // Found base family, adjust style based on original name
                var weight = style.Weight;
                var width = style.Width;

                // Determine base weight from font name
                var baseWeight = (int) SKFontStyleWeight.Normal;
                if (FontHelpers.ImpliesBold(fontFamily))
                {
                    // Use the weight based on specific name match
                    if (fontFamily.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                        fontFamily.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
                        fontFamily.Contains("Heavy", StringComparison.OrdinalIgnoreCase))
                    {
                        baseWeight = (int) SKFontStyleWeight.Bold;
                    }
                    else
                    {
                        baseWeight = (int) SKFontStyleWeight.SemiBold;
                    }
                }
                else if (fontFamily.Contains("Light", StringComparison.OrdinalIgnoreCase) ||
                         fontFamily.Contains("Thin", StringComparison.OrdinalIgnoreCase))
                {
                    baseWeight = (int) SKFontStyleWeight.Light;
                }

                // Use the heavier of the requested weight and the font name's weight
                weight = Math.Max(weight, baseWeight);

                if (fontFamily.Contains("Condensed", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Narrow", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Compressed", StringComparison.OrdinalIgnoreCase))
                {
                    width = (int) SKFontStyleWidth.Condensed;
                }

                style = new(weight, width, style.Slant);
            }
            else
            {
                return null;
            }
        }

        // Try to find best matching font file based on style
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

                // Score based on style match
                var score = 0;
                var isBold = tf.FontStyle.Weight >= 600;
                var isItalic = tf.FontStyle.Slant != SKFontStyleSlant.Upright;
                var isCondensed = tf.FontStyle.Width <= (int) SKFontStyleWidth.SemiCondensed;
                var isExtended = tf.FontStyle.Width >= (int) SKFontStyleWidth.SemiExpanded;

                var wantBold = style.Weight >= 600;
                var wantItalic = style.Slant != SKFontStyleSlant.Upright;
                var wantCondensed = style.Width <= (int) SKFontStyleWidth.SemiCondensed;
                var wantExtended = style.Width >= (int) SKFontStyleWidth.SemiExpanded;

                // Width matching is most important for visual accuracy
                if (isCondensed == wantCondensed && isExtended == wantExtended)
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

                // Prefer regular weight for non-bold requests
                if (!wantBold && tf.FontStyle.Weight is >= 400 and <= 500)
                {
                    score += 1;
                }

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

    public SKFont CreateFont(RunProperties props)
    {
        var typeface = GetTypeface(props.FontFamily, props.Bold, props.Italic);
        var fontSize = (float) props.FontSizePoints;

        // Subscript and superscript use reduced font size (approximately 58% per OpenXML convention)
        if (props.VerticalAlignment != VerticalRunAlignment.Baseline)
        {
            fontSize *= 0.58f;
        }

        return new(typeface, fontSize * Scale)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias,
            Hinting = SKFontHinting.Normal
        };
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
    public SKFont CreateFontFromTypeface(SKTypeface typeface, float fontSizePoints) =>
        new(typeface, fontSizePoints * Scale)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias,
            Hinting = SKFontHinting.Normal
        };

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
