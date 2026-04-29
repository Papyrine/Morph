/// <summary>
/// Minimal reader for the OpenType <c>name</c> and <c>OS/2</c> tables. Used to index
/// font files by every name they declare (Family, Full Name, PostScript Name, Typographic
/// Family/Subfamily) and to surface their weight/width/italic metrics so the resolver can
/// pick the closest face without consulting platform font managers.
/// </summary>
/// <remarks>
/// Reading the font's own metadata is the authoritative way to resolve a string like
/// <c>"Segoe UI Semilight"</c>: it is literally Name ID 4 of <c>segoeuisl.ttf</c>. This
/// avoids the heuristics needed when the only available signal is <c>SKTypeface.FamilyName</c>
/// (which on Windows collapses Semilight into the base "Segoe UI" family) or a
/// hardcoded list of weight suffixes.
/// </remarks>
static class OpenTypeReader
{
    // sfntVersion magic numbers
    const uint ttfMagic = 0x00010000;       // TrueType outlines
    const uint otfMagic = 0x4F54544F;       // 'OTTO' — CFF outlines
    const uint trueMagic = 0x74727565;      // 'true' — older Apple TrueType
    const uint ttcMagic = 0x74746366;       // 'ttcf' — TrueType Collection

    // Table tags
    const uint nameTag = 0x6E616D65;        // 'name'
    const uint os2Tag = 0x4F532F32;         // 'OS/2'

    // Name IDs we extract (ignoring the rest)
    const int nameIdFamily = 1;
    const int nameIdSubfamily = 2;
    const int nameIdFullName = 4;
    const int nameIdPostScript = 6;
    const int nameIdTypographicFamily = 16;
    const int nameIdTypographicSubfamily = 17;

    /// <summary>
    /// Reads every face in <paramref name="path"/> and returns its metadata along with
    /// the names under which it should be indexed.
    /// </summary>
    public static IEnumerable<(FontFace Face, IReadOnlyList<string> Names)> ReadFaces(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch
        {
            yield break;
        }

        if (bytes.Length < 12)
        {
            yield break;
        }

        var sig = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (sig == ttcMagic)
        {
            // TTC: header is { tag, version, numFonts, offsets[numFonts] }
            if (bytes.Length < 12)
            {
                yield break;
            }

            var numFonts = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4));
            // Cap numFonts at a sane value so we don't blow up on malformed files
            if (numFonts > 0x10000)
            {
                yield break;
            }

            for (var i = 0; i < numFonts; i++)
            {
                var offsetPos = 12 + i * 4;
                if (offsetPos + 4 > bytes.Length)
                {
                    yield break;
                }

                var faceOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offsetPos, 4));
                if (TryReadFace(bytes, (int) faceOffset, path, i, out var face, out var names))
                {
                    yield return (face, names);
                }
            }
        }
        else if (sig == ttfMagic || sig == otfMagic || sig == trueMagic)
        {
            if (TryReadFace(bytes, 0, path, 0, out var face, out var names))
            {
                yield return (face, names);
            }
        }
    }

    static bool TryReadFace(byte[] bytes, int faceOffset, string path, int index,
        out FontFace face, out IReadOnlyList<string> names)
    {
        face = null!;
        names = [];

        if (faceOffset + 12 > bytes.Length)
        {
            return false;
        }

        // Table directory: sfntVersion(4) + numTables(2) + searchRange(2) + entrySelector(2) + rangeShift(2)
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(faceOffset + 4, 2));
        var recordsStart = faceOffset + 12;
        if (recordsStart + numTables * 16 > bytes.Length)
        {
            return false;
        }

        int nameOffset = -1, nameLength = 0;
        int os2Offset = -1, os2Length = 0;

        for (var i = 0; i < numTables; i++)
        {
            var recordPos = recordsStart + i * 16;
            var tag = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordPos, 4));
            // [4..8] = checksum
            var off = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordPos + 8, 4));
            var len = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(recordPos + 12, 4));

            if (tag == nameTag)
            {
                nameOffset = (int) off;
                nameLength = (int) len;
            }
            else if (tag == os2Tag)
            {
                os2Offset = (int) off;
                os2Length = (int) len;
            }
        }

        if (nameOffset < 0 || nameOffset + nameLength > bytes.Length)
        {
            return false;
        }

        var nameRecords = ReadNameTable(bytes, nameOffset, nameLength);
        if (nameRecords.Count == 0)
        {
            return false;
        }

        var (weight, width, italic) = ReadOs2(bytes, os2Offset, os2Length);

        face = new()
        {
            Path = path,
            Index = index,
            Weight = weight,
            Width = width,
            Italic = italic,
        };
        names = BuildIndexNames(nameRecords);
        return true;
    }

    /// <summary>
    /// Parses the <c>name</c> table at <paramref name="tableOffset"/> into a map of
    /// <c>nameID</c> → string, preferring English Windows records over Mac/Unicode
    /// records when the same name is declared multiple times.
    /// </summary>
    static Dictionary<int, string> ReadNameTable(byte[] bytes, int tableOffset, int tableLength)
    {
        var result = new Dictionary<int, string>();
        if (tableLength < 6)
        {
            return result;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tableOffset + 2, 2));
        var storageOffset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tableOffset + 4, 2));
        var recordsStart = tableOffset + 6;
        if (recordsStart + count * 12 > bytes.Length)
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
            var platformId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos, 2));
            var encodingId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos + 2, 2));
            var languageId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos + 4, 2));
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos + 6, 2));
            var stringLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos + 8, 2));
            var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordPos + 10, 2));

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

            if (bestPriority.TryGetValue(nameId, out var existing) && existing <= priority)
            {
                continue;
            }

            var stringStart = tableOffset + storageOffset + stringOffset;
            if (stringStart < 0 || stringStart + stringLength > bytes.Length)
            {
                continue;
            }

            var decoded = DecodeString(bytes.AsSpan(stringStart, stringLength), platformId, encodingId);
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
        if (platformId == 3 && (encodingId == 1 || encodingId == 10) && languageId == 0x0409)
        {
            return 0;
        }

        // Windows + Unicode, any English variant.
        if (platformId == 3 && (encodingId == 1 || encodingId == 10) && (languageId & 0xFF) == 0x09)
        {
            return 1;
        }

        // Windows + Unicode, any language: still useful since the strings are in the font
        // even if not English (e.g. CJK fonts).
        if (platformId == 3 && (encodingId == 1 || encodingId == 10))
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
        if (platformId == 3 || platformId == 0)
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
    static IReadOnlyList<string> BuildIndexNames(Dictionary<int, string> nameRecords)
    {
        // HashSet preserves insertion order via a List<string> we pass alongside, so we
        // can deduplicate cheaply while keeping a stable lookup order.
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

    static (int Weight, int Width, bool Italic) ReadOs2(byte[] bytes, int tableOffset, int tableLength)
    {
        // Defaults match the OS/2 spec: regular weight, normal width, upright.
        if (tableOffset < 0 || tableLength < 64 || tableOffset + 64 > bytes.Length)
        {
            return (400, 5, false);
        }

        // version(2) + xAvgCharWidth(2) = offset 4 → usWeightClass
        var weight = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tableOffset + 4, 2));
        var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tableOffset + 6, 2));
        // fsSelection at offset 62; bit 0 is italic
        var fsSelection = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tableOffset + 62, 2));

        return (weight == 0 ? 400 : weight, width == 0 ? 5 : width, (fsSelection & 0x01) != 0);
    }
}
