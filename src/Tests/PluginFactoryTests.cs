/// <summary>
/// The optional-backend factories name their types literally so a trimmed app keeps them
/// (<c>AotSmoke</c>). A type renamed or moved would then go unfound without an error, and the feature
/// behind it would quietly degrade, so each name is pinned here: with both backends deployed, every
/// factory finds its type.
/// </summary>
public class PluginFactoryTests
{
    [Test]
    public async Task The_image_codec_is_found() =>
        await Assert.That(ImageCodecFactory.TryGet()?.GetType().Name).IsEqualTo("ImageSharpImageCodec");

    [Test]
    public async Task The_pdf_image_effects_are_found() =>
        await Assert.That(ImageEffectsFactory.TryGet()?.GetType().Name).IsEqualTo("SkiaImageEffects");

    [Test]
    public async Task The_pdf_wordart_rasterizer_is_found() =>
        await Assert.That(WordArtRasterizerFactory.TryGet()?.GetType().Name).IsEqualTo("SkiaWordArtRasterizer");
}
