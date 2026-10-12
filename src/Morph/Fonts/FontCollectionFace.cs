/// <summary>
/// Lifts one face out of a TrueType collection (<c>.ttc</c>) as a font file of its own. A collection
/// is several table directories over shared table data; a consumer that takes one font's bytes
/// (PdfSharp's <c>IFontResolver.GetFont</c>) cannot be handed the collection, so the member's
/// tables are copied out behind a directory of their own.
/// </summary>
static class FontCollectionFace
{
    // 'ttcf' — TrueType Collection
    const uint collectionTag = 0x74746366;

    // 'head', whose checkSumAdjustment covers the whole file and so has to be recomputed
    const uint headTag = 0x68656164;

    const uint checksumMagic = 0xB1B0AFBA;

    /// <summary>
    /// Returns face <paramref name="index"/> of <paramref name="collection"/> as a standalone sfnt, or
    /// null when the bytes are not a collection, the index is out of range, or a table lies outside
    /// the file.
    /// </summary>
    public static byte[]? Extract(ReadOnlySpan<byte> collection, int index)
    {
        if (collection.Length < 12 ||
            BinaryPrimitives.ReadUInt32BigEndian(collection) != collectionTag)
        {
            return null;
        }

        var faceCount = BinaryPrimitives.ReadUInt32BigEndian(collection[8..]);
        if (index < 0 ||
            (uint) index >= faceCount ||
            collection.Length < 12 + (index + 1) * 4)
        {
            return null;
        }

        var directory = (int) BinaryPrimitives.ReadUInt32BigEndian(collection[(12 + index * 4)..]);
        if (directory < 0 ||
            directory + 12 > collection.Length)
        {
            return null;
        }

        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(collection[(directory + 4)..]);
        var recordsEnd = directory + 12 + tableCount * 16;
        if (recordsEnd > collection.Length)
        {
            return null;
        }

        var size = 12 + tableCount * 16;
        for (var i = 0; i < tableCount; i++)
        {
            var record = collection.Slice(directory + 12 + i * 16, 16);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            if ((long) offset + length > collection.Length)
            {
                return null;
            }

            size += Padded((int) length);
        }

        var face = new byte[size];
        collection.Slice(directory, 12).CopyTo(face);

        var position = 12 + tableCount * 16;
        var headOffset = -1;
        for (var i = 0; i < tableCount; i++)
        {
            var record = collection.Slice(directory + 12 + i * 16, 16);
            var target = face.AsSpan(12 + i * 16, 16);
            record.CopyTo(target);

            var offset = (int) BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = (int) BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            collection.Slice(offset, length).CopyTo(face.AsSpan(position));
            BinaryPrimitives.WriteUInt32BigEndian(target[8..], (uint) position);

            if (BinaryPrimitives.ReadUInt32BigEndian(record) == headTag &&
                length >= 12)
            {
                headOffset = position;
            }

            position += Padded(length);
        }

        if (headOffset >= 0)
        {
            var adjustment = face.AsSpan(headOffset + 8, 4);
            adjustment.Clear();
            BinaryPrimitives.WriteUInt32BigEndian(adjustment, checksumMagic - Checksum(face));
        }

        return face;
    }

    static int Padded(int length) =>
        (length + 3) & ~3;

    static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0u;
        var whole = bytes.Length & ~3;
        for (var i = 0; i < whole; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(bytes[i..]);
        }

        return sum;
    }
}
