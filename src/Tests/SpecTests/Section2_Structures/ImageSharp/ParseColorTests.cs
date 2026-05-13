extern alias ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public class ParseColorTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("auto")]
    public async Task NullOrEmptyOrAuto_ReturnsBlack(string? input) =>
        await Assert.That(ImageSharpRenderContext.ParseColor(input)).IsEqualTo(Color.Black);

    [Test]
    public async Task SixCharHex_ParsesRgb()
    {
        var result = ImageSharpRenderContext.ParseColor("FF8040");
        await Assert.That(result).IsEqualTo(Color.FromPixel(new Rgb24(0xFF, 0x80, 0x40)));
    }

    [Test]
    public async Task SixCharHex_Black() =>
        await Assert.That(ImageSharpRenderContext.ParseColor("000000")).IsEqualTo(Color.FromPixel(new Rgb24(0, 0, 0)));

    [Test]
    public async Task SixCharHex_White() =>
        await Assert.That(ImageSharpRenderContext.ParseColor("FFFFFF")).IsEqualTo(Color.FromPixel(new Rgb24(255, 255, 255)));

    [Test]
    public async Task EightCharHex_FullyOpaque()
    {
        var result = ImageSharpRenderContext.ParseColor("FFFF8040");
        await Assert.That(result).IsEqualTo(Color.FromPixel(new Rgba32((byte) 0xFF, (byte) 0x80, (byte) 0x40, (byte) 0xFF)));
    }

    [Test]
    public async Task EightCharHex_SemiTransparent()
    {
        var result = ImageSharpRenderContext.ParseColor("80FF0000");
        await Assert.That(result).IsEqualTo(Color.FromPixel(new Rgba32((byte) 0xFF, (byte) 0x00, (byte) 0x00, (byte) 0x80)));
    }

    [Test]
    public async Task EightCharHex_FullyTransparent()
    {
        var result = ImageSharpRenderContext.ParseColor("00FF8040");
        await Assert.That(result).IsEqualTo(Color.FromPixel(new Rgba32((byte) 0xFF, (byte) 0x80, (byte) 0x40, (byte) 0x00)));
    }

    [Test]
    public async Task InvalidHex_ReturnsBlack() =>
        await Assert.That(ImageSharpRenderContext.ParseColor("ZZZZZZ")).IsEqualTo(Color.Black);

    [Test]
    public async Task WrongLength_ReturnsBlack() =>
        await Assert.That(ImageSharpRenderContext.ParseColor("FFF")).IsEqualTo(Color.Black);
}
