/// <summary>
/// A package's zip entries, uncompressed and in the order the zip holds them: what a
/// <see cref="DocumentPatch"/> and a merge work on, below the Open XML SDK.
/// </summary>
static class PackageEntries
{
    /// <summary>The entries of <paramref name="docx"/>, by name, in the order they are stored.</summary>
    public static OrderedDictionary<string, byte[]> Read(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = new OrderedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            // A folder's own entry holds nothing.
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            using var content = entry.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            entries[entry.FullName] = buffer.ToArray();
        }

        return entries;
    }

    // The time every entry written here is stamped with, so the same entries make the same package.
    static readonly DateTimeOffset written = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A package of <paramref name="entries"/>, in their order, deflated fast.</summary>
    public static byte[] Write(OrderedDictionary<string, byte[]> entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                entry.LastWriteTime = written;
                using var output = entry.Open();
                output.Write(content);
            }
        }

        return stream.ToArray();
    }
}
