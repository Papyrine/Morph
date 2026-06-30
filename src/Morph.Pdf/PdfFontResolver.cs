/// <summary>
/// Maps font family + style requests onto the bundled TrueType files so PdfSharp can embed real
/// glyphs. PdfSharp resolves fonts through a single process-global <see cref="GlobalFontSettings.FontResolver"/>,
/// so this is a registered-once singleton; directories are added additively (keyed by absolute file
/// path) which lets multiple <see cref="ExportOptions.FontDirectory"/> values coexist.
///
/// Font files follow the bundled naming convention <c>{Family}_{weight}[_Italic].ttf</c> (spaces in
/// the family become underscores), e.g. <c>Arial_Nova_700.ttf</c>, <c>Aptos_400_Italic.ttf</c>.
/// </summary>
sealed class PdfFontResolver : IFontResolver
{
    public static PdfFontResolver Instance { get; } = new();

    Lock gate = new();
    Dictionary<string, string> faceToPath = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<(string Family, bool Bold, bool Italic), string> index = [];
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
            // the first file seen) and which file wins when two map to the same family/style key.
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
        var bold = weight >= 600;

        faceToPath[path] = path;

        // The bundled file name is the authoritative key and overrides any earlier entry.
        index[(family.ToLowerInvariant(), bold, italic)] = path;

        // Also index the font's own declared names (Family, Full, PostScript, Typographic
        // from the name table) so a request using an abbreviated or alternate spelling — e.g.
        // "Trade Gothic Next Cond" for Trade_Gothic_Next_Condensed — finds this exact file
        // instead of being suffix-stripped onto a different family ("Trade Gothic Next").
        // Mirrors the shared FontFileCache, which indexes every declared name. TryAdd keeps
        // the file-name key (and the first file in ordinal order) authoritative on collisions.
        foreach (var declaredName in ReadDeclaredNames(path))
        {
            index.TryAdd((declaredName.ToLowerInvariant(), bold, italic), path);
        }

        defaultFace ??= path;
    }

    static IEnumerable<string> ReadDeclaredNames(string path)
    {
        try
        {
            return OpenTypeReader.ReadFaces(path)
                .SelectMany(_ => _.Names)
                .Where(_ => !string.IsNullOrEmpty(_))
                .ToList();
        }
        catch
        {
            // A font we can't parse contributes no alternate names; the file-name index
            // entry still serves it.
            return [];
        }
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
            return defaultFace == null ? null : new FontResolverInfo(defaultFace);
        }
    }

    bool TryResolve(string familyName, bool bold, bool italic, out string face)
    {
        // Order matters: keep the upright/italic axis correct before relaxing weight. When an
        // upright face is requested but only the italic of that exact weight is bundled (e.g.
        // Century Schoolbook ships 400-Italic, 700, 700-Italic but no 400 upright), falling back
        // to a different-weight upright reads far closer than swapping in a slanted same-weight
        // face. This mirrors the shared resolver's ScoreFace, where an italic mismatch (10_000)
        // outweighs any weight mismatch.
        Span<(bool Bold, bool Italic)> attempts =
        [
            (bold, italic),
            (!bold, italic),
            (bold, !italic),
            (!bold, !italic)
        ];

        // Try the requested family, then its suffix-stripped base (e.g. "Bodoni MT Condensed"
        // -> "Bodoni MT"), mirroring the shared resolver's candidate-name fallback so a width-
        // or weight-suffixed request still finds the bundled base face instead of dropping to
        // the default sans fallback.
        var candidates = FontHelpers.GetCandidateNames(familyName, bold);
        foreach (var candidateName in FontFileCache.EnumerateCandidateNames(candidates))
        {
            var family = candidateName.ToLowerInvariant();
            foreach (var (attemptBold, attemptItalic) in attempts)
            {
                if (index.TryGetValue((family, attemptBold, attemptItalic), out var found))
                {
                    face = found;
                    return true;
                }
            }
        }

        face = "";
        return false;
    }

    public byte[]? GetFont(string faceName)
    {
        lock (gate)
        {
            return faceToPath.TryGetValue(faceName, out var path) ? File.ReadAllBytes(path) : null;
        }
    }
}
