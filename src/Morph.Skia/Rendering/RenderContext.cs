/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext : RenderContextBase, IDisposable
{
    Dictionary<(string, int, SKFontStyleSlant), SKTypeface> typefaceCache = [];

    static Lazy<FontFileCache> cloudFontsCache = new(() => new(FontCacheLoader.GetCloudFontFiles(), ReadFamilyName));
    static Lazy<FontFileCache> officeFontsCache = new(() => new(FontCacheLoader.GetOfficeFontFiles(), ReadFamilyName));
    static Lazy<FontFileCache> userFontsCache = new(() => new(FontCacheLoader.GetUserFontFiles(), ReadFamilyName));

    static string? ReadFamilyName(string fontFile)
    {
        using var tf = SKTypeface.FromFile(fontFile);
        return tf?.FamilyName;
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
