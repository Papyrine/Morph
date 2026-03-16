using WordRender.Rendering;

/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext : RenderContextBase, IDisposable
{
    Dictionary<string, SKTypeface> typefaceCache = new();

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
            using var tf = SKTypeface.FromFile(fontFile);
            if (tf == null)
            {
                continue;
            }

            if (!result.TryGetValue(tf.FamilyName, out var files))
            {
                files = new();
                result[tf.FamilyName] = files;
            }

            files.Add(fontFile);
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public RenderContext(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility = null, double fontWidthScale = 1.0)
        : base(pageSettings, dpi, compatibility, fontWidthScale)
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

        var key = $"{effectiveFontFamily}_{style.Weight}_{style.Slant}";

        if (!typefaceCache.TryGetValue(key, out var typeface))
        {
            typeface = SKTypeface.FromFamilyName(effectiveFontFamily, style);

            // If font wasn't found (fell back to default), try stripping style suffixes, then font caches
            // Compare against effectiveFontFamily since we may have stripped weight suffixes
            if (typeface.FamilyName != effectiveFontFamily && !typeface.FamilyName.StartsWith(effectiveFontFamily, StringComparison.OrdinalIgnoreCase))
            {
                // Try stripping style suffixes (e.g. "Avenir Next LT Pro Light" → "Avenir Next LT Pro")
                var strippedName = FontHelpers.StripWeightSuffixes(fontFamily);
                if (!string.IsNullOrEmpty(strippedName) && strippedName != fontFamily && strippedName != effectiveFontFamily)
                {
                    var strippedTypeface = SKTypeface.FromFamilyName(strippedName, style);
                    if (strippedTypeface.FamilyName.Equals(strippedName, StringComparison.OrdinalIgnoreCase)
                        || strippedTypeface.FamilyName.StartsWith(strippedName, StringComparison.OrdinalIgnoreCase))
                    {
                        typeface = strippedTypeface;
                        typefaceCache[key] = typeface;
                        return typeface;
                    }
                }

                var userTypeface = TryLoadFromFontCache(userFontsCache.Value, effectiveFontFamily, style)
                                   ?? TryLoadFromFontCache(userFontsCache.Value, fontFamily, style);
                if (userTypeface != null)
                {
                    typeface = userTypeface;
                }
                else
                {
                    var officeTypeface = TryLoadFromFontCache(officeFontsCache.Value, effectiveFontFamily, style)
                                         ?? TryLoadFromFontCache(officeFontsCache.Value, fontFamily, style);
                    if (officeTypeface != null)
                    {
                        typeface = officeTypeface;
                    }
                    else
                    {
                        var cloudTypeface = TryLoadFromFontCache(cloudFontsCache.Value, effectiveFontFamily, style)
                                            ?? TryLoadFromFontCache(cloudFontsCache.Value, fontFamily, style);
                        if (cloudTypeface != null)
                        {
                            typeface = cloudTypeface;
                        }
                        else if (FontHelpers.FontFallbacks.TryGetValue(effectiveFontFamily, out var fallbackFont)
                                 || FontHelpers.FontFallbacks.TryGetValue(fontFamily, out fallbackFont)
                                 || (!string.IsNullOrEmpty(strippedName) && FontHelpers.FontFallbacks.TryGetValue(strippedName, out fallbackFont)))
                        {
                            // Try known fallback font
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
                        else
                        {
                            throw new InvalidOperationException($"Font '{fontFamily}' not found. Checked system fonts, user fonts, Office fonts, and cloud cache.");
                        }
                    }
                }
            }

            typefaceCache[key] = typeface;
        }

        return typeface;
    }

    static string[] styleSuffixes => FontHelpers.StyleSuffixes;

    static SKTypeface? TryLoadFromFontCache(Dictionary<string, string[]> fontCache, string fontFamily, SKFontStyle style)
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

        // Handle colors like "000000" (6 chars) or "FF000000" (8 chars with alpha)
        if (hexColor.Length == 6)
        {
            if (uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var rgb))
            {
                return new(
                    (byte) ((rgb >> 16) & 0xFF),
                    (byte) ((rgb >> 8) & 0xFF),
                    (byte) (rgb & 0xFF)
                );
            }
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
