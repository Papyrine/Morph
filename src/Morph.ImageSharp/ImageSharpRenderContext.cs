/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class ImageSharpRenderContext : RenderContextBase, IDisposable
{
    /// <summary>
    /// Per-instance collection that grows as faces are resolved. Each
    /// <see cref="LoadFace"/> call pre-loads every sibling face under the matched
    /// candidate name so <c>FamilyHandle.Family.CreateFont(size, FontStyle.Italic)</c>
    /// finds the italic variant even when the resolver's score-pick happened to be
    /// the Regular face. Mirrors the old <c>LoadFilesIntoSharedCollection</c> behavior
    /// inside the unified <see cref="FontResolver{TFont}"/> shape.
    /// </summary>
    readonly FontCollection sharedFontCollection = new();

    /// <summary>Path-keyed dedupe for <see cref="sharedFontCollection"/> — files only get added once.</summary>
    readonly HashSet<string> loadedPaths = [with(StringComparer.OrdinalIgnoreCase)];

    readonly FontResolver<FontFamilyHandle> resolver;

    public ImageSharpRenderContext(
        PageSettings pageSettings,
        int dpi,
        CompatibilitySettings? compatibility = null,
        double fontWidthScale = 1.0,
        Func<string, string?>? fontFallback = null,
        string? fontDirectory = null,
        bool? deterministicRendering = null) :
        base(pageSettings, dpi, compatibility, fontWidthScale, fontFallback, fontDirectory, deterministicRendering) =>
        // FontCollection / FontFamily don't own unmanaged state — no per-cache disposal needed.
        resolver = new(
            loadFace: LoadFace,
            systemFallback: fontDirectory == null ? TryResolveFromSystem : null,
            releaseFont: null,
            fontDirectory: fontDirectory,
            fontFallback: fontFallback,
            seed: FontResolver<FontFamilyHandle>.BuildBundledSeed(LoadFromBytes));

    static FontFamilyHandle LoadFromBytes(byte[] bytes)
    {
        // Wrap the face in its own single-style FontCollection so the returned
        // FontFamily exposes exactly the style this byte[] encodes. The resolver
        // only returns a seeded entry on an exact (family, weight, italic) match,
        // so PickAvailableStyle's later style lookup always hits the only style
        // present.
        var collection = new FontCollection();
        using var stream = new MemoryStream(bytes, writable: false);
        var family = collection.Add(stream);
        return new(family);
    }

    /// <summary>
    /// Loads every face matching the candidate name into <see cref="sharedFontCollection"/>
    /// (idempotent via <see cref="loadedPaths"/>) so the family that ImageSharp returns
    /// has every available style — Regular, Bold, Italic, BoldItalic — even though the
    /// resolver only score-picked one of them. This matters because at <c>GetFont</c> time
    /// we hand the family to <see cref="PickAvailableStyle"/>, which routes
    /// <c>CreateFont(size, FontStyle.Italic)</c> to the actual italic face inside the
    /// family. Without sibling pre-loading the italic face would never appear there.
    /// </summary>
    FontFamilyHandle? LoadFace(FontFace bestFace, IReadOnlyList<FontFace> allCandidateFaces)
    {
        foreach (var face in allCandidateFaces)
        {
            if (!loadedPaths.Add(face.Path))
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
                // Individual file load failures don't fail the resolution; the resolver's
                // retry loop tries the next-best face if this one's family can't be located.
            }
        }

        if (bestFace.Family == null)
        {
            return null;
        }

        return sharedFontCollection.TryGet(bestFace.Family, out var family) ? new(family) : null;
    }

    /// <summary>
    /// OS font manager fallback. SixLabors' <see cref="SystemFonts"/> indexes by family
    /// name; numeric weight isn't filterable here, so style validation is deferred to
    /// <see cref="PickAvailableStyle"/> at <c>CreateFont</c> time.
    /// </summary>
    static FontFamilyHandle? TryResolveFromSystem(FontNameCandidates candidates, int targetWeight, bool targetItalic)
    {
        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            if (SystemFonts.TryGet(name, out var family))
            {
                return new(family);
            }
        }

        return null;
    }

    public FontFamily GetFontFamily(string fontFamily, bool bold, bool italic) =>
        resolver.Resolve(fontFamily, bold, italic).Family;

    // Font allocations are a hot-path cost in ImageSharp (`family.CreateFont`
    // performs internal style lookup + font instantiation). The result is fully
    // determined by (family, size, style) — cache for the lifetime of the context.
    readonly Dictionary<(FontFamily, float, FontStyle), Font> fontCache = [];

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

        return GetOrCreateCachedFont(family, fontSize, PickAvailableStyle(family, style));
    }

    Font GetOrCreateCachedFont(FontFamily family, float fontSize, FontStyle style)
    {
        var key = (family, fontSize, style);
        if (fontCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var font = family.CreateFont(fontSize, style);
        fontCache[key] = font;
        return font;
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

        // Ordered fallback attempts per requested style. For pure Bold requests, prefer
        // Regular over BoldItalic — wrong slant is more visually disruptive than missing
        // weight (e.g. some bundled font directories carry Regular/Italic/BoldItalic but
        // not Bold; falling to BoldItalic there would render upright text as italic).
        IEnumerable<FontStyle> fallbackOrder = requested switch
        {
            FontStyle.BoldItalic => [FontStyle.Bold, FontStyle.Italic, FontStyle.Regular],
            FontStyle.Bold => [FontStyle.Regular, FontStyle.BoldItalic, FontStyle.Italic],
            FontStyle.Italic => [FontStyle.BoldItalic, FontStyle.Regular, FontStyle.Bold],
            _ => [FontStyle.Bold, FontStyle.Italic, FontStyle.BoldItalic]
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

        return GetOrCreateCachedFont(family, sizePoints, PickAvailableStyle(family, style));
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

    // SolidBrush and SolidPen are immutable in ImageSharp.Drawing — every fragment
    // render currently allocates a fresh instance. Documents typically use a small set
    // of colours/widths, so caching by key gives near-100% hit rate after warm-up.
    readonly Dictionary<Color, SolidBrush> brushCache = [];
    readonly Dictionary<(Color, float), SolidPen> penCache = [];

    public SolidBrush GetBrush(Color color)
    {
        if (brushCache.TryGetValue(color, out var cached))
        {
            return cached;
        }
        var brush = new SolidBrush(color);
        brushCache[color] = brush;
        return brush;
    }

    public SolidPen GetPen(Color color, float width)
    {
        var key = (color, width);
        if (penCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var pen = new SolidPen(color, width);
        penCache[key] = pen;
        return pen;
    }

    /// <summary>
    /// Per-page sink for source images handed to <c>DrawingCanvas.DrawImage</c>. The canvas queues
    /// an <c>ImageBrush</c> that retains the source image until the canvas is disposed and its
    /// timeline is rendered, so the image must outlive any caller-side <c>using</c>. Page renderer
    /// drains this list after canvas dispose in <c>FinishCurrentPage</c>/<c>DiscardCurrentPage</c>.
    /// </summary>
    public List<IDisposable> PendingPageDisposables { get; } = [];

    public void RetainForPage(IDisposable disposable) =>
        PendingPageDisposables.Add(disposable);

    public void DisposePendingPageDisposables()
    {
        foreach (var disposable in PendingPageDisposables)
        {
            disposable.Dispose();
        }
        PendingPageDisposables.Clear();
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
            return Color.FromPixel(new Rgb24(
                (byte) ((rgb >> 16) & 0xFF),
                (byte) ((rgb >> 8) & 0xFF),
                (byte) (rgb & 0xFF)));
        }

        if (hexColor.Length == 8 &&
            uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var argb))
        {
            return Color.FromPixel(new Rgba32(
                (byte) ((argb >> 16) & 0xFF),
                (byte) ((argb >> 8) & 0xFF),
                (byte) (argb & 0xFF),
                (byte) ((argb >> 24) & 0xFF)));
        }

        return Color.Black;
    }

    public void Dispose() => resolver.Dispose();
}

/// <summary>
/// Adapter around <see cref="FontFamily"/> (a struct in SixLabors.Fonts) so it can flow
/// through the reference-typed <see cref="FontResolver{TFont}"/> cache.
/// </summary>
sealed class FontFamilyHandle(FontFamily family)
{
    public FontFamily Family { get; } = family;
}
