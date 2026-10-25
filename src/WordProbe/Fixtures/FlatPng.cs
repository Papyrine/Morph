// A flat-colour PNG without an imaging library: the picture every image fixture embeds.
using System.IO.Compression;

static class FlatPng
{
    public static byte[] Create(int width, int height, byte red, byte green, byte blue)
    {
        var raw = new byte[height * (1 + width * 3)];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0;
            for (var x = 0; x < width; x++)
            {
                raw[offset++] = red;
                raw[offset++] = green;
                raw[offset++] = blue;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 2;
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BigEndian(length, 0, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        BigEndian(crcBytes, 0, (int) crc);
        output.Write(crcBytes);
    }

    static void BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte) (value >> 24);
        buffer[offset + 1] = (byte) (value >> 16);
        buffer[offset + 2] = (byte) (value >> 8);
        buffer[offset + 3] = (byte) value;
    }

    static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var bytes in new[] {first, second})
        {
            foreach (var value in bytes)
            {
                crc ^= value;
                for (var i = 0; i < 8; i++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
