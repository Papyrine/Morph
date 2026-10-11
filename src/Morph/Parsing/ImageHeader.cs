using System.Buffers.Binary;

/// <summary>
/// Reads a raster picture's pixel size and declared resolution straight from its header, with no
/// decoder: enough to know how large the picture is on paper, which a tiled fill is drawn at.
/// </summary>
static class ImageHeader
{
    /// <summary>The resolution a picture is taken at when its file declares none.</summary>
    const double defaultDpi = 96;

    /// <summary>
    /// The picture's size in points: its pixels at its own declared resolution, 96 dpi when it
    /// declares none. False for a format this does not read (PNG, JPEG, GIF and BMP are read) or a
    /// header it cannot follow.
    /// </summary>
    public static bool TryReadSizePoints(ReadOnlySpan<byte> data, out double widthPoints, out double heightPoints)
    {
        widthPoints = 0;
        heightPoints = 0;
        if (!TryRead(data, out var width, out var height, out var dpiX, out var dpiY))
        {
            return false;
        }

        widthPoints = width * 72.0 / dpiX;
        heightPoints = height * 72.0 / dpiY;
        return true;
    }

    /// <summary>
    /// The picture's pixel size and resolution. A resolution the file does not declare, or declares
    /// as nonsense, comes back as 96.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> data, out int width, out int height, out double dpiX, out double dpiY)
    {
        width = 0;
        height = 0;
        dpiX = defaultDpi;
        dpiY = defaultDpi;
        var read =
            TryReadPng(data, ref width, ref height, ref dpiX, ref dpiY) ||
            TryReadJpeg(data, ref width, ref height, ref dpiX, ref dpiY) ||
            TryReadGif(data, ref width, ref height) ||
            TryReadBmp(data, ref width, ref height, ref dpiX, ref dpiY);
        if (!read || width <= 0 || height <= 0)
        {
            return false;
        }

        if (dpiX is < 1 or > 100000 || dpiY is < 1 or > 100000)
        {
            dpiX = defaultDpi;
            dpiY = defaultDpi;
        }

        return true;
    }

    static bool TryReadPng(ReadOnlySpan<byte> data, ref int width, ref int height, ref double dpiX, ref double dpiY)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 33 || !data[..8].SequenceEqual(signature))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
        height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);

        // Chunks run length, type, payload, checksum. pHYs comes ahead of the pixel data when it
        // is there at all: pixels per unit on each axis, the unit a metre when its last byte is 1.
        var offset = 8;
        while (offset + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            var type = data.Slice(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
            {
                break;
            }

            if (type.SequenceEqual("pHYs"u8) && length == 9 && offset + 17 <= data.Length)
            {
                var perUnitX = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 8)..]);
                var perUnitY = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 12)..]);
                if (data[offset + 16] == 1 && perUnitX > 0 && perUnitY > 0)
                {
                    dpiX = perUnitX * 0.0254;
                    dpiY = perUnitY * 0.0254;
                }

                break;
            }

            if (length > int.MaxValue - 12 - offset)
            {
                break;
            }

            offset += 12 + (int) length;
        }

        return true;
    }

    static bool TryReadJpeg(ReadOnlySpan<byte> data, ref int width, ref int height, ref double dpiX, ref double dpiY)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF)
            {
                return false;
            }

            var marker = data[offset + 1];
            if (marker == 0xFF)
            {
                // Fill bytes ahead of a marker.
                offset++;
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            if (length < 2 || offset + 2 + length > data.Length)
            {
                return false;
            }

            var segment = data.Slice(offset + 4, length - 2);

            // JFIF: "JFIF\0", version, then the density unit (1 per inch, 2 per centimetre, 0 none)
            // and the density on each axis.
            if (marker == 0xE0 && segment.Length >= 12 && segment[..5].SequenceEqual("JFIF\0"u8))
            {
                var unit = segment[7];
                var densityX = BinaryPrimitives.ReadUInt16BigEndian(segment[8..]);
                var densityY = BinaryPrimitives.ReadUInt16BigEndian(segment[10..]);
                if (densityX > 0 && densityY > 0)
                {
                    if (unit == 1)
                    {
                        dpiX = densityX;
                        dpiY = densityY;
                    }
                    else if (unit == 2)
                    {
                        dpiX = densityX * 2.54;
                        dpiY = densityY * 2.54;
                    }
                }
            }

            // A start-of-frame segment carries the size: precision, height, width. C4, C8 and CC
            // share the range and are tables, not frames.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC) && segment.Length >= 5)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                return true;
            }

            if (marker == 0xDA)
            {
                return false;
            }

            offset += 2 + length;
        }

        return false;
    }

    static bool TryReadGif(ReadOnlySpan<byte> data, ref int width, ref int height)
    {
        if (data.Length < 10 || !(data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        return true;
    }

    static bool TryReadBmp(ReadOnlySpan<byte> data, ref int width, ref int height, ref double dpiX, ref double dpiY)
    {
        if (data.Length < 46 || data[0] != (byte) 'B' || data[1] != (byte) 'M')
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        // A negative height is a top-down bitmap of that many rows.
        height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..]));
        var perMetreX = BinaryPrimitives.ReadInt32LittleEndian(data[38..]);
        var perMetreY = BinaryPrimitives.ReadInt32LittleEndian(data[42..]);
        if (perMetreX > 0 && perMetreY > 0)
        {
            dpiX = perMetreX * 0.0254;
            dpiY = perMetreY * 0.0254;
        }

        return true;
    }
}
