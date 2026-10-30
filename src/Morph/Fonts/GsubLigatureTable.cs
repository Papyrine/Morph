/// <summary>
/// Standard ligatures read from a font's <c>GSUB</c> table — the <c>liga</c> feature's LookupType 4
/// (ligature substitution) subtables, including type-7 extension wrappers. Word's layout measures a
/// ligature by its own advance in compatibility mode 15 (<see cref="RunProperties.Ligatures"/>), so the
/// measurer substitutes before it adds advances (<see cref="CanonicalTextMeasurer"/>). Lookups are
/// applied as one greedy pass, the longest match at each glyph first and the lookups in font order
/// among equals, which is what Calibri's <c>ffi</c> / <c>ff</c> / <c>fi</c> tables need; contextual
/// and chained substitutions are out of scope.
/// </summary>
sealed class GsubLigatureTable
{
    // first glyph -> (the glyphs that follow it, the ligature), longest first
    readonly Dictionary<ushort, List<(ushort[] Following, ushort Ligature)>> ligatures;

    GsubLigatureTable(Dictionary<ushort, List<(ushort[] Following, ushort Ligature)>> ligatures) =>
        this.ligatures = ligatures;

    /// <summary>
    /// The glyphs with each ligature substituted: <paramref name="glyphs"/> itself when nothing
    /// matches.
    /// </summary>
    public IReadOnlyList<ushort> Substitute(IReadOnlyList<ushort> glyphs)
    {
        List<ushort>? result = null;
        var index = 0;
        while (index < glyphs.Count)
        {
            var matched = 0;
            ushort ligature = 0;
            if (ligatures.TryGetValue(glyphs[index], out var candidates))
            {
                foreach (var (rest, glyph) in candidates)
                {
                    if (Matches(glyphs, index + 1, rest))
                    {
                        matched = rest.Length + 1;
                        ligature = glyph;
                        break;
                    }
                }
            }

            if (matched == 0)
            {
                result?.Add(glyphs[index]);
                index++;
                continue;
            }

            result ??= [.. glyphs.Take(index)];
            result.Add(ligature);
            index += matched;
        }

        return result ?? glyphs;
    }

    static bool Matches(IReadOnlyList<ushort> glyphs, int start, ushort[] rest)
    {
        if (start + rest.Length > glyphs.Count)
        {
            return false;
        }

        for (var i = 0; i < rest.Length; i++)
        {
            if (glyphs[start + i] != rest[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses the <c>liga</c> feature's ligature substitutions out of a raw <c>GSUB</c> table. Null
    /// when it has none. Offsets inside <paramref name="gsub"/> are all table-relative.
    /// </summary>
    public static GsubLigatureTable? Read(ReadOnlySpan<byte> gsub)
    {
        if (gsub.Length < 10)
        {
            return null;
        }

        var featureListOffset = ReadU16(gsub, 6);
        var lookupListOffset = ReadU16(gsub, 8);
        if (featureListOffset == 0 || lookupListOffset == 0)
        {
            return null;
        }

        var ligaLookups = new SortedSet<ushort>();
        var featureCount = ReadU16(gsub, featureListOffset);
        for (var i = 0; i < featureCount; i++)
        {
            var record = featureListOffset + 2 + 6 * i;
            if (gsub[record] != 'l' || gsub[record + 1] != 'i' || gsub[record + 2] != 'g' || gsub[record + 3] != 'a')
            {
                continue;
            }

            var feature = featureListOffset + ReadU16(gsub, record + 4);
            var lookupCount = ReadU16(gsub, feature + 2);
            for (var j = 0; j < lookupCount; j++)
            {
                ligaLookups.Add(ReadU16(gsub, feature + 4 + 2 * j));
            }
        }

        var ligatures = new Dictionary<ushort, List<(ushort[] Following, ushort Ligature)>>();
        var totalLookups = ReadU16(gsub, lookupListOffset);
        foreach (var lookupIndex in ligaLookups)
        {
            if (lookupIndex >= totalLookups)
            {
                continue;
            }

            var lookup = lookupListOffset + ReadU16(gsub, lookupListOffset + 2 + 2 * lookupIndex);
            var lookupType = ReadU16(gsub, lookup);
            var subtableCount = ReadU16(gsub, lookup + 4);
            for (var s = 0; s < subtableCount; s++)
            {
                var subtable = lookup + ReadU16(gsub, lookup + 6 + 2 * s);
                var type = lookupType;
                if (type == 7)
                {
                    // extension substitution: the real type and a 32-bit offset from its header
                    type = ReadU16(gsub, subtable + 2);
                    subtable += (int) ReadU32(gsub, subtable + 4);
                }

                if (type == 4 &&
                    ReadU16(gsub, subtable) == 1)
                {
                    ReadLigatureSubst(gsub, subtable, ligatures);
                }
            }
        }

        if (ligatures.Count == 0)
        {
            return null;
        }

        // Longest first; a stable sort keeps the font's order among ligatures of one length.
        foreach (var list in ligatures.Values)
        {
            var ordered = list.OrderByDescending(_ => _.Following.Length).ToList();
            list.Clear();
            list.AddRange(ordered);
        }

        return new(ligatures);
    }

    static void ReadLigatureSubst(ReadOnlySpan<byte> gsub, int subtable, Dictionary<ushort, List<(ushort[] Following, ushort Ligature)>> ligatures)
    {
        var coverage = ReadCoverage(gsub, subtable + ReadU16(gsub, subtable + 2));
        var setCount = ReadU16(gsub, subtable + 4);
        for (var i = 0; i < setCount && i < coverage.Count; i++)
        {
            var set = subtable + ReadU16(gsub, subtable + 6 + 2 * i);
            var count = ReadU16(gsub, set);
            for (var j = 0; j < count; j++)
            {
                var ligature = set + ReadU16(gsub, set + 2 + 2 * j);
                var glyph = ReadU16(gsub, ligature);
                var components = ReadU16(gsub, ligature + 2);
                if (components < 2)
                {
                    continue;
                }

                var rest = new ushort[components - 1];
                for (var k = 0; k < rest.Length; k++)
                {
                    rest[k] = ReadU16(gsub, ligature + 4 + 2 * k);
                }

                if (!ligatures.TryGetValue(coverage[i], out var list))
                {
                    list = [];
                    ligatures[coverage[i]] = list;
                }

                list.Add((rest, glyph));
            }
        }
    }

    static List<ushort> ReadCoverage(ReadOnlySpan<byte> gsub, int offset)
    {
        var glyphs = new List<ushort>();
        var format = ReadU16(gsub, offset);
        if (format == 1)
        {
            var count = ReadU16(gsub, offset + 2);
            for (var i = 0; i < count; i++)
            {
                glyphs.Add(ReadU16(gsub, offset + 4 + 2 * i));
            }
        }
        else if (format == 2)
        {
            var rangeCount = ReadU16(gsub, offset + 2);
            for (var i = 0; i < rangeCount; i++)
            {
                var start = ReadU16(gsub, offset + 4 + 6 * i);
                var end = ReadU16(gsub, offset + 6 + 6 * i);
                for (var glyph = start; glyph <= end && glyph >= start; glyph++)
                {
                    glyphs.Add(glyph);
                }
            }
        }

        return glyphs;
    }

    static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        (ushort) ((data[offset] << 8) | data[offset + 1]);

    static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        ((uint) data[offset] << 24) | ((uint) data[offset + 1] << 16) | ((uint) data[offset + 2] << 8) | data[offset + 3];
}
