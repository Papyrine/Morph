using System.Buffers.Binary;

/// <summary>
/// A picture fill that repeats (<c>a:blipFill/a:tile</c>): the tile is the picture at its own size, read
/// from the file's header, and the painters lay it edge to edge from the tile's aligned corner.
/// </summary>
public class TiledPictureFillTests
{
    static byte[] Png(int width, int height, uint? pixelsPerMetre)
    {
        var data = new List<byte> {0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A};
        void Chunk(string type, byte[] payload)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint) payload.Length);
            data.AddRange(length);
            data.AddRange(Encoding.ASCII.GetBytes(type));
            data.AddRange(payload);
            data.AddRange(new byte[4]);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        Chunk("IHDR", header);
        if (pixelsPerMetre is { } density)
        {
            var physical = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(physical, density);
            BinaryPrimitives.WriteUInt32BigEndian(physical.AsSpan(4), density);
            physical[8] = 1;
            Chunk("pHYs", physical);
        }

        Chunk("IDAT", []);
        return [.. data];
    }

    [Test]
    public async Task A_png_gives_its_pixels_and_the_resolution_it_declares()
    {
        // 3780 pixels per metre is 96 dpi; 11811 is 300.
        await Assert.That(ImageHeader.TryRead(Png(128, 42, 3780), out var width, out var height, out var dpiX, out var dpiY)).IsTrue();
        await Assert.That(width).IsEqualTo(128);
        await Assert.That(height).IsEqualTo(42);
        await Assert.That(dpiX).IsEqualTo(96).Within(0.02);
        await Assert.That(dpiY).IsEqualTo(96).Within(0.02);

        await Assert.That(ImageHeader.TryReadSizePoints(Png(600, 300, 11811), out var widthPoints, out var heightPoints)).IsTrue();
        await Assert.That(widthPoints).IsEqualTo(144).Within(0.01);
        await Assert.That(heightPoints).IsEqualTo(72).Within(0.01);
    }

    [Test]
    public async Task A_picture_that_declares_no_resolution_is_taken_at_96_dpi()
    {
        await Assert.That(ImageHeader.TryReadSizePoints(Png(128, 64, null), out var widthPoints, out var heightPoints)).IsTrue();
        await Assert.That(widthPoints).IsEqualTo(96);
        await Assert.That(heightPoints).IsEqualTo(48);
    }

    [Test]
    public async Task A_jpeg_is_read_through_its_jfif_and_frame_segments()
    {
        // SOI, APP0 "JFIF" at 96 dots per inch, SOF0 of 445 rows by 667 columns.
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x60, 0x00, 0x60, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xBD, 0x02, 0x9B, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01
        ];

        await Assert.That(ImageHeader.TryRead(jpeg, out var width, out var height, out var dpiX, out _)).IsTrue();
        await Assert.That(width).IsEqualTo(667);
        await Assert.That(height).IsEqualTo(445);
        await Assert.That(dpiX).IsEqualTo(96);
    }

    [Test]
    public async Task What_is_not_a_raster_header_is_not_read()
    {
        await Assert.That(ImageHeader.TryRead("<svg xmlns='http://www.w3.org/2000/svg'/>"u8, out _, out _, out _, out _)).IsFalse();
        await Assert.That(ImageHeader.TryRead([], out _, out _, out _, out _)).IsFalse();
    }

    [Test]
    public async Task Tiles_run_from_the_aligned_corner_and_cover_the_box()
    {
        var topLeft = new ImageTile(100, 50, 0, 0, ImageTileAlignment.TopLeft);
        await Assert.That(topLeft.Origin(250, 120)).IsEqualTo((0d, 0d));
        await Assert.That(topLeft.Corners(250, 120).ToList()).IsEquivalentTo(new List<(double X, double Y)>
        {
            (0, 0), (100, 0), (200, 0), (0, 50), (100, 50), (200, 50), (0, 100), (100, 100), (200, 100)
        });

        // Aligned to the bottom right, the pattern still fills the box up and to the left.
        var bottomRight = new ImageTile(100, 50, 0, 0, ImageTileAlignment.BottomRight);
        await Assert.That(bottomRight.Origin(250, 120)).IsEqualTo((150d, 70d));
        var corners = bottomRight.Corners(250, 120).ToList();
        await Assert.That(corners.Min(_ => _.X)).IsEqualTo(-50);
        await Assert.That(corners.Min(_ => _.Y)).IsEqualTo(-30);
        await Assert.That(corners.Count).IsEqualTo(9);

        // The offsets shift the whole pattern.
        var shifted = new ImageTile(100, 50, 30, -10, ImageTileAlignment.TopLeft);
        await Assert.That(shifted.Corners(250, 120).First()).IsEqualTo((-70d, -10d));
    }

    // brochures/04's brick wall: a 667x445 photograph at 96 dpi tiled from the top left of a triangle
    // 794pt wide, so Word shows a tile and a half of bricks where a stretch showed one squeezed photo.
    [Test]
    public async Task The_parse_carries_the_tile_at_the_pictures_own_size_to_the_placed_picture()
    {
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "brochures", "04", "input.docx"));
        var document = new DocumentParser().Parse(stream);
        var wall = document.Elements.OfType<FloatingShapeElement>().Single(_ => _.ImageTile != null);

        await Assert.That(wall.ImageTile!.WidthPoints).IsEqualTo(667 * 0.75).Within(0.01);
        await Assert.That(wall.ImageTile.HeightPoints).IsEqualTo(445 * 0.75).Within(0.01);
        await Assert.That(wall.ImageTile.Alignment).IsEqualTo(ImageTileAlignment.TopLeft);
        await Assert.That(wall.Subpaths).IsNotNull();

        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility).Layout(document.Elements, document.PageSettings);
        var placed = laidOut.Pages.SelectMany(_ => _.Items).OfType<PlacedImage>().Single(_ => _.Tile != null);
        await Assert.That(placed.Tile).IsEqualTo(wall.ImageTile);
        await Assert.That(placed.ClipSubpaths).IsNotNull();
    }
}
