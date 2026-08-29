using A = DocumentFormat.OpenXml.Drawing;

public class BlipColorEffectParseTests
{
    // ReadBlipColorEffect is internal static — Tests has InternalsVisibleTo via the csproj.
    // We exercise it through DocumentParser indirectly: build a tiny docx with a blip carrying
    // each effect kid and walk the result.

    [Test]
    public async Task ReadsGrayscaleEffectFromBlip()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.Grayscale());

        var result = DocumentParser.ReadBlipColorEffect(blip, null).Effect;

        await Assert.That(result).IsEqualTo(BlipColorEffect.Grayscale);
    }

    [Test]
    public async Task ReadsDuotoneEffectFromBlip()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.Duotone());

        var result = DocumentParser.ReadBlipColorEffect(blip, null).Effect;

        await Assert.That(result).IsEqualTo(BlipColorEffect.Duotone);
    }

    [Test]
    public async Task ReadsLumWashoutWhenBrightnessPositive()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.LuminanceEffect {Brightness = 70000});

        var result = DocumentParser.ReadBlipColorEffect(blip, null).Effect;

        await Assert.That(result).IsEqualTo(BlipColorEffect.Washout);
    }

    [Test]
    public async Task ReturnsNoneWhenLumBrightnessNonPositive()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.LuminanceEffect {Brightness = -30000});

        var result = DocumentParser.ReadBlipColorEffect(blip, null).Effect;

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }

    [Test]
    public async Task ReturnsNoneForUnmodelledEffects()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.BiLevel {Threshold = 50000});

        var result = DocumentParser.ReadBlipColorEffect(blip, null).Effect;

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }

    [Test]
    public async Task ReturnsNoneForEmptyBlip()
    {
        var result = DocumentParser.ReadBlipColorEffect(new A.Blip(), null).Effect;

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }

    [Test]
    public async Task ReturnsNoneWhenBlipNull()
    {
        var result = DocumentParser.ReadBlipColorEffect(null, null).Effect;

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }

    [Test]
    public async Task ReadsAlphaModFixAsAnOpacityMultiplier()
    {
        // brochures/08's blips, the corpus's only alphaModFix: @amt is a percentage in thousandths,
        // so 50000 is half opacity.
        var blip = new A.Blip();
        blip.AppendChild(new A.AlphaModulationFixed {Amount = 50000});

        await Assert.That(DocumentParser.ReadBlipOpacity(blip)).IsEqualTo(0.5).Within(0.0001);
    }

    [Test]
    public async Task OpacityIsOpaqueWithoutAlphaModFix()
    {
        await Assert.That(DocumentParser.ReadBlipOpacity(new A.Blip())).IsEqualTo(1).Within(0.0001);
        await Assert.That(DocumentParser.ReadBlipOpacity(null)).IsEqualTo(1).Within(0.0001);
    }

    [Test]
    public async Task OpacityIsReadAlongsideAColourTransform()
    {
        // The two are separate children of the same blip and the effect scan returns on the first
        // colour transform it finds, so a picture carrying both must not lose either.
        var blip = new A.Blip();
        blip.AppendChild(new A.Grayscale());
        blip.AppendChild(new A.AlphaModulationFixed {Amount = 30000});

        await Assert.That(DocumentParser.ReadBlipColorEffect(blip, null).Effect).IsEqualTo(BlipColorEffect.Grayscale);
        await Assert.That(DocumentParser.ReadBlipOpacity(blip)).IsEqualTo(0.3).Within(0.0001);
    }
}
