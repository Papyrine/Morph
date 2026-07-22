extern alias Skia;

/// <summary>
/// Tests that the Skia RenderContext resolves font families correctly
/// when the document references a font name with style suffixes
/// (e.g. "Avenir Next LT Pro Light" → "Avenir Next LT Pro").
/// </summary>
public class SkiaFontResolutionTests
{
    static SkiaRenderContext CreateContext() =>
        new(new(), 96, fontDirectory: ProjectFonts.Directory);

    static SkiaRenderContext CreateSystemContext() =>
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
    [Arguments("AvenirNext LT Pro Bold")]
    [Arguments("AvenirNext LT Pro Light")]
    public async Task GetTypeface_NameMissingASpace_PrefersRepairedFamilyOverFallback(string fontFamily)
    {
        // "AvenirNext LT Pro" (no space) has a Century Gothic entry in FontFallbacks, for
        // environments that lack Avenir Next. Here the family IS bundled, and
        // FontFileCache.EnumerateCandidateNames restores the missing space as a last-resort
        // candidate — so the real family wins and the approximation never fires.
        // FindFallback still returns Century Gothic for these names, which
        // FontNameCandidatesTests pins; the resolver simply reaches the real family first.
        using var context = CreateContext();
        using var typeface = context.GetTypeface(fontFamily, false, false);
        await Assert.That(typeface.FamilyName).IsEqualTo("Avenir Next LT Pro");
    }

    [Test]
    public async Task GetTypeface_UnknownFont_NoFallback_Throws()
    {
        using var context = CreateContext();
        // ReSharper disable once AccessToDisposedClosure
        await Assert.That(() => context.GetTypeface("NonExistentFont12345", false, false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetTypeface_UnknownFont_ExceptionMessageIncludesSearchedPaths()
    {
        // Uses the system-font path (no FontDirectory) so the error message
        // enumerates system/user/Office/cloud caches.
        using var context = CreateSystemContext();
        var ex = Assert.Throws<InvalidOperationException>(
            // ReSharper disable once AccessToDisposedClosure
            () => context.GetTypeface("NonExistentFont12345", false, false));
        await Assert.That(ex.Message).Contains("NonExistentFont12345");
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
        // ReSharper disable once AccessToDisposedClosure
        await Assert.That(() => context.GetTypeface("NonExistentFont12345", false, false))
            .Throws<InvalidOperationException>();
    }
}
