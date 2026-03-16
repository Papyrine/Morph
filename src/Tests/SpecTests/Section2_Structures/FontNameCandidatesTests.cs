/// <summary>
/// Tests for the shared FontHelpers.GetCandidateNames and FindFallback methods
/// that compute font name resolution candidates across both rendering backends.
/// </summary>
public class FontNameCandidatesTests
{
    [Test]
    public async Task GetCandidateNames_PlainFont_EffectiveEqualsOriginal()
    {
        var candidates = FontHelpers.GetCandidateNames("Arial", false);
        await Assert.That(candidates.Effective).IsEqualTo("Arial");
        await Assert.That(candidates.Original).IsEqualTo("Arial");
        await Assert.That(candidates.Stripped).IsNull();
    }

    [Test]
    public async Task GetCandidateNames_BoldSuffix_StrippedIsBaseName()
    {
        var candidates = FontHelpers.GetCandidateNames("Avenir Next LT Pro Bold", false);
        await Assert.That(candidates.Effective).IsEqualTo("Avenir Next LT Pro Bold");
        await Assert.That(candidates.Stripped).IsEqualTo("Avenir Next LT Pro");
    }

    [Test]
    public async Task GetCandidateNames_LightSuffix_StrippedIsBaseName()
    {
        var candidates = FontHelpers.GetCandidateNames("Calibri Light", false);
        await Assert.That(candidates.Effective).IsEqualTo("Calibri Light");
        await Assert.That(candidates.Stripped).IsEqualTo("Calibri");
    }

    [Test]
    public async Task GetCandidateNames_BoldWithMediumSuffix_EffectiveIsStripped()
    {
        // bold=true + medium weight suffix → effective is the base name
        var candidates = FontHelpers.GetCandidateNames("Aptos Medium", true);
        await Assert.That(candidates.Effective).IsEqualTo("Aptos");
        await Assert.That(candidates.Original).IsEqualTo("Aptos Medium");
        // Stripped equals effective, so it should be null to avoid redundancy
        await Assert.That(candidates.Stripped).IsNull();
    }

    [Test]
    public async Task FindFallback_DirectMatch_ReturnsFallback()
    {
        var candidates = FontHelpers.GetCandidateNames("Avenir Next LT Pro", false);
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsEqualTo("Century Gothic");
    }

    [Test]
    public async Task FindFallback_MatchViaStrippedName_ReturnsFallback()
    {
        // "AvenirNext LT Pro Bold" → stripped to "AvenirNext LT Pro" → has fallback
        var candidates = FontHelpers.GetCandidateNames("AvenirNext LT Pro Bold", false);
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsEqualTo("Century Gothic");
    }

    [Test]
    public async Task FindFallback_NoMatch_ReturnsNull()
    {
        var candidates = FontHelpers.GetCandidateNames("Arial", false);
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsNull();
    }
}
