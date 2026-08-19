/// <summary>
/// Pair kerning read from a font's <c>GPOS</c> table — the <c>kern</c> feature's LookupType 2
/// (PairPos) subtables, including type-9 extension wrappers, taking the FIRST glyph's
/// <c>xAdvance</c> adjustment. Backend-independent, like every metric the layout engine reads
/// (<see cref="FontMetrics"/>): Word applies these values itself when kerning is enabled
/// (<c>w:kern</c>), quantized per pair — measured via the <c>_probe_kern_*</c> fixtures as
/// <c>round(unkernedAdvance + round16(kern × em / upm))</c>, kern snapped to 1/16 px and the
/// pair's first-glyph advance then rounded to a whole layout pixel (even where the unkerned
/// advance was fractional: 24pt Calibri <c>Ta</c> renders T at 17.000px from an unkerned
/// 20.042). The quantization lives in <see cref="CanonicalTextMeasurer"/>; this type only
/// answers "how many design units does this pair adjust by".
///
/// <para>Class-based (format 2) subtables are kept as class matrices rather than expanded —
/// Calibri's expansion is ~350k pairs. Lookup walks subtables in font order and returns the
/// first nonzero adjustment whose coverage contains the first glyph, which matches how the
/// dominant single-lookup fonts behave; chained/contextual kerning (GSUB-driven) is out of
/// scope. Fonts with no GPOS kern feature yield a null table.</para>
/// </summary>
sealed class GposKernTable
{
    // format 1: exact pairs, key = (first glyph << 16) | second glyph
    readonly Dictionary<uint, short> pairs;
    readonly List<ClassSubtable> classSubtables;

    sealed record ClassSubtable(
        HashSet<ushort> Coverage,
        Dictionary<ushort, ushort> FirstClasses,
        Dictionary<ushort, ushort> SecondClasses,
        ushort SecondClassCount,
        short[] Matrix);

    GposKernTable(Dictionary<uint, short> pairs, List<ClassSubtable> classSubtables)
    {
        this.pairs = pairs;
        this.classSubtables = classSubtables;
    }

    /// <summary>The pair's xAdvance adjustment for <paramref name="first"/> followed by
    /// <paramref name="second"/>, in design units — 0 when the pair does not kern.</summary>
    public short KernUnits(ushort first, ushort second)
    {
        if (pairs.TryGetValue(((uint) first << 16) | second, out var exact) && exact != 0)
        {
            return exact;
        }

        foreach (var sub in classSubtables)
        {
            if (!sub.Coverage.Contains(first))
            {
                continue;
            }

            var c1 = sub.FirstClasses.GetValueOrDefault(first, (ushort) 0);
            var c2 = sub.SecondClasses.GetValueOrDefault(second, (ushort) 0);
            var value = sub.Matrix[c1 * sub.SecondClassCount + c2];
            if (value != 0)
            {
                return value;
            }
        }

        return 0;
    }

    /// <summary>
    /// Parses the <c>kern</c>-feature pair kerning out of a raw <c>GPOS</c> table. Returns null
    /// when the table carries no usable pair data. Offsets inside <paramref name="gpos"/> are
    /// all table-relative, so the caller only needs the GPOS slice.
    /// </summary>
    public static GposKernTable? Read(ReadOnlySpan<byte> gpos)
    {
        if (gpos.Length < 10)
        {
            return null;
        }

        var featureListOffset = ReadU16(gpos, 6);
        var lookupListOffset = ReadU16(gpos, 8);
        if (featureListOffset == 0 || lookupListOffset == 0)
        {
            return null;
        }

        // collect lookup indices referenced by any 'kern' feature
        var kernLookups = new SortedSet<ushort>();
        var featureCount = ReadU16(gpos, featureListOffset);
        for (var i = 0; i < featureCount; i++)
        {
            var rec = featureListOffset + 2 + 6 * i;
            if (rec + 6 > gpos.Length)
            {
                return null;
            }

            if (gpos[rec] != 'k' || gpos[rec + 1] != 'e' || gpos[rec + 2] != 'r' || gpos[rec + 3] != 'n')
            {
                continue;
            }

            var feature = featureListOffset + ReadU16(gpos, rec + 4);
            var lookupCount = ReadU16(gpos, feature + 2);
            for (var j = 0; j < lookupCount; j++)
            {
                kernLookups.Add(ReadU16(gpos, feature + 4 + 2 * j));
            }
        }

        if (kernLookups.Count == 0)
        {
            return null;
        }

        var pairs = new Dictionary<uint, short>();
        var classSubtables = new List<ClassSubtable>();
        var totalLookups = ReadU16(gpos, lookupListOffset);
        foreach (var lookupIndex in kernLookups)
        {
            if (lookupIndex >= totalLookups)
            {
                continue;
            }

            var lookup = lookupListOffset + ReadU16(gpos, lookupListOffset + 2 + 2 * lookupIndex);
            var lookupType = ReadU16(gpos, lookup);
            var subtableCount = ReadU16(gpos, lookup + 4);
            for (var s = 0; s < subtableCount; s++)
            {
                var subtable = lookup + ReadU16(gpos, lookup + 6 + 2 * s);
                var type = lookupType;
                if (type == 9)
                {
                    // extension positioning: real type + 32-bit offset from the extension header
                    type = ReadU16(gpos, subtable + 2);
                    subtable += (int) ReadU32(gpos, subtable + 4);
                }

                if (type != 2)
                {
                    continue;
                }

                ParsePairPos(gpos, subtable, pairs, classSubtables);
            }
        }

        return pairs.Count == 0 && classSubtables.Count == 0 ? null : new(pairs, classSubtables);
    }

    static void ParsePairPos(ReadOnlySpan<byte> gpos, int subtable, Dictionary<uint, short> pairs, List<ClassSubtable> classSubtables)
    {
        var format = ReadU16(gpos, subtable);
        var valueFormat1 = ReadU16(gpos, subtable + 4);
        var valueFormat2 = ReadU16(gpos, subtable + 6);
        var value1Length = 2 * System.Numerics.BitOperations.PopCount(valueFormat1);
        var value2Length = 2 * System.Numerics.BitOperations.PopCount(valueFormat2);

        if (format == 1)
        {
            var coverage = ReadCoverage(gpos, subtable + ReadU16(gpos, subtable + 2));
            var pairSetCount = ReadU16(gpos, subtable + 8);
            for (var i = 0; i < pairSetCount && i < coverage.Count; i++)
            {
                var first = coverage[i];
                var pairSet = subtable + ReadU16(gpos, subtable + 10 + 2 * i);
                var pairCount = ReadU16(gpos, pairSet);
                var record = pairSet + 2;
                for (var j = 0; j < pairCount; j++)
                {
                    var second = ReadU16(gpos, record);
                    var advance = ReadXAdvance(gpos, record + 2, valueFormat1);
                    if (advance != 0)
                    {
                        pairs.TryAdd(((uint) first << 16) | second, advance);
                    }

                    record += 2 + value1Length + value2Length;
                }
            }

            return;
        }

        if (format != 2)
        {
            return;
        }

        var cov = ReadCoverage(gpos, subtable + ReadU16(gpos, subtable + 2));
        var class1 = ReadClassDef(gpos, subtable + ReadU16(gpos, subtable + 8));
        var class2 = ReadClassDef(gpos, subtable + ReadU16(gpos, subtable + 10));
        var class1Count = ReadU16(gpos, subtable + 12);
        var class2Count = ReadU16(gpos, subtable + 14);
        var matrix = new short[class1Count * class2Count];
        var any = false;
        var pos = subtable + 16;
        for (var c1 = 0; c1 < class1Count; c1++)
        {
            for (var c2 = 0; c2 < class2Count; c2++)
            {
                var advance = ReadXAdvance(gpos, pos, valueFormat1);
                matrix[c1 * class2Count + c2] = advance;
                any |= advance != 0;
                pos += value1Length + value2Length;
            }
        }

        if (any)
        {
            classSubtables.Add(new([.. cov], class1, class2, (ushort) class2Count, matrix));
        }
    }

    static List<ushort> ReadCoverage(ReadOnlySpan<byte> gpos, int offset)
    {
        var glyphs = new List<ushort>();
        var format = ReadU16(gpos, offset);
        if (format == 1)
        {
            var count = ReadU16(gpos, offset + 2);
            for (var i = 0; i < count; i++)
            {
                glyphs.Add(ReadU16(gpos, offset + 4 + 2 * i));
            }
        }
        else if (format == 2)
        {
            var rangeCount = ReadU16(gpos, offset + 2);
            for (var i = 0; i < rangeCount; i++)
            {
                var start = ReadU16(gpos, offset + 4 + 6 * i);
                var end = ReadU16(gpos, offset + 6 + 6 * i);
                for (var g = start; g <= end && g >= start; g++)
                {
                    glyphs.Add(g);
                }
            }
        }

        return glyphs;
    }

    static Dictionary<ushort, ushort> ReadClassDef(ReadOnlySpan<byte> gpos, int offset)
    {
        var classes = new Dictionary<ushort, ushort>();
        var format = ReadU16(gpos, offset);
        if (format == 1)
        {
            var start = ReadU16(gpos, offset + 2);
            var count = ReadU16(gpos, offset + 4);
            for (var i = 0; i < count; i++)
            {
                var cls = ReadU16(gpos, offset + 6 + 2 * i);
                if (cls != 0)
                {
                    classes[(ushort) (start + i)] = cls;
                }
            }
        }
        else if (format == 2)
        {
            var rangeCount = ReadU16(gpos, offset + 2);
            for (var i = 0; i < rangeCount; i++)
            {
                var start = ReadU16(gpos, offset + 4 + 6 * i);
                var end = ReadU16(gpos, offset + 6 + 6 * i);
                var cls = ReadU16(gpos, offset + 8 + 6 * i);
                if (cls == 0)
                {
                    continue;
                }

                for (var g = start; g <= end && g >= start; g++)
                {
                    classes[g] = cls;
                }
            }
        }

        return classes;
    }

    // xAdvance sits after any xPlacement/yPlacement the value format declares
    static short ReadXAdvance(ReadOnlySpan<byte> gpos, int offset, ushort valueFormat)
    {
        if ((valueFormat & 0x0004) == 0)
        {
            return 0;
        }

        var pos = offset;
        if ((valueFormat & 0x0001) != 0)
        {
            pos += 2;
        }

        if ((valueFormat & 0x0002) != 0)
        {
            pos += 2;
        }

        return (short) ReadU16(gpos, pos);
    }

    static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        (ushort) ((data[offset] << 8) | data[offset + 1]);

    static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        ((uint) data[offset] << 24) | ((uint) data[offset + 1] << 16) | ((uint) data[offset + 2] << 8) | data[offset + 3];
}
