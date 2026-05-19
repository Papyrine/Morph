extern alias ImageSharp;

using SixLabors.Fonts;

public class KerningModeResolutionTests
{
    [Test]
    public async Task DefaultsToStandardWhenNoConstraints()
    {
        var props = new RunProperties {FontSizePoints = 11};

        var mode = ImageSharp::TextShaping.ResolveKerningMode(props);

        await Assert.That(mode).IsEqualTo(KerningMode.Standard);
    }

    [Test]
    public async Task DisablesKerningBelowSizeThreshold()
    {
        // Word's default w:kern is 16pt; runs at smaller sizes shouldn't kern.
        var props = new RunProperties
        {
            FontSizePoints = 11,
            KerningMinFontSizePoints = 16
        };

        var mode = ImageSharp::TextShaping.ResolveKerningMode(props);

        await Assert.That(mode).IsEqualTo(KerningMode.None);
    }

    [Test]
    public async Task KeepsStandardAtOrAboveThreshold()
    {
        var props = new RunProperties
        {
            FontSizePoints = 16,
            KerningMinFontSizePoints = 16
        };

        var mode = ImageSharp::TextShaping.ResolveKerningMode(props);

        await Assert.That(mode).IsEqualTo(KerningMode.Standard);
    }

    [Test]
    public async Task ZeroThresholdIsTreatedAsNoExplicitSetting()
    {
        // Threshold of 0 means "no w:kern element captured" — default kerning behaviour applies.
        var props = new RunProperties
        {
            FontSizePoints = 8,
            KerningMinFontSizePoints = 0
        };

        var mode = ImageSharp::TextShaping.ResolveKerningMode(props);

        await Assert.That(mode).IsEqualTo(KerningMode.Standard);
    }

    [Test]
    public async Task DisablesKerningWhenLigaturesNone()
    {
        // w14:ligatures="none" disables both ligatures and kerning under SixLabors' shaper.
        var props = new RunProperties
        {
            FontSizePoints = 24,
            Ligatures = LigatureMode.None
        };

        var mode = ImageSharp::TextShaping.ResolveKerningMode(props);

        await Assert.That(mode).IsEqualTo(KerningMode.None);
    }

    [Test]
    public async Task ThresholdTakesPrecedenceOverLigatureStandard()
    {
        // Both rules say None when violated — verify the threshold rule fires first
        // (matters if we ever extend the resolver to return different reason codes).
        var props = new RunProperties
        {
            FontSizePoints = 8,
            KerningMinFontSizePoints = 16,
            Ligatures = LigatureMode.Standard
        };

        var mode = ImageSharp::TextShaping.ResolveKerningMode(props);

        await Assert.That(mode).IsEqualTo(KerningMode.None);
    }
}
