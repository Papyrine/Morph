/// <summary>
/// Reads the OpenType metric tables a font declares — <c>head</c>, <c>hhea</c> (line metrics) and
/// <c>maxp</c>/<c>hmtx</c>/<c>cmap</c> (glyph advances) — backend-independently, as the canonical
/// metric source for the layout engine (<c>docs/layout-engine-proposal.md</c>). It uses the same
/// random-access table-directory parsing as <see cref="OpenTypeReader"/>; that reader pulls the
/// <c>name</c>/<c>OS/2</c> strings resolution needs, this one pulls the numbers layout needs.
/// </summary>
static class FontMetricsReader
{
    const uint ttfMagic = 0x00010000;   // TrueType outlines
    const uint otfMagic = 0x4F54544F;   // 'OTTO' — CFF outlines
    const uint trueMagic = 0x74727565;  // 'true' — older Apple TrueType
    const uint ttcMagic = 0x74746366;   // 'ttcf' — TrueType Collection

    const uint headTag = 0x68656164;    // 'head'
    const uint hheaTag = 0x68686561;    // 'hhea'
    const uint hmtxTag = 0x686D7478;    // 'hmtx'
    const uint cmapTag = 0x636D6170;    // 'cmap'

    // Safety bound on the eagerly-expanded codepoint→glyph map, so a malformed cmap claiming a
    // group spanning the whole Unicode range can't allocate unbounded memory. Far above any real
    // font's mapped-codepoint count.
    const int maxCmapEntries = 200_000;

    /// <summary>Reads face 0 of the font file at <paramref name="path"/>.</summary>
    public static FontMetrics? Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, 0);
    }

    /// <summary>
    /// Reads face <paramref name="faceIndex"/> from <paramref name="stream"/> (0 for a single-face
    /// ttf/otf; the collection member for a ttc). Returns null when the stream is not sfnt, the face
    /// index is out of range, or the <c>head</c>/<c>hhea</c> tables are missing or malformed. Advance
    /// data is best-effort: a missing or malformed <c>maxp</c>/<c>hmtx</c>/<c>cmap</c> leaves the line
    /// metrics valid and the advance data empty. The stream must be seekable and is left open.
    /// </summary>
    public static FontMetrics? Read(Stream stream, int faceIndex = 0)
    {
        var header = new byte[12];
        if (!TryReadExactAt(stream, 0, header))
        {
            return null;
        }

        var sig = BinaryPrimitives.ReadUInt32BigEndian(header);

        long sfntOffset;
        if (sig == ttcMagic)
        {
            var numFonts = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
            if (faceIndex < 0 || (uint) faceIndex >= numFonts || numFonts > 0x10000)
            {
                return null;
            }

            var offsetBytes = new byte[4];
            if (!TryReadExactAt(stream, 12 + faceIndex * 4, offsetBytes))
            {
                return null;
            }

            sfntOffset = BinaryPrimitives.ReadUInt32BigEndian(offsetBytes);
            if (!TryReadExactAt(stream, sfntOffset, header))
            {
                return null;
            }

            sig = BinaryPrimitives.ReadUInt32BigEndian(header);
        }
        else
        {
            sfntOffset = 0;
        }

        if (sig is not (ttfMagic or otfMagic or trueMagic))
        {
            return null;
        }

        var numTables = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (numTables == 0)
        {
            return null;
        }

        var directory = new byte[numTables * 16];
        if (!TryReadExactAt(stream, sfntOffset + 12, directory))
        {
            return null;
        }

        var tables = new Dictionary<uint, (long Offset, int Length)>();
        for (var i = 0; i < numTables; i++)
        {
            var recordPos = i * 16;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos, 4));
            var off = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos + 8, 4));
            var len = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos + 12, 4));
            tables[tag] = (off, (int) len);
        }

        if (!tables.TryGetValue(headTag, out var head) ||
            !tables.TryGetValue(hheaTag, out var hhea))
        {
            return null;
        }

        // head: unitsPerEm is a uint16 at offset 18.
        var headBytes = new byte[20];
        if (!TryReadExactAt(stream, head.Offset, headBytes))
        {
            return null;
        }

        var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(headBytes.AsSpan(18, 2));
        if (unitsPerEm == 0)
        {
            return null;
        }

        // hhea: ascender (int16 @4), descender (int16 @6), lineGap (int16 @8), numberOfHMetrics (uint16 @34).
        var hheaBytes = new byte[36];
        if (!TryReadExactAt(stream, hhea.Offset, hheaBytes))
        {
            return null;
        }

        var numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hheaBytes.AsSpan(34, 2));

        return new()
        {
            UnitsPerEm = unitsPerEm,
            Ascender = BinaryPrimitives.ReadInt16BigEndian(hheaBytes.AsSpan(4, 2)),
            Descender = BinaryPrimitives.ReadInt16BigEndian(hheaBytes.AsSpan(6, 2)),
            LineGap = BinaryPrimitives.ReadInt16BigEndian(hheaBytes.AsSpan(8, 2)),
            AdvanceWidths = ReadAdvanceWidths(stream, tables, numberOfHMetrics),
            GlyphForCodepoint = ReadCmap(stream, tables)
        };
    }

    /// <summary>
    /// Reads the <c>hmtx</c> advance widths — one uint16 per glyph for the first
    /// <paramref name="numberOfHMetrics"/> glyphs (each 4-byte record is advanceWidth + leftSideBearing;
    /// only the advance is kept). Empty on any malformation.
    /// </summary>
    static ushort[] ReadAdvanceWidths(Stream stream, Dictionary<uint, (long Offset, int Length)> tables, int numberOfHMetrics)
    {
        if (numberOfHMetrics == 0 || !tables.TryGetValue(hmtxTag, out var hmtx))
        {
            return [];
        }

        var needed = numberOfHMetrics * 4;
        var bytes = new byte[needed];
        if (hmtx.Length < needed || !TryReadExactAt(stream, hmtx.Offset, bytes))
        {
            return [];
        }

        var advances = new ushort[numberOfHMetrics];
        for (var i = 0; i < numberOfHMetrics; i++)
        {
            advances[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i * 4, 2));
        }

        return advances;
    }

    /// <summary>
    /// Reads the <c>cmap</c> table, selects the best Unicode subtable, and expands it to a
    /// codepoint→glyph map. Prefers a Windows UCS-4 (3,10) or BMP (3,1) subtable, then any Unicode
    /// (platform 0). Formats 4 and 12 are supported; anything else yields an empty map.
    /// </summary>
    static Dictionary<int, ushort> ReadCmap(Stream stream, Dictionary<uint, (long Offset, int Length)> tables)
    {
        var result = new Dictionary<int, ushort>();
        if (!tables.TryGetValue(cmapTag, out var cmap) || cmap.Length < 4)
        {
            return result;
        }

        var bytes = new byte[cmap.Length];
        if (!TryReadExactAt(stream, cmap.Offset, bytes))
        {
            return result;
        }

        var numSubtables = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2));
        long bestOffset = -1;
        var bestScore = -1;
        for (var i = 0; i < numSubtables; i++)
        {
            var recordPos = 4 + i * 8;
            if (recordPos + 8 > bytes.Length)
            {
                break;
            }

            var platform = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos, 2));
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos + 2, 2));
            var subtableOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordPos + 4, 4));

            // Score Unicode subtables; higher is better. Windows UCS-4 outranks Windows BMP, which
            // outranks the Unicode platform, matching what SkiaSharp / SixLabors pick.
            var score = (platform, encoding) switch
            {
                (3, 10) => 4,
                (3, 1) => 3,
                (0, _) => 2,
                _ => -1
            };
            if (score > bestScore && subtableOffset < (uint) bytes.Length)
            {
                bestScore = score;
                bestOffset = subtableOffset;
            }
        }

        if (bestOffset < 0 || bestOffset + 2 > bytes.Length)
        {
            return result;
        }

        var format = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan((int) bestOffset, 2));
        if (format == 4)
        {
            ExpandFormat4(bytes, (int) bestOffset, result);
        }
        else if (format == 12)
        {
            ExpandFormat12(bytes, (int) bestOffset, result);
        }

        return result;
    }

    /// <summary>Expands a format-4 (segment-mapped BMP) cmap subtable into <paramref name="map"/>.</summary>
    static void ExpandFormat4(byte[] bytes, int subtable, Dictionary<int, ushort> map)
    {
        var segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(subtable + 6, 2));
        var segCount = segCountX2 / 2;
        var endCodes = subtable + 14;
        var startCodes = endCodes + segCountX2 + 2;       // + reservedPad
        var idDeltas = startCodes + segCountX2;
        var idRangeOffsets = idDeltas + segCountX2;
        if (idRangeOffsets + segCountX2 > bytes.Length)
        {
            return;
        }

        for (var seg = 0; seg < segCount; seg++)
        {
            int end = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(endCodes + seg * 2, 2));
            int start = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(startCodes + seg * 2, 2));
            var delta = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(idDeltas + seg * 2, 2));
            var rangeOffsetPos = idRangeOffsets + seg * 2;
            int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(rangeOffsetPos, 2));

            if (start > end || start == 0xFFFF)
            {
                continue; // the required 0xFFFF sentinel segment maps nothing
            }

            for (var code = start; code <= end; code++)
            {
                ushort glyph;
                if (rangeOffset == 0)
                {
                    glyph = (ushort) ((code + delta) & 0xFFFF);
                }
                else
                {
                    // OpenType's glyph-index indirection: the address is relative to the position of
                    // this segment's idRangeOffset entry.
                    var glyphAddr = rangeOffsetPos + rangeOffset + (code - start) * 2;
                    if (glyphAddr + 2 > bytes.Length)
                    {
                        continue;
                    }

                    var raw = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(glyphAddr, 2));
                    glyph = raw == 0 ? (ushort) 0 : (ushort) ((raw + delta) & 0xFFFF);
                }

                if (glyph != 0)
                {
                    map[code] = glyph;
                    if (map.Count >= maxCmapEntries)
                    {
                        return;
                    }
                }
            }
        }
    }

    /// <summary>Expands a format-12 (segmented-coverage, full-Unicode) cmap subtable into <paramref name="map"/>.</summary>
    static void ExpandFormat12(byte[] bytes, int subtable, Dictionary<int, ushort> map)
    {
        if (subtable + 16 > bytes.Length)
        {
            return;
        }

        var numGroups = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(subtable + 12, 4));
        var groups = subtable + 16;
        for (uint g = 0; g < numGroups; g++)
        {
            var recordPos = groups + (int) g * 12;
            if (recordPos + 12 > bytes.Length)
            {
                return;
            }

            var start = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordPos, 4));
            var end = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordPos + 4, 4));
            var startGlyph = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordPos + 8, 4));
            if (start > end)
            {
                continue;
            }

            for (var code = start; code <= end; code++)
            {
                var glyph = startGlyph + (code - start);
                if (glyph is > 0 and <= 0xFFFF)
                {
                    map[(int) code] = (ushort) glyph;
                    if (map.Count >= maxCmapEntries)
                    {
                        return;
                    }
                }

                if (code == 0xFFFFFFFF)
                {
                    break; // guard the unsigned wrap at the top of the range
                }
            }
        }
    }

    static bool TryReadExactAt(Stream stream, long position, byte[] buffer)
    {
        if (position < 0 || position + buffer.Length > stream.Length)
        {
            return false;
        }

        stream.Position = position;
        try
        {
            stream.ReadExactly(buffer);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }
}
