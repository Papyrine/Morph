/// <summary>
/// A hanging-indent list item whose first line starts with a positioning tab (manual
/// <c>"1."</c> + <c>w:tab</c> + text, as produced by Word for numbered clauses) must wrap its
/// first line at the content width, exactly like the wrapped continuation lines below it.
///
/// Regression guard for issue #151: the PDF backend let any line carrying a tab spill into the
/// right margin (up to the page edge) so tab COLUMNS wouldn't wrap. The list marker's positioning
/// tab tripped that flag too, so the first line alone ran a whole right-margin wider than the
/// content edge while the continuation lines stopped correctly — the raster backends never did this
/// and rendered the same document fine. The overflow allowance was removed entirely: every line now
/// wraps at the content width. (Tab columns that appeared to need it were really relying on a
/// dropped right indent, now honoured — see <see cref="PdfRightIndentWrapTests"/>.)
/// </summary>
public class PdfHangingIndentTabWrapTests
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

    [Test]
    public async Task LeadingTabDoesNotLetFirstLineSpillIntoRightMargin()
    {
        var (context, engine) = CreateEngine();

        // Mirrors the issue document's clause paragraphs: w:ind w:left="425" w:hanging="255"
        // (twips → points), a literal "1." followed by a tab, then a long body run that wraps.
        const double leftIndentPoints = 425 / 20d;
        var runProperties = new RunProperties {FontFamily = "Arial", FontSizePoints = 11};
        // Short words fill the first line at a fine granularity, so before the fix the leading tab's
        // right-margin licence lets the line pack words to roughly a whole right margin past the
        // content edge; wrapped at the content width it stops within one short word of it.
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

        // The wrap width the flow renderer uses for this paragraph (see PdfTextEngine.Render):
        // the section content width minus the paragraph's left indent.
        var wrapWidth = (float) (context.ContentWidth - leftIndentPoints);

        // MeasureParagraphNaturalWidth lays the paragraph out at wrapWidth and returns the widest
        // line's width plus the left indent. Wrapped at wrapWidth, no line — the tab-led first line
        // included — may exceed it. Before the fix the first line ran ~one right-margin (72pt) over.
        var widestExtent = engine.MeasureParagraphNaturalWidth(paragraph, wrapWidth);

        await Assert.That(widestExtent).IsLessThanOrEqualTo((float) (wrapWidth + leftIndentPoints) + 0.5f);
    }
}
