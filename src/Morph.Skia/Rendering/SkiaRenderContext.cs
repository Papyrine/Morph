using Morph;

/// <summary>
/// Maintains rendering state during page layout and rendering.
/// </summary>
sealed class SkiaRenderContext(
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

    /// <summary>
    /// Index of every font file Morph can discover on the host (user, Office, M365 cloud,
    /// system) in a single name-keyed cache. The four sources are merged in priority order
    /// during construction, so when two files share a name the higher-priority source's
    /// face still appears first in the bucket — keeping the original "user installs win
    /// on ties" rule without needing four separate caches.
    /// </summary>
    static readonly FontFileCache allFontsCache = new(FontCacheLoader.GetAllFontFiles(), OpenTypeReader.ReadFaces);

    /// <summary>
    /// Typefaces loaded from <see cref="EmbeddedFonts"/> (the Aptos faces shipped inside
    /// <c>Morph.dll</c>). Indexed by family name (case-insensitive). Held statically so
    /// the byte arrays are decoded by Skia just once per process — and so per-instance
    /// <see cref="Dispose"/> can identify and skip these instead of releasing them
    /// (they're shared across every render context in the process).
    /// </summary>
    static readonly Dictionary<string, List<SKTypeface>> embeddedTypefaces = LoadEmbeddedTypefaces();
    static readonly HashSet<SKTypeface> embeddedTypefaceSet = [..embeddedTypefaces.Values.SelectMany(_ => _)];

    static Dictionary<string, List<SKTypeface>> LoadEmbeddedTypefaces()
    {
        var result = new Dictionary<string, List<SKTypeface>>(StringComparer.OrdinalIgnoreCase);
        foreach (var stream in EmbeddedFonts.OpenStreams())
        {
            using (stream)
            {
                var typeface = SKTypeface.FromStream(stream)
                    ?? throw new InvalidOperationException("Failed to load embedded font from Morph.dll resource stream.");

                if (!result.TryGetValue(typeface.FamilyName, out var list))
                {
                    list = new();
                    result[typeface.FamilyName] = list;
                }
                list.Add(typeface);
            }
        }
        return result;
    }

    static readonly ConcurrentDictionary<string, FontFileCache> directoryCaches = new(StringComparer.OrdinalIgnoreCase);

    static FontFileCache GetDirectoryCache(string fontDirectory) =>
        directoryCaches.GetOrAdd(
            Path.GetFullPath(fontDirectory),
            path => new(FontCacheLoader.EnumerateFontFilesInDirectory(path, recursive: true), OpenTypeReader.ReadFaces));

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

        if (typefaceCache.TryGetValue(key, out var typeface))
        {
            return typeface;
        }

        var targetWeight = FontHelpers.ResolveTargetWeight(fontFamily, bold);
        var targetItalic = italic;

        if (FontDirectory != null)
        {
            typeface = ResolveFromDirectory(candidates, fontFamily, bold, targetWeight, targetItalic);
            if (typeface == null)
            {
                throw new InvalidOperationException($"Font '{fontFamily}' not found in '{FontDirectory}'.");
            }

            typefaceCache[key] = typeface;
            return typeface;
        }

        // 1. Search all known caches for any of the candidate names; pick the best face.
        typeface = ResolveFromCaches(candidates, targetWeight, targetItalic);

        // 2. Fall back to the OS font manager (handles "user has installed something we
        //    didn't index" plus localized system fonts on macOS/Linux).
        typeface ??= TryResolveFromSystem(candidates, style);

        // 3. Last-resort: the faces Morph ships embedded in Morph.dll (Aptos). This is
        //    what makes the default font render on machines that don't have it installed.
        typeface ??= TryResolveFromEmbedded(candidates, targetWeight, targetItalic);

        // 3. Configured/runtime fallback name (e.g. "Segoe UI Variable" → "Segoe UI") —
        //    re-resolve the alias through the same path.
        if (typeface == null)
        {
            var fallbackName = FontHelpers.FindFallback(candidates) ?? FontFallback?.Invoke(fontFamily);
            if (fallbackName != null)
            {
                var fallbackCandidates = FontHelpers.GetCandidateNames(fallbackName, bold);
                var fallbackTargetWeight = FontHelpers.ResolveTargetWeight(fallbackName, bold);
                typeface = ResolveFromCaches(fallbackCandidates, fallbackTargetWeight, targetItalic)
                    ?? TryResolveFromSystem(fallbackCandidates, style);
            }
        }

        if (typeface == null)
        {
            throw new InvalidOperationException(
                $"Font '{fontFamily}' not found. Checked:{Environment.NewLine}  " +
                string.Join($"{Environment.NewLine}  ", FontCacheLoader.GetSearchedPaths()));
        }

        typefaceCache[key] = typeface;
        return typeface;
    }

    SKTypeface? ResolveFromDirectory(FontNameCandidates candidates, string fontFamily, bool bold, int targetWeight, bool targetItalic)
    {
        var cache = GetDirectoryCache(FontDirectory!);
        var typeface = ResolveFromCache(cache, candidates, targetWeight, targetItalic);
        if (typeface != null)
        {
            return typeface;
        }

        var fallbackName = FontHelpers.FindFallback(candidates) ?? FontFallback?.Invoke(fontFamily);
        if (fallbackName == null)
        {
            return null;
        }

        var fallbackCandidates = FontHelpers.GetCandidateNames(fallbackName, bold);
        var fallbackTargetWeight = FontHelpers.ResolveTargetWeight(fallbackName, bold);
        return ResolveFromCache(cache, fallbackCandidates, fallbackTargetWeight, targetItalic);
    }

    /// <summary>
    /// Looks the candidate names up in <see cref="allFontsCache"/> and picks the face
    /// whose weight/italic/width is closest to the request. A Light face beats a
    /// Regular when Light was asked for; when scores tie, the face from a
    /// higher-priority ingestion source (user before system) wins because it appears
    /// first in the bucket.
    /// </summary>
    static SKTypeface? ResolveFromCaches(FontNameCandidates candidates, int targetWeight, bool targetItalic)
    {
        if (!allFontsCache.TryGet(candidates, out var faces))
        {
            return null;
        }

        FontFace? bestSoFar = null;
        var bestScore = int.MaxValue;
        foreach (var face in faces)
        {
            var score = FontHelpers.ScoreFace(face, targetWeight, targetItalic);
            if (score < bestScore)
            {
                bestScore = score;
                bestSoFar = face;
            }
        }

        return bestSoFar == null ? null : LoadFace(bestSoFar);
    }

    static SKTypeface? ResolveFromCache(FontFileCache cache, FontNameCandidates candidates, int targetWeight, bool targetItalic)
    {
        if (!cache.TryGet(candidates, out var faces))
        {
            return null;
        }

        var face = FontHelpers.PickBestFace(faces, targetWeight, targetItalic);
        return face == null ? null : LoadFace(face);
    }

    static SKTypeface? LoadFace(FontFace face)
    {
        try
        {
            return face.Index == 0
                ? SKTypeface.FromFile(face.Path)
                : SKTypeface.FromFile(face.Path, face.Index);
        }
        catch
        {
            return null;
        }
    }

    static SKTypeface? TryResolveFromEmbedded(FontNameCandidates candidates, int targetWeight, bool targetItalic)
    {
        SKTypeface? best = null;
        var bestScore = int.MaxValue;

        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            if (!embeddedTypefaces.TryGetValue(name, out var typefaces))
            {
                continue;
            }

            foreach (var typeface in typefaces)
            {
                var faceItalic = typeface.FontStyle.Slant != SKFontStyleSlant.Upright;
                var score = Math.Abs(typeface.FontStyle.Weight - targetWeight) + (faceItalic == targetItalic ? 0 : 100);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = typeface;
                }
            }
        }

        return best;
    }

    static SKTypeface? TryResolveFromSystem(FontNameCandidates candidates, SKFontStyle style)
    {
        foreach (var name in FontFileCache.EnumerateCandidateNames(candidates))
        {
            var typeface = SKTypeface.FromFamilyName(name, style);
            if (typeface == null)
            {
                continue;
            }

            // SKTypeface.FromFamilyName never returns null but may collapse the requested
            // family into a parent (e.g. "Segoe UI Semilight" → "Segoe UI"). Accept only
            // if the returned FamilyName matches what we asked for, AND the returned face
            // honors the requested italic — otherwise we'd miss an italic variant in a
            // later cache.
            if (typeface.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                typeface.FamilyName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                var wantItalic = style.Slant != SKFontStyleSlant.Upright;
                var gotItalic = typeface.FontStyle.Slant != SKFontStyleSlant.Upright;
                if (wantItalic == gotItalic)
                {
                    return typeface;
                }
            }

            typeface.Dispose();
        }

        return null;
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

    public static SKColor ParseColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor) ||
            hexColor == "auto")
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
            // Skip shared embedded typefaces — they live in a static registry and are
            // reused by every render context in the process; disposing here would leave
            // the next render with a disposed handle.
            if (embeddedTypefaceSet.Contains(typeface))
            {
                continue;
            }

            typeface.Dispose();
        }

        typefaceCache.Clear();
    }
}
