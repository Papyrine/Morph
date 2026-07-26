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

    // Advance widths (design units) cross-checked against an independent cmap-format-4 + hmtx parse.
    [Test]
    public async Task Reads_glyph_advances_via_cmap_and_hmtx()
    {
        var aptos = Read("Aptos_400.ttf");
        await Assert.That(aptos.AdvanceUnits('A')).IsEqualTo(1207);
        await Assert.That(aptos.AdvanceUnits('M')).IsEqualTo(1618);
        await Assert.That(aptos.AdvanceUnits('i')).IsEqualTo(489);
        await Assert.That(aptos.AdvanceUnits(' ')).IsEqualTo(416);
        await Assert.That(aptos.AdvanceUnits('W')).IsEqualTo(1827);

        var calibri = Read("Calibri_400.ttf");
        await Assert.That(calibri.AdvanceUnits('A')).IsEqualTo(1185);
        await Assert.That(calibri.AdvanceUnits(' ')).IsEqualTo(463);
    }

    [Test]
    public async Task Unmapped_codepoint_falls_back_to_notdef_advance()
    {
        var aptos = Read("Aptos_400.ttf");
        // A codepoint above the BMP that a Latin font can't cover resolves to glyph 0 (.notdef) and
        // takes its advance — the tofu box's width — rather than throwing or measuring nothing.
        await Assert.That(aptos.AdvanceUnits(0x10FFFF)).IsEqualTo(aptos.AdvanceWidths[0]);
    }

    [Test]
    public async Task Raw_advance_width_matches_an_independent_reader()
    {
        await Assert.That(CanonicalTextMeasurer.MeasureWidthRawPoints(Read("Aptos_400.ttf"), "Hello", 11)).IsEqualTo(25.3677).Within(0.001);
        await Assert.That(CanonicalTextMeasurer.MeasureWidthRawPoints(Read("Calibri_400.ttf"), "Hello", 11)).IsEqualTo(23.1763).Within(0.001);
    }

    // src/page_counts.md advance model: ppem = round(size * 120/72), so 11pt and 10.5pt both lay out
    // at 18px (em 10.8pt) — which is why they wrap identically.
    [Test]
    public async Task Ppem_quantizes_at_120_dpi()
    {
        await Assert.That(CanonicalTextMeasurer.Ppem(11)).IsEqualTo(18);
        await Assert.That(CanonicalTextMeasurer.Ppem(10.5)).IsEqualTo(18);
        await Assert.That(CanonicalTextMeasurer.Ppem(12)).IsEqualTo(20);
    }

    [Test]
    public async Task Quantized_width_tracks_the_raw_width()
    {
        var aptos = Read("Aptos_400.ttf");
        var raw = CanonicalTextMeasurer.MeasureWidthRawPoints(aptos, "Hello", 11);
        var quantized = CanonicalTextMeasurer.MeasureWidthPoints(aptos, "Hello", 11);
        // Per-glyph pixel rounding keeps the 5-glyph total within a pixel or two of linear.
        await Assert.That(Math.Abs(quantized - raw) < 1.5).IsTrue();
    }

    [Test]
    public async Task Wraps_greedily_at_the_measure()
    {
        var aptos = Read("Aptos_400.ttf");
        const string text = "The quick brown fox jumps over the lazy dog again and again";
        var lines = CanonicalTextMeasurer.WrapLines(aptos, text, 11, 80);

        await Assert.That(lines.Count > 1).IsTrue();
        // Every multi-word line stays within the measure (a lone over-wide word may overflow).
        foreach (var line in lines.Where(_ => _.Contains(' ')))
        {
            await Assert.That(CanonicalTextMeasurer.MeasureWidthPoints(aptos, line, 11) <= 80).IsTrue();
        }

        // Wrapping only inserts breaks at existing spaces — the words are preserved.
        await Assert.That(string.Join(" ", lines)).IsEqualTo(text);
    }

    [Test]
    public async Task Reader_returns_null_for_non_font_bytes()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        await Assert.That(FontMetricsReader.Read(stream)).IsNull();
    }
}
