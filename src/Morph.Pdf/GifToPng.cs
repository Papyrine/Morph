using System.Buffers.Binary;
using System.IO.Compression;

/// <summary>
/// Transcodes the first frame of a GIF to an 8-bit indexed PNG.
///
/// <para>PDFsharp's cross-platform build decodes only BMP/PNG/JPEG, so a GIF is dropped from the
/// export entirely — <c>image_wrap_square</c>'s globe reserves its wrap band and then renders
/// nothing inside it. The raster backends decode GIF through their image codecs; the PDF backend
/// has none, so the frame is unpacked here (GIF is LZW-compressed indexed colour) and re-emitted as
/// the 8-bit indexed PNG PDFsharp does handle, carrying the palette and, when the frame declares
/// one, a single transparent index through <c>tRNS</c>. Only the first frame is produced;
/// animation is irrelevant to a printed page.</para>
///
/// <para>Anything malformed returns null and the caller keeps its original bytes.</para>
/// </summary>
static class GifToPng
{
    public static bool IsGif(byte[] data) =>
        data.Length >= 6 &&
        data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8' &&
        (data[4] == '7' || data[4] == '9') && data[5] == 'a';

    public static byte[]? Convert(byte[] data)
    {
        try
        {
            return ConvertCore(data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static byte[]? ConvertCore(byte[] data)
    {
        var reader = new Reader(data);
        // header
        reader.Skip(6);

        // Logical Screen Descriptor.
        // canvas width/height — the frame carries its own
        reader.Skip(4);
        var packed = reader.Byte();
        // background colour index, pixel aspect ratio
        reader.Skip(2);

        var globalTable = (packed & 0x80) != 0 ? reader.ColorTable(2 << (packed & 0x07)) : null;

        var transparentIndex = -1;

        while (true)
        {
            var block = reader.Byte();
            // trailer / end of data before an image
            if (block is 0x3B or -1)
            {
                return null;
            }

            // extension
            if (block == 0x21)
            {
                var label = reader.Byte();
                // graphic control — may name a transparent index
                if (label == 0xF9)
                {
                    // always 4
                    var size = reader.Byte();
                    var flags = reader.Byte();
                    // delay
                    reader.Skip(2);
                    var index = reader.Byte();
                    reader.Skip(size - 4);
                    // block terminator
                    reader.Skip(1);
                    if ((flags & 0x01) != 0)
                    {
                        transparentIndex = index;
                    }
                }
                else
                {
                    reader.SkipSubBlocks();
                }

                continue;
            }

            // anything other than an image descriptor is unexpected
            if (block != 0x2C)
            {
                return null;
            }

            // Image Descriptor.
            // left, top
            reader.Skip(4);
            var width = reader.UInt16();
            var height = reader.UInt16();
            var imagePacked = reader.Byte();
            var interlaced = (imagePacked & 0x40) != 0;
            var localTable = (imagePacked & 0x80) != 0 ? reader.ColorTable(2 << (imagePacked & 0x07)) : null;
            var palette = localTable ?? globalTable;
            if (palette == null || width == 0 || height == 0)
            {
                return null;
            }

            var minCodeSize = reader.Byte();
            var lzw = reader.SubBlockBytes();
            var indices = LzwDecode(lzw, minCodeSize, width * height);
            if (indices == null)
            {
                return null;
            }

            if (interlaced)
            {
                indices = Deinterlace(indices, width, height);
            }

            return BuildPng(indices, width, height, palette, transparentIndex);
        }
    }

    static byte[]? LzwDecode(byte[] input, int minCodeSize, int pixelCount)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var codeSize = minCodeSize + 1;

        var prefix = new int[4096];
        var suffix = new byte[4096];
        var stack = new byte[4096];
        for (var i = 0; i < clear; i++)
        {
            suffix[i] = (byte) i;
        }

        var output = new byte[pixelCount];
        var written = 0;

        var next = end + 1;
        var bitBuffer = 0;
        var bitCount = 0;
        var position = 0;
        var previous = -1;

        while (written < pixelCount)
        {
            while (bitCount < codeSize)
            {
                if (position >= input.Length)
                {
                    return written == pixelCount ? output : null;
                }

                bitBuffer |= input[position++] << bitCount;
                bitCount += 8;
            }

            var code = bitBuffer & ((1 << codeSize) - 1);
            bitBuffer >>= codeSize;
            bitCount -= codeSize;

            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (code == end)
            {
                break;
            }

            var top = 0;
            int current;
            if (code < next)
            {
                current = code;
            }
            else
            {
                // First code after a clear, or a code that references itself — emit prefix + first.
                if (previous < 0)
                {
                    return null;
                }

                stack[top++] = FirstByte(previous, prefix, suffix, clear);
                current = previous;
            }

            while (current >= clear)
            {
                if (top >= stack.Length || current >= prefix.Length)
                {
                    return null;
                }

                stack[top++] = suffix[current];
                current = prefix[current];
            }

            var firstByte = (byte) current;
            stack[top++] = firstByte;

            while (top > 0 && written < pixelCount)
            {
                output[written++] = stack[--top];
            }

            if (previous >= 0 && next < 4096)
            {
                prefix[next] = previous;
                suffix[next] = firstByte;
                next++;
                if (next == 1 << codeSize && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previous = code;
        }

        return written == pixelCount ? output : null;
    }

    static byte FirstByte(int code, int[] prefix, byte[] suffix, int clear)
    {
        while (code >= clear)
        {
            code = prefix[code];
        }

        return suffix[code];
    }

    static byte[] Deinterlace(byte[] indices, int width, int height)
    {
        var result = new byte[indices.Length];
        var source = 0;
        // GIF interlace passes: rows 0,8,…; 4,12,…; 2,6,…; 1,3,….
        foreach (var (start, step) in new[] {(0, 8), (4, 8), (2, 4), (1, 2)})
        {
            for (var row = start; row < height; row += step)
            {
                Array.Copy(indices, source * width, result, row * width, width);
                source++;
            }
        }

        return result;
    }

    static byte[] BuildPng(byte[] indices, int width, int height, byte[][] palette, int transparentIndex)
    {
        // One filter-0 byte per scanline, then one index byte per pixel.
        var raw = new byte[height * (width + 1)];
        for (var row = 0; row < height; row++)
        {
            var destination = row * (width + 1);
            raw[destination] = 0;
            Array.Copy(indices, row * width, raw, destination + 1, width);
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), (uint) width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint) height);
        // bit depth
        header[8] = 8;
        // colour type: indexed
        header[9] = 3;
        WriteChunk(output, "IHDR", header);

        var plte = new byte[palette.Length * 3];
        for (var i = 0; i < palette.Length; i++)
        {
            plte[i * 3] = palette[i][0];
            plte[i * 3 + 1] = palette[i][1];
            plte[i * 3 + 2] = palette[i][2];
        }

        WriteChunk(output, "PLTE", plte);

        if (transparentIndex >= 0 && transparentIndex < palette.Length)
        {
            // tRNS holds one alpha byte per palette entry up to the transparent one; the rest are
            // opaque by omission.
            var trns = new byte[transparentIndex + 1];
            Array.Fill(trns, (byte) 255);
            trns[transparentIndex] = 0;
            WriteChunk(output, "tRNS", trns);
        }

        WriteChunk(output, "IDAT", Deflate(raw));
        WriteChunk(output, "IEND", []);
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

    static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint) data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc.Compute(typeBytes, data));
        stream.Write(crc);
    }

    sealed class Reader(byte[] data)
    {
        int offset;

        public void Skip(int count) => offset += count;

        public int Byte() => offset < data.Length ? data[offset++] : -1;

        public int UInt16()
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
            offset += 2;
            return value;
        }

        public byte[][] ColorTable(int entries)
        {
            var table = new byte[entries][];
            for (var i = 0; i < entries; i++)
            {
                table[i] = [data[offset], data[offset + 1], data[offset + 2]];
                offset += 3;
            }

            return table;
        }

        public void SkipSubBlocks()
        {
            while (true)
            {
                var size = Byte();
                if (size <= 0)
                {
                    return;
                }

                offset += size;
            }
        }

        public byte[] SubBlockBytes()
        {
            using var buffer = new MemoryStream();
            while (true)
            {
                var size = Byte();
                if (size <= 0)
                {
                    return buffer.ToArray();
                }

                buffer.Write(data, offset, size);
                offset += size;
            }
        }
    }

    static class Crc
    {
        static readonly uint[] table = Build();

        static uint[] Build()
        {
            var result = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                result[n] = c;
            }

            return result;
        }

        public static uint Compute(byte[] type, byte[] data)
        {
            var c = 0xFFFFFFFFu;
            foreach (var b in type)
            {
                c = table[(c ^ b) & 0xFF] ^ (c >> 8);
            }

            foreach (var b in data)
            {
                c = table[(c ^ b) & 0xFF] ^ (c >> 8);
            }

            return c ^ 0xFFFFFFFFu;
        }
    }
}
