namespace WordRender;

/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext : IDisposable
{
    Dictionary<string, SKTypeface> typefaceCache = new();

    // Font fallback mappings for fonts that may not be installed
    static Dictionary<string, string> fontFallbacks = new(StringComparer.OrdinalIgnoreCase)
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

    // Cloud fonts cache from Microsoft 365
    static Lazy<Dictionary<string, string[]>> cloudFontsCache = new(LoadCloudFontsCache);

    // Office private fonts (bundled with Microsoft Office)
    static Lazy<Dictionary<string, string[]>> officeFontsCache = new(LoadOfficeFontsCache);

    // User-installed fonts (installed without admin rights)
    static Lazy<Dictionary<string, string[]>> userFontsCache = new(LoadUserFontsCache);

    static Dictionary<string, string[]> LoadCloudFontsCache()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cloudFontsPath = Path.Combine(localAppData, "Microsoft", "FontCache", "4", "CloudFonts");

        if (Directory.Exists(cloudFontsPath))
        {
            foreach (var fontDir in Directory.GetDirectories(cloudFontsPath))
            {
                foreach (var fontFile in Directory.GetFiles(fontDir, "*.ttf"))
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
            }
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    static Dictionary<string, string[]> LoadOfficeFontsCache()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var officeFontsPath = @"C:\Program Files\Microsoft Office\root\vfs\Fonts\private";

        if (Directory.Exists(officeFontsPath))
        {
            foreach (var fontFile in Directory.GetFiles(officeFontsPath, "*.ttf"))
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
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    static Dictionary<string, string[]> LoadUserFontsCache()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userFontsPath = Path.Combine(localAppData, "Microsoft", "Windows", "Fonts");

        if (Directory.Exists(userFontsPath))
        {
            foreach (var fontFile in Directory.GetFiles(userFontsPath, "*.ttf")
                         .Concat(Directory.GetFiles(userFontsPath, "*.otf")))
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
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public PageSettings PageSettings { get; private set; }
    public CompatibilitySettings Compatibility { get; }
    public int Dpi { get; }
    public float Scale { get; }

    /// <summary>
    /// Scale factor for font width measurements. Values > 1.0 make text wider (earlier line wrapping).
    /// </summary>
    public float FontWidthScale { get; }

    // Header/footer space adjustments
    float headerSpace;
    float footerSpace;

    // Current position on the page (in points)
    public float CurrentY { get; set; }
    public int CurrentPageNumber { get; private set; } = 1;
    public int CurrentColumn { get; private set; }

    // Line numbering state
    int currentLineNumber = 1;

    // Contextual spacing state - tracks if the previous paragraph had contextual spacing
    public bool LastParagraphHadContextualSpacing { get; set; }

    /// <summary>
    /// Tracks the last paragraph's SpacingAfter for margin collapsing.
    /// When a paragraph has SpacingBefore, we collapse it with the previous SpacingAfter
    /// (use max instead of sum, similar to CSS margin collapsing).
    /// </summary>
    public float LastParagraphSpacingAfterPoints { get; set; }

    /// <summary>
    /// Tracks the last paragraph's style ID for contextual spacing.
    /// Contextual spacing only collapses spacing between paragraphs of the same style.
    /// </summary>
    public string? LastParagraphStyleId { get; set; }

    // Page dimensions in pixels (recalculated when page settings change)
    public int PageWidthPixels { get; private set; }
    public int PageHeightPixels { get; private set; }

    // Full content area bounds (before column division)
    float FullContentLeft => (float) PageSettings.MarginLeft;
    float FullContentTop => (float) PageSettings.MarginTop + headerSpace;
    float FullContentBottom => (float) (PageSettings.HeightPoints - PageSettings.MarginBottom) - footerSpace;

    // Current column content area bounds in points
    public float ContentLeft => FullContentLeft + CurrentColumn * ((float) PageSettings.ColumnWidth + (float) PageSettings.ColumnSpacing);
    public float ContentTop => FullContentTop;
    public float ContentBottom => FullContentBottom;
    public float ContentWidth => (float) PageSettings.ColumnWidth;
    public float ContentHeight => FullContentBottom - FullContentTop;

    public RenderContext(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility = null, double fontWidthScale = 1.0)
    {
        PageSettings = pageSettings;
        Compatibility = compatibility ?? new CompatibilitySettings();
        Dpi = dpi;
        Scale = dpi / 72f; // Points to pixels
        FontWidthScale = (float) fontWidthScale;

        PageWidthPixels = (int) (pageSettings.WidthPoints * Scale);
        PageHeightPixels = (int) (pageSettings.HeightPoints * Scale);

        CurrentY = ContentTop;
    }

    /// <summary>
    /// Sets the space reserved for header and footer content.
    /// Only adjusts content area if header/footer content actually overflows their designated space.
    /// </summary>
    public void SetHeaderFooterSpace(float headerHeight, float footerHeight)
    {
        // Header starts at HeaderDistance from top
        // If header extends past MarginTop, we need to push content down
        // Only apply if there's actual header content (height > 0)
        var headerEnd = (float) PageSettings.HeaderDistance + headerHeight;
        if (headerHeight > 0 && headerEnd > (float) PageSettings.MarginTop)
        {
            headerSpace = headerEnd - (float) PageSettings.MarginTop;
        }
        else
        {
            headerSpace = 0; // Header fits within the margin area or is empty
        }

        // Footer ends at FooterDistance from bottom (measured from bottom edge)
        // If footer extends past MarginBottom, we need to push content up
        // Only apply if there's actual footer content (height > 0)
        var footerEnd = (float) PageSettings.FooterDistance + footerHeight;
        if (footerHeight > 0 && footerEnd > (float) PageSettings.MarginBottom)
        {
            footerSpace = footerEnd - (float) PageSettings.MarginBottom;
        }
        else
        {
            footerSpace = 0; // Footer fits within the margin area or is empty
        }

        // Reset CurrentY to account for new header space
        CurrentY = ContentTop;
    }

    public void StartNewPage()
    {
        CurrentPageNumber++;
        CurrentColumn = 0;
        CurrentY = ContentTop;
    }

    /// <summary>
    /// Moves to the next column. Returns true if moved to next column, false if need new page.
    /// </summary>
    public bool MoveToNextColumn()
    {
        if (CurrentColumn < PageSettings.ColumnCount - 1)
        {
            CurrentColumn++;
            CurrentY = ContentTop;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resets to the first column (used for continuous section breaks).
    /// Does not reset CurrentY since continuous sections flow without interruption.
    /// </summary>
    public void ResetColumn() =>
        // Note: Do NOT reset CurrentY here - continuous section breaks
        // should continue from the current position, not restart at the top
        CurrentColumn = 0;

    /// <summary>
    /// Updates page settings for a new section.
    /// </summary>
    public void UpdatePageSettings(PageSettings newSettings)
    {
        PageSettings = newSettings;
        PageWidthPixels = (int) (newSettings.WidthPoints * Scale);
        PageHeightPixels = (int) (newSettings.HeightPoints * Scale);
    }

    public bool HasSpaceFor(float heightPoints)
    {
        // Allow slight overflow (2% of content height) to prevent premature page breaks
        // This helps match Word's pagination behavior
        var tolerance = ContentHeight * 0.02f;
        return CurrentY + heightPoints <= ContentBottom + tolerance;
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
        if (bold && HasMediumWeightSuffix(fontFamily))
        {
            var baseName = StripWeightSuffixes(fontFamily);
            if (!string.IsNullOrEmpty(baseName) && baseName != fontFamily)
            {
                effectiveFontFamily = baseName;
            }
        }

        var key = $"{effectiveFontFamily}_{style.Weight}_{style.Slant}";

        if (!typefaceCache.TryGetValue(key, out var typeface))
        {
            typeface = SKTypeface.FromFamilyName(effectiveFontFamily, style);

            // If font wasn't found (fell back to default), try user fonts, Office fonts, then cloud cache
            // Compare against effectiveFontFamily since we may have stripped weight suffixes
            if (typeface.FamilyName != effectiveFontFamily && !typeface.FamilyName.StartsWith(effectiveFontFamily, StringComparison.OrdinalIgnoreCase))
            {
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
                        else if (fontFallbacks.TryGetValue(effectiveFontFamily, out var fallbackFont)
                                 || fontFallbacks.TryGetValue(fontFamily, out fallbackFont))
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

    // Common font style suffixes to strip when looking for base family
    // Note: Do NOT include vendor suffixes like " MT", " Pro", " LT", " ITC" as these are part of the font name
    static string[] styleSuffixes =
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

    static bool HasMediumWeightSuffix(string fontFamily)
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

    static string StripWeightSuffixes(string fontFamily)
    {
        var result = fontFamily;
        bool changed;
        do
        {
            changed = false;
            foreach (var suffix in styleSuffixes)
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
                if (fontFamily.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Heavy", StringComparison.OrdinalIgnoreCase))
                {
                    baseWeight = (int) SKFontStyleWeight.Bold;
                }
                else if (fontFamily.Contains("Light", StringComparison.OrdinalIgnoreCase) ||
                         fontFamily.Contains("Thin", StringComparison.OrdinalIgnoreCase))
                {
                    baseWeight = (int) SKFontStyleWeight.Light;
                }
                else if (fontFamily.Contains("Medium", StringComparison.OrdinalIgnoreCase) ||
                         fontFamily.Contains("Demi", StringComparison.OrdinalIgnoreCase) ||
                         fontFamily.Contains("Semibold", StringComparison.OrdinalIgnoreCase))
                {
                    baseWeight = (int) SKFontStyleWeight.SemiBold;
                }

                // Use the heavier of the requested weight and the font name's weight
                // This ensures that if Bold is requested for "Segoe UI Semibold", we get Bold (700) not just Semibold (600)
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

    public float PointsToPixels(float points) => points * Scale;

    /// <summary>
    /// Gets the current line number and increments for the next line.
    /// </summary>
    public int GetNextLineNumber() =>
        currentLineNumber++;

    /// <summary>
    /// Resets line numbers for a new page (if restart mode is NewPage).
    /// </summary>
    public void ResetLineNumbersForPage()
    {
        if (PageSettings.LineNumbers?.Restart == LineNumberRestart.NewPage)
        {
            currentLineNumber = PageSettings.LineNumbers.Start;
        }
    }

    /// <summary>
    /// Resets line numbers for a new section (if restart mode is NewSection).
    /// </summary>
    public void ResetLineNumbersForSection()
    {
        if (PageSettings.LineNumbers?.Restart is LineNumberRestart.NewSection or LineNumberRestart.NewPage)
        {
            currentLineNumber = PageSettings.LineNumbers.Start;
        }
    }

    /// <summary>
    /// Initializes line numbering based on page settings.
    /// </summary>
    public void InitializeLineNumbers()
    {
        if (PageSettings.LineNumbers != null)
        {
            currentLineNumber = PageSettings.LineNumbers.Start;
        }
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
