/// <summary>
/// Backend-agnostic font-resolution machinery shared by every renderer in Morph.
/// Generic over <typeparamref name="TFont"/> — Skia caches <c>SKTypeface</c>, ImageSharp
/// caches <c>FontFamily</c> — but the candidate-name fallback chain, the host font index
/// (user/Office/cloud/system), the directory-only mode, and the score-based face picking
/// all live here.
/// </summary>
/// <remarks>
/// <para>
/// Resolution order in the default mode (no <c>FontDirectory</c>):
/// </para>
/// <list type="number">
///   <item>Per-instance cache keyed by <c>(name, weight, italic)</c>.</item>
///   <item>Pre-seeded entries (typically embedded fonts).</item>
///   <item><see cref="FontFileCache"/> indexed at startup from the merged host caches.</item>
///   <item>Backend-supplied OS font manager (e.g. <c>SKTypeface.FromFamilyName</c> /
///   <c>SystemFonts.TryGet</c>).</item>
///   <item>Configured fallback name (<see cref="FontHelpers.FindFallback"/>) and
///   user-supplied <c>fontFallback</c> delegate, re-entered through the same chain.</item>
/// </list>
/// <para>
/// In directory mode (<c>fontDirectory</c> non-null), only the directory's index and the
/// fallback names are consulted — system caches and the OS font manager are skipped, and
/// a missing font throws.
/// </para>
/// </remarks>
sealed class FontResolver<TFont> : IDisposable where TFont : class
{
    /// <summary>
    /// Loads the picked face into a backend-specific <typeparamref name="TFont"/>.
    /// Returning <c>null</c> means the leaf load failed (e.g. Skia handed a WOFF2) —
    /// the resolver moves on to the next-best face.
    /// </summary>
    /// <param name="bestFace">The face whose metrics best match the request.</param>
    /// <param name="allCandidateFaces">Every face indexed under the matching candidate
    /// name, in score order. Skia ignores this — an <c>SKTypeface</c> wraps a single file.
    /// ImageSharp uses it to pre-load sibling style variants into a per-instance
    /// <c>FontCollection</c> so the resulting <c>FontFamily</c> can answer
    /// <c>CreateFont(size, FontStyle.Italic)</c> correctly even when the picked face
    /// happens to be Regular.</param>
    public delegate TFont? LoadFaceFunc(FontFace bestFace, IReadOnlyList<FontFace> allCandidateFaces);

    /// <summary>
    /// Asks the OS font manager for the family. Returning <c>null</c> falls through to
    /// the configured fallback name; <c>null</c> as the delegate itself disables the
    /// system-fallback step entirely (used by directory mode).
    /// </summary>
    public delegate TFont? SystemFallbackFunc(FontNameCandidates candidates, int targetWeight, bool targetItalic);

    /// <summary>
    /// Builds the seed for the fonts shipped inside <c>Morph.dll</c> from a backend-supplied
    /// byte-array decoder: the four standard Aptos faces, plus the <c>Morph Bullets</c>
    /// glyph subset used by <see cref="TextRenderer"/> to draw list bullet markers
    /// cross-platform (see <c>EmbeddedFonts/Bullets.md</c>). The faces are known at build
    /// time, so the <c>(family, weight, italic)</c> keys are hard-coded instead of being
    /// read out of each face's <c>name</c>/<c>OS/2</c> tables.
    /// </summary>
    public static IEnumerable<((string Name, int Weight, bool Italic) Key, TFont Font)> BuildBundledSeed(
        Func<byte[], TFont> loadFromBytes)
    {
        yield return (("Aptos", 400, false), loadFromBytes(EmbeddedFonts.Aptos400));

        yield return (("Aptos", 400, true), loadFromBytes(EmbeddedFonts.Aptos400Italic));

        yield return (("Aptos", 700, false), loadFromBytes(EmbeddedFonts.Aptos700));

        yield return (("Aptos", 700, true), loadFromBytes(EmbeddedFonts.Aptos700Italic));

        yield return (("Morph Bullets", 400, false), loadFromBytes(EmbeddedFonts.Bullets));
    }

    static readonly FontFileCache allFontsCache =
        new(FontCacheLoader.GetAllFontFiles(), OpenTypeReader.ReadFaces);

    static readonly ConcurrentDictionary<string, FontFileCache> directoryCaches =
        new(StringComparer.OrdinalIgnoreCase);

    static FontFileCache GetDirectoryCache(string fontDirectory) =>
        directoryCaches.GetOrAdd(
            Path.GetFullPath(fontDirectory),
            path => new(FontCacheLoader.EnumerateFontFilesInDirectory(path, recursive: true), OpenTypeReader.ReadFaces));

    LoadFaceFunc loadFace;
    SystemFallbackFunc? systemFallback;
    Action<TFont>? releaseFont;
    string? fontDirectory;
    Func<string, string?>? fontFallback;
    Dictionary<(string Name, int Weight, bool Italic), TFont> cache = new();
    HashSet<TFont> seededFonts = new();

    /// <param name="loadFace">Loads a single face from disk into <typeparamref name="TFont"/>.</param>
    /// <param name="systemFallback">OS-level fallback. Pass <c>null</c> in directory mode.</param>
    /// <param name="releaseFont">Disposer for non-seeded fonts; <c>null</c> if the type
    /// doesn't need disposal.</param>
    /// <param name="fontDirectory">Directory mode root, or <c>null</c> for default mode.</param>
    /// <param name="fontFallback">User-supplied "if we couldn't find X, try Y" delegate.</param>
    /// <param name="seed">Pre-resolved fonts (e.g. embedded faces) to seed the cache with.
    /// Their disposal is skipped — they're considered shared/process-wide.</param>
    public FontResolver(
        LoadFaceFunc loadFace,
        SystemFallbackFunc? systemFallback,
        Action<TFont>? releaseFont,
        string? fontDirectory,
        Func<string, string?>? fontFallback,
        IEnumerable<((string Name, int Weight, bool Italic) Key, TFont Font)> seed)
    {
        this.loadFace = loadFace;
        this.systemFallback = systemFallback;
        this.releaseFont = releaseFont;
        this.fontDirectory = fontDirectory;
        this.fontFallback = fontFallback;

        foreach (var (key, font) in seed)
        {
            cache[key] = font;
            seededFonts.Add(font);
        }
    }

    /// <summary>
    /// Resolves <paramref name="fontFamily"/> + style flags to a backend font, throwing
    /// when nothing matches.
    /// </summary>
    public TFont Resolve(string fontFamily, bool bold, bool italic)
    {
        var candidates = FontHelpers.GetCandidateNames(fontFamily, bold);
        var targetWeight = FontHelpers.ResolveTargetWeight(fontFamily, bold);
        var key = (candidates.Effective, targetWeight, italic);

        if (cache.TryGetValue(key, out var font))
        {
            return font;
        }

        if (fontDirectory == null)
        {
            font = TryResolveDefaultMode(candidates, fontFamily, bold, targetWeight, italic);
        }
        else
        {
            font = TryResolveDirectoryMode(candidates, fontFamily, bold, targetWeight, italic);
        }

        if (font == null)
        {
            throw new InvalidOperationException(BuildNotFoundMessage(fontFamily));
        }

        cache[key] = font;
        return font;
    }

    TFont? TryResolveDefaultMode(FontNameCandidates candidates, string fontFamily, bool bold, int targetWeight, bool targetItalic)
    {
        // 1. Search the merged host cache (user → Office → cloud → system, in priority order)
        //    for any of the candidate names, picking the closest face by weight/italic/width.
        var font = TryResolveFromCache(allFontsCache, candidates, targetWeight, targetItalic);

        // 2. Fall back to the OS font manager (handles "user has installed something we
        //    didn't index" plus localized system fonts on macOS/Linux).
        font ??= systemFallback?.Invoke(candidates, targetWeight, targetItalic);

        // 3. Configured/runtime fallback name (e.g. "Segoe UI Variable" → "Segoe UI") —
        //    re-resolve the alias through the same path.
        if (font == null)
        {
            var fallbackName = FontHelpers.FindFallback(candidates) ?? fontFallback?.Invoke(fontFamily);
            if (fallbackName != null)
            {
                var fallbackCandidates = FontHelpers.GetCandidateNames(fallbackName, bold);
                var fallbackWeight = FontHelpers.ResolveTargetWeight(fallbackName, bold);
                font = TryResolveFromCache(allFontsCache, fallbackCandidates, fallbackWeight, targetItalic)
                       ?? systemFallback?.Invoke(fallbackCandidates, fallbackWeight, targetItalic);
            }
        }

        return font;
    }

    TFont? TryResolveDirectoryMode(FontNameCandidates candidates, string fontFamily, bool bold, int targetWeight, bool targetItalic)
    {
        var directoryCache = GetDirectoryCache(fontDirectory!);
        var font = TryResolveFromCache(directoryCache, candidates, targetWeight, targetItalic);
        if (font != null)
        {
            return font;
        }

        var fallbackName = FontHelpers.FindFallback(candidates) ?? fontFallback?.Invoke(fontFamily);
        if (fallbackName == null)
        {
            throw new InvalidOperationException($"Font '{fontFamily}' not found in '{fontDirectory}'.");
        }

        var fallbackCandidates = FontHelpers.GetCandidateNames(fallbackName, bold);
        var fallbackWeight = FontHelpers.ResolveTargetWeight(fallbackName, bold);
        return TryResolveFromCache(directoryCache, fallbackCandidates, fallbackWeight, targetItalic);
    }

    /// <summary>
    /// Iterates faces matching the candidate names in score order (closest weight/italic
    /// first), returning the first one that the leaf <see cref="LoadFaceFunc"/> can
    /// actually load. The retry loop handles cases like a Skia path being handed a WOFF2
    /// (parsed fine for indexing, but Skia can't decode it) — we silently advance to the
    /// next-best face that the backend can load.
    /// </summary>
    TFont? TryResolveFromCache(FontFileCache fileCache, FontNameCandidates candidates, int targetWeight, bool targetItalic)
    {
        if (!fileCache.TryGet(candidates, out var faces))
        {
            return null;
        }

        // Sorting is in-place and avoids LINQ to keep the per-resolve allocations small.
        var rankedFaces = new FontFace[faces.Length];
        var scores = new int[faces.Length];
        for (var i = 0; i < faces.Length; i++)
        {
            rankedFaces[i] = faces[i];
            scores[i] = FontHelpers.ScoreFace(faces[i], targetWeight, targetItalic);
        }
        Array.Sort(scores, rankedFaces);

        foreach (var face in rankedFaces)
        {
            var font = loadFace(face, rankedFaces);
            if (font != null)
            {
                return font;
            }
        }

        return null;
    }

    static string BuildNotFoundMessage(string fontFamily) =>
        $"Font '{fontFamily}' not found. Checked:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", FontCacheLoader.GetSearchedPaths())}";

    public void Dispose()
    {
        if (releaseFont != null)
        {
            foreach (var font in cache.Values)
            {
                if (seededFonts.Contains(font))
                {
                    continue;
                }

                releaseFont(font);
            }
        }

        cache.Clear();
        seededFonts.Clear();
    }
}
