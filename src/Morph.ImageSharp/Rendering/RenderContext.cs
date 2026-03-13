/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class RenderContext : IDisposable
{
    Dictionary<string, FontFamily> fontFamilyCache = new();

    // Shared font collection for fonts loaded from file (cloud, Office, user caches)
    FontCollection sharedFontCollection = new();

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
        var cloudFontsPath = System.IO.Path.Combine(localAppData, "Microsoft", "FontCache", "4", "CloudFonts");

        if (Directory.Exists(cloudFontsPath))
        {
            foreach (var fontDir in Directory.GetDirectories(cloudFontsPath))
            {
                foreach (var fontFile in Directory.GetFiles(fontDir, "*.ttf"))
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
            }
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    static Dictionary<string, string[]> LoadOfficeFontsCache()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var officeFontsPath in GetOfficeFontPaths())
        {
            if (!Directory.Exists(officeFontsPath))
            {
                continue;
            }

            foreach (var fontFile in Directory.GetFiles(officeFontsPath, "*.ttf"))
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
        }

        return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    static IEnumerable<string> GetOfficeFontPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Office", "root", "vfs", "Fonts", "private");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Microsoft Word.app/Contents/Resources/DFonts";
            yield return "/Applications/Microsoft Excel.app/Contents/Resources/DFonts";
            yield return "/Applications/Microsoft PowerPoint.app/Contents/Resources/DFonts";
        }
    }

    static Dictionary<string, string[]> LoadUserFontsCache()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userFontsPath = System.IO.Path.Combine(localAppData, "Microsoft", "Windows", "Fonts");

        if (Directory.Exists(userFontsPath))
        {
            foreach (var fontFile in Directory.GetFiles(userFontsPath, "*.ttf")
                         .Concat(Directory.GetFiles(userFontsPath, "*.otf")))
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
        // Points to pixels
        Scale = dpi / 72f;
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
            // Header fits within the margin area or is empty
            headerSpace = 0;
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
            // Footer fits within the margin area or is empty
            footerSpace = 0;
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
        if (bold && HasMediumWeightSuffix(fontFamily))
        {
            var baseName = StripWeightSuffixes(fontFamily);
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
            else if (fontFallbacks.TryGetValue(effectiveFontFamily, out var fallbackFont)
                     || fontFallbacks.TryGetValue(fontFamily, out fallbackFont))
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
                if (fontFamily.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Medium", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Demi", StringComparison.OrdinalIgnoreCase) ||
                    fontFamily.Contains("Semibold", StringComparison.OrdinalIgnoreCase))
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
        if (props.Bold && props.Italic)
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

    public void Dispose() =>
        fontFamilyCache.Clear();
}
