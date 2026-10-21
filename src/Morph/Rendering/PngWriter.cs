using System.Buffers.Binary;
using System.IO.Compression;

/// <summary>
/// Encodes a rendered page as the smallest of the four 8-bit PNG forms its pixels allow, where the
/// backends' own encoders always write 8-bit RGBA:
/// * greyscale when every pixel is an opaque grey — a page of black text on white;
/// * a palette of at most 256 colours — text and flat fills, which most pages are;
/// * RGB when every pixel is opaque;
/// * RGBA otherwise.
/// Every form decodes to the same pixels, so a page reads exactly as before; only the bytes shrink.
/// Rows take PNG's adaptive filter (the one of its five filters whose output sums smallest), except a
/// palette's, which the specification advises leaving unfiltered since an index is not a magnitude.
/// The bit depth stays 8 throughout, as every decoder handles it.
/// </summary>
static class PngWriter
{
    static readonly byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
    static readonly uint[] crcTable = BuildCrcTable();

    /// <summary>
    /// Writes the pixels as a PNG. <paramref name="rgba"/> holds <paramref name="height"/> rows of
    /// <paramref name="width"/> RGBA pixels, <paramref name="stride"/> bytes apart. Opaque pixels are the
    /// same premultiplied or not; a translucent page is taken as straight alpha.
    /// </summary>
    public static void Write(Stream output, ReadOnlySpan<byte> rgba, int width, int height, int stride, CompressionLevel level = CompressionLevel.Optimal)
    {
        var (opaque, grey, palette, indices) = Analyse(rgba, width, height, stride);

        byte colourType;
        int channels;
        if (opaque && grey)
        {
            colourType = 0;
            channels = 1;
        }
        else if (opaque && palette != null)
        {
            colourType = 3;
            channels = 1;
        }
        else if (opaque)
        {
            colourType = 2;
            channels = 3;
        }
        else
        {
            colourType = 6;
            channels = 4;
        }

        output.Write(signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = colourType;
        WriteChunk(output, "IHDR"u8, header);

        if (colourType == 3)
        {
            var entries = new byte[palette!.Count * 3];
            for (var index = 0; index < palette.Count; index++)
            {
                entries[index * 3] = (byte) (palette[index] >> 16);
                entries[index * 3 + 1] = (byte) (palette[index] >> 8);
                entries[index * 3 + 2] = (byte) palette[index];
            }

            WriteChunk(output, "PLTE"u8, entries);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, level, leaveOpen: true))
        {
            var rowLength = width * channels;
            var previous = new byte[rowLength];
            var current = new byte[rowLength];
            var filtered = new byte[rowLength + 1];
            var best = new byte[rowLength + 1];
            for (var y = 0; y < height; y++)
            {
                var row = rgba.Slice(y * stride, width * 4);
                if (colourType == 3)
                {
                    best[0] = 0;
                    indices.AsSpan(y * width, width).CopyTo(best.AsSpan(1));
                    zlib.Write(best, 0, rowLength + 1);
                    continue;
                }

                Pack(row, current, colourType);

                // A row the same as the one above filters to all zeros under Up, the least any filter
                // can sum to, and the adaptive choice takes the first filter that reaches it: None when
                // the row is itself all zeros (its raw bytes are then the same zeros), Up otherwise. Most
                // of a page is such rows — its margins, and the whole of a page with nothing on it — and
                // trying all five filters on each had made encoding a large page cost twenty times its
                // painting (MalformedPackageTests' 14,400pt pages). The bytes are the five-way search's.
                if (y > 0 && current.AsSpan().SequenceEqual(previous))
                {
                    best[0] = current.AsSpan().IndexOfAnyExcept((byte) 0) < 0 ? (byte) 0 : (byte) 2;
                    best.AsSpan(1).Clear();
                    zlib.Write(best, 0, rowLength + 1);
                    (previous, current) = (current, previous);
                    continue;
                }

                var bestSum = long.MaxValue;
                for (byte filter = 0; filter < 5; filter++)
                {
                    var sum = Filter(filter, current, previous, channels, filtered);
                    if (sum < bestSum)
                    {
                        bestSum = sum;
                        (best, filtered) = (filtered, best);
                    }
                }

                zlib.Write(best, 0, rowLength + 1);
                (previous, current) = (current, previous);
            }
        }

        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int) compressed.Length));
        WriteChunk(output, "IEND"u8, []);
    }

    // Whether every pixel is opaque, whether every one is grey, and — when there are at most 256
    // colours — the palette and each pixel's index into it, in reading order.
    static (bool Opaque, bool Grey, List<uint>? Palette, byte[] Indices) Analyse(ReadOnlySpan<byte> rgba, int width, int height, int stride)
    {
        var grey = true;
        List<uint>? palette = [];
        var lookup = new Dictionary<uint, byte>();
        var indices = new byte[width * height];
        var last = uint.MaxValue;
        byte lastIndex = 0;
        for (var y = 0; y < height; y++)
        {
            var row = rgba.Slice(y * stride, width * 4);

            // A row the same as the one above holds nothing new: its pixels were all checked there, and
            // its indices are that row's.
            if (y > 0 && row.SequenceEqual(rgba.Slice((y - 1) * stride, width * 4)))
            {
                if (palette != null)
                {
                    indices.AsSpan((y - 1) * width, width).CopyTo(indices.AsSpan(y * width, width));
                }

                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var r = row[x * 4];
                var g = row[x * 4 + 1];
                var b = row[x * 4 + 2];
                if (row[x * 4 + 3] != 255)
                {
                    return (false, false, null, []);
                }

                if (r != g || g != b)
                {
                    grey = false;
                }

                if (palette == null)
                {
                    continue;
                }

                var colour = (uint) (r << 16 | g << 8 | b);
                if (colour != last)
                {
                    if (!lookup.TryGetValue(colour, out lastIndex))
                    {
                        if (palette.Count == 256)
                        {
                            palette = null;
                            continue;
                        }

                        lastIndex = (byte) palette.Count;
                        lookup[colour] = lastIndex;
                        palette.Add(colour);
                    }

                    last = colour;
                }

                indices[y * width + x] = lastIndex;
            }
        }

        return (true, grey, palette, indices);
    }

    static void Pack(ReadOnlySpan<byte> row, byte[] packed, byte colourType)
    {
        var width = row.Length / 4;
        switch (colourType)
        {
            case 0:
                for (var x = 0; x < width; x++)
                {
                    packed[x] = row[x * 4];
                }

                break;
            case 2:
                for (var x = 0; x < width; x++)
                {
                    packed[x * 3] = row[x * 4];
                    packed[x * 3 + 1] = row[x * 4 + 1];
                    packed[x * 3 + 2] = row[x * 4 + 2];
                }

                break;
            default:
                row.CopyTo(packed);
                break;
        }
    }

    // Applies one of PNG's five row filters into output (its first byte the filter type) and returns
    // the sum of the filtered bytes taken as signed — the adaptive heuristic's measure.
    static long Filter(byte filter, byte[] current, byte[] previous, int channels, byte[] output)
    {
        output[0] = filter;
        long sum = 0;
        for (var i = 0; i < current.Length; i++)
        {
            var left = i >= channels ? current[i - channels] : 0;
            int up = previous[i];
            var upLeft = i >= channels ? previous[i - channels] : 0;
            var predicted = filter switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => 0
            };
            var value = (byte) (current[i] - predicted);
            output[i + 1] = value;
            sum += value < 128 ? value : 256 - value;
        }

        return sum;
    }

    static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }

        if (toUp <= toUpLeft)
        {
            return up;
        }

        return upLeft;
    }

    static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = Crc(Crc(uint.MaxValue, type), data) ^ uint.MaxValue;
        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc);
        output.Write(check);
    }

    static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
