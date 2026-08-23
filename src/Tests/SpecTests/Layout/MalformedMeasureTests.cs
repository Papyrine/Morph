using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Pins the two independent bounds that keep a malformed measure from turning into unbounded work.
/// </summary>
/// <remarks>
/// <para>
/// Reported against 1.4.0 by an external fuzzer: a sub-1KB document whose single run declares
/// <c>w:sz="-2147483647"</c> and is followed by a tab with a <c>dot</c> leader. The size multiplies
/// every advance in the paragraph, so one character put the pen at roughly -5.7e8pt; the leader then
/// spanned the whole distance and the painters tiled it with 166 million dots. In PDF, where each
/// glyph appends to the content stream, that exhausts memory — the report was an OOM, and the
/// captured stack was <c>StringBuilder.ToString</c> under <c>XGraphicsPdfRenderer.GetContent</c>.
/// </para>
/// <para>
/// The fix is two layers, and they are tested separately because neither subsumes the other:
/// </para>
/// <para>
/// 1. <c>OoxmlUnits.FontSizeHalfPointsToPoints</c> rejects out-of-schema sizes at the parse, which
/// keeps the pen in the range the engine was written for.
/// </para>
/// <para>
/// 2. <c>LeaderTiling</c> clips the tiling to the page, which keeps the painters finite whatever the
/// pen does. This is NOT redundant: <c>w:spacing</c> (character spacing) reaches the same pen through
/// a different attribute, and it is schema-legal when negative, so it cannot be clamped away the way
/// an unsigned size can. Measured on the pre-fix build, a document differing from the report only in
/// using <c>w:spacing="-2147483647"</c> instead of <c>w:sz</c> also exhausted memory in PDF and
/// ImageSharp, and took SkiaSharp 35 seconds. Layer 2 alone brings that to ~50ms.
/// </para>
/// </remarks>
public class MalformedMeasureTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    /// <summary>
    /// The report's own document, end to end. The assertion is a budget rather than an exact size:
    /// what is being pinned is that the work is bounded, and pre-fix this did not complete at all.
    /// </summary>
    [Test]
    [Arguments("sz", "-2147483647")]
    [Arguments("sz", "2147483647")]
    [Arguments("sz", "4294967295")]
    [Arguments("spacing", "-2147483647")]
    [Arguments("spacing", "2147483647")]
    public async Task A_malformed_measure_before_a_dot_leader_stays_bounded(string attribute, string value)
    {
        using var stream = BuildLeaderDocument(attribute, value);

        var watch = Stopwatch.StartNew();
        var pdf = new WordDocument(stream).ExportToPdf(new() {FontDirectory = fontsDirectory});
        watch.Stop();

        // A one-paragraph page. 166 million dots was ~5GB of content stream before it died.
        await Assert.That(pdf.Length).IsLessThan(200_000);
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Layer 1, observed where it acts: an out-of-schema <c>w:sz</c> leaves the run at the inherited
    /// size rather than carrying the malformed one into layout.
    /// </summary>
    [Test]
    [Arguments("-2147483647")]
    [Arguments("0")]
    [Arguments("not-a-number")]
    public async Task An_out_of_schema_size_is_inherited_not_carried(string value)
    {
        using var stream = BuildLeaderDocument("sz", value);

        var document = new DocumentParser().Parse(stream);
        var run = document.Elements.OfType<ParagraphElement>().First().Runs[0];

        // 12pt is the built-in default this bare package inherits (no styles part, no docDefaults).
        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(12.0);
    }

    /// <summary>
    /// Layer 1's upper end. Word's own UI and file format stop at 1638pt, so a larger declared size
    /// renders AT that cap rather than being discarded — discarding it would silently shrink text a
    /// document legitimately asked to be enormous.
    /// </summary>
    [Test]
    public async Task An_oversized_size_is_capped_at_Words_own_maximum()
    {
        using var stream = BuildLeaderDocument("sz", "4294967295");

        var document = new DocumentParser().Parse(stream);
        var run = document.Elements.OfType<ParagraphElement>().First().Runs[0];

        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(OoxmlUnits.MaxFontSizePoints);
    }

    /// <summary>
    /// The same clamp closing a second, unreported crash found while probing the report: a
    /// non-numeric <c>w:sz</c> in <c>docDefaults</c>. Three of the six size-parse sites read it with a
    /// bare <c>double.Parse</c>, so anything non-numeric threw <see cref="FormatException"/> out of
    /// every entry point — measured pre-fix on all five outputs. The report's own document does not
    /// reach those sites: with no styles part it resolves through the one site that already used
    /// TryParse, which is why this needs its own fixture rather than another value on the one above.
    /// </summary>
    [Test]
    [Arguments("GARBAGE")]
    [Arguments("-2147483647")]
    [Arguments("")]
    public async Task A_malformed_docDefaults_size_falls_back_instead_of_throwing(string value)
    {
        using var stream = BuildDocDefaultsDocument(value);

        var document = new DocumentParser().Parse(stream);
        var run = document.Elements.OfType<ParagraphElement>().First().Runs[0];

        // docDefaults IS present, so an unusable w:sz lands on the spec's 10pt, exactly as an absent
        // one does — not on the 12pt built-in that a document with no docDefaults at all inherits.
        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(10.0);
    }

    /// <summary>
    /// Layer 2 in isolation, as arithmetic. The clip drops WHOLE tiles, so a leader that already fits
    /// on the page has to come back identical to the unclipped formula — that is what makes the bound
    /// invisible to correct documents and keeps the corpus baselines unmoved.
    /// </summary>
    [Test]
    public async Task Clipping_leaves_an_on_page_leader_exactly_where_it_was()
    {
        // 100pt of leader at 5pt pitch on a 595pt page: entirely on-page, nothing to clip.
        var found = LeaderTiling.TryGetRange(x: 72, width: 100, glyphWidth: 5, spacing: 5, pageWidth: 595, out var startX, out var count);

        await Assert.That(found).IsTrue();
        await Assert.That(startX).IsEqualTo(72);
        await Assert.That(count).IsEqualTo((int) Math.Floor((100 - 5) / 5.0) + 1);
    }

    /// <summary>
    /// Layer 2 on the shape that caused the report: a filler starting far off the left edge. Only the
    /// tail is on the page, and the surviving tiles stay on the grid anchored at the original X — the
    /// leader is not re-anchored to the page edge, which would shift every dot.
    /// </summary>
    [Test]
    public async Task Clipping_keeps_the_visible_tail_on_the_original_grid()
    {
        var found = LeaderTiling.TryGetRange(x: -570_425_300, width: 570_425_600, glyphWidth: 4, spacing: 4, pageWidth: 595, out var startX, out var count);

        await Assert.That(found).IsTrue();
        // On the grid: the offset from the original X is a whole number of tiles.
        await Assert.That((startX + 570_425_300) % 4).IsEqualTo(0);
        // The first surviving tile is the first one at or after the left page edge.
        await Assert.That(startX).IsGreaterThanOrEqualTo(0);
        await Assert.That(startX).IsLessThan(4);
        // Bounded by the page, not by the declared width.
        await Assert.That(count).IsLessThanOrEqualTo((int) (595 / 4.0) + 1);
    }

    /// <summary>A filler wholly off the page, or carrying a non-finite measure, draws nothing.</summary>
    [Test]
    public async Task A_leader_entirely_off_the_page_draws_nothing()
    {
        var offLeft = LeaderTiling.TryGetRange(-1000, 500, 4, 4, 595, out _, out _);
        var offRight = LeaderTiling.TryGetRange(900, 500, 4, 4, 595, out _, out _);
        var degenerate = LeaderTiling.TryGetRange(0, double.NaN, 4, 4, 595, out _, out _);

        await Assert.That(offLeft).IsFalse();
        await Assert.That(offRight).IsFalse();
        await Assert.That(degenerate).IsFalse();
    }

    /// <summary>
    /// The report's package, rebuilt: one run carrying the measure under test, then a tab into a
    /// right-aligned stop with a dot leader.
    /// </summary>
    static MemoryStream BuildLeaderDocument(string attribute, string value)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var runProperties = new W.RunProperties();
            if (attribute == "sz")
            {
                runProperties.Append(new W.FontSize {Val = value});
            }
            else
            {
                runProperties.Append(new W.Spacing {Val = int.Parse(value, CultureInfo.InvariantCulture)});
            }

            var paragraph = new W.Paragraph(
                new W.ParagraphProperties(
                    new W.Tabs(
                        new W.TabStop
                        {
                            Val = W.TabStopValues.Right,
                            Position = 5000,
                            Leader = W.TabStopLeaderCharValues.Dot
                        })),
                new W.Run(runProperties, new W.Text("a")),
                new W.Run(new W.TabChar()));

            document.AddMainDocumentPart().Document = [with(new W.Body(paragraph))];
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>A package whose <c>docDefaults</c> declares the size under test.</summary>
    static MemoryStream BuildDocDefaultsDocument(string value)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = [with(new W.Body(new W.Paragraph(new W.Run(new W.Text("a")))))];

            var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles =
            [
                with(new W.DocDefaults(
                    new W.RunPropertiesDefault(
                        new W.RunPropertiesBaseStyle(
                            new W.FontSize {Val = value}))))
            ];
        }

        stream.Position = 0;
        return stream;
    }
}
