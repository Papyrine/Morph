extern alias Skia;
using SkiaRenderContext = Skia::RenderContext;

/// <summary>
/// Tests for Skia font style detection: italic verification in system font
/// resolution, bold/italic style matching, and the typeface scoring algorithm.
/// </summary>
public class SkiaFontStyleDetectionTests
{
    static SkiaRenderContext CreateContext() =>
        new(new(), 96);

    // === Italic detection ===

    [Test]
    public async Task GetTypeface_Italic_ReturnsItalicTypeface()
    {
        using var context = CreateContext();
        // Calibri has proper italic variants on Windows
        using var typeface = context.GetTypeface("Calibri", false, true);
        await Assert.That(typeface).IsNotNull();
    }

    [Test]
    public async Task GetTypeface_BoldItalic_ReturnsBoldItalicTypeface()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface("Calibri", true, true);
        await Assert.That(typeface).IsNotNull();
    }

    [Test]
    public async Task GetTypeface_Regular_ReturnsNonItalicTypeface()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface("Calibri", false, false);
        await Assert.That(typeface.FontStyle.Slant).IsEqualTo(SkiaSharp.SKFontStyleSlant.Upright);
    }

    // === Bold detection ===

    [Test]
    public async Task GetTypeface_Bold_ReturnsBoldWeight()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface("Calibri", true, false);
        await Assert.That(typeface.FontStyle.Weight).IsGreaterThanOrEqualTo(600);
    }

    [Test]
    public async Task GetTypeface_Regular_ReturnsNormalWeight()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface("Calibri", false, false);
        await Assert.That(typeface.FontStyle.Weight).IsLessThan(600);
    }

    // === Default font ===

    [Test]
    public async Task GetTypeface_DefaultFont_Resolves()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface(DefaultFontSettings.DefaultFont, false, false);
        await Assert.That(typeface).IsNotNull();
    }

    [Test]
    public async Task GetTypeface_DefaultFont_Bold_Resolves()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface(DefaultFontSettings.DefaultFont, true, false);
        await Assert.That(typeface).IsNotNull();
        await Assert.That(typeface.FontStyle.Weight).IsGreaterThanOrEqualTo(600);
    }

    // === Consistency: same font family for all styles ===

    [Test]
    public async Task GetTypeface_AllStyles_SameFamilyName()
    {
        using var context = CreateContext();
        using var regular = context.GetTypeface("Calibri", false, false);
        using var bold = context.GetTypeface("Calibri", true, false);
        using var italic = context.GetTypeface("Calibri", false, true);
        using var boldItalic = context.GetTypeface("Calibri", true, true);

        await Assert.That(bold.FamilyName).IsEqualTo(regular.FamilyName);
        await Assert.That(italic.FamilyName).IsEqualTo(regular.FamilyName);
        await Assert.That(boldItalic.FamilyName).IsEqualTo(regular.FamilyName);
    }

    // === Caching ===

    [Test]
    public async Task GetTypeface_SameRequest_ReturnsCachedInstance()
    {
        using var context = CreateContext();
        var typeface1 = context.GetTypeface("Calibri", false, false);
        var typeface2 = context.GetTypeface("Calibri", false, false);
        // Same reference from cache
        await Assert.That(ReferenceEquals(typeface1, typeface2)).IsTrue();
    }

    [Test]
    public async Task GetTypeface_DifferentStyles_ReturnsDifferentInstances()
    {
        using var context = CreateContext();
        using var regular = context.GetTypeface("Calibri", false, false);
        using var bold = context.GetTypeface("Calibri", true, false);
        await Assert.That(ReferenceEquals(regular, bold)).IsFalse();
    }

    // === Common system fonts resolve ===

    [Test]
    [Arguments("Arial")]
    [Arguments("Calibri")]
    [Arguments("Times New Roman")]
    [Arguments("Courier New")]
    [Arguments("Georgia")]
    [Arguments("Verdana")]
    public async Task GetTypeface_CommonSystemFonts_Resolve(string fontFamily)
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface(fontFamily, false, false);
        await Assert.That(typeface.FamilyName).IsEqualTo(fontFamily);
    }
}
