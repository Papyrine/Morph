/// <summary>
/// <c>w:contextualSpacing</c> collapses the gap between consecutive paragraphs of the SAME style that
/// both opt in. Regression guard for the bug where the PDF path applied each paragraph's spacing
/// unconditionally, so a run of same-style lines (a Details block of Date / Time / Facilitator)
/// re-added the style's before-spacing on every line — opening a gap on each and, cumulatively,
/// overflowing onto an extra page. The scenario snapshots did not catch it: they had baked the buggy
/// output in as the baseline. This asserts the behaviour directly, in spacing units, so it stays
/// meaningful regardless of font rasterisation. The rule now lives once in the <see cref="Fragmenter"/>,
/// so the assertions read placed-line positions rather than a per-backend cursor.
/// </summary>
public class CanonicalContextualSpacingTests
{
    // 360 twips — the before-spacing the Details style in the agendas-minutes templates carries.
    const float spacingBeforePoints = 18;

    static readonly Fragmenter fragmenter = new(LayoutTestFonts.Measurer);

    static readonly PageSettings page = new()
    {
        WidthPoints = 612,
        HeightPoints = 792,
        MarginTop = 72,
        MarginBottom = 72,
        MarginLeft = 72,
        MarginRight = 72
    };

    static ParagraphElement Line(string text, bool contextual, string? styleId) =>
        new()
        {
            Runs = [new() {Text = text, Properties = new() {FontFamily = "Arial", FontSizePoints = 11}}],
            Properties = new()
            {
                StyleId = styleId,
                ContextualSpacing = contextual,
                SpacingBeforePoints = spacingBeforePoints,
                SpacingAfterPoints = 0
            }
        };

    // The tops of the placed lines, in flow order — one per single-line paragraph.
    static List<float> LineTops(params ParagraphElement[] paragraphs)
    {
        var document = fragmenter.Layout(paragraphs, page);
        return document.Pages
            .SelectMany(_ => _.Items)
            .OfType<PlacedLine>()
            .Select(_ => _.Y)
            .ToList();
    }

    [Test]
    public async Task SameStyleContextual_CollapsesBeforeSpacing()
    {
        var tops = LineTops(
            Line("Date: 1/9/23", contextual: true, styleId: "Details"),
            Line("Time: 9:00 AM", contextual: true, styleId: "Details"),
            Line("Facilitator: Ann", contextual: true, styleId: "Details"));

        // The first line keeps its leading before-spacing (the document start); each same-style
        // contextual sibling collapses it, so the later advances are shorter by exactly that spacing.
        var firstAdvance = tops[1] - tops[0];
        var secondAdvance = tops[2] - tops[1];
        await Assert.That(firstAdvance).IsEqualTo(secondAdvance).Within(0.5f);
        await Assert.That(tops[0]).IsEqualTo((float) page.MarginTop + spacingBeforePoints).Within(0.5f);
    }

    [Test]
    public async Task NonContextual_KeepsBeforeSpacingOnEveryLine()
    {
        var contextualTops = LineTops(
            Line("Date: 1/9/23", contextual: true, styleId: "Details"),
            Line("Time: 9:00 AM", contextual: true, styleId: "Details"));
        var plainTops = LineTops(
            Line("Date: 1/9/23", contextual: false, styleId: "Details"),
            Line("Time: 9:00 AM", contextual: false, styleId: "Details"));

        // Without contextual spacing the second line still carries the before-spacing, so its advance
        // exceeds the collapsed one by exactly that spacing.
        var collapsed = contextualTops[1] - contextualTops[0];
        var kept = plainTops[1] - plainTops[0];
        await Assert.That(kept - collapsed).IsEqualTo(spacingBeforePoints).Within(0.5f);
    }

    [Test]
    public async Task ContextualButDifferentStyle_DoesNotCollapse()
    {
        var sameStyle = LineTops(
            Line("Heading", contextual: true, styleId: "Details"),
            Line("Body", contextual: true, styleId: "Details"));
        var differentStyle = LineTops(
            Line("Heading", contextual: true, styleId: "Heading1"),
            Line("Body", contextual: true, styleId: "Details"));

        // Contextual spacing only collapses between paragraphs of the SAME style, so a style change
        // keeps the before-spacing on the second line.
        await Assert.That(differentStyle[1] - differentStyle[0] - (sameStyle[1] - sameStyle[0]))
            .IsEqualTo(spacingBeforePoints).Within(0.5f);
    }
}
