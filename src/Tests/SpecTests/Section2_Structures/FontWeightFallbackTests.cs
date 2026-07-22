extern alias Skia;

/// <summary>
/// Pins <c>FontResolver.weightFallbackThreshold</c>: when the requested family resolves only to a
/// face far from the requested weight, a configured fallback offering a closer weight wins instead.
///
/// <c>src/Fonts</c> carries Daytona Bold and no other Daytona face, which makes it the only
/// "family present, weight absent" example available. No scenario exercises it any more —
/// <c>business-plans</c> 01/07/08 were re-pointed at Calibri once it emerged that their Word
/// references had been rendered against a Daytona family Office no longer supplies — so without
/// these tests the rule would have no coverage at all.
/// </summary>
public class FontWeightFallbackTests
{
    [Test]
    public async Task OnlyABoldDaytonaIsBundled()
    {
        // The premise the other two tests rest on. If a lighter Daytona is ever added, they change
        // meaning rather than fail, so assert the premise directly.
        var faces = Directory.GetFiles(ProjectFonts.Directory, "Daytona*")
            .Select(_ => Path.GetFileName(_))
            .Order();
        await Assert.That(faces).IsEquivalentTo(["Daytona_700.ttf"]);
    }

    [Test]
    public async Task LightRequestPrefersACloserWeightFallbackOverTheWrongWeightFamily()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface("Daytona Light", false, false);

        // Daytona Bold sits 400 from the requested 300, past the threshold, and FontFallbacks maps
        // "Daytona Light" to Calibri Light — an exact hit, so it takes the request.
        await Assert.That(typeface.FamilyName).StartsWith("Calibri");
        await Assert.That(typeface.FontStyle.Weight).IsEqualTo(300);
    }

    [Test]
    public async Task BoldRequestKeepsTheFamilyWhenTheWeightMatches()
    {
        using var context = CreateContext();
        using var typeface = context.GetTypeface("Daytona", true, false);

        // Nothing to improve on here, so the fallback must not fire and divert a correct match.
        await Assert.That(typeface.FamilyName).IsEqualTo("Daytona");
        await Assert.That(typeface.FontStyle.Weight).IsEqualTo(FontHelpers.BoldWeight);
    }

    static SkiaRenderContext CreateContext() =>
        new(new(), 96, fontDirectory: ProjectFonts.Directory);
}
