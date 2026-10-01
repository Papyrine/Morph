/// <summary>
/// Which pages holding only empty paragraphs Word keeps. Word-probed on a page filled exactly by 54
/// 12pt exact lines (<c>_probe_sbo1</c>-<c>_sbo5</c>, page counts and XPS): the empty paragraph that
/// carries a next-page section break takes no page when it overflows, and every other empty paragraph
/// that overflows — ahead of that mark, of a page break or of a page-break-before paragraph — gets a
/// blank page of its own.
/// </summary>
public class SectionMarkOverflowTests
{
    // 160pt of body: eight 20pt exact lines fill it exactly.
    static readonly PageSettings page = new() { WidthPoints = 300, HeightPoints = 200, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task An_overflowing_section_mark_takes_no_page()
    {
        var document = Layout([.. Fill(), Empty(), NextPage(), Line("NEXT")]);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(Text(document.Pages[1])).IsEqualTo("NEXT");
    }

    [Test]
    public async Task An_empty_paragraph_overflowing_ahead_of_the_section_mark_keeps_its_page()
    {
        var document = Layout([.. Fill(), Empty(), Empty(), NextPage(), Line("NEXT")]);

        await Assert.That(document.Pages.Count).IsEqualTo(3);
        await Assert.That(Text(document.Pages[2])).IsEqualTo("NEXT");
    }

    [Test]
    public async Task An_empty_paragraph_overflowing_ahead_of_a_page_break_keeps_its_page()
    {
        var document = Layout([.. Fill(), Empty(), new PageBreakElement(), Line("NEXT")]);

        await Assert.That(document.Pages.Count).IsEqualTo(3);
    }

    static LaidOutDocument Layout(List<DocumentElement> elements) =>
        new Fragmenter(LayoutTestFonts.Measurer).Layout(elements, page);

    static string Text(LaidOutPage laidOutPage) =>
        string.Concat(laidOutPage.Items.OfType<PlacedLine>().SelectMany(_ => _.Runs).Select(_ => _.Text));

    static IEnumerable<DocumentElement> Fill() =>
        Enumerable.Range(1, 8).Select(_ => Line($"L{_}"));

    static SectionBreakElement NextPage() => new() { BreakType = SectionBreakType.NextPage };

    static ParagraphElement Empty() => new() { Runs = [], Properties = Exact };

    static ParagraphElement Line(string text) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = Exact
    };

    static ParagraphProperties Exact => new()
    {
        SpacingBeforePoints = 0,
        SpacingAfterPoints = 0,
        LineSpacingRule = LineSpacingRule.Exactly,
        LineSpacingPoints = 20
    };
}
