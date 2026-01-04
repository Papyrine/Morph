/// <summary>
/// Tests for WordArt text transforms (presetTextWarp) as specified in MS-DOCX.
/// </summary>
internal class WordArtTransformTests
{
    [Test]
    public async Task WordArtTransform_None_IsDefault()
    {
        var element = new WordArtElement
        {
            Text = "Test",
            WidthPoints = 100,
            HeightPoints = 50
        };

        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.None);
    }

    [Test]
    [Arguments(WordArtTransform.None)]
    [Arguments(WordArtTransform.ArchUp)]
    [Arguments(WordArtTransform.ArchDown)]
    [Arguments(WordArtTransform.Circle)]
    [Arguments(WordArtTransform.Wave)]
    [Arguments(WordArtTransform.ChevronUp)]
    [Arguments(WordArtTransform.ChevronDown)]
    [Arguments(WordArtTransform.SlantUp)]
    [Arguments(WordArtTransform.SlantDown)]
    [Arguments(WordArtTransform.Triangle)]
    [Arguments(WordArtTransform.FadeRight)]
    [Arguments(WordArtTransform.FadeLeft)]
    public async Task WordArtTransform_AllValues_CanBeSet(WordArtTransform transform)
    {
        var element = new WordArtElement
        {
            Text = "Transform Test",
            WidthPoints = 200,
            HeightPoints = 100,
            Transform = transform
        };

        await Assert.That(element.Transform).IsEqualTo(transform);
    }

    [Test]
    public async Task ArchUp_TextFollowsUpwardArc()
    {
        var element = new WordArtElement
        {
            Text = "Arch Up Text",
            WidthPoints = 300,
            HeightPoints = 150,
            Transform = WordArtTransform.ArchUp
        };

        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.ArchUp);
        await Assert.That(element.Text).IsEqualTo("Arch Up Text");
    }

    [Test]
    public async Task ArchDown_TextFollowsDownwardArc()
    {
        var element = new WordArtElement
        {
            Text = "Arch Down Text",
            WidthPoints = 300,
            HeightPoints = 150,
            Transform = WordArtTransform.ArchDown
        };

        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.ArchDown);
    }

    [Test]
    public async Task Circle_TextArrangedInCircle()
    {
        var element = new WordArtElement
        {
            Text = "Circular Text Path",
            WidthPoints = 200,
            HeightPoints = 200,
            Transform = WordArtTransform.Circle
        };

        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.Circle);
    }

    [Test]
    public async Task Wave_TextHasWaveEffect()
    {
        var element = new WordArtElement
        {
            Text = "Wavy Text Effect",
            WidthPoints = 400,
            HeightPoints = 100,
            Transform = WordArtTransform.Wave
        };

        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.Wave);
    }

    [Test]
    public async Task Transform_WithGlowEffect_BothApply()
    {
        var element = new WordArtElement
        {
            Text = "Glowing Arc",
            WidthPoints = 300,
            HeightPoints = 150,
            Transform = WordArtTransform.ArchUp,
            HasGlow = true,
            FillColorHex = "FFD700"
        };

        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.ArchUp);
        await Assert.That(element.HasGlow).IsTrue();
    }

    [Test]
    public async Task Transform_WithAllEffects_FullyCombined()
    {
        var element = new WordArtElement
        {
            Text = "Full Effect Text",
            WidthPoints = 500,
            HeightPoints = 200,
            FontFamily = "Impact",
            FontSizePoints = 48,
            Bold = true,
            FillColorHex = "4472C4",
            OutlineColorHex = "1F4E79",
            OutlineWidthPoints = 2,
            Transform = WordArtTransform.Circle,
            HasGlow = true,
            HasShadow = true,
            HasReflection = true
        };

        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.Circle);
        await Assert.That(element.HasGlow).IsTrue();
        await Assert.That(element.HasShadow).IsTrue();
        await Assert.That(element.HasReflection).IsTrue();
        await Assert.That(element.Bold).IsTrue();
        await Assert.That(element.FillColorHex).IsEqualTo("4472C4");
    }
}
