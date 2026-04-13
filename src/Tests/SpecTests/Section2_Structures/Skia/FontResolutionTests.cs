extern alias Skia;
using SkiaRenderContext = Skia::RenderContext;

/// <summary>
/// Tests that the Skia RenderContext resolves font families correctly
/// when the document references a font name with style suffixes
/// (e.g. "Avenir Next LT Pro Light" → "Avenir Next LT Pro").
/// </summary>
public class SkiaFontResolutionTests
{
    static SkiaRenderContext CreateContext() =>
        new(new(), 96);

    [Test]
    [Arguments("Calibri Bold", "Calibri")]
    [Arguments("Arial Black", "Arial")]
    [Arguments("Avenir Next LT Pro Bold", "Avenir Next LT Pro")]
    [Arguments("Avenir Next LT Pro Light", "Avenir Next LT Pro")]
    [Arguments("Calibri Light", "Calibri")]
    public async Task GetTypeface_StyleSuffixStripped_ResolvesBaseFamily(string fontFamily, string expectedFamily)
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface(fontFamily, false, false);
        await Assert.That(typeface.FamilyName).IsEqualTo(expectedFamily);
    }

    [Test]
    [Arguments("AvenirNext LT Pro Bold", "Century Gothic")]
    [Arguments("AvenirNext LT Pro Light", "Century Gothic")]
    public async Task GetTypeface_StrippedNameMatchesFallback_ResolvesFallback(string fontFamily, string expectedFamily)
    {
        // "AvenirNext LT Pro" (no space) has a fallback to "Century Gothic" in FontFallbacks.
        // Stripping " Bold"/" Light" should find the fallback.
        using var context = CreateContext();
        using var typeface = context.GetTypeface(fontFamily, false, false);
        await Assert.That(typeface.FamilyName).IsEqualTo(expectedFamily);
    }

    [Test]
    public async Task GetTypeface_UnknownFont_NoFallback_Throws()
    {
        using var context = CreateContext();
        await Assert.That(() => context.GetTypeface("NonExistentFont12345", false, false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetTypeface_UnknownFont_ExceptionMessageIncludesSearchedPaths()
    {
        using var context = CreateContext();
        var ex = Assert.Throws<InvalidOperationException>(
            () => context.GetTypeface("NonExistentFont12345", false, false));
        await Assert.That(ex!.Message).Contains("NonExistentFont12345");
        foreach (var path in FontCacheLoader.GetSearchedPaths())
        {
            await Assert.That(ex.Message).Contains(path);
        }
    }

    [Test]
    public async Task GetTypeface_UnknownFont_DelegateFallback_ResolvesToFallback()
    {
        using var context = new SkiaRenderContext(
            new(), 96, fontFallback: _ => "Arial");
        using var typeface = context.GetTypeface("NonExistentFont12345", false, false);
        await Assert.That(typeface.FamilyName).IsEqualTo("Arial");
    }

    [Test]
    public async Task GetTypeface_UnknownFont_DelegateReturnsNull_Throws()
    {
        using var context = new SkiaRenderContext(
            new(), 96, fontFallback: _ => null);
        await Assert.That(() => context.GetTypeface("NonExistentFont12345", false, false))
            .Throws<InvalidOperationException>();
    }
}
