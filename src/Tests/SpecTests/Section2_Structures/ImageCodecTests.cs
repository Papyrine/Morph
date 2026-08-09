/// <summary>
/// Covers both <see cref="ImageCodec"/> implementations against the same expectations, since
/// <see cref="ImageCompressor"/> is entitled to the same behaviour from whichever one it finds.
/// </summary>
public class ImageCodecTests
{
    public static IEnumerable<Func<(string Name, ImageCodec Codec)>> Codecs()
    {
        yield return () => ("ImageSharp", new ImageSharpImageCodec());
        yield return () => ("Skia", new SkiaImageCodec());
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task ReadsDimensions((string Name, ImageCodec Codec) codec)
    {
        var probe = codec.Codec.Probe(TestImages.Photograph(120, 80));

        await Assert.That(probe!.Width).IsEqualTo(120);
        await Assert.That(probe.Height).IsEqualTo(80);
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task ReportsAnOpaqueImageAsOpaque((string Name, ImageCodec Codec) codec)
    {
        var probe = codec.Codec.Probe(TestImages.Photograph(120, 80));

        await Assert.That(probe!.HasTranslucency).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task NoticesASingleTransparentPixel((string Name, ImageCodec Codec) codec)
    {
        // the fixture is opaque but for one corner: anything less than a full scan misses it, and
        // a missed one is a picture flattened onto black by a JPEG conversion
        var probe = codec.Codec.Probe(TestImages.Photograph(120, 80, translucent: true));

        await Assert.That(probe!.HasTranslucency).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task AppliesExifOrientationBeforeDiscardingIt((string Name, ImageCodec Codec) codec)
    {
        // 120x80 pixels, tagged as a quarter turn — so it is really 80 wide and 120 tall
        var sideOn = TestImages.SideOn(120, 80);

        var probe = codec.Codec.Probe(sideOn);
        await Assert.That(probe!.Width).IsEqualTo(80);
        await Assert.That(probe.Height).IsEqualTo(120);

        // and the pixels must come out that way too, or the encoded result is on its side with no
        // tag left to correct it
        var encoded = codec.Codec.Encode(sideOn, new(80, 120, "image/jpeg", 80));
        await Assert.That(TestImages.Width(encoded!)).IsEqualTo(80);
        await Assert.That(TestImages.Height(encoded!)).IsEqualTo(120);
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task ResamplesToTheRequestedSize((string Name, ImageCodec Codec) codec)
    {
        var encoded = codec.Codec.Encode(TestImages.Photograph(400, 300), new(100, 75, "image/png", 80));

        await Assert.That(TestImages.Width(encoded!)).IsEqualTo(100);
        await Assert.That(TestImages.Height(encoded!)).IsEqualTo(75);
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task WritesJpegWhenAskedFor((string Name, ImageCodec Codec) codec)
    {
        var source = TestImages.Photograph(400, 300);

        var encoded = codec.Codec.Encode(source, new(400, 300, "image/jpeg", 80));

        // noise is the case PNG cannot help with and JPEG can
        await Assert.That(encoded!.Length).IsLessThan(source.Length);
        await Assert.That(encoded[0]).IsEqualTo((byte) 0xFF);
        await Assert.That(encoded[1]).IsEqualTo((byte) 0xD8);
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task WritesPngWhenAskedFor((string Name, ImageCodec Codec) codec)
    {
        var encoded = codec.Codec.Encode(TestImages.Photograph(80, 60), new(80, 60, "image/png", 80));

        await Assert.That(encoded!.Take(8)).IsEquivalentTo(new byte[] {0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A});
    }

    [Test]
    [MethodDataSource(nameof(Codecs))]
    public async Task ReturnsNullRatherThanThrowingOnRubbish((string Name, ImageCodec Codec) codec)
    {
        await Assert.That(codec.Codec.Probe([1, 2, 3, 4, 5])).IsNull();
        await Assert.That(codec.Codec.Encode([1, 2, 3, 4, 5], new(10, 10, "image/png", 80))).IsNull();
    }
}
