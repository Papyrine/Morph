/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class ImageSharpRenderContext : RenderContextBase, IDisposable
{
    /// <summary>
    /// FontCollection populated from <see cref="EmbeddedFonts"/> — the Aptos faces shipped
    /// inside <c>Morph.dll</c>. Held statically so the streams are parsed once per process.
    /// </summary>
    static readonly FontCollection embeddedFontCollection = LoadEmbeddedFontCollection();

    /// <summary>
    /// Pre-computed seed for <see cref="FontResolver{TFont}"/>. Each Aptos face's
    /// (FamilyName, Weight, Italic) — read from the OpenType <c>name</c>/<c>OS/2</c>
    /// tables via <see cref="OpenTypeReader"/> — points at the multi-face FontFamily
    /// inside <see cref="embeddedFontCollection"/>. Bold/Italic combinations of Aptos
    /// hit the resolver cache directly without touching disk.
    /// </summary>
    static readonly ((string Name, int Weight, bool Italic) Key, FontFamilyHandle Font)[] embeddedSeed = BuildEmbeddedSeed();

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
    readonly HashSet<string> loadedPaths = new(StringComparer.OrdinalIgnoreCase);

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
            seed: embeddedSeed);

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

    static ((string Name, int Weight, bool Italic), FontFamilyHandle)[] BuildEmbeddedSeed()
    {
        // Wrap each unique embedded family in a single handle so the multi-style seed
        // entries all point at the same instance — the resolver's seededFonts HashSet
        // dedupes by reference and skips disposal for everything in the seed.
        var handlesByName = new Dictionary<string, FontFamilyHandle>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in embeddedFontCollection.Families)
        {
            handlesByName[family.Name] = new(family);
        }

        var entries = new List<((string, int, bool), FontFamilyHandle)>();
        foreach (var bytes in EmbeddedFonts.AllFaceBytes)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            foreach (var (face, _) in OpenTypeReader.ReadFaces(stream, "(embedded)"))
            {
                if (handlesByName.TryGetValue(face.Family, out var handle))
                {
                    entries.Add(((face.Family, face.Weight, face.Italic), handle));
                }
            }
        }
        return entries.ToArray();
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
