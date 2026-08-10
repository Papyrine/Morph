/// <summary>
/// Tests for w:pgSz/@w:code — the Windows printer paper code (DMPAPER_*) — taking priority over
/// the w:w/w:h twips beside it. The twips are a rounded copy of a paper whose exact size the code
/// names, and Word renders the exact one.
/// </summary>
public class PaperCodePageSizeTests
{
    static ParsedDocument Parse(params string[] scenario)
    {
        var segments = new[] {ProjectFiles.ProjectDirectory, "Inputs", "word"}
            .Concat(scenario)
            .Append("input.docx")
            .ToArray();
        return new DocumentParser().Parse(Path.Combine(segments));
    }

    /// <summary>
    /// The scenario declares w:h=16840 (842.0pt) alongside w:code="9". Word renders A4's real
    /// 841.89pt, which is a page one pixel shorter at 150 DPI — enough to null the scenario's
    /// SSIM, since <c>PageComparison</c> only scores structure when the dimensions agree.
    /// </summary>
    [Test]
    public async Task RoundedTwips_SnapToTheCodesExactPaper()
    {
        var settings = Parse("nonstandard_main_part_name").PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(595.2756).Within(0.0001);
        await Assert.That(settings.HeightPoints).IsEqualTo(841.8898).Within(0.0001);
    }

    /// <summary>
    /// A code whose paper the declared size does not match is stale and must be ignored: cards/03
    /// is a 7x5in card still carrying code 23, a 5x11.5in envelope, from whatever it was branched
    /// off. Honouring that unconditionally would resize the card to an envelope.
    /// </summary>
    [Test]
    public async Task StaleCode_LeavesTheDeclaredSizeAlone()
    {
        var settings = Parse("cards", "03").PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(504.05).Within(0.0001);
        await Assert.That(settings.HeightPoints).IsEqualTo(360.05).Within(0.0001);
    }

    /// <summary>
    /// An inch-defined paper is a whole number of points and so exactly representable in twips —
    /// the snap is a no-op there, in landscape as in portrait (Word writes w:w/w:h already
    /// swapped, so the code's portrait nominal has to be matched either way round).
    /// </summary>
    [Test]
    public async Task LandscapeLetter_IsUnchanged()
    {
        var settings = Parse("brochures", "01").PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(792);
        await Assert.That(settings.HeightPoints).IsEqualTo(612);
    }

    /// <summary>A document with no w:code at all keeps its declared twips verbatim.</summary>
    [Test]
    public async Task NoCode_KeepsDeclaredTwips()
    {
        var settings = Parse("page_a4").PageSettings;

        // 16838 twips = 841.9pt — deliberately NOT snapped to A4's 841.8898.
        await Assert.That(settings.WidthPoints).IsEqualTo(595.3).Within(0.0001);
        await Assert.That(settings.HeightPoints).IsEqualTo(841.9).Within(0.0001);
    }
}
