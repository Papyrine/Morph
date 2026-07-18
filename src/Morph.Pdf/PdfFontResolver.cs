/// <summary>
/// Maps font family + style requests onto the bundled TrueType files so PdfSharp can embed real
/// glyphs. PdfSharp resolves fonts through a single process-global <see cref="GlobalFontSettings.FontResolver"/>,
/// so this is a registered-once singleton; directories are added additively (keyed by absolute file
/// path) which lets multiple <see cref="ExportOptions.FontDirectory"/> values coexist.
///
/// Font files follow the bundled naming convention <c>{Family}_{weight}[_Italic].ttf</c> (spaces in
/// the family become underscores), e.g. <c>Arial_Nova_700.ttf</c>, <c>Aptos_400_Italic.ttf</c>.
///
/// Resolution mirrors the shared <see cref="FontResolver{TFont}"/> directory mode: candidate names
/// (<see cref="FontHelpers.GetCandidateNames"/>) score every indexed face by weight distance and
/// italic mismatch (<see cref="FontHelpers.ScoreFace"/>), so a bold request picks a 700 face over a
/// 900 one; the curated <see cref="FontHelpers.FontFallbacks"/> aliases fire when the direct match
/// is missing or a poor weight fit; and a family that still misses falls back to
/// <see cref="DefaultFontSettings.DefaultFont"/> at the requested style rather than an arbitrary
/// bundled file.
/// </summary>
sealed class PdfFontResolver : IFontResolver
{
    public static PdfFontResolver Instance { get; } = new();

    Lock gate = new();
    Dictionary<string, string> faceToPath = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, List<FontFace>> index = [];
    HashSet<string> scannedDirectories = [with(StringComparer.OrdinalIgnoreCase)];
    string? defaultFace;

    PdfFontResolver()
    {
    }

    /// <summary>Registers the resolver globally (idempotent) and indexes <paramref name="directory"/>.</summary>
    public static void Register(string? directory)
    {
        if (GlobalFontSettings.FontResolver is not PdfFontResolver)
        {
            GlobalFontSettings.FontResolver = Instance;
        }

        Instance.ScanDirectory(directory);
    }

    void ScanDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var full = Path.GetFullPath(directory);
        lock (gate)
        {
            if (!scannedDirectories.Add(full) || !Directory.Exists(full))
            {
                return;
            }

            // Sort ordinally so indexing is filesystem-order independent: Directory.EnumerateFiles
            // has no defined order, yet that order decides the default fallback face (defaultFace is
            // the first file seen) and which file wins a score tie between identical-metric faces.
            // Without this the same FontDirectory embeds different fallback fonts on different
            // filesystems (e.g. a Windows bind mount vs CI's ext4), so the generated PDF bytes differ
            // across machines despite an identical container image.
            foreach (var path in Directory.EnumerateFiles(full, "*.ttf", SearchOption.AllDirectories)
                         .OrderBy(_ => _, StringComparer.Ordinal))
            {
                IndexFile(path);
            }
        }
    }

    void IndexFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var italic = parts[^1].Equals("Italic", StringComparison.OrdinalIgnoreCase);
        var end = italic ? parts.Length - 1 : parts.Length;

        var weight = 400;
        if (end > 1 && int.TryParse(parts[end - 1], out var parsedWeight))
        {
            weight = parsedWeight;
            end--;
        }

        if (end <= 0)
        {
            return;
        }

        var family = string.Join(' ', parts[..end]);

        faceToPath[path] = path;

        // Index faces with the OS/2 weight/width/italic OpenTypeReader captured, so resolution can
        // score candidates exactly like the shared FontResolver. A file it can't parse still
        // resolves through a synthetic face built from the file-name convention.
        var indexed = false;
        foreach (var (face, declaredNames) in ReadFaces(path))
        {
            indexed = true;

            // The bundled file name is the curated key (Arial_700 -> "Arial" bold) ...
            AddFace(family, face);

            // ... and the font's own declared names (Family, Full, PostScript, Typographic) cover
            // abbreviated or alternate spellings - e.g. "Trade Gothic Next Cond" for
            // Trade_Gothic_Next_Condensed - mirroring the shared FontFileCache, which indexes every
            // declared name.
            foreach (var declaredName in declaredNames)
            {
                if (!string.IsNullOrEmpty(declaredName))
                {
                    AddFace(declaredName, face);
                }
            }
        }

        if (!indexed)
        {
            AddFace(family, new()
            {
                Path = path,
                Weight = weight,
                Width = 5,
                Italic = italic
            });
        }

        defaultFace ??= path;
    }

    static IEnumerable<(FontFace Face, IReadOnlyList<string> Names)> ReadFaces(string path)
    {
        try
        {
            return OpenTypeReader.ReadFaces(path).ToList();
        }
        catch
        {
            // A font we can't parse contributes no OS/2 metadata; the synthetic file-name entry
            // still serves it.
            return [];
        }
    }

    void AddFace(string name, FontFace face)
    {
        var key = name.ToLowerInvariant();
        if (!index.TryGetValue(key, out var faces))
        {
            faces = [];
            index[key] = faces;
        }

        // The same file often declares the same name several times (Family + Full + Typographic);
        // one entry per (name, file) keeps the score tie-break on distinct files only.
        foreach (var existing in faces)
        {
            if (existing.Path == face.Path)
            {
                return;
            }
        }

        faces.Add(face);
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        lock (gate)
        {
            if (TryResolve(familyName, isBold, isItalic, out var face))
            {
                return new(face);
            }
        }

        // Fall back to the platform resolver (system fonts) when the family isn't bundled and no
        // FontDirectory restricted us. Keeps non-deterministic-but-available output on the host OS.
        var platform = PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);
        if (platform != null)
        {
            return platform;
        }

        lock (gate)
        {
            // Last resort: the render default font at the requested style - the same family the
            // raster backends fall back to - rather than an arbitrary bundled file (the ordinally
            // first used to be Aharoni_700, a bold face, which made every unresolved family render
            // heavy and wide).
            if (TryResolve(DefaultFontSettings.DefaultFont, isBold, isItalic, out var fallbackFace))
            {
                return new(fallbackFace);
            }

            return defaultFace == null ? null : new FontResolverInfo(defaultFace);
        }
    }

    bool TryResolve(string familyName, bool bold, bool italic, out string face)
    {
        var candidates = FontHelpers.GetCandidateNames(familyName, bold);
        var targetWeight = FontHelpers.ResolveTargetWeight(familyName, bold);
        var direct = BestFace(candidates, targetWeight, italic, out var directDelta);

        // Curated alias (e.g. "Grandview Display" -> "Grandview") - taken when the direct match is
        // missing, or is a poor weight fit and the alias lands closer (the shared resolver's
        // weightFallbackThreshold rule).
        var fallbackName = FontHelpers.FindFallback(candidates);
        if (fallbackName != null && (direct == null || directDelta >= weightFallbackThreshold))
        {
            var fallbackCandidates = FontHelpers.GetCandidateNames(fallbackName, bold);
            var fallbackWeight = FontHelpers.ResolveTargetWeight(fallbackName, bold);
            var fallback = BestFace(fallbackCandidates, fallbackWeight, italic, out var fallbackDelta);
            if (fallback != null && (direct == null || fallbackDelta < directDelta))
            {
                face = fallback.Path;
                return true;
            }
        }

        if (direct != null)
        {
            face = direct.Path;
            return true;
        }

        face = "";
        return false;
    }

    // Mirrors FontResolver.TryResolveFromCache: the first candidate name with any indexed faces
    // supplies them, and the face with the lowest ScoreFace wins (italic mismatch dominates any
    // weight distance, so a bold request never trades the upright axis for a closer weight).
    FontFace? BestFace(FontNameCandidates candidates, int targetWeight, bool targetItalic, out int weightDelta)
    {
        weightDelta = int.MaxValue;
        foreach (var candidateName in FontFileCache.EnumerateCandidateNames(candidates))
        {
            if (!index.TryGetValue(candidateName.ToLowerInvariant(), out var faces))
            {
                continue;
            }

            FontFace? best = null;
            var bestScore = int.MaxValue;
            foreach (var candidate in faces)
            {
                var score = FontHelpers.ScoreFace(candidate, targetWeight, targetItalic);
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                weightDelta = Math.Abs(best.Weight - targetWeight);
                return best;
            }
        }

        return null;
    }

    // Threshold above which a direct match is a bad enough weight fit to prefer the curated
    // fallback name - identical to the shared FontResolver's rule.
    const int weightFallbackThreshold = 300;

    public byte[]? GetFont(string faceName)
    {
        lock (gate)
        {
            return faceToPath.TryGetValue(faceName, out var path) ? File.ReadAllBytes(path) : null;
        }
    }
}
