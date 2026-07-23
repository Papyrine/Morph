/// <summary>
/// A name-indexed view of font files available from one of the system font caches
/// (cloud, Office, user, system) or from a custom font directory. The cache reads
/// each font's <c>name</c> table at construction time and indexes the file under
/// every name it declares — Family, Full Name, PostScript Name, Typographic
/// Family/Subfamily — so a request like <c>"Segoe UI Semilight"</c> matches the
/// face directly without falling back to suffix-stripping heuristics.
/// </summary>
sealed class FontFileCache
{
    Dictionary<string, FontFace[]> index;

    /// <summary>
    /// Builds an index from <paramref name="fontFiles"/> using <paramref name="readFaces"/>
    /// (typically <see cref="OpenTypeReader.ReadFaces(string)"/>) to extract per-face metadata
    /// and the names each face should be indexed under.
    /// </summary>
    public FontFileCache(
        IEnumerable<string> fontFiles,
        Func<string, IEnumerable<(FontFace Face, IReadOnlyList<string> Names)>> readFaces)
    {
        var temp = new Dictionary<string, List<FontFace>>(StringComparer.OrdinalIgnoreCase);

        foreach (var fontFile in fontFiles)
        {
            // The extractor is typically an iterator, so exceptions surface during
            // enumeration — keep the foreach inside the try.
            try
            {
                foreach (var (face, names) in readFaces(fontFile))
                {
                    foreach (var name in names)
                    {
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        if (!temp.TryGetValue(name, out var list))
                        {
                            list = [];
                            temp[name] = list;
                        }

                        list.Add(face);
                    }
                }
            }
            catch
            {
                // Files that fail to parse simply aren't indexed — they can't be served
                // anyway, and the next cache layer (e.g. system fallback) gets a chance.
            }
        }

        var final = new Dictionary<string, FontFace[]>(temp.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in temp)
        {
            final[kvp.Key] = kvp.Value.ToArray();
        }

        index = final;
    }

    /// <summary>
    /// Convenience constructor for tests that supply a string-based extractor (one or
    /// more family names per file) without OS/2 metrics. Synthesises a placeholder
    /// <see cref="FontFace"/> with default weight/width/italic so the legacy lookup
    /// path keeps working in unit tests.
    /// </summary>
    public FontFileCache(
        IEnumerable<string> fontFiles,
        Func<string, IEnumerable<string>> readFamilyNames) :
        this(fontFiles, file => Wrap(file, readFamilyNames))
    {
    }

    static IEnumerable<(FontFace Face, IReadOnlyList<string> Names)> Wrap(
        string file, Func<string, IEnumerable<string>> readFamilyNames)
    {
        var names = readFamilyNames(file)
            .Where(_ => !string.IsNullOrEmpty(_))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
        {
            yield break;
        }

        yield return (
            new()
            {
                Path = file,
                Index = 0,
                Weight = 400,
                Width = 5,
                Italic = false
            },
            names);
    }

    /// <summary>
    /// Looks up faces by exact name, falling back to a weight-stripped base name
    /// (e.g. <c>"Arial Bold"</c> → <c>"Arial"</c>) only when the exact name isn't
    /// indexed. The fallback exists so synthetic test caches and badly-named files
    /// still resolve; well-formed font files declare their full name in the
    /// <c>name</c> table and hit the exact-match path.
    /// </summary>
    public bool TryGet(string candidateName, [NotNullWhen(true)] out FontFace[]? faces)
    {
        if (index.TryGetValue(candidateName, out faces))
        {
            return true;
        }

        var baseName = FontHelpers.StripWeightSuffixes(candidateName);
        if (baseName != candidateName &&
            index.TryGetValue(baseName, out faces))
        {
            return true;
        }

        faces = null;
        return false;
    }

    /// <summary>
    /// Looks up faces by iterating the priority-ordered name candidates
    /// (effective, original, stripped). Returns the first match.
    /// </summary>
    public bool TryGet(FontNameCandidates candidates, [NotNullWhen(true)] out FontFace[]? faces)
    {
        foreach (var name in EnumerateCandidateNames(candidates))
        {
            if (TryGet(name, out faces))
            {
                return true;
            }
        }

        faces = null;
        return false;
    }

    public bool Contains(string candidateName) => TryGet(candidateName, out _);

    /// <summary>
    /// Enumerates candidate names in priority order, skipping duplicates.
    /// </summary>
    public static IEnumerable<string> EnumerateCandidateNames(FontNameCandidates candidates)
    {
        yield return candidates.Effective;
        if (candidates.Original != candidates.Effective)
        {
            yield return candidates.Original;
        }

        if (candidates.Stripped != null)
        {
            yield return candidates.Stripped;
        }

        // Last resort: the same names with spaces restored between a lower-case letter and the
        // upper-case one after it. Yielded after everything else so a family that resolves exactly
        // is never diverted — this only rescues a name written without its spaces, which would
        // otherwise fall through to an unrelated face.
        var spaced = FontHelpers.InsertMissingSpaces(candidates.Original);
        if (spaced != candidates.Original)
        {
            yield return spaced;

            var spacedStripped = FontHelpers.StripWeightSuffixes(spaced);
            if (!string.IsNullOrEmpty(spacedStripped) &&
                spacedStripped != spaced &&
                spacedStripped != candidates.Stripped)
            {
                yield return spacedStripped;
            }
        }
    }
}
