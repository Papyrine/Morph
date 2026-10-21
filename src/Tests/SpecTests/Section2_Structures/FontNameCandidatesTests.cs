/// <summary>
/// Tests for the shared FontHelpers.GetCandidateNames and FindFallback methods
/// that compute font name resolution candidates across both rendering backends.
/// </summary>
public class FontNameCandidatesTests
{
    [Test]
    public async Task GetCandidateNames_PlainFont_HasNoStrippedName()
    {
        var candidates = FontHelpers.GetCandidateNames("Arial");
        await Assert.That(candidates.Original).IsEqualTo("Arial");
        await Assert.That(candidates.Stripped).IsNull();
    }

    [Test]
    public async Task GetCandidateNames_BoldSuffix_StrippedIsBaseName()
    {
        var candidates = FontHelpers.GetCandidateNames("Avenir Next LT Pro Bold");
        await Assert.That(candidates.Original).IsEqualTo("Avenir Next LT Pro Bold");
        await Assert.That(candidates.Stripped).IsEqualTo("Avenir Next LT Pro");
    }

    [Test]
    public async Task GetCandidateNames_LightSuffix_StrippedIsBaseName()
    {
        var candidates = FontHelpers.GetCandidateNames("Calibri Light");
        await Assert.That(candidates.Original).IsEqualTo("Calibri Light");
        await Assert.That(candidates.Stripped).IsEqualTo("Calibri");
    }

    [Test]
    public async Task GetCandidateNames_MediumSuffix_NamedFamilyThenBase()
    {
        // The family as named comes first whether or not the run is bold; the base name is the fallback.
        var candidates = FontHelpers.GetCandidateNames("Aptos Medium");
        await Assert.That(candidates.Original).IsEqualTo("Aptos Medium");
        await Assert.That(candidates.Stripped).IsEqualTo("Aptos");
    }

    [Test]
    public async Task FindFallback_DirectMatch_ReturnsFallback()
    {
        var candidates = FontHelpers.GetCandidateNames("Avenir Next LT Pro");
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsEqualTo("Century Gothic");
    }

    [Test]
    public async Task FindFallback_MatchViaStrippedName_ReturnsFallback()
    {
        // "AvenirNext LT Pro Bold" → stripped to "AvenirNext LT Pro" → has fallback
        var candidates = FontHelpers.GetCandidateNames("AvenirNext LT Pro Bold");
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsEqualTo("Calibri");
    }

    [Test]
    public async Task FindFallback_NoMatch_ReturnsNull()
    {
        var candidates = FontHelpers.GetCandidateNames("Arial");
        var fallback = FontHelpers.FindFallback(candidates);
        await Assert.That(fallback).IsNull();
    }
}
