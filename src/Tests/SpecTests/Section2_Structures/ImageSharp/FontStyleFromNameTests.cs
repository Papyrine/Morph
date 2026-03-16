extern alias ImageSharp;
using ImageSharpRenderContext = ImageSharp::RenderContext;

/// <summary>
/// Tests that font names implying bold (e.g. "Arial Black") cause
/// the font creation path to request bold style.
/// Regression test for a bug where ImpliesBold adjustments in
/// TryLoadFromFontCache were applied to a by-value parameter and lost.
/// </summary>
public class FontStyleFromNameTests
{
    static ImageSharpRenderContext CreateContext() =>
        new(new(), 96);

    [Test]
    [Arguments("Calibri Bold", true)]
    [Arguments("Arial Black", true)]
    [Arguments("Franklin Gothic Heavy", true)]
    [Arguments("Franklin Gothic Medium", true)]
    [Arguments("Eras Demi ITC", true)]
    [Arguments("Segoe UI Semibold", true)]
    [Arguments("Calibri", false)]
    [Arguments("Arial", false)]
    [Arguments("Times New Roman", false)]
    public async Task ImpliesBold_DetectsWeightFromFontName(string fontFamily, bool expected) =>
        await Assert.That(FontHelpers.ImpliesBold(fontFamily)).IsEqualTo(expected);

    [Test]
    public async Task GetFontForFamily_ExplicitBold_CreatesBoldFont()
    {
        using var context = CreateContext();
        var font = context.GetFontForFamily("Calibri", 12f, true, false);
        await Assert.That(font.IsBold).IsTrue();
    }

    [Test]
    public async Task GetFontForFamily_NoBold_CreatesRegularFont()
    {
        using var context = CreateContext();
        var font = context.GetFontForFamily("Calibri", 12f, false, false);
        await Assert.That(font.IsBold).IsFalse();
    }

    [Test]
    public async Task GetFont_ExplicitBold_CreatesBoldFont()
    {
        using var context = CreateContext();
        var props = new RunProperties
        {
            FontFamily = "Calibri",
            Bold = true,
            FontSizePoints = 12
        };
        var font = context.GetFont(props);
        await Assert.That(font.IsBold).IsTrue();
    }

    [Test]
    public async Task GetFont_NoBold_CreatesRegularFont()
    {
        using var context = CreateContext();
        var props = new RunProperties
        {
            FontFamily = "Calibri",
            Bold = false,
            FontSizePoints = 12
        };
        var font = context.GetFont(props);
        await Assert.That(font.IsBold).IsFalse();
    }

    [Test]
    [Arguments("Calibri Bold", "Calibri")]
    [Arguments("Arial Black", "Arial Black")]
    [Arguments("Avenir Next LT Pro Bold", "Avenir Next LT Pro")]
    [Arguments("Avenir Next LT Pro Light", "Avenir Next LT Pro")]
    [Arguments("Calibri Light", "Calibri Light")]
    public async Task GetFontFamily_StyleSuffixStripped_ResolvesBaseFamily(string fontFamily, string expectedFamily)
    {
        using var context = CreateContext();
        var family = context.GetFontFamily(fontFamily, false, false);
        await Assert.That(family.Name).IsEqualTo(expectedFamily);
    }

    [Test]
    [Arguments("AvenirNext LT Pro Bold", "Century Gothic")]
    [Arguments("AvenirNext LT Pro Light", "Century Gothic")]
    public async Task GetFontFamily_StrippedNameMatchesFallback_ResolvesFallback(string fontFamily, string expectedFamily)
    {
        // "AvenirNext LT Pro" (no space) has a fallback to "Century Gothic" in FontFallbacks.
        // Stripping " Bold"/" Light" should find the fallback.
        using var context = CreateContext();
        var family = context.GetFontFamily(fontFamily, false, false);
        await Assert.That(family.Name).IsEqualTo(expectedFamily);
    }
}
