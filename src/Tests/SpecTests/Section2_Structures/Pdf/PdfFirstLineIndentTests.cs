/// <summary>
/// The PDF backend must apply a paragraph's <c>FirstLineIndentPoints</c> (Word's <c>w:firstLine</c>,
/// a positive first-line indent — distinct from a hanging indent): the first line is pushed right and
/// wraps that much narrower, while continuation lines use the full width. It used to ignore the
/// property entirely, so the first line ran the full width flush with the rest. The Skia/ImageSharp
/// backends narrow the first line's wrap; this exercises the shared <c>Layout</c> via the flow-height
/// measure as the observable proxy.
/// </summary>
public class PdfFirstLineIndentTests
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

    static ParagraphElement Paragraph(double firstLineIndent) =>
        new()
        {
            Properties = new() {FirstLineIndentPoints = firstLineIndent},
            Runs = [new() {Text = "The quick brown fox jumps over the lazy dog", Properties = new() {FontFamily = "Arial", FontSizePoints = 11}}]
        };

    [Test]
    public async Task FirstLineIndentForcesAnEarlierWrapOnTheFirstLineOnly()
    {
        var (context, engine) = CreateEngine();

        // The sentence fits on one line at the full content width. A first-line indent wider than that
        // slack cannot fit it on the (now narrower) first line, so the paragraph gains a second line —
        // proving the indent narrows the first line's wrap. Before the fix the indent was ignored and
        // the paragraph stayed a single line.
        var naturalWidth = engine.MeasureParagraphNaturalWidth(Paragraph(0), float.MaxValue / 4);
        double slack = context.ContentWidth - naturalWidth;
        await Assert.That(slack).IsGreaterThan(0d); // guard: the sentence really does fit on one line

        var plainHeight = engine.MeasureFlowHeight(Paragraph(0), context.ContentWidth, 0);
        var indentedHeight = engine.MeasureFlowHeight(Paragraph(slack + 20), context.ContentWidth, 0);

        await Assert.That(indentedHeight).IsGreaterThan(plainHeight);
    }

    static ParagraphElement HangingParagraph(double hangingIndent) =>
        new()
        {
            Properties = new() {HangingIndentPoints = hangingIndent},
            Runs =
            [
                new()
                {
                    Text = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog", 3)),
                    Properties = new() {FontFamily = "Arial", FontSizePoints = 11}
                }
            ]
        };

    [Test]
    public async Task MarkerlessHangingIndentOutdentsAndWidensTheFirstLine()
    {
        var (context, engine) = CreateEngine();

        // A markerless (no numbering) hanging paragraph outdents its first line, so the first line wraps
        // that much WIDER than the content width — Word draws a bibliography entry's first line at the
        // margin with continuation lines indented. Sized so the whole run fits on the widened first line,
        // the paragraph collapses to fewer lines than it wraps to without the hanging indent. Before the
        // fix the hanging indent didn't move the first line, so both wrapped identically.
        var naturalWidth = engine.MeasureParagraphNaturalWidth(HangingParagraph(0), float.MaxValue / 4);
        await Assert.That((double) naturalWidth).IsGreaterThan(context.ContentWidth); // guard: wraps at content width

        var hanging = naturalWidth - context.ContentWidth + 20;
        var plainHeight = engine.MeasureFlowHeight(HangingParagraph(0), context.ContentWidth, 0);
        var hangingHeight = engine.MeasureFlowHeight(HangingParagraph(hanging), context.ContentWidth, 0);

        await Assert.That(hangingHeight).IsLessThan(plainHeight);
    }
}
