/// <summary>
/// The measurer must subtract <c>ParagraphProperties.RightIndentPoints</c> from the wrap width, exactly
/// like the left indent. The PDF backend used to drop the right indent entirely, so a right-indented
/// paragraph wrapped at a Word-divergent (too wide) width — masked in tab-column layouts by a
/// since-removed tab-overflow hack (issue #151 follow-up). These assertions began as PDF-backend
/// regression guards; the rule now lives once in <see cref="CanonicalParagraphMeasurer"/>, which every
/// output measures through, so the per-backend divergence class they guarded is structurally gone and
/// what remains to protect is the shared rule.
/// </summary>
public class CanonicalIndentWrapTests
{
    // The old PDF fixture's geometry: 612x792 with 72pt margins.
    const float contentWidth = 612 - 144;

    static readonly CanonicalParagraphMeasurer measurer = LayoutTestFonts.Measurer;

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
        // Same paragraph indented 120pt on the left vs 120pt on the right. Both remove 120pt from the
        // wrap width, so the body wraps to the same number of lines and the height is identical.
        var leftIndented = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 120, rightIndent: 0), contentWidth);
        var rightIndented = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 120), contentWidth);

        await Assert.That(rightIndented).IsEqualTo(leftIndented).Within(0.5f);
    }

    [Test]
    public async Task RightIndentProducesMoreLinesThanNoIndent()
    {
        // A wrapping paragraph is strictly taller once a right indent narrows its wrap width.
        var noIndent = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 0), contentWidth);
        var rightIndented = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 120), contentWidth);

        await Assert.That(rightIndented).IsGreaterThan(noIndent);
    }

    // Table-cell path: MeasureParagraphHeightWithWidth takes the cell inner width and must remove both
    // indents, matching how the cell lays out. Passing the raw width to the wrap made an indented cell
    // paragraph measure shorter than it renders, under-sizing rows.
    [Test]
    public async Task TableCellRightIndentNarrowsCellWrapLikeLeftIndent()
    {
        const float cellInnerWidth = 300;
        var leftIndented = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 90, rightIndent: 0), cellInnerWidth);
        var rightIndented = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 90), cellInnerWidth);

        await Assert.That(rightIndented).IsEqualTo(leftIndented).Within(0.5f);
    }

    [Test]
    public async Task TableCellRightIndentProducesMoreLinesThanNoIndent()
    {
        const float cellInnerWidth = 300;
        var noIndent = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 0), cellInnerWidth);
        var rightIndented = measurer.MeasureParagraphHeightWithWidth(Paragraph(leftIndent: 0, rightIndent: 90), cellInnerWidth);

        await Assert.That(rightIndented).IsGreaterThan(noIndent);
    }

    // Autofit column width (MeasureParagraphNaturalWidth) is the bare content width — the shared
    // TableLayout adds cell padding/margin itself, so a paragraph's own indent must not inflate it.
    // The PDF measurer used to add the left indent, making its autofit columns a left-indent wider
    // than the raster ones.
    [Test]
    public async Task AutofitNaturalWidthExcludesTheParagraphIndent()
    {
        const float unbounded = float.MaxValue / 4;
        var indented = measurer.MeasureParagraphNaturalWidth(Paragraph(leftIndent: 100, rightIndent: 0), unbounded);
        var plain = measurer.MeasureParagraphNaturalWidth(Paragraph(leftIndent: 0, rightIndent: 0), unbounded);

        await Assert.That(indented).IsEqualTo(plain).Within(0.5f);
    }

    static ParagraphElement FirstLineIndented(double firstLineIndent) =>
        new()
        {
            Properties = new() {FirstLineIndentPoints = firstLineIndent},
            Runs = [new() {Text = "The quick brown fox jumps over the lazy dog", Properties = new() {FontFamily = "Arial", FontSizePoints = 11}}]
        };

    /// <summary>
    /// <c>w:firstLine</c> (a positive first-line indent — distinct from a hanging indent) pushes the first
    /// line right and wraps it that much narrower, while continuation lines use the full width.
    /// </summary>
    [Test]
    public async Task FirstLineIndentForcesAnEarlierWrapOnTheFirstLineOnly()
    {
        // The sentence fits on one line at the full content width. A first-line indent wider than that
        // slack cannot fit it on the (now narrower) first line, so the paragraph gains a second line —
        // proving the indent narrows the first line's wrap.
        var naturalWidth = measurer.MeasureParagraphNaturalWidth(FirstLineIndented(0), float.MaxValue / 4);
        var slack = contentWidth - naturalWidth;
        // guard: the sentence really does fit on one line
        await Assert.That(slack).IsGreaterThan(0f);

        var plainHeight = measurer.MeasureParagraphHeightWithWidth(FirstLineIndented(0), contentWidth);
        var indentedHeight = measurer.MeasureParagraphHeightWithWidth(FirstLineIndented(slack + 20), contentWidth);

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

    /// <summary>
    /// A markerless (no numbering) hanging paragraph outdents its first line, so that line wraps WIDER
    /// than the content width — Word draws a bibliography entry's first line at the margin with
    /// continuation lines indented.
    /// </summary>
    [Test]
    public async Task MarkerlessHangingIndentOutdentsAndWidensTheFirstLine()
    {
        // Sized so the whole run fits on the widened first line, the paragraph collapses to fewer lines
        // than it wraps to without the hanging indent.
        var naturalWidth = measurer.MeasureParagraphNaturalWidth(HangingParagraph(0), float.MaxValue / 4);
        // guard: wraps at content width
        await Assert.That(naturalWidth).IsGreaterThan(contentWidth);

        var hanging = naturalWidth - contentWidth + 20;
        var plainHeight = measurer.MeasureParagraphHeightWithWidth(HangingParagraph(0), contentWidth);
        var hangingHeight = measurer.MeasureParagraphHeightWithWidth(HangingParagraph(hanging), contentWidth);

        await Assert.That(hangingHeight).IsLessThan(plainHeight);
    }

    /// <summary>
    /// A leading tab must not license the first line to spill into the right margin: wrapped at a given
    /// width, no line — the tab-led first one included — may exceed it. Before the fix the first line ran
    /// about a whole right margin (72pt) over.
    /// </summary>
    [Test]
    public async Task LeadingTabDoesNotLetFirstLineSpillIntoRightMargin()
    {
        // Mirrors the issue document's clause paragraphs: w:ind w:left="425" w:hanging="255"
        // (twips → points), a literal "1." followed by a tab, then a long body run that wraps.
        const double leftIndentPoints = 425 / 20d;
        var runProperties = new RunProperties {FontFamily = "Arial", FontSizePoints = 11};
        // Short words fill the first line at a fine granularity, so before the fix the leading tab's
        // right-margin licence let the line pack words to roughly a whole right margin past the content
        // edge; wrapped at the content width it stops within one short word of it.
        var body = string.Join(' ', Enumerable.Repeat("en te de op in za om of", 30));
        var paragraph = new ParagraphElement
        {
            Properties = new()
            {
                LeftIndentPoints = leftIndentPoints,
                HangingIndentPoints = 255 / 20d
            },
            Runs =
            [
                new() {Text = "1.", Properties = runProperties},
                new() {Text = "\t", IsTab = true, Properties = runProperties},
                new() {Text = body, Properties = runProperties}
            ]
        };

        var wrapWidth = (float) (contentWidth - leftIndentPoints);
        var widestExtent = measurer.MeasureParagraphNaturalWidth(paragraph, wrapWidth);

        await Assert.That(widestExtent).IsLessThanOrEqualTo(wrapWidth + 0.5f);
    }
}
