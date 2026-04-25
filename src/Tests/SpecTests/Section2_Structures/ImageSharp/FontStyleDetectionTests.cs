extern alias ImageSharp;

/// <summary>
/// Tests for ImageSharp font style detection: italic verification,
/// bold/italic style matching, font cache merging, and fallback resolution.
/// </summary>
public class ImageSharpFontStyleDetectionTests
{
    static ImageSharpRenderContext CreateContext() =>
        new(new(), 96);

    // === Italic detection ===

    [Test]
    public async Task GetFont_Italic_CreatesItalicFont()
    {
        using var context = CreateContext();
        var props = new RunProperties
        {
            FontFamily = "Calibri",
            FontSizePoints = 12,
            Italic = true
        };
        var font = context.GetFont(props);
        await Assert.That(font.IsItalic).IsTrue();
    }

    [Test]
    public async Task GetFont_BoldItalic_CreatesBoldItalicFont()
    {
        using var context = CreateContext();
        var props = new RunProperties
        {
            FontFamily = "Calibri",
            FontSizePoints = 12,
            Bold = true,
            Italic = true
        };
        var font = context.GetFont(props);
        await Assert.That(font.IsBold).IsTrue();
        await Assert.That(font.IsItalic).IsTrue();
    }

    [Test]
    public async Task GetFont_Regular_CreatesNonItalicFont()
    {
        using var context = CreateContext();
        var props = new RunProperties
        {
            FontFamily = "Calibri",
            FontSizePoints = 12
        };
        var font = context.GetFont(props);
        await Assert.That(font.IsItalic).IsFalse();
    }

    // === GetFontForFamily italic ===

    [Test]
    public async Task GetFontForFamily_Italic_CreatesItalicFont()
    {
        using var context = CreateContext();
        var font = context.GetFontForFamily("Calibri", 12f, false, true);
        await Assert.That(font.IsItalic).IsTrue();
    }

    [Test]
    public async Task GetFontForFamily_BoldItalic_CreatesBoldItalicFont()
    {
        using var context = CreateContext();
        var font = context.GetFontForFamily("Calibri", 12f, true, true);
        await Assert.That(font.IsBold).IsTrue();
        await Assert.That(font.IsItalic).IsTrue();
    }

    // === Default font ===

    [Test]
    public async Task GetFontFamily_DefaultFont_Resolves()
    {
        using var context = CreateContext();
        var family = context.GetFontFamily(DefaultFontSettings.DefaultFont, false, false);
        await Assert.That(family.Name).IsNotNull();
    }

    [Test]
    public async Task GetFontFamily_DefaultFont_Bold_Resolves()
    {
        using var context = CreateContext();
        var family = context.GetFontFamily(DefaultFontSettings.DefaultFont, true, false);
        await Assert.That(family.Name).IsNotNull();
    }

    // === Consistency: all styles resolve ===

    [Test]
    public async Task GetFontFamily_AllStyles_Resolve()
    {
        using var context = CreateContext();
        var regular = context.GetFontFamily("Calibri", false, false);
        var bold = context.GetFontFamily("Calibri", true, false);
        var italic = context.GetFontFamily("Calibri", false, true);
        var boldItalic = context.GetFontFamily("Calibri", true, true);

        await Assert.That(regular.Name).IsEqualTo("Calibri");
        await Assert.That(bold.Name).IsEqualTo("Calibri");
        await Assert.That(italic.Name).IsEqualTo("Calibri");
        await Assert.That(boldItalic.Name).IsEqualTo("Calibri");
    }

    // === Font size and subscript/superscript ===

    [Test]
    public async Task GetFont_SubscriptReducesSize()
    {
        using var context = CreateContext();
        var props = new RunProperties
        {
            FontFamily = "Calibri",
            FontSizePoints = 20,
            VerticalAlignment = VerticalRunAlignment.Subscript
        };
        var font = context.GetFont(props);
        await Assert.That(font.Size).IsEqualTo(20f * 0.58f);
    }

    [Test]
    public async Task GetFont_SuperscriptReducesSize()
    {
        using var context = CreateContext();
        var props = new RunProperties
        {
            FontFamily = "Calibri",
            FontSizePoints = 20,
            VerticalAlignment = VerticalRunAlignment.Superscript
        };
        var font = context.GetFont(props);
        await Assert.That(font.Size).IsEqualTo(20f * 0.58f);
    }

    // === ImpliesBold from font name ===

    [Test]
    public async Task GetFontForFamily_ImpliesBold_RequestsBoldStyle()
    {
        // When ImpliesBold is true, GetFontForFamily should request bold style
        // even when bold=false. Test with a font that has a bold variant.
        using var context = CreateContext();
        var font = context.GetFontForFamily("Calibri Bold", 12f, false, false);
        await Assert.That(font.IsBold).IsTrue();
    }

    // === Fallback delegate receives correct name ===

    [Test]
    public async Task GetFontFamily_FallbackDelegate_ReceivesOriginalName()
    {
        string? received = null;
        using var context = new ImageSharpRenderContext(
            new(), 96, fontFallback: name =>
            {
                received = name;
                return "Arial";
            });
        context.GetFontFamily("ZZZ_NonExistentFont_12345", false, false);
        await Assert.That(received).IsEqualTo("ZZZ_NonExistentFont_12345");
    }

    // === Common system fonts resolve ===

    [Test]
    [Arguments("Arial")]
    [Arguments("Calibri")]
    [Arguments("Times New Roman")]
    [Arguments("Courier New")]
    [Arguments("Georgia")]
    [Arguments("Verdana")]
    public async Task GetFontFamily_CommonSystemFonts_Resolve(string fontFamily)
    {
        using var context = CreateContext();
        var family = context.GetFontFamily(fontFamily, false, false);
        await Assert.That(family.Name).IsEqualTo(fontFamily);
    }
}
