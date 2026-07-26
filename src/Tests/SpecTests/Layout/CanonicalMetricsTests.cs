/// <summary>
/// Validates the backend-independent metric source for the layout engine
/// (<c>docs/layout-engine-proposal.md</c>, step 1): <see cref="FontMetricsReader"/> reading a font's
/// own <c>head</c>/<c>hhea</c> tables, and <see cref="CanonicalTextMeasurer"/> turning them into line
/// heights. The line-pitch assertions pin the reader against Word's XPS-measured numbers recorded in
/// <c>src/page_counts.md</c> ("Height model") — so the canonical model is unit-tested directly, not
/// only via full-suite pixel comparison.
/// </summary>
public class CanonicalMetricsTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static FontMetrics Read(string file) =>
        FontMetricsReader.Read(Path.Combine(fontsDirectory, file))
        ?? throw new InvalidOperationException($"Could not read metrics from {file}");

    [Test]
    public async Task Reads_the_fonts_own_hhea_and_head()
    {
        var metrics = Read("Aptos_400.ttf");
        await Assert.That(metrics.UnitsPerEm).IsEqualTo(2048);
        await Assert.That(metrics.Ascender).IsEqualTo(1923);
        await Assert.That(metrics.Descender).IsEqualTo(-577);
        await Assert.That(metrics.LineGap).IsEqualTo(0);
        // Line box = ascender - descender + lineGap = 1923 + 577 + 0.
        await Assert.That(metrics.LineBoxUnits).IsEqualTo(2500);
    }

    // src/page_counts.md, Height model: "Aptos 12pt single = 14.65pt, Calibri 10.8pt single = 13.18pt"
    // — the XPS-measured Word line pitch. Reading hhea + head reproduces both to the recorded precision.
    [Test]
    public async Task Aptos_12pt_pitch_matches_Word_XPS() =>
        await Assert.That(Read("Aptos_400.ttf").LinePitchPoints(12)).IsEqualTo(14.65).Within(0.01);

    [Test]
    public async Task Calibri_10point8_pitch_matches_Word_XPS() =>
        await Assert.That(Read("Calibri_400.ttf").LinePitchPoints(10.8)).IsEqualTo(13.18).Within(0.01);

    [Test]
    public async Task Pitch_scales_linearly_with_size()
    {
        var metrics = Read("Aptos_400.ttf");
        await Assert.That(metrics.LinePitchPoints(24)).IsEqualTo(metrics.LinePitchPoints(12) * 2).Within(1e-9);
    }

    [Test]
    public async Task LineHeight_applies_words_spacing_rules()
    {
        var metrics = Read("Aptos_400.ttf");
        var single = metrics.LinePitchPoints(12); // ~14.648

        // Auto: single-spaced by default, scaled by the multiplier.
        await Assert.That(CanonicalTextMeasurer.LineHeightPoints(metrics, 12)).IsEqualTo(single).Within(1e-9);
        await Assert.That(CanonicalTextMeasurer.LineHeightPoints(metrics, 12, LineSpacingRule.Auto, 1.5)).IsEqualTo(single * 1.5).Within(1e-9);

        // Exactly: the explicit value wins outright.
        await Assert.That(CanonicalTextMeasurer.LineHeightPoints(metrics, 12, LineSpacingRule.Exactly, explicitPoints: 20)).IsEqualTo(20).Within(1e-9);

        // AtLeast: max(pitch, value) — value when larger, pitch when smaller.
        await Assert.That(CanonicalTextMeasurer.LineHeightPoints(metrics, 12, LineSpacingRule.AtLeast, explicitPoints: 20)).IsEqualTo(20).Within(1e-9);
        await Assert.That(CanonicalTextMeasurer.LineHeightPoints(metrics, 12, LineSpacingRule.AtLeast, explicitPoints: 10)).IsEqualTo(single).Within(1e-9);
    }

    [Test]
    public async Task Reader_returns_null_for_non_font_bytes()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        await Assert.That(FontMetricsReader.Read(stream)).IsNull();
    }
}
