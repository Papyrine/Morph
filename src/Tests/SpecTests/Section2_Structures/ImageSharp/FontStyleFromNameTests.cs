extern alias ImageSharp;

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

    static ImageSharpRenderContext CreateBundledFontContext() =>
        new(new(), 96, fontDirectory: ProjectFonts.Directory);

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
    [Arguments("Calibri Light", "Calibri Light")]
    public async Task GetFontFamily_StyleSuffixStripped_ResolvesBaseFamily(string fontFamily, string expectedFamily)
    {
        using var context = CreateContext();
        var family = context.GetFontFamily(fontFamily, false, false);
        await Assert.That(family.Name).IsEqualTo(expectedFamily);
    }

    [Test]
    [Arguments("Avenir Next LT Pro Bold", "Avenir Next LT Pro")]
    public async Task GetFontFamily_StyleSuffixStripped_ResolvesBaseFamily_FromBundle(string fontFamily, string expectedFamily)
    {
        // Avenir Next LT Pro isn't a default-installed Windows font, so this case is
        // exercised against the bundled fonts in src/Fonts.
        using var context = CreateBundledFontContext();
        var family = context.GetFontFamily(fontFamily, false, false);
        await Assert.That(family.Name).IsEqualTo(expectedFamily);
    }

    [Test]
    [Arguments("AvenirNext LT Pro Bold")]
    [Arguments("AvenirNext LT Pro Light")]
    public async Task GetFontFamily_NameMissingASpace_PrefersRepairedFamilyOverFallback(string fontFamily)
    {
        // "AvenirNext LT Pro" (no space) has a Century Gothic entry in FontFallbacks, for
        // environments that lack Avenir Next. Here the family IS bundled, and
        // FontFileCache.EnumerateCandidateNames restores the missing space as a last-resort
        // candidate — so the real family wins and the approximation never fires.
        // Uses the bundled font directory since neither family ships on Windows by default.
        using var context = CreateBundledFontContext();
        var family = context.GetFontFamily(fontFamily, false, false);
        await Assert.That(family.Name).IsEqualTo("Avenir Next LT Pro");
    }

    [Test]
    public async Task GetFontFamily_UnknownFont_NoFallback_Throws()
    {
        using var context = CreateContext();
        await Assert.That(() => context.GetFontFamily("NonExistentFont12345", false, false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetFontFamily_UnknownFont_ExceptionMessageIncludesSearchedPaths()
    {
        using var context = CreateContext();
        var ex = Assert.Throws<InvalidOperationException>(
            // ReSharper disable once AccessToDisposedClosure
            () => context.GetFontFamily("NonExistentFont12345", false, false));
        await Assert.That(ex.Message).Contains("NonExistentFont12345");
        foreach (var path in FontCacheLoader.GetSearchedPaths())
        {
            await Assert.That(ex.Message).Contains(path);
        }
    }

    [Test]
    public async Task GetFontFamily_UnknownFont_DelegateFallback_ResolvesToFallback()
    {
        using var context = new ImageSharpRenderContext(
            new(), 96, fontFallback: _ => "Arial");
        var family = context.GetFontFamily("NonExistentFont12345", false, false);
        await Assert.That(family.Name).IsEqualTo("Arial");
    }

    [Test]
    public async Task GetFontFamily_UnknownFont_DelegateReturnsNull_Throws()
    {
        using var context = new ImageSharpRenderContext(
            new(), 96, fontFallback: _ => null);
        await Assert.That(() => context.GetFontFamily("NonExistentFont12345", false, false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetFontFamily_UnknownFont_DelegateReceivesCorrectName()
    {
        string? receivedName = null;
        using var context = new ImageSharpRenderContext(
            new(), 96, fontFallback: name =>
            {
                receivedName = name;
                return "Arial";
            });
        context.GetFontFamily("NonExistentFont12345", false, false);
        await Assert.That(receivedName).IsEqualTo("NonExistentFont12345");
    }
}
