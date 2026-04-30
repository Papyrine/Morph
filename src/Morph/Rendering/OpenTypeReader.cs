using System.IO.Compression;

/// <summary>
/// Minimal reader for the OpenType <c>name</c> and <c>OS/2</c> tables. Used to index
/// font files by every name they declare (Family, Full Name, PostScript Name, Typographic
/// Family/Subfamily) and to surface their weight/width/italic metrics so the resolver can
/// pick the closest face without consulting platform font managers.
/// </summary>
/// <remarks>
/// <para>
/// Reading the font's own metadata is the authoritative way to resolve a string like
/// <c>"Segoe UI Semilight"</c>: it is literally Name ID 4 of <c>segoeuisl.ttf</c>. This
/// avoids the heuristics needed when the only available signal is <c>SKTypeface.FamilyName</c>
/// (which on Windows collapses Semilight into the base "Segoe UI" family) or a hardcoded
/// list of weight suffixes.
/// </para>
/// <para>
/// The reader uses random-access stream seeks rather than slurping the whole file into
/// memory: only the table directory and the <c>name</c>/<c>OS/2</c> table bytes are
/// pulled from disk, which matters when scanning thousands of system fonts at startup.
/// WOFF2 files are handled via a partial Brotli decode that stops as soon as the target
/// tables have been emitted by the decompressor — the rest of the compressed payload
/// (typically the bulk of the file: <c>glyf</c>/<c>loca</c>/<c>CFF</c>) is never touched.
/// </para>
/// </remarks>
static class OpenTypeReader
{
    // sfntVersion magic numbers
    const uint ttfMagic = 0x00010000;       // TrueType outlines
    const uint otfMagic = 0x4F54544F;       // 'OTTO' — CFF outlines
    const uint trueMagic = 0x74727565;      // 'true' — older Apple TrueType
    const uint ttcMagic = 0x74746366;       // 'ttcf' — TrueType Collection
    const uint woff2Magic = 0x774F4632;     // 'wOF2' — Web Open Font Format 2.0

    // Table tags (big-endian 4-char codes)
    const uint nameTag = 0x6E616D65;        // 'name'
    const uint os2Tag = 0x4F532F32;         // 'OS/2'

    // Name IDs we extract (ignoring the rest)
    const int nameIdFamily = 1;
    const int nameIdSubfamily = 2;
    const int nameIdFullName = 4;
    const int nameIdPostScript = 6;
    const int nameIdTypographicFamily = 16;
    const int nameIdTypographicSubfamily = 17;

    // RFC 8467 §5.1 — WOFF2 directory entries with index < 0x3F resolve to one of
    // these tags by position; index 0x3F means an explicit 4-byte tag follows.
    static readonly string[] woff2KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ",
        "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT", "EBLC", "gasp",
        "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF",
        "GPOS", "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL",
        "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc",
        "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar", "mort", "morx",
        "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill"
    ];

    /// <summary>
    /// Reads every face in <paramref name="path"/> and returns its metadata along with
    /// the names under which it should be indexed.
    /// </summary>
    public static IEnumerable<(FontFace Face, IReadOnlyList<string> Names)> ReadFaces(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (var item in ReadFaces(stream, path))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Reads every face from <paramref name="stream"/> using <paramref name="sourcePath"/>
    /// as the value to record on each <see cref="FontFace.Path"/> (typically the file path
    /// the bytes came from, or a synthetic identifier for in-memory sources). The stream
    /// must be seekable and is left open after enumeration completes.
    /// </summary>
    public static IEnumerable<(FontFace Face, IReadOnlyList<string> Names)> ReadFaces(Stream stream, string sourcePath)
    {
        var header = new byte[12];
        if (!TryReadExact(stream, header))
        {
            yield break;
        }

        var sig = BinaryPrimitives.ReadUInt32BigEndian(header);

        if (sig == woff2Magic)
        {
            if (TryReadWoff2Face(stream, header, sourcePath, out var face, out var names))
            {
                yield return (face, names);
            }

            yield break;
        }

        if (sig == ttcMagic)
        {
            // TTC: header is { tag, version, numFonts, offsets[numFonts] }; the first
            // 12 bytes carry tag/version/numFonts. Cap numFonts at a sane value so we
            // don't blow up on malformed files.
            var numFonts = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
            if (numFonts is 0 or > 0x10000)
            {
                yield break;
            }

            var offsetBytes = new byte[numFonts * 4];
            if (!TryReadExact(stream, offsetBytes))
            {
                yield break;
            }

            for (var i = 0; i < numFonts; i++)
            {
                var faceOffset = BinaryPrimitives.ReadUInt32BigEndian(offsetBytes.AsSpan(i * 4, 4));
                if (TryReadSfntFace(stream, faceOffset, sourcePath, i, out var face, out var names))
                {
                    yield return (face, names);
                }
            }

            yield break;
        }

        if (sig is ttfMagic or otfMagic or trueMagic)
        {
            // The 12-byte buffer we already read IS this single face's offset table
            // header, so we can pass it straight through without re-reading.
            if (TryReadSfntFaceFromHeader(stream, header, sourcePath, 0, out var face, out var names))
            {
                yield return (face, names);
            }
        }
    }

    /// <summary>
    /// Seeks to <paramref name="faceOffset"/> in <paramref name="stream"/>, reads the
    /// 12-byte offset table header, and parses that face. Used for TTC member faces.
    /// </summary>
    static bool TryReadSfntFace(Stream stream, long faceOffset, string path, int index,
        out FontFace face, out IReadOnlyList<string> names)
    {
        face = null!;
        names = [];

        if (faceOffset < 0 || faceOffset + 12 > stream.Length)
        {
            return false;
        }

        stream.Position = faceOffset;
        var header = new byte[12];
        if (!TryReadExact(stream, header))
        {
            return false;
        }

        return TryReadSfntFaceFromHeader(stream, header, path, index, out face, out names);
    }

    /// <summary>
    /// Reads a single SFNT face given its already-read 12-byte offset table header. The
    /// stream must be positioned immediately after the header (at the start of the
    /// table directory records).
    /// </summary>
    static bool TryReadSfntFaceFromHeader(Stream stream, byte[] header, string path, int index,
        out FontFace face, out IReadOnlyList<string> names)
    {
        face = null!;
        names = [];

        var numTables = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (numTables == 0)
        {
            return false;
        }

        var directorySize = numTables * 16;
        if (stream.Position + directorySize > stream.Length)
        {
            return false;
        }

        var directory = new byte[directorySize];
        if (!TryReadExact(stream, directory))
        {
            return false;
        }

        long nameOffset = -1, os2Offset = -1;
        var nameLength = 0;
        var os2Length = 0;

        for (var i = 0; i < numTables; i++)
        {
            var recordPos = i * 16;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos, 4));
            // [recordPos + 4 .. + 8] = checksum (unused)
            var off = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos + 8, 4));
            var len = BinaryPrimitives.ReadUInt32BigEndian(directory.AsSpan(recordPos + 12, 4));

            if (tag == nameTag)
            {
                nameOffset = off;
                nameLength = (int) len;
            }
            else if (tag == os2Tag)
            {
                os2Offset = off;
                os2Length = (int) len;
            }
        }

        if (nameOffset < 0 || nameLength <= 0 || nameOffset + nameLength > stream.Length)
        {
            return false;
        }

        var nameBytes = new byte[nameLength];
        stream.Position = nameOffset;
        if (!TryReadExact(stream, nameBytes))
        {
            return false;
        }

        var os2Bytes = Array.Empty<byte>();
        if (os2Offset >= 0 && os2Length > 0 && os2Offset + os2Length <= stream.Length)
        {
            os2Bytes = new byte[os2Length];
            stream.Position = os2Offset;
            if (!TryReadExact(stream, os2Bytes))
            {
                os2Bytes = Array.Empty<byte>();
            }
        }

        var nameRecords = ReadNameTable(nameBytes);
        if (nameRecords.Count == 0)
        {
            return false;
        }

        var (weight, width, italic) = ReadOs2(os2Bytes);

        face = new()
        {
            Path = path,
            Index = index,
            Family = PrimaryFamilyName(nameRecords),
            Weight = weight,
            Width = width,
            Italic = italic,
        };
        names = BuildIndexNames(nameRecords);
        return true;
    }

    /// <summary>
    /// Parses a single-font WOFF2 file and decompresses just enough of its Brotli payload
    /// to read the <c>name</c> and <c>OS/2</c> tables. Font collections (flavor 'ttcf')
    /// are skipped — the partial-decode path is single-stream only.
    /// </summary>
    static bool TryReadWoff2Face(Stream stream, byte[] header12, string path,
        out FontFace face, out IReadOnlyList<string> names)
    {
        face = null!;
        names = [];

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(header12.AsSpan(4, 4));
        if (flavor == ttcMagic)
        {
            // TTC inside WOFF2 uses a CollectionDirectory that maps faces to overlapping
            // subsets of the global table list — supporting it would mean tracking which
            // logical face owns which uncompressed-stream range. Rare in practice; skip.
            return false;
        }

        // Read the remaining 36 bytes of the 48-byte WOFF2 header.
        var rest = new byte[36];
        if (!TryReadExact(stream, rest))
        {
            return false;
        }

        var numTables = BinaryPrimitives.ReadUInt16BigEndian(rest.AsSpan(0, 2));
        if (numTables == 0)
        {
            return false;
        }

        // Walk the (uncompressed) table directory, computing each table's offset and
        // length within the decompressed Brotli stream. The decompressed stream is the
        // tables concatenated in directory order; each contributes either origLength
        // bytes or, when the entry is transformed, transformLength bytes.
        var entries = new (string Tag, ulong Offset, ulong Length)[numTables];
        ulong uncompOffset = 0;

        for (var i = 0; i < numTables; i++)
        {
            var flagByte = stream.ReadByte();
            if (flagByte < 0)
            {
                return false;
            }

            var knownIndex = flagByte & 0x3F;
            var xformVersion = (flagByte >> 6) & 0x03;

            string tag;
            if (knownIndex == 0x3F)
            {
                var tagBytes = new byte[4];
                if (!TryReadExact(stream, tagBytes))
                {
                    return false;
                }
                tag = Encoding.ASCII.GetString(tagBytes);
            }
            else
            {
                tag = woff2KnownTags[knownIndex];
            }

            // For glyf/loca, transform version 0 means "transformed" (transformLength
            // present); for any other table, a non-zero transform version means the same.
            // We don't care which form the bytes take — only their length in the
            // decompressed stream.
            var transformed = tag is "glyf" or "loca" ? xformVersion == 0 : xformVersion != 0;

            if (!TryReadUIntBase128(stream, out var origLength))
            {
                return false;
            }

            ulong tableLengthInStream = origLength;
            if (transformed)
            {
                if (!TryReadUIntBase128(stream, out var xformLength))
                {
                    return false;
                }

                tableLengthInStream = xformLength;
            }

            var nextOffset = uncompOffset + tableLengthInStream;
            if (nextOffset < uncompOffset)
            {
                return false; // overflow
            }

            entries[i] = (tag, uncompOffset, tableLengthInStream);
            uncompOffset = nextOffset;
        }

        // After the directory, the file stream sits at the start of the Brotli payload.
        ulong nameOffset = 0, nameLength = 0;
        ulong os2Offset = 0, os2Length = 0;
        var nameFound = false;
        var os2Found = false;

        foreach (var entry in entries)
        {
            if (entry.Tag == "name")
            {
                nameOffset = entry.Offset;
                nameLength = entry.Length;
                nameFound = true;
            }
            else if (entry.Tag == "OS/2")
            {
                os2Offset = entry.Offset;
                os2Length = entry.Length;
                os2Found = true;
            }
        }

        if (!nameFound || nameLength == 0)
        {
            return false;
        }

        var maxNeeded = nameOffset + nameLength;
        if (os2Found)
        {
            var os2End = os2Offset + os2Length;
            if (os2End > maxNeeded)
            {
                maxNeeded = os2End;
            }
        }

        // Sanity bound: a metadata-only read shouldn't need anywhere near 2 GB. If the
        // directory claims it does, the file is malformed or hostile.
        if (maxNeeded > int.MaxValue)
        {
            return false;
        }

        var decompressed = new byte[(int) maxNeeded];
        try
        {
            // BrotliStream pulls bytes lazily — calling ReadExactly for `maxNeeded`
            // bytes only decompresses up to the last needed table. The trailing
            // payload (typically glyf/loca/CFF) is never read or decoded.
            using var brotli = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true);
            brotli.ReadExactly(decompressed);
        }
        catch
        {
            return false;
        }

        var nameBytes = new byte[(int) nameLength];
        Array.Copy(decompressed, (int) nameOffset, nameBytes, 0, (int) nameLength);

        var os2Bytes = Array.Empty<byte>();
        if (os2Found && os2Length > 0)
        {
            os2Bytes = new byte[(int) os2Length];
            Array.Copy(decompressed, (int) os2Offset, os2Bytes, 0, (int) os2Length);
        }

        var nameRecords = ReadNameTable(nameBytes);
        if (nameRecords.Count == 0)
        {
            return false;
        }

        var (weight, width, italic) = ReadOs2(os2Bytes);

        face = new()
        {
            Path = path,
            Index = 0,
            Family = PrimaryFamilyName(nameRecords),
            Weight = weight,
            Width = width,
            Italic = italic,
        };
        names = BuildIndexNames(nameRecords);
        return true;
    }

    /// <summary>
    /// Returns the canonical family name for a face — <c>name</c> table ID 1 (Family),
    /// falling back to ID 16 (Typographic Family) only when ID 1 is missing. ID 1 is
    /// what SixLabors' <c>FontCollection</c> registers families by, so for the file
    /// <c>Calibri_300.ttf</c> we want "Calibri Light" (ID 1) here, not "Calibri" (ID 16,
    /// the typographic family). The latter strips the weight out, which is right for
    /// typographic grouping but wrong for the path-keyed lookup ImageSharp needs.
    /// </summary>
    static string PrimaryFamilyName(Dictionary<int, string> nameRecords)
    {
        if (nameRecords.TryGetValue(nameIdFamily, out var family) &&
            !string.IsNullOrWhiteSpace(family))
        {
            return family;
        }

        return nameRecords.GetValueOrDefault(nameIdTypographicFamily, "");
    }

    /// <summary>
    /// Parses a <c>name</c> table from its raw bytes into a map of <c>nameID</c> →
    /// string, preferring English Windows records over Mac/Unicode records when the
    /// same name is declared multiple times.
    /// </summary>
    static Dictionary<int, string> ReadNameTable(byte[] tableBytes)
    {
        var result = new Dictionary<int, string>();
        if (tableBytes.Length < 6)
        {
            return result;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(2, 2));
        var storageOffset = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(4, 2));
        const int recordsStart = 6;
        if (recordsStart + count * 12 > tableBytes.Length)
        {
            return result;
        }

        // Track best (lowest priority value) record we've seen for each name ID;
        // smaller priority wins. We pass through every record so a Windows English
        // string supersedes a Mac Roman one even if the Mac record is read first.
        var bestPriority = new Dictionary<int, int>();

        for (var i = 0; i < count; i++)
        {
            var recordPos = recordsStart + i * 12;
            var platformId = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(recordPos, 2));
            var encodingId = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(recordPos + 2, 2));
            var languageId = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(recordPos + 4, 2));
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(recordPos + 6, 2));
            var stringLength = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(recordPos + 8, 2));
            var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(recordPos + 10, 2));

            if (nameId is not (nameIdFamily or nameIdSubfamily or nameIdFullName
                or nameIdPostScript or nameIdTypographicFamily or nameIdTypographicSubfamily))
            {
                continue;
            }

            var priority = RecordPriority(platformId, encodingId, languageId);
            if (priority == int.MaxValue)
            {
                continue;
            }

            if (bestPriority.TryGetValue(nameId, out var existing) &&
                existing <= priority)
            {
                continue;
            }

            var stringStart = storageOffset + stringOffset;
            if (stringStart + stringLength > tableBytes.Length)
            {
                continue;
            }

            var decoded = DecodeString(tableBytes.AsSpan(stringStart, stringLength), platformId, encodingId);
            if (string.IsNullOrEmpty(decoded))
            {
                continue;
            }

            result[nameId] = decoded;
            bestPriority[nameId] = priority;
        }

        return result;
    }

    /// <summary>
    /// Returns the preference rank for a (platform, encoding, language) triple. Lower is
    /// better. <see cref="int.MaxValue"/> means we don't read this record at all.
    /// </summary>
    static int RecordPriority(int platformId, int encodingId, int languageId)
    {
        // Windows (3) + Unicode BMP (1) / UCS-4 (10), English (0x0409): the canonical
        // modern path for fonts authored on Windows.
        if (platformId == 3 && encodingId is 1 or 10 && languageId == 0x0409)
        {
            return 0;
        }

        // Windows + Unicode, any English variant.
        if (platformId == 3 && encodingId is 1 or 10 && (languageId & 0xFF) == 0x09)
        {
            return 1;
        }

        // Windows + Unicode, any language: still useful since the strings are in the font
        // even if not English (e.g. CJK fonts).
        if (platformId == 3 && encodingId is 1 or 10)
        {
            return 2;
        }

        // Unicode platform (0): rare on modern fonts but valid.
        if (platformId == 0)
        {
            return 3;
        }

        // Mac (1) + Roman (0) + English (0): older fonts.
        if (platformId == 1 && encodingId == 0 && languageId == 0)
        {
            return 4;
        }

        return int.MaxValue;
    }

    static string? DecodeString(ReadOnlySpan<byte> bytes, int platformId, int encodingId)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        // Platform 3 (Windows) and Platform 0 (Unicode) store strings as UTF-16 big-endian.
        if (platformId is 3 or 0)
        {
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        // Platform 1 (Mac) Encoding 0 (Roman) is mostly ASCII for English font names.
        if (platformId == 1 && encodingId == 0)
        {
            return Encoding.ASCII.GetString(bytes);
        }

        return null;
    }

    /// <summary>
    /// Builds the list of distinct, non-empty names this face should be indexed under.
    /// </summary>
    static List<string> BuildIndexNames(Dictionary<int, string> nameRecords)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                ordered.Add(value);
            }
        }

        nameRecords.TryGetValue(nameIdFamily, out var family);
        nameRecords.TryGetValue(nameIdSubfamily, out var subfamily);
        nameRecords.TryGetValue(nameIdFullName, out var fullName);
        nameRecords.TryGetValue(nameIdPostScript, out var psName);
        nameRecords.TryGetValue(nameIdTypographicFamily, out var typographicFamily);
        nameRecords.TryGetValue(nameIdTypographicSubfamily, out var typographicSubfamily);

        Add(fullName);
        Add(psName);
        Add(typographicFamily);
        Add(family);

        // "Typographic Family + Subfamily" — captures the visible name as Word writes it
        // for fonts with extended families (e.g. "Segoe UI Black" where the Subfamily on
        // its file is "Black" and Typographic Family is "Segoe UI").
        if (!string.IsNullOrWhiteSpace(typographicFamily) && !string.IsNullOrWhiteSpace(typographicSubfamily))
        {
            Add($"{typographicFamily} {typographicSubfamily}");
        }

        // Same combo from the basic Family + Subfamily, when it differs from the above and
        // the subfamily is something other than the boring "Regular" / "Bold" / "Italic"
        // (which add no naming information beyond what id 1 already gave us).
        if (!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(subfamily) &&
            !IsTrivialSubfamily(subfamily))
        {
            Add($"{family} {subfamily}");
        }

        return ordered;
    }

    static bool IsTrivialSubfamily(string subfamily) =>
        subfamily.Equals("Regular", StringComparison.OrdinalIgnoreCase) ||
        subfamily.Equals("Bold", StringComparison.OrdinalIgnoreCase) ||
        subfamily.Equals("Italic", StringComparison.OrdinalIgnoreCase) ||
        subfamily.Equals("Bold Italic", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads weight, width, and italic flag from <c>OS/2</c> table bytes. Returns the
    /// spec defaults (regular weight, normal width, upright) when the table is missing
    /// or too short.
    /// </summary>
    static (int Weight, int Width, bool Italic) ReadOs2(byte[] tableBytes)
    {
        // OS/2 versions 0–5 all carry usWeightClass at offset 4, usWidthClass at offset 6,
        // and fsSelection at offset 62. Version 0 is 78 bytes total, so 64 is enough.
        if (tableBytes.Length < 64)
        {
            return (400, 5, false);
        }

        var weight = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(4, 2));
        var width = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(6, 2));
        var fsSelection = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.AsSpan(62, 2));

        return (weight == 0 ? 400 : weight, width == 0 ? 5 : width, (fsSelection & 0x01) != 0);
    }

    static bool TryReadExact(Stream stream, byte[] buffer)
    {
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

    /// <summary>
    /// Decodes a UIntBase128 variable-length integer per RFC 8467 §6.1.2. Returns false
    /// for malformed encodings: leading 0x80 byte, sequence longer than 5 bytes, or
    /// values that overflow 32 bits.
    /// </summary>
    static bool TryReadUIntBase128(Stream stream, out uint result)
    {
        result = 0;
        uint accum = 0;
        for (var i = 0; i < 5; i++)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return false;
            }

            // No leading zeros: a stream starting with 0x80 is not a valid encoding.
            if (i == 0 && b == 0x80)
            {
                return false;
            }

            // If any of the top 7 bits are set, shifting left by 7 would overflow.
            if ((accum & 0xFE000000u) != 0)
            {
                return false;
            }

            accum = (accum << 7) | (uint) (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                result = accum;
                return true;
            }
        }

        return false;
    }
}
