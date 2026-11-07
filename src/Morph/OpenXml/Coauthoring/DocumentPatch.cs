/// <summary>
/// What changed between two versions of a package: the entries added or changed, each whole and
/// uncompressed, and the names of those taken out. Coauthoring sends one for each edit and each version,
/// rather than the whole file: an edit to a paragraph changes the main document part, perhaps the styles,
/// and leaves the pictures as they were.
///
/// Entries are compared by their content, not their compressed bytes, which differ with the zip
/// library's settings and the time an entry was written.
/// </summary>
sealed class DocumentPatch
{
    static readonly byte[] magic = "MPT1"u8.ToArray();

    DocumentPatch(IReadOnlyDictionary<string, byte[]> changed, IReadOnlyList<string> removed)
    {
        Changed = changed;
        Removed = removed;
    }

    /// <summary>The entries added or changed, by name, uncompressed.</summary>
    public IReadOnlyDictionary<string, byte[]> Changed { get; }

    /// <summary>The names of the entries taken out.</summary>
    public IReadOnlyList<string> Removed { get; }

    public bool IsEmpty =>
        Changed.Count == 0 &&
        Removed.Count == 0;

    public static DocumentPatch Empty { get; } = new(new Dictionary<string, byte[]>(), []);

    /// <summary>What turns <paramref name="from"/> into <paramref name="to"/>.</summary>
    public static DocumentPatch Diff(byte[] from, byte[] to)
    {
        var before = PackageEntries.Read(from);
        var after = PackageEntries.Read(to);
        var changed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, content) in after)
        {
            if (!before.TryGetValue(name, out var old) ||
                !old.AsSpan().SequenceEqual(content))
            {
                changed[name] = content;
            }
        }

        var removed = before.Keys
            .Where(_ => !after.ContainsKey(_))
            .ToList();
        return new(changed, removed);
    }

    /// <summary>The package <paramref name="docx"/> becomes with this patch applied.</summary>
    public byte[] Apply(byte[] docx)
    {
        if (IsEmpty)
        {
            return docx;
        }

        var entries = PackageEntries.Read(docx);
        foreach (var name in Removed)
        {
            entries.Remove(name);
        }

        foreach (var (name, content) in Changed)
        {
            entries[name] = content;
        }

        return PackageEntries.Write(entries);
    }

    /// <summary>The patch as bytes, to send: deflated, since the XML of a part is most of it.</summary>
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        stream.Write(magic);
        using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(deflate, Encoding.UTF8))
        {
            writer.Write(Changed.Count);
            foreach (var (name, content) in Changed)
            {
                writer.Write(name);
                writer.Write(content.Length);
                writer.Write(content);
            }

            writer.Write(Removed.Count);
            foreach (var name in Removed)
            {
                writer.Write(name);
            }
        }

        return stream.ToArray();
    }

    /// <summary>A patch sent as <see cref="ToBytes"/> wrote it.</summary>
    public static DocumentPatch FromBytes(byte[] bytes)
    {
        if (bytes.Length < magic.Length ||
            !bytes.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            throw new InvalidDataException("Not a document patch");
        }

        using var stream = new MemoryStream(bytes, magic.Length, bytes.Length - magic.Length, writable: false);
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(deflate, Encoding.UTF8);
        var changed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var count = reader.ReadInt32();
        for (var index = 0; index < count; index++)
        {
            var name = reader.ReadString();
            var length = reader.ReadInt32();
            changed[name] = reader.ReadBytes(length);
        }

        var removed = new List<string>();
        count = reader.ReadInt32();
        for (var index = 0; index < count; index++)
        {
            removed.Add(reader.ReadString());
        }

        return new(changed, removed);
    }
}
