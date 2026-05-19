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

        var result = DocumentParser.ReadBlipColorEffect(blip);

        await Assert.That(result).IsEqualTo(BlipColorEffect.Grayscale);
    }

    [Test]
    public async Task ReadsDuotoneEffectFromBlip()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.Duotone());

        var result = DocumentParser.ReadBlipColorEffect(blip);

        await Assert.That(result).IsEqualTo(BlipColorEffect.Duotone);
    }

    [Test]
    public async Task ReadsLumWashoutWhenBrightnessPositive()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.LuminanceEffect {Brightness = 70000});

        var result = DocumentParser.ReadBlipColorEffect(blip);

        await Assert.That(result).IsEqualTo(BlipColorEffect.Washout);
    }

    [Test]
    public async Task ReturnsNoneWhenLumBrightnessNonPositive()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.LuminanceEffect {Brightness = -30000});

        var result = DocumentParser.ReadBlipColorEffect(blip);

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }

    [Test]
    public async Task ReturnsNoneForUnmodelledEffects()
    {
        var blip = new A.Blip();
        blip.AppendChild(new A.BiLevel {Threshold = 50000});

        var result = DocumentParser.ReadBlipColorEffect(blip);

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }

    [Test]
    public async Task ReturnsNoneForEmptyBlip()
    {
        var result = DocumentParser.ReadBlipColorEffect(new A.Blip());

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }

    [Test]
    public async Task ReturnsNoneWhenBlipNull()
    {
        var result = DocumentParser.ReadBlipColorEffect(null);

        await Assert.That(result).IsEqualTo(BlipColorEffect.None);
    }
}
