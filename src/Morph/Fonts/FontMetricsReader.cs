/// <summary>
/// Reads the OpenType metric tables a font declares (<c>head</c>, <c>hhea</c>), backend-independently,
/// as the canonical metric source for the layout engine (<c>docs/layout-engine-proposal.md</c>). It
/// uses the same random-access table-directory parsing as <see cref="OpenTypeReader"/> — that reader
/// pulls the <c>name</c>/<c>OS/2</c> strings resolution needs; this one pulls the numbers layout needs.
/// Glyph-advance tables (<c>maxp</c>/<c>cmap</c>/<c>hmtx</c>) will attach here next.
/// </summary>
static class FontMetricsReader
{
    const uint ttfMagic = 0x00010000;   // TrueType outlines
    const uint otfMagic = 0x4F54544F;   // 'OTTO' — CFF outlines
    const uint trueMagic = 0x74727565;  // 'true' — older Apple TrueType
    const uint ttcMagic = 0x74746366;   // 'ttcf' — TrueType Collection

    const uint headTag = 0x68656164;    // 'head'
    const uint hheaTag = 0x68686561;    // 'hhea'

    /// <summary>Reads face 0 of the font file at <paramref name="path"/>.</summary>
    public static FontMetrics? Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, 0);
    }

    /// <summary>
    /// Reads face <paramref name="faceIndex"/> from <paramref name="stream"/> (0 for a single-face
    /// ttf/otf; the collection member for a ttc). Returns null when the stream is not sfnt, the face
    /// index is out of range, or the <c>head</c>/<c>hhea</c> tables are missing or malformed. The
    /// stream must be seekable and is left open.
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
            // Re-read the member's own 12-byte offset table header.
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

        long headOffset = -1, hheaOffset = -1;
        for (var i = 0; i < numTables; i++)
        {
            var recordPos = i * 16;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos, 4));
            var off = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos + 8, 4));
            if (tag == headTag)
            {
                headOffset = off;
            }
            else if (tag == hheaTag)
            {
                hheaOffset = off;
            }
        }

        if (headOffset < 0 || hheaOffset < 0)
        {
            return null;
        }

        // head: unitsPerEm is a uint16 at offset 18.
        var headBytes = new byte[20];
        if (!TryReadExactAt(stream, headOffset, headBytes))
        {
            return null;
        }

        var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(headBytes.AsSpan(18, 2));
        if (unitsPerEm == 0)
        {
            return null;
        }

        // hhea: ascender (int16 @4), descender (int16 @6), lineGap (int16 @8).
        var hheaBytes = new byte[10];
        if (!TryReadExactAt(stream, hheaOffset, hheaBytes))
        {
            return null;
        }

        return new()
        {
            UnitsPerEm = unitsPerEm,
            Ascender = BinaryPrimitives.ReadInt16BigEndian(hheaBytes.AsSpan(4, 2)),
            Descender = BinaryPrimitives.ReadInt16BigEndian(hheaBytes.AsSpan(6, 2)),
            LineGap = BinaryPrimitives.ReadInt16BigEndian(hheaBytes.AsSpan(8, 2))
        };
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
