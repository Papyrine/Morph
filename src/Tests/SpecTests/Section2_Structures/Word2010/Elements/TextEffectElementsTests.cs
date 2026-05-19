/// <summary>
/// Tests for text effect elements as specified in MS-DOCX Section 2.6.1.
/// Covers glow, reflection, shadow, textFill, textOutline.
/// </summary>
public class TextEffectElementsTests
{
    [Test]
    public async Task WordArtElement_RequiredProperties_CanBeSet()
    {
        var element = new WordArtElement
        {
            Text = "Test WordArt",
            WidthPoints = 300,
            HeightPoints = 50
        };

        await Assert.That(element.Text).IsEqualTo("Test WordArt");
        await Assert.That(element.WidthPoints).IsEqualTo(300);
        await Assert.That(element.HeightPoints).IsEqualTo(50);
    }

    [Test]
    public async Task WordArtElement_DefaultValues_AreCorrect()
    {
        var element = new WordArtElement
        {
            Text = "Test",
            WidthPoints = 100,
            HeightPoints = 50
        };

        await Assert.That(element.FontFamily).IsEqualTo("Aptos");
        await Assert.That(element.FontSizePoints).IsEqualTo(36);
        await Assert.That(element.Bold).IsFalse();
        await Assert.That(element.Italic).IsFalse();
        await Assert.That(element.FillColorHex).IsNull();
        await Assert.That(element.OutlineColorHex).IsNull();
        await Assert.That(element.OutlineWidthPoints).IsEqualTo(0);
        await Assert.That(element.HasShadow).IsFalse();
        await Assert.That(element.HasReflection).IsFalse();
        await Assert.That(element.HasGlow).IsFalse();
        await Assert.That(element.Transform).IsEqualTo(WordArtTransform.None);
    }

    [Test]
    public async Task HasGlow_WhenTrue_IndicatesGlowEffect()
    {
        var element = new WordArtElement
        {
            Text = "Glowing Text",
            WidthPoints = 200,
            HeightPoints = 50,
            HasGlow = true
        };

        await Assert.That(element.HasGlow).IsTrue();
    }

    [Test]
    public async Task HasShadow_WhenTrue_IndicatesShadowEffect()
    {
        var element = new WordArtElement
        {
            Text = "Shadowed Text",
            WidthPoints = 200,
            HeightPoints = 50,
            HasShadow = true
        };

        await Assert.That(element.HasShadow).IsTrue();
    }

    [Test]
    public async Task HasReflection_WhenTrue_IndicatesReflectionEffect()
    {
        var element = new WordArtElement
        {
            Text = "Reflected Text",
            WidthPoints = 200,
            HeightPoints = 50,
            HasReflection = true
        };

        await Assert.That(element.HasReflection).IsTrue();
    }

    [Test]
    public async Task FillColorHex_WhenSet_IndicatesSolidFill()
    {
        var element = new WordArtElement
        {
            Text = "Filled Text",
            WidthPoints = 200,
            HeightPoints = 50,
            FillColorHex = "FF0000"
        };

        await Assert.That(element.FillColorHex).IsEqualTo("FF0000");
    }

    [Test]
    public async Task OutlineColorHex_WhenSet_IndicatesOutline()
    {
        var element = new WordArtElement
        {
            Text = "Outlined Text",
            WidthPoints = 200,
            HeightPoints = 50,
            OutlineColorHex = "000000"
        };

        await Assert.That(element.OutlineColorHex).IsEqualTo("000000");
    }

    [Test]
    public async Task OutlineWidthPoints_WhenSet_DefinesStrokeWidth()
    {
        var element = new WordArtElement
        {
            Text = "Outlined Text",
            WidthPoints = 200,
            HeightPoints = 50,
            OutlineColorHex = "000000",
            OutlineWidthPoints = 2.5
        };

        await Assert.That(element.OutlineWidthPoints).IsEqualTo(2.5);
    }

    [Test]
    public async Task MultipleEffects_CanBeCombined()
    {
        var element = new WordArtElement
        {
            Text = "Full Effects Text",
            WidthPoints = 300,
            HeightPoints = 100,
            FillColorHex = "4472C4",
            OutlineColorHex = "1F4E79",
            OutlineWidthPoints = 1.5,
            HasGlow = true,
            HasShadow = true,
            HasReflection = true
        };

        await Assert.That(element.FillColorHex).IsEqualTo("4472C4");
        await Assert.That(element.OutlineColorHex).IsEqualTo("1F4E79");
        await Assert.That(element.OutlineWidthPoints).IsEqualTo(1.5);
        await Assert.That(element.HasGlow).IsTrue();
        await Assert.That(element.HasShadow).IsTrue();
        await Assert.That(element.HasReflection).IsTrue();
    }

    [Test]
    public async Task Bold_WhenTrue_TextIsBold()
    {
        var element = new WordArtElement
        {
            Text = "Bold Text",
            WidthPoints = 200,
            HeightPoints = 50,
            Bold = true
        };

        await Assert.That(element.Bold).IsTrue();
    }

    [Test]
    public async Task Italic_WhenTrue_TextIsItalic()
    {
        var element = new WordArtElement
        {
            Text = "Italic Text",
            WidthPoints = 200,
            HeightPoints = 50,
            Italic = true
        };

        await Assert.That(element.Italic).IsTrue();
    }
}
