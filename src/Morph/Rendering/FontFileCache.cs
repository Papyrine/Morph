using System.Diagnostics.CodeAnalysis;

/// <summary>
/// A name-indexed view of font files available from one of the system font caches
/// (cloud, Office, or user fonts). Backends provide a family-name extractor (e.g.
/// `SKTypeface.FromFile` or `FontCollection.Add`) at construction time and can then
/// look up files by candidate name, with automatic fallback to weight-stripped names.
/// </summary>
sealed class FontFileCache
{
    readonly Dictionary<string, string[]> index;

    public FontFileCache(IEnumerable<string> fontFiles, Func<string, IEnumerable<string>> readFamilyNames)
    {
        var temp = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var fontFile in fontFiles)
        {
            IEnumerable<string> familyNames;
            try
            {
                familyNames = readFamilyNames(fontFile);
            }
            catch
            {
                // Ignore files that fail to load
                continue;
            }

            foreach (var familyName in familyNames)
            {
                if (string.IsNullOrEmpty(familyName))
                {
                    continue;
                }

                if (!temp.TryGetValue(familyName, out var files))
                {
                    files = [];
                    temp[familyName] = files;
                }

                files.Add(fontFile);
            }
        }

        var final = new Dictionary<string, string[]>(temp.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in temp)
        {
            final[kvp.Key] = kvp.Value.ToArray();
        }

        index = final;
    }

    /// <summary>
    /// Looks up font files by exact candidate name, falling back to a weight-stripped
    /// base name (e.g. "Arial Bold" → "Arial") if the exact name isn't indexed.
    /// </summary>
    public bool TryGet(string candidateName, [NotNullWhen(true)] out string[]? files)
    {
        if (index.TryGetValue(candidateName, out files))
        {
            return true;
        }

        var baseName = FontHelpers.StripWeightSuffixes(candidateName);
        if (baseName != candidateName &&
            index.TryGetValue(baseName, out files))
        {
            return true;
        }

        files = null;
        return false;
    }

    /// <summary>
    /// Looks up font files by iterating the priority-ordered name candidates
    /// (effective, original, stripped). Returns the first match.
    /// </summary>
    public bool TryGet(FontNameCandidates candidates, [NotNullWhen(true)] out string[]? files)
    {
        foreach (var name in EnumerateCandidateNames(candidates))
        {
            if (TryGet(name, out files))
            {
                return true;
            }
        }

        files = null;
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
    }
}
