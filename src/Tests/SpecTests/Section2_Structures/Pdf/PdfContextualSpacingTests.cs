/// <summary>
/// Tests that the PDF backend honours <c>w:contextualSpacing</c>: a paragraph's before-spacing is
/// collapsed between consecutive paragraphs of the SAME style that both opt in, and a contextual
/// paragraph suppresses its own after-spacing.
///
/// Regression guard for the bug where <see cref="PdfTextEngine"/> applied each paragraph's spacing
/// unconditionally, so a run of same-style lines (e.g. a Details block of Date / Time / Facilitator)
/// re-added the style's before-spacing on every line — opening a gap on each one and, cumulatively,
/// overflowing onto an extra page. The scenario snapshots did not catch this: they had baked the
/// buggy output in as the baseline. This asserts the intended behaviour directly, in spacing units,
/// so it stays meaningful regardless of font rasterisation.
/// </summary>
public class PdfContextualSpacingTests
{
    // 360 twips — the before-spacing the Details style in the agendas-minutes templates carries.
    const float spacingBeforePoints = 18;

    static (PdfRenderContext context, PdfTextEngine engine) CreateEngine()
    {
        var pageSettings = new PageSettings
        {
            WidthPoints = 612,
            HeightPoints = 792,
            MarginTop = 72,
            MarginBottom = 72,
            MarginLeft = 72,
            MarginRight = 72
        };
        var context = new PdfRenderContext(
            pageSettings,
            compatibility: null,
            fontWidthScale: 1,
            fontFallback: null,
            fontDirectory: ProjectFonts.Directory);
        return (context, new(context));
    }

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

    [Test]
    public async Task SameStyleContextual_CollapsesBeforeSpacing()
    {
        var (context, engine) = CreateEngine();

        var start = context.CurrentY;
        engine.Render(Line("Date: 1/9/23", contextual: true, styleId: "Details"));
        var afterFirst = context.CurrentY;
        engine.Render(Line("Time: 9:00 AM", contextual: true, styleId: "Details"));
        var afterSecond = context.CurrentY;

        // The first line keeps its leading before-spacing (the previous style differs); the second,
        // a same-style contextual sibling, collapses it. The two single-line advances therefore
        // differ by exactly the before-spacing.
        var firstAdvance = afterFirst - start;
        var secondAdvance = afterSecond - afterFirst;
        await Assert.That(firstAdvance - secondAdvance).IsEqualTo(spacingBeforePoints).Within(0.5f);
    }

    [Test]
    public async Task NonContextual_KeepsBeforeSpacingOnEveryLine()
    {
        var (context, engine) = CreateEngine();

        var start = context.CurrentY;
        engine.Render(Line("Date: 1/9/23", contextual: false, styleId: "Details"));
        var afterFirst = context.CurrentY;
        engine.Render(Line("Time: 9:00 AM", contextual: false, styleId: "Details"));
        var afterSecond = context.CurrentY;

        // Without contextual spacing every line carries the same before-spacing → equal advances.
        await Assert.That(afterSecond - afterFirst).IsEqualTo(afterFirst - start).Within(0.5f);
    }

    [Test]
    public async Task ContextualButDifferentStyle_DoesNotCollapse()
    {
        var (context, engine) = CreateEngine();

        var start = context.CurrentY;
        engine.Render(Line("Heading", contextual: true, styleId: "Heading1"));
        var afterFirst = context.CurrentY;
        engine.Render(Line("Body", contextual: true, styleId: "Details"));
        var afterSecond = context.CurrentY;

        // Contextual spacing only collapses between paragraphs of the SAME style, so a style change
        // keeps the before-spacing on the second line.
        await Assert.That(afterSecond - afterFirst).IsEqualTo(afterFirst - start).Within(0.5f);
    }
}
