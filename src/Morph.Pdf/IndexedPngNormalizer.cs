using System.Buffers.Binary;
using System.IO.Compression;

/// <summary>
/// Re-encodes a sub-8-bit indexed PNG (<c>colourType 3</c>, <c>bitDepth</c> 1/2/4) to the same
/// palette at 8 bits per pixel.
///
/// <para>PDFsharp builds an image's soft mask from the PNG's <c>tRNS</c> transparency, and for
/// these packed depths it emits an <b>all-zero</b> SMask — a fully transparent alpha channel — so
/// the picture is written into the PDF, referenced, and drawn at the right place, yet renders as
/// nothing. cards/19 is the case: its card-back stripe motif is a 4-bit indexed PNG and every one
/// of the ten draws on pages 2 and 4 came out invisible, while the 8-bit indexed backgrounds on
/// pages 1 and 3 masked correctly. Expanding to 8 bits keeps the palette, the <c>tRNS</c> entries
/// and the pixel indices identical, and that depth is the one PDFsharp already handles.</para>
///
/// <para>Anything this does not understand — a different colour type, 8-bit or deeper, an
/// interlaced image, a malformed chunk — is returned untouched, so the normal path is unchanged.</para>
/// </summary>
static class IndexedPngNormalizer
{
    static ReadOnlySpan<byte> Signature => [0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Normalize(byte[] data)
    {
        if (!Looks8BitExpandable(data, out var width, out var height, out var bitDepth))
        {
            return data;
        }

        try
        {
            var chunks = ReadChunks(data);
            var idat = chunks
                .Where(_ => _.Type == "IDAT")
                .SelectMany(_ => _.Data)
                .ToArray();

            var raw = Inflate(idat);
            var expanded = ExpandRows(raw, width, height, bitDepth);
            if (expanded == null)
            {
                return data;
            }

            return Rebuild(chunks, expanded, width, height);
        }
        catch (Exception)
        {
            // A PNG this doesn't understand is not worth failing an export over — emit the
            // original and let PDFsharp do whatever it did before.
            return data;
        }
    }

    static bool Looks8BitExpandable(byte[] data, out int width, out int height, out int bitDepth)
    {
        width = height = bitDepth = 0;
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return false;
        }

        // IHDR is always the first chunk: length(4) type(4) then width, height, depth, colour,
        // compression, filter, interlace.
        if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4)) != 0x49484452) // "IHDR"
        {
            return false;
        }

        width = (int) BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
        height = (int) BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
        bitDepth = data[24];
        var colourType = data[25];
        var interlace = data[28];

        return colourType == 3 &&
               bitDepth is 1 or 2 or 4 &&
               interlace == 0 &&
               width > 0 &&
               height > 0;
    }

    record struct Chunk(string Type, byte[] Data);

    static List<Chunk> ReadChunks(byte[] data)
    {
        var chunks = new List<Chunk>();
        var offset = 8;
        while (offset + 8 <= data.Length)
        {
            var length = (int) BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
            var type = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
            var payload = data.AsSpan(offset + 8, length).ToArray();
            chunks.Add(new(type, payload));
            offset += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }

        return chunks;
    }

    static byte[] Inflate(byte[] zlibData)
    {
        using var input = new MemoryStream(zlibData);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        return output.ToArray();
    }

    static byte[] Deflate(byte[] plain)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(plain, 0, plain.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Un-filters each scanline, unpacks its indices to one byte each, and re-emits every row with
    /// filter 0. Returns null when the data is not the size the header claims.
    /// </summary>
    static byte[]? ExpandRows(byte[] raw, int width, int height, int bitDepth)
    {
        var packedStride = (width * bitDepth + 7) / 8;
        if (raw.Length < height * (packedStride + 1))
        {
            return null;
        }

        var previous = new byte[packedStride];
        var current = new byte[packedStride];
        var result = new byte[height * (width + 1)];

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * (packedStride + 1);
            var filter = raw[sourceOffset];
            raw.AsSpan(sourceOffset + 1, packedStride).CopyTo(current);

            // Filtering for a packed indexed image works on BYTES, so the "bytes per pixel"
            // distance used by Sub/Average/Paeth is 1 regardless of the bit depth.
            for (var i = 0; i < packedStride; i++)
            {
                var left = i >= 1 ? current[i - 1] : 0;
                var up = (int) previous[i];
                var upLeft = i >= 1 ? previous[i - 1] : 0;
                current[i] = filter switch
                {
                    0 => current[i],
                    1 => (byte) (current[i] + left),
                    2 => (byte) (current[i] + up),
                    3 => (byte) (current[i] + ((left + up) >> 1)),
                    4 => (byte) (current[i] + Paeth(left, up, upLeft)),
                    _ => current[i]
                };
            }

            var destination = row * (width + 1);
            result[destination] = 0; // filter None
            for (var x = 0; x < width; x++)
            {
                var bitOffset = x * bitDepth;
                var value = current[bitOffset / 8];
                var shift = 8 - bitDepth - bitOffset % 8;
                result[destination + 1 + x] = (byte) ((value >> shift) & ((1 << bitDepth) - 1));
            }

            (previous, current) = (current, previous);
        }

        return result;
    }

    static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    static byte[] Rebuild(List<Chunk> chunks, byte[] expanded, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        var wroteIdat = false;
        foreach (var chunk in chunks)
        {
            switch (chunk.Type)
            {
                case "IHDR":
                    var header = (byte[]) chunk.Data.Clone();
                    header[8] = 8; // bit depth
                    WriteChunk(output, "IHDR", header);
                    break;

                case "IDAT":
                    if (!wroteIdat)
                    {
                        WriteChunk(output, "IDAT", Deflate(expanded));
                        wroteIdat = true;
                    }

                    break;

                // PLTE and tRNS carry over verbatim: the indices did not change, only their
                // packing. Everything else (gAMA, pHYs, …) is dropped as irrelevant to the render.
                case "PLTE":
                case "tRNS":
                case "IEND":
                    WriteChunk(output, chunk.Type, chunk.Data);
                    break;
            }
        }

        return output.ToArray();
    }

    static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint) data.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data));
        stream.Write(crc);
    }

    static readonly uint[] crcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        foreach (var b in data)
        {
            c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }
}
