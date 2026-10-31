/// <summary>
/// What a painter asks of an <see cref="SvgDrawing"/>: where it lands in a picture's box, with or
/// without an <c>a:srcRect</c> crop; its colours under a Recolor transform; and the one reading
/// per picture part that <see cref="SvgDrawing.For"/> hands out.
/// </summary>
public class SvgDrawingTests
{
    const string markup = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"10 20 100 50\"><rect x=\"10\" y=\"20\" width=\"100\" height=\"50\" fill=\"#808080\" fill-opacity=\"0.5\" stroke=\"#FF0000\"/></svg>";

    // On the viewBox itself rather than the frame the reader gives it (half a pixel larger each way,
    // SvgReaderTests), so the numbers below are the placement's alone.
    static readonly SvgDrawing drawing = new(10, 20, 100, 50, SvgTestSupport.Draw(markup).Paths);

    [Test]
    public async Task The_frame_is_stretched_over_the_box()
    {
        var placement = drawing.Place(5, 5, 200, 150, null);

        // Twice as wide and three times as tall; the frame's corner (10, 20) lands on the box's.
        await Assert.That(placement.ScaleX).IsEqualTo(2d);
        await Assert.That(placement.ScaleY).IsEqualTo(3d);
        await Assert.That(10 * placement.ScaleX + placement.OffsetX).IsEqualTo(5d);
        await Assert.That(20 * placement.ScaleY + placement.OffsetY).IsEqualTo(5d);
        await Assert.That((placement.Left, placement.Top, placement.Right, placement.Bottom)).IsEqualTo((5d, 5d, 205d, 155d));
        await Assert.That(placement.StrokeScale).IsEqualTo(Math.Sqrt(6)).Within(1e-9);
    }

    [Test]
    public async Task A_crop_lays_the_cropped_part_over_the_box_and_cuts_the_rest_off()
    {
        // A quarter off each side: the middle half of the frame fills the box, so the whole frame
        // is twice the box's width and starts half a box to its left.
        var placement = drawing.Place(0, 0, 100, 50, new() {Left = 0.25, Right = 0.25});

        await Assert.That(placement.ScaleX).IsEqualTo(2d);
        await Assert.That(placement.ScaleY).IsEqualTo(1d);
        await Assert.That((10 + 25) * placement.ScaleX + placement.OffsetX).IsEqualTo(0d);
        await Assert.That((placement.Left, placement.Top, placement.Right, placement.Bottom)).IsEqualTo((0d, 0d, 100d, 50d));
    }

    [Test]
    public async Task A_negative_crop_shrinks_the_frame_into_the_box()
    {
        // Padding of the picture's own height above it: the frame is the lower half of the box.
        var placement = drawing.Place(0, 0, 100, 50, new() {Top = -1});

        await Assert.That(placement.ScaleY).IsEqualTo(0.5);
        await Assert.That((placement.Left, placement.Top, placement.Right, placement.Bottom)).IsEqualTo((0d, 25d, 100d, 50d));
        await Assert.That(placement.IsVisible).IsTrue();
    }

    [Test]
    public async Task A_recolour_maps_every_colour_and_leaves_alpha_alone()
    {
        var grey = drawing.Recolored(ImageRecolor.For(BlipColorEffect.Grayscale, null, null)).Paths.Single();

        // Rec. 709 luminance: grey stays grey, and pure red is 21% of white.
        await Assert.That(grey.FillColorHex).IsEqualTo("808080");
        await Assert.That(grey.FillAlpha).IsEqualTo(0.5);
        await Assert.That(grey.Stroke!.ColorHex).IsEqualTo("363636");

        await Assert.That(ReferenceEquals(drawing.Recolored(null), drawing)).IsTrue();
    }

    [Test]
    public async Task One_part_is_read_once_and_only_an_SVG_the_reader_takes_gives_a_drawing()
    {
        var bytes = Encoding.UTF8.GetBytes(markup);

        await Assert.That(SvgDrawing.For(bytes, "image/svg+xml")).IsNotNull();
        await Assert.That(ReferenceEquals(SvgDrawing.For(bytes, "image/svg+xml"), SvgDrawing.For(bytes, "image/svg+xml"))).IsTrue();

        await Assert.That(SvgDrawing.For(bytes, "image/png")).IsNull();
        await Assert.That(SvgDrawing.For(null, "image/svg+xml")).IsNull();
        await Assert.That(SvgDrawing.For("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 9 9\"><text>Hi</text></svg>"u8.ToArray(), "image/svg+xml")).IsNull();
    }
}
