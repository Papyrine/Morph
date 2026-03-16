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
}
