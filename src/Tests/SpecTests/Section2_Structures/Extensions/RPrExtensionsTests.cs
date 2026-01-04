/// <summary>
/// Tests for rPr (run properties) extensions as specified in MS-DOCX Section 2.2.1.
/// </summary>
public class RPrExtensionsTests
{
    [Test]
    public async Task Run_Text_CanBeSet()
    {
        var run = new Run { Text = "Hello World" };
        await Assert.That(run.Text).IsEqualTo("Hello World");
    }

    [Test]
    public async Task Run_Text_CanBeEmpty()
    {
        var run = new Run { Text = "" };
        await Assert.That(run.Text).IsEqualTo("");
    }

    [Test]
    public async Task RunProperties_Defaults_AreCorrect()
    {
        var props = new RunProperties();

        await Assert.That(props.FontFamily).IsEqualTo("Aptos");
        await Assert.That(props.FontSizePoints).IsEqualTo(11);
        await Assert.That(props.Bold).IsFalse();
        await Assert.That(props.Italic).IsFalse();
        await Assert.That(props.Underline).IsFalse();
        await Assert.That(props.Strikethrough).IsFalse();
        await Assert.That(props.AllCaps).IsFalse();
        await Assert.That(props.ColorHex).IsNull();
    }

    [Test]
    public async Task Run_DefaultProperties_WhenNotSpecified()
    {
        var run = new Run { Text = "Test" };

        await Assert.That(run.Properties.FontFamily).IsEqualTo("Aptos");
        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(11);
        await Assert.That(run.Properties.Bold).IsFalse();
    }

    [Test]
    public async Task RunProperties_Bold_CanBeTrue()
    {
        var props = new RunProperties { Bold = true };
        await Assert.That(props.Bold).IsTrue();
    }

    [Test]
    public async Task RunProperties_Italic_CanBeTrue()
    {
        var props = new RunProperties { Italic = true };
        await Assert.That(props.Italic).IsTrue();
    }

    [Test]
    public async Task RunProperties_Underline_CanBeTrue()
    {
        var props = new RunProperties { Underline = true };
        await Assert.That(props.Underline).IsTrue();
    }

    [Test]
    public async Task RunProperties_Strikethrough_CanBeTrue()
    {
        var props = new RunProperties { Strikethrough = true };
        await Assert.That(props.Strikethrough).IsTrue();
    }

    [Test]
    public async Task RunProperties_AllCaps_CanBeTrue()
    {
        var props = new RunProperties { AllCaps = true };
        await Assert.That(props.AllCaps).IsTrue();
    }

    [Test]
    [Arguments("Arial")]
    [Arguments("Calibri")]
    [Arguments("Times New Roman")]
    [Arguments("Courier New")]
    public async Task RunProperties_FontFamily_CommonFontsAccepted(string fontFamily)
    {
        var props = new RunProperties { FontFamily = fontFamily };
        await Assert.That(props.FontFamily).IsEqualTo(fontFamily);
    }

    [Test]
    [Arguments(8.0)]
    [Arguments(11.0)]
    [Arguments(12.0)]
    [Arguments(24.0)]
    [Arguments(72.0)]
    public async Task RunProperties_FontSizePoints_CommonSizesAccepted(double fontSize)
    {
        var props = new RunProperties { FontSizePoints = fontSize };
        await Assert.That(props.FontSizePoints).IsEqualTo(fontSize);
    }

    [Test]
    public async Task RunProperties_ColorHex_CanBeSet()
    {
        var props = new RunProperties { ColorHex = "FF0000" };
        await Assert.That(props.ColorHex).IsEqualTo("FF0000");
    }

    [Test]
    [Arguments("FF0000")]
    [Arguments("00FF00")]
    [Arguments("0000FF")]
    [Arguments("000000")]
    [Arguments("FFFFFF")]
    public async Task RunProperties_ColorHex_ValidColorsAccepted(string colorHex)
    {
        var props = new RunProperties { ColorHex = colorHex };
        await Assert.That(props.ColorHex).IsEqualTo(colorHex);
    }

    [Test]
    public async Task RunProperties_MultipleStyles_CanBeCombined()
    {
        var props = new RunProperties
        {
            Bold = true,
            Italic = true,
            Underline = true,
            FontFamily = "Georgia",
            FontSizePoints = 16,
            ColorHex = "FF0000"
        };

        await Assert.That(props.Bold).IsTrue();
        await Assert.That(props.Italic).IsTrue();
        await Assert.That(props.Underline).IsTrue();
        await Assert.That(props.FontFamily).IsEqualTo("Georgia");
        await Assert.That(props.FontSizePoints).IsEqualTo(16);
        await Assert.That(props.ColorHex).IsEqualTo("FF0000");
    }

    [Test]
    public async Task Run_WithCustomProperties_Accepted()
    {
        var run = new Run
        {
            Text = "Formatted Text",
            Properties = new RunProperties
            {
                Bold = true,
                FontFamily = "Impact",
                FontSizePoints = 24,
                ColorHex = "4472C4"
            }
        };

        await Assert.That(run.Text).IsEqualTo("Formatted Text");
        await Assert.That(run.Properties.Bold).IsTrue();
        await Assert.That(run.Properties.FontFamily).IsEqualTo("Impact");
        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(24);
        await Assert.That(run.Properties.ColorHex).IsEqualTo("4472C4");
    }

    [Test]
    public async Task RunProperties_SameValues_AreEqual()
    {
        var props1 = new RunProperties { Bold = true, FontSizePoints = 12 };
        var props2 = new RunProperties { Bold = true, FontSizePoints = 12 };

        await Assert.That(props1).IsEqualTo(props2);
    }

    [Test]
    public async Task RunProperties_DifferentValues_AreNotEqual()
    {
        var props1 = new RunProperties { Bold = true };
        var props2 = new RunProperties { Bold = false };

        await Assert.That(props1).IsNotEqualTo(props2);
    }
}
