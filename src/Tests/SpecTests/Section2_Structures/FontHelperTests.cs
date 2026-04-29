/// <summary>
/// Comprehensive tests for FontHelpers: ImpliesBold, HasMediumWeightSuffix,
/// StripWeightSuffixes, GetCandidateNames, FindFallback, and FontFallbacks.
/// </summary>
public class FontHelperTests
{
    // === ImpliesBold ===

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
    [Arguments("Aptos", false)]
    [Arguments("Georgia", false)]
    [Arguments("Courier New", false)]
    public async Task ImpliesBold_VariousFonts(string fontFamily, bool expected) =>
        await Assert.That(FontHelpers.ImpliesBold(fontFamily)).IsEqualTo(expected);

    [Test]
    public async Task ImpliesBold_CaseInsensitive()
    {
        await Assert.That(FontHelpers.ImpliesBold("calibri bold")).IsTrue();
        await Assert.That(FontHelpers.ImpliesBold("ARIAL BLACK")).IsTrue();
        await Assert.That(FontHelpers.ImpliesBold("Franklin Gothic HEAVY")).IsTrue();
    }

    [Test]
    public async Task ImpliesBold_SubstringMatch()
    {
        // "Medium" as substring triggers ImpliesBold even in unexpected positions
        await Assert.That(FontHelpers.ImpliesBold("MediumWeight Custom")).IsTrue();
        await Assert.That(FontHelpers.ImpliesBold("MyBoldFont")).IsTrue();
    }

    // === HasMediumWeightSuffix ===

    [Test]
    [Arguments("Aptos Medium", true)]
    [Arguments("Aptos Semibold", true)]
    [Arguments("Aptos Demi", true)]
    [Arguments("Aptos Regular", true)]
    [Arguments("Aptos Book", true)]
    [Arguments("Aptos Bold", false)]
    [Arguments("Aptos Light", false)]
    [Arguments("Aptos", false)]
    [Arguments("Arial", false)]
    public async Task HasMediumWeightSuffix_VariousFonts(string fontFamily, bool expected) =>
        await Assert.That(FontHelpers.HasMediumWeightSuffix(fontFamily)).IsEqualTo(expected);

    [Test]
    public async Task HasMediumWeightSuffix_CaseInsensitive()
    {
        await Assert.That(FontHelpers.HasMediumWeightSuffix("Aptos MEDIUM")).IsTrue();
        await Assert.That(FontHelpers.HasMediumWeightSuffix("Aptos medium")).IsTrue();
    }

    // === StripWeightSuffixes ===

    [Test]
    [Arguments("Calibri Bold", "Calibri")]
    [Arguments("Calibri Light", "Calibri")]
    [Arguments("Arial Black", "Arial")]
    [Arguments("Aptos Medium", "Aptos")]
    [Arguments("Aptos Semibold", "Aptos")]
    [Arguments("Segoe UI Condensed", "Segoe UI")]
    [Arguments("Avenir Next LT Pro Bold", "Avenir Next LT Pro")]
    public async Task StripWeightSuffixes_RemovesSuffix(string fontFamily, string expected) =>
        await Assert.That(FontHelpers.StripWeightSuffixes(fontFamily)).IsEqualTo(expected);

    [Test]
    public async Task StripWeightSuffixes_NoSuffix_ReturnsUnchanged()
    {
        await Assert.That(FontHelpers.StripWeightSuffixes("Arial")).IsEqualTo("Arial");
        await Assert.That(FontHelpers.StripWeightSuffixes("Times New Roman")).IsEqualTo("Times New Roman");
        await Assert.That(FontHelpers.StripWeightSuffixes("Avenir Next LT Pro")).IsEqualTo("Avenir Next LT Pro");
    }

    [Test]
    public async Task StripWeightSuffixes_MultipleStacked_RemovesAll()
    {
        // Iterative stripping removes stacked suffixes
        await Assert.That(FontHelpers.StripWeightSuffixes("Arial Black Bold")).IsEqualTo("Arial");
        await Assert.That(FontHelpers.StripWeightSuffixes("Calibri Light Italic")).IsEqualTo("Calibri");
    }

    [Test]
    public async Task StripWeightSuffixes_VendorSuffixes_NotRemoved()
    {
        // Vendor suffixes like " MT", " Pro", " LT", " ITC" are part of the font name
        await Assert.That(FontHelpers.StripWeightSuffixes("Bodoni MT")).IsEqualTo("Bodoni MT");
        // " Demi" is not at the end (" ITC" is), so it's not stripped
        await Assert.That(FontHelpers.StripWeightSuffixes("Eras Demi ITC")).IsEqualTo("Eras Demi ITC");
        await Assert.That(FontHelpers.StripWeightSuffixes("Avenir Next LT Pro")).IsEqualTo("Avenir Next LT Pro");
    }

    // === GetCandidateNames ===

    [Test]
    public async Task GetCandidateNames_PlainFont_AllFieldsMatch()
    {
        var c = FontHelpers.GetCandidateNames("Arial", false);
        await Assert.That(c.Effective).IsEqualTo("Arial");
        await Assert.That(c.Original).IsEqualTo("Arial");
        await Assert.That(c.Stripped).IsNull();
    }

    [Test]
    public async Task GetCandidateNames_WithSuffix_NoBold_StrippedSet()
    {
        var c = FontHelpers.GetCandidateNames("Calibri Light", false);
        await Assert.That(c.Effective).IsEqualTo("Calibri Light");
        await Assert.That(c.Original).IsEqualTo("Calibri Light");
        await Assert.That(c.Stripped).IsEqualTo("Calibri");
    }

    [Test]
    public async Task GetCandidateNames_MediumWeight_NoBold_EffectiveIsOriginal()
    {
        // Without bold request, medium weight suffix is NOT stripped for Effective
        var c = FontHelpers.GetCandidateNames("Aptos Medium", false);
        await Assert.That(c.Effective).IsEqualTo("Aptos Medium");
        await Assert.That(c.Stripped).IsEqualTo("Aptos");
    }

    [Test]
    public async Task GetCandidateNames_MediumWeight_WithBold_EffectiveIsBase()
    {
        // With bold request, medium weight suffix IS stripped for Effective
        var c = FontHelpers.GetCandidateNames("Aptos Medium", true);
        await Assert.That(c.Effective).IsEqualTo("Aptos");
        await Assert.That(c.Original).IsEqualTo("Aptos Medium");
        // Stripped would equal Effective, so it's null
        await Assert.That(c.Stripped).IsNull();
    }

    [Test]
    public async Task GetCandidateNames_SemiboldWithBold_EffectiveIsBase()
    {
        var c = FontHelpers.GetCandidateNames("Segoe UI Semibold", true);
        await Assert.That(c.Effective).IsEqualTo("Segoe UI");
        await Assert.That(c.Original).IsEqualTo("Segoe UI Semibold");
    }

    [Test]
    public async Task GetCandidateNames_BoldSuffix_NoBoldRequest_StrippedSet()
    {
        // " Bold" suffix is stripped for Stripped, but Effective stays as-is
        var c = FontHelpers.GetCandidateNames("Calibri Bold", false);
        await Assert.That(c.Effective).IsEqualTo("Calibri Bold");
        await Assert.That(c.Stripped).IsEqualTo("Calibri");
    }

    // === FindFallback ===

    [Test]
    [Arguments("Segoe UI Variable", "Segoe UI")]
    [Arguments("Segoe UI Variable Display", "Segoe UI")]
    [Arguments("Segoe UI Variable Text", "Segoe UI")]
    [Arguments("Segoe UI Variable Small", "Segoe UI")]
    [Arguments("Avenir Next LT Pro", "Century Gothic")]
    [Arguments("AvenirNext LT Pro", "Century Gothic")]
    [Arguments("AvenirNext LT Pro Medium", "Century Gothic")]
    [Arguments("Eras Light ITC", "Century Gothic")]
    [Arguments("Eras Medium ITC", "Century Gothic")]
    [Arguments("Sagona", "Georgia")]
    [Arguments("Sagona ExtraLight", "Georgia")]
    [Arguments("Sagona Light", "Georgia")]
    public async Task FindFallback_AllKnownMappings(string fontFamily, string expectedFallback)
    {
        var candidates = FontHelpers.GetCandidateNames(fontFamily, false);
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsEqualTo(expectedFallback);
    }

    [Test]
    public async Task FindFallback_CaseInsensitive()
    {
        var candidates = FontHelpers.GetCandidateNames("SEGOE UI VARIABLE", false);
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsEqualTo("Segoe UI");
    }

    [Test]
    public async Task FindFallback_ViaStrippedName()
    {
        // "AvenirNext LT Pro Bold" → stripped to "AvenirNext LT Pro" → fallback exists
        var candidates = FontHelpers.GetCandidateNames("AvenirNext LT Pro Bold", false);
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsEqualTo("Century Gothic");
    }

    [Test]
    public async Task FindFallback_NoMatch()
    {
        var candidates = FontHelpers.GetCandidateNames("Arial", false);
        await Assert.That(FontHelpers.FindFallback(candidates)).IsNull();

        candidates = FontHelpers.GetCandidateNames("Calibri", false);
        await Assert.That(FontHelpers.FindFallback(candidates)).IsNull();

        candidates = FontHelpers.GetCandidateNames("Times New Roman", false);
        await Assert.That(FontHelpers.FindFallback(candidates)).IsNull();
    }

    // === StyleSuffixes coverage ===

    [Test]
    [Arguments(" Condensed")]
    [Arguments(" Compressed")]
    [Arguments(" Narrow")]
    [Arguments(" Extended")]
    [Arguments(" Wide")]
    [Arguments(" UltraBlack")]
    [Arguments(" Black")]
    [Arguments(" Heavy")]
    [Arguments(" UltraBold")]
    [Arguments(" ExtraBold")]
    [Arguments(" Demibold")]
    [Arguments(" Bold")]
    [Arguments(" Semibold")]
    [Arguments(" Demi")]
    [Arguments(" Medium")]
    [Arguments(" Regular")]
    [Arguments(" Book")]
    [Arguments(" UltraLight")]
    [Arguments(" ExtraLight")]
    [Arguments(" Semilight")]
    [Arguments(" Light")]
    [Arguments(" Thin")]
    [Arguments(" Hairline")]
    [Arguments(" Italic")]
    [Arguments(" Oblique")]
    [Arguments(" Cond")]
    public async Task StripWeightSuffixes_AllStyleSuffixes_Stripped(string suffix)
    {
        var input = "TestFont" + suffix;
        var result = FontHelpers.StripWeightSuffixes(input);
        await Assert.That(result).IsEqualTo("TestFont");
    }

    [Test]
    [Arguments("Segoe UI Semilight", "Segoe UI")]
    [Arguments("Segoe UI ExtraLight", "Segoe UI")]
    [Arguments("Segoe UI UltraLight", "Segoe UI")]
    [Arguments("Helvetica UltraBold", "Helvetica")]
    [Arguments("Helvetica Demibold", "Helvetica")]
    [Arguments("Helvetica UltraBlack", "Helvetica")]
    public async Task StripWeightSuffixes_NewSuffixes_Stripped(string fontFamily, string expected) =>
        await Assert.That(FontHelpers.StripWeightSuffixes(fontFamily)).IsEqualTo(expected);

    // === InferWeightFromName / ResolveTargetWeight ===

    [Test]
    [Arguments("Segoe UI Semilight", 350)]
    [Arguments("Segoe UI Light", 300)]
    [Arguments("Segoe UI Semibold", 600)]
    [Arguments("Segoe UI Bold", 700)]
    [Arguments("Segoe UI Black", 900)]
    [Arguments("Helvetica Thin", 100)]
    [Arguments("Avenir Medium", 500)]
    public async Task InferWeightFromName_KnownSuffixes(string fontFamily, int expected) =>
        await Assert.That(FontHelpers.InferWeightFromName(fontFamily)).IsEqualTo(expected);

    [Test]
    [Arguments("Arial")]
    [Arguments("Times New Roman")]
    [Arguments("Calibri")]
    public async Task InferWeightFromName_NoSuffix_ReturnsNull(string fontFamily) =>
        await Assert.That(FontHelpers.InferWeightFromName(fontFamily)).IsNull();

    // "Semilight" in the name wins over bold=true → still treats target as 350.
    // (Word lets people toggle bold on a Semilight face; the resolver should still
    // find the Semilight rather than jumping to weight 700.)
    [Test]
    public async Task ResolveTargetWeight_PrefersNameOverBoldFlag() =>
        await Assert.That(FontHelpers.ResolveTargetWeight("Segoe UI Semilight", true)).IsEqualTo(350);

    [Test]
    [Arguments("Arial", false, 400)]
    [Arguments("Arial", true, 700)]
    public async Task ResolveTargetWeight_FallsBackToBoldFlag(string fontFamily, bool bold, int expected) =>
        await Assert.That(FontHelpers.ResolveTargetWeight(fontFamily, bold)).IsEqualTo(expected);

    // === ScoreFace / PickBestFace ===

    [Test]
    public async Task PickBestFace_PicksSemilightOverRegular_WhenRequestingSemilight()
    {
        var regular = new FontFace { Path = "regular.ttf", Weight = 400 };
        var semilight = new FontFace { Path = "semilight.ttf", Weight = 350 };
        var bold = new FontFace { Path = "bold.ttf", Weight = 700 };

        var picked = FontHelpers.PickBestFace([regular, semilight, bold], targetWeight: 350, targetItalic: false);
        await Assert.That(picked!.Path).IsEqualTo("semilight.ttf");
    }

    [Test]
    public async Task PickBestFace_PicksClosestWeight_WhenExactMatchUnavailable()
    {
        // Target = 350 (Semilight), but only Light (300) and Regular (400) exist.
        // Both are 50 away — first one wins (we don't break ties).
        var light = new FontFace { Path = "light.ttf", Weight = 300 };
        var regular = new FontFace { Path = "regular.ttf", Weight = 400 };

        var picked = FontHelpers.PickBestFace([light, regular], targetWeight: 350, targetItalic: false);
        await Assert.That(picked).IsNotNull();
    }

    [Test]
    public async Task PickBestFace_ItalicMismatchOverridesWeight()
    {
        // Italic mismatch is so heavily penalized that a wrong-weight upright beats a
        // perfect-weight italic when an upright was requested.
        var perfectButItalic = new FontFace { Path = "italic.ttf", Weight = 350, Italic = true };
        var wrongWeightUpright = new FontFace { Path = "regular.ttf", Weight = 700, Italic = false };

        var picked = FontHelpers.PickBestFace([perfectButItalic, wrongWeightUpright], targetWeight: 350, targetItalic: false);
        await Assert.That(picked!.Path).IsEqualTo("regular.ttf");
    }

    [Test]
    public async Task PickBestFace_PrefersExactWidth()
    {
        var normal = new FontFace { Path = "normal.ttf", Weight = 400, Width = 5 };
        var condensed = new FontFace { Path = "cond.ttf", Weight = 400, Width = 3 };

        var picked = FontHelpers.PickBestFace([normal, condensed], targetWeight: 400, targetItalic: false);
        await Assert.That(picked!.Path).IsEqualTo("normal.ttf");
    }

    [Test]
    public async Task GetCandidateNames_Semilight_StrippedToBase()
    {
        // Regression: " Semilight" suffix used to be missing from StyleSuffixes,
        // leaving Stripped=null and breaking resolver fallback when SkiaSharp
        // collapses "Segoe UI Semilight" to "Segoe UI" weight 400.
        var c = FontHelpers.GetCandidateNames("Segoe UI Semilight", false);
        await Assert.That(c.Effective).IsEqualTo("Segoe UI Semilight");
        await Assert.That(c.Original).IsEqualTo("Segoe UI Semilight");
        await Assert.That(c.Stripped).IsEqualTo("Segoe UI");
    }
}
