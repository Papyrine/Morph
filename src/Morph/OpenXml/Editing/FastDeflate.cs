using System.Buffers.Binary;

/// <summary>
/// Marks a package's entries so that the parts an edit changes are written back at the fastest
/// deflate level. The OpenXml SDK gives an existing part no compression option of its own; it writes
/// one back through <see cref="System.IO.Compression.ZipArchive"/>, which since .NET 9 keeps the level
/// an entry's general-purpose flags declare — and Word declares "normal", which is Optimal.
///
/// On a body the size of newsletters/03's (2.6MB of XML) Optimal is ~60ms of every edit and Fastest
/// ~25ms, for a body ~30% larger; on ordinary XML Fastest compresses nearly as well (complex_tables'
/// body: 3KB against 2KB). The flags say nothing a reader needs: a deflated entry inflates the same
/// whatever they hold, so entries the edit does not touch are copied raw as before.
/// </summary>
static class FastDeflate
{
    const uint endOfCentralDirectory = 0x06054b50;
    const uint centralHeader = 0x02014b50;
    const uint localHeader = 0x04034b50;
    const ushort deflated = 8;

    // Bits 1 and 2 of the general-purpose flags: 10 is "fast".
    const ushort levelBits = 0b110;
    const ushort fast = 0b100;

    /// <summary>
    /// Marks every deflated entry of <paramref name="zip"/> fast, in its central-directory record and
    /// its local header. Leaves alone what it cannot read, which the package open will report: a
    /// Zip64 archive, or anything out of bounds.
    /// </summary>
    public static void Mark(Span<byte> zip)
    {
        var end = FindEndOfCentralDirectory(zip);
        if (end < 0)
        {
            return;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(zip[(end + 10)..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(zip[(end + 16)..]);
        if (count == ushort.MaxValue || offset == uint.MaxValue)
        {
            return;
        }

        var record = (long)offset;
        for (var index = 0; index < count; index++)
        {
            if (record + 46 > zip.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(zip[(int)record..]) != centralHeader)
            {
                return;
            }

            var header = zip[(int)record..];
            var method = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            var local = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);
            if (method == deflated &&
                local + 30L <= zip.Length &&
                BinaryPrimitives.ReadUInt32LittleEndian(zip[(int)local..]) == localHeader)
            {
                SetFast(header[8..]);
                SetFast(zip[((int)local + 6)..]);
            }

            record += 46 + nameLength + extraLength + commentLength;
        }
    }

    // The record sits at the end, after a comment of up to 64KB.
    static int FindEndOfCentralDirectory(ReadOnlySpan<byte> zip)
    {
        var last = zip.Length - 22;
        var first = Math.Max(0, last - ushort.MaxValue);
        for (var at = last; at >= first; at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip[at..]) == endOfCentralDirectory)
            {
                return at;
            }
        }

        return -1;
    }

    static void SetFast(Span<byte> flags)
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(flags);
        BinaryPrimitives.WriteUInt16LittleEndian(flags, (ushort)((value & ~levelBits) | fast));
    }
}
