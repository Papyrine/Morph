/// <summary>
/// Tests the block-flow slice of the <see cref="Fragmenter"/> (step 3 of
/// <c>docs/layout-engine-proposal.md</c>): single-column pagination with line-level page breaks and the
/// height-model spacing rules. A small page geometry forces the interesting boundaries.
/// </summary>
public class CanonicalFragmenterTests
{
    static readonly Fragmenter Fragmenter = new(LayoutTestFonts.Measurer);

    // 300pt wide, 20pt margins → 260pt measure. At 200pt tall the content band is 160pt = 11 Aptos-11
    // lines (14.5pt each), so the twelfth line breaks the page.
    static PageSettings Page(double heightPoints) =>
        new() { WidthPoints = 300, HeightPoints = heightPoints, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    static ParagraphElement P(string text, ParagraphProperties? properties = null) =>
        new()
        {
            Runs = [new Run { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
            Properties = properties ?? new()
        };

    [Test]
    public async Task Short_paragraphs_fit_on_one_page()
    {
        var document = Fragmenter.Layout([P("One"), P("Two"), P("Three")], Page(200));
        await Assert.That(document.Pages.Count).IsEqualTo(1);
        await Assert.That(document.Pages[0].Items.Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_tall_paragraph_splits_across_pages_at_line_boundaries()
    {
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 220)));
        var page = Page(200);
        var totalLines = LayoutTestFonts.Measurer.LayoutLines(paragraph, (float) page.ContentWidth).Count;

        var document = Fragmenter.Layout([paragraph], page);

        // Every wrapped line is placed exactly once, and the paragraph continues onto a second page —
        // the line-level split the raster backends cannot do.
        await Assert.That(document.Pages.Sum(_ => _.Items.Count)).IsEqualTo(totalLines);
        await Assert.That(document.Pages.Count > 1).IsTrue();
        await Assert.That(ReferenceEquals(((PlacedLine) document.Pages[1].Items[0]).Paragraph, paragraph)).IsTrue();

        // No placed line overflows the content bottom (180pt here).
        foreach (var placed in document.Pages.SelectMany(_ => _.Items))
        {
            await Assert.That(placed.Y + placed.Height <= 180.01f).IsTrue();
        }
    }

    [Test]
    public async Task Space_before_is_dropped_at_a_broken_page_top()
    {
        // Eleven single-line paragraphs fill page 1; a twelfth with a big space-before lands atop page 2.
        var fillers = Enumerable.Range(0, 11).Select(_ => P("filler")).ToArray();
        var moved = P("moved", new ParagraphProperties { SpacingBeforePoints = 50 });

        var document = Fragmenter.Layout([.. fillers, moved], Page(200));

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        // Its first line sits at the content top — the 50pt before was dropped, not applied.
        await Assert.That(document.Pages[1].Items[0].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task Page_break_element_starts_a_new_page()
    {
        var document = Fragmenter.Layout([P("before"), new PageBreakElement(), P("after")], Page(400));
        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[1].Items[0].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task Empty_document_is_one_empty_page()
    {
        var document = Fragmenter.Layout([], Page(200));
        await Assert.That(document.Pages.Count).IsEqualTo(1);
        await Assert.That(document.Pages[0].Items.Count).IsEqualTo(0);
    }
}
