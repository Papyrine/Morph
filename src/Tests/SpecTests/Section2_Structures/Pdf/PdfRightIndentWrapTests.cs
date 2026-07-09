/// <summary>
/// The PDF backend must subtract <c>ParagraphProperties.RightIndentPoints</c> from the wrap width,
/// exactly like the left indent and exactly like the Skia / ImageSharp backends. It used to drop the
/// right indent entirely, so a right-indented paragraph wrapped at a Word-divergent (too wide) width
/// — masked in tab-column layouts by a since-removed tab-overflow hack (issue #151 follow-up).
///
/// The flow render (<c>PdfTextEngine.Render</c>) and the flow height measure (<c>MeasureFlowHeight</c>,
/// used for keep-together / keep-with-next pagination) both derive the wrap width the same way, so
/// this exercises <c>MeasureFlowHeight</c> as the observable proxy for the shared wrap width.
/// </summary>
public class PdfRightIndentWrapTests
{
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

    static ParagraphElement Paragraph(double leftIndent, double rightIndent)
    {
        var runProperties = new RunProperties {FontFamily = "Arial", FontSizePoints = 11};
        var body = string.Join(' ', Enumerable.Repeat("the quick brown fox jumps over the lazy dog", 8));
        return new()
        {
            Properties = new() {LeftIndentPoints = leftIndent, RightIndentPoints = rightIndent},
            Runs = [new() {Text = body, Properties = runProperties}]
        };
    }

    [Test]
    public async Task RightIndentNarrowsWrapWidthLikeAnEqualLeftIndent()
    {
        var (context, engine) = CreateEngine();

        // Same paragraph indented 120pt on the left vs 120pt on the right. Both remove 120pt from the
        // wrap width, so the body wraps to the same number of lines and the flow height is identical.
        // Before the fix the right-indented paragraph wrapped at the full content width (right indent
        // ignored), producing fewer lines and a shorter height.
        var leftIndented = engine.MeasureFlowHeight(Paragraph(leftIndent: 120, rightIndent: 0), context.ContentWidth, 0);
        var rightIndented = engine.MeasureFlowHeight(Paragraph(leftIndent: 0, rightIndent: 120), context.ContentWidth, 0);

        await Assert.That(rightIndented).IsEqualTo(leftIndented).Within(0.5f);
    }

    [Test]
    public async Task RightIndentProducesMoreLinesThanNoIndent()
    {
        var (context, engine) = CreateEngine();

        // A wrapping paragraph is strictly taller once a right indent narrows its wrap width.
        var noIndent = engine.MeasureFlowHeight(Paragraph(leftIndent: 0, rightIndent: 0), context.ContentWidth, 0);
        var rightIndented = engine.MeasureFlowHeight(Paragraph(leftIndent: 0, rightIndent: 120), context.ContentWidth, 0);

        await Assert.That(rightIndented).IsGreaterThan(noIndent);
    }

    // Table-cell path: MeasureParagraphHeightWithWidth (the IParagraphMeasurer method the shared
    // table height calc uses) takes the cell inner width and must remove both indents too, matching
    // how RenderInBounds draws the cell. Before the fix it passed the raw width to Layout, so an
    // indented cell paragraph measured shorter than it renders and rows were under-sized.
    [Test]
    public async Task TableCellRightIndentNarrowsCellWrapLikeLeftIndent()
    {
        var (_, engine) = CreateEngine();

        const float cellInnerWidth = 300;
        var leftIndented = engine.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 90, rightIndent: 0), cellInnerWidth);
        var rightIndented = engine.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 90), cellInnerWidth);

        await Assert.That(rightIndented).IsEqualTo(leftIndented).Within(0.5f);
    }

    [Test]
    public async Task TableCellRightIndentProducesMoreLinesThanNoIndent()
    {
        var (_, engine) = CreateEngine();

        const float cellInnerWidth = 300;
        var noIndent = engine.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 0), cellInnerWidth);
        var rightIndented = engine.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 90), cellInnerWidth);

        await Assert.That(rightIndented).IsGreaterThan(noIndent);
    }

    // Autofit column width (MeasureParagraphNaturalWidth) is the bare content width — the shared
    // TableLayout adds cell padding/margin itself, so a paragraph's own indent must not inflate it.
    // The PDF backend used to add the left indent, making its autofit columns a left-indent wider
    // than the Skia/ImageSharp measurers (which return bare widest).
    [Test]
    public async Task AutofitNaturalWidthExcludesTheParagraphIndent()
    {
        var (_, engine) = CreateEngine();

        const float unbounded = float.MaxValue / 4;
        var indented = engine.MeasureParagraphNaturalWidth(Paragraph(leftIndent: 100, rightIndent: 0), unbounded);
        var plain = engine.MeasureParagraphNaturalWidth(Paragraph(leftIndent: 0, rightIndent: 0), unbounded);

        await Assert.That(indented).IsEqualTo(plain).Within(0.5f);
    }
}
