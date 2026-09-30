/// <summary>
/// Covers <c>w:vAlign</c> in <c>w:sectPr</c> — the section's vertical alignment on the page — in
/// <see cref="Fragmenter"/>. The rules are Word's, read off three probes and pinned by the
/// <c>page_vertical_alignment</c> scenario (see its notes.md): the extent runs from the band top to the
/// lowest line, space-after or float; center moves the page down by half the slack and bottom by all of
/// it, floats included; both shares the slack equally between paragraphs and table rows.
/// </summary>
public class PageVerticalAlignmentTests
{
    static readonly Fragmenter fragmenter = new(LayoutTestFonts.Measurer);

    const float top = 20;
    const float bottom = 380;

    // 300 x 400 with 20pt margins: a 360pt band from 20 to 380.
    static PageSettings Page(PageVerticalAlignment alignment) =>
        new()
        {
            WidthPoints = 300,
            HeightPoints = 400,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20,
            VerticalAlignment = alignment
        };

    static ParagraphElement P(string text, ParagraphProperties? properties = null) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = text,
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = properties ?? new()
        };

    static List<PlacedLine> Lines(LaidOutDocument document, int pageIndex) =>
        document.Pages[pageIndex].Items.OfType<PlacedLine>().OrderBy(_ => _.Y).ToList();

    static PlacedLine Line(LaidOutDocument document, string text) =>
        document.Pages.SelectMany(_ => _.Items).OfType<PlacedLine>().First(_ => _.Runs.Any(_ => _.Text == text));

    static float Bottom(PlacedItem item) => item.Y + item.Height;

    [Test]
    public async Task Top_is_the_default()
    {
        var document = fragmenter.Layout([P("a"), P("b")], Page(PageVerticalAlignment.Top));

        await Assert.That(Lines(document, 0)[0].Y).IsEqualTo(top).Within(0.01f);
    }

    [Test]
    public async Task Center_leaves_equal_space_above_and_below()
    {
        var document = fragmenter.Layout([P("a"), P("b"), P("c")], Page(PageVerticalAlignment.Center));

        var lines = Lines(document, 0);
        var above = lines[0].Y - top;
        var below = bottom - Bottom(lines[^1]);
        await Assert.That(above).IsGreaterThan(100f);
        await Assert.That(above).IsEqualTo(below).Within(0.01f);
    }

    [Test]
    public async Task Center_counts_the_first_space_before_and_the_last_space_after()
    {
        // Word's page 2: B1's 36pt before and B3's 72pt after are both inside the centred extent.
        var document = fragmenter.Layout(
            [P("a", new() { SpacingBeforePoints = 30 }), P("b", new() { SpacingAfterPoints = 60 })],
            Page(PageVerticalAlignment.Center));

        var lines = Lines(document, 0);
        var above = lines[0].Y - 30 - top;
        var below = bottom - (Bottom(lines[^1]) + 60);
        await Assert.That(above).IsEqualTo(below).Within(0.01f);
    }

    [Test]
    public async Task Bottom_ends_the_last_space_after_on_the_margin()
    {
        var document = fragmenter.Layout(
            [P("a"), P("b", new() { SpacingAfterPoints = 40 })],
            Page(PageVerticalAlignment.Bottom));

        await Assert.That(Bottom(Lines(document, 0)[^1]) + 40).IsEqualTo(bottom).Within(0.01f);
    }

    [Test]
    public async Task Justified_shares_the_slack_equally_between_paragraphs_and_keeps_line_pitch()
    {
        var wrapped = string.Join(' ', Enumerable.Repeat("lorem", 30));
        List<DocumentElement> Content() =>
            [P("a", new() { SpacingAfterPoints = 0 }), P("b", new() { SpacingAfterPoints = 12 }), P(wrapped), P("d")];

        var document = fragmenter.Layout(Content(), Page(PageVerticalAlignment.Justified));
        var topAligned = fragmenter.Layout(Content(), Page(PageVerticalAlignment.Top));

        // Each paragraph's first line moves by one more equal step than the one before.
        var firsts = Lines(document, 0).GroupBy(_ => _.Paragraph).Select(_ => _.First().Y).ToList();
        var topFirsts = Lines(topAligned, 0).GroupBy(_ => _.Paragraph).Select(_ => _.First().Y).ToList();
        var step = firsts[1] - topFirsts[1];
        await Assert.That(step).IsGreaterThan(10f);
        for (var index = 0; index < firsts.Count; index++)
        {
            await Assert.That(firsts[index] - topFirsts[index]).IsEqualTo(step * index).Within(0.01f);
        }

        // The wrapped paragraph's lines stay at their own pitch.
        var wrappedLines = Lines(document, 0).Where(_ => _.Runs.Any(_ => _.Text.Contains("lorem"))).ToList();
        var topWrapped = Lines(topAligned, 0).Where(_ => _.Runs.Any(_ => _.Text.Contains("lorem"))).ToList();
        await Assert.That(wrappedLines.Count).IsGreaterThan(1);
        await Assert.That(wrappedLines[1].Y - wrappedLines[0].Y).IsEqualTo(topWrapped[1].Y - topWrapped[0].Y).Within(0.01f);

        await Assert.That(Bottom(Lines(document, 0)[^1])).IsEqualTo(bottom).Within(0.01f);
    }

    [Test]
    public async Task Justified_leaves_a_single_paragraph_at_the_top()
    {
        var document = fragmenter.Layout([P("alone")], Page(PageVerticalAlignment.Justified));

        await Assert.That(Lines(document, 0)[0].Y).IsEqualTo(top).Within(0.01f);
    }

    [Test]
    public async Task Justified_spreads_table_rows_apart_like_paragraphs()
    {
        // Word's _probe_valign3 Q1: a paragraph, three rows and two paragraphs came out as five equal gaps.
        static TableRow Row(string text) => new() { Cells = [new() { Properties = new() { WidthPoints = 200 }, Content = [P(text)] }] };
        var table = new TableElement
        {
            Rows = [Row("r1"), Row("r2"), Row("r3")],
            Properties = new() { GridColumnWidths = [200], PreferredWidthPoints = 200 }
        };

        var document = fragmenter.Layout([P("before"), table, P("after")], Page(PageVerticalAlignment.Justified));

        var items = document.Pages[0].Items;
        var rows = items.OfType<PlacedTableRow>().OrderBy(_ => _.Y).ToList();
        var before = Line(document, "before");
        var after = Line(document, "after");
        var gaps = new List<float>
        {
            rows[0].Y - Bottom(before),
            rows[1].Y - Bottom(rows[0]),
            rows[2].Y - Bottom(rows[1]),
            after.Y - Bottom(rows[2])
        };

        await Assert.That(gaps[0]).IsGreaterThan(10f);
        foreach (var gap in gaps)
        {
            await Assert.That(gap).IsEqualTo(gaps[0]).Within(0.01f);
        }
    }

    [Test]
    public async Task Every_page_of_the_section_is_aligned_on_its_own()
    {
        // Exact 30pt lines: twelve fill the 360pt band, so page 1 has no slack and page 2's three lines
        // are centred — Word's pages 5 and 6.
        var exact = new ParagraphProperties { LineSpacingRule = LineSpacingRule.Exactly, LineSpacingPoints = 30 };
        var paragraphs = Enumerable.Range(1, 15).Select(_ => P($"line{_}", exact)).ToList<DocumentElement>();

        var document = fragmenter.Layout(paragraphs, Page(PageVerticalAlignment.Center));

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(Lines(document, 0)[0].Y).IsEqualTo(top).Within(0.01f);
        var second = Lines(document, 1);
        await Assert.That(second.Count).IsEqualTo(3);
        await Assert.That(second[0].Y).IsEqualTo(top + (360 - 90) / 2f).Within(0.01f);
    }

    [Test]
    public async Task A_page_carrying_two_sections_stays_at_the_top()
    {
        var document = fragmenter.Layout(
            [
                P("first"),
                new SectionBreakElement { BreakType = SectionBreakType.Continuous, NewSectionSettings = Page(PageVerticalAlignment.Center) },
                P("second")
            ],
            Page(PageVerticalAlignment.Top));

        await Assert.That(Lines(document, 0)[0].Y).IsEqualTo(top).Within(0.01f);
    }

    [Test]
    public async Task A_next_page_section_takes_its_own_alignment()
    {
        var document = fragmenter.Layout(
            [
                P("first"),
                new SectionBreakElement { BreakType = SectionBreakType.NextPage, NewSectionSettings = Page(PageVerticalAlignment.Bottom) },
                P("second")
            ],
            Page(PageVerticalAlignment.Top));

        await Assert.That(Lines(document, 0)[0].Y).IsEqualTo(top).Within(0.01f);
        await Assert.That(Bottom(Lines(document, 1)[^1])).IsEqualTo(bottom).Within(0.01f);
    }

    static FloatingImageElement PageFloat(ParagraphElement anchor, double y, double height = 50) =>
        new()
        {
            ImageData = "PNG"u8.ToArray(),
            WidthPoints = 50,
            HeightPoints = height,
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalPositionPoints = 200,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalPositionPoints = y,
            AnchorParagraph = anchor
        };

    [Test]
    public async Task A_float_below_the_text_counts_in_the_extent_and_moves_with_it()
    {
        // Word's _probe_valign2 P2: a page float low on the page cut the slack, and moved with the text.
        var anchor = P("anchored");
        var document = fragmenter.Layout([anchor, PageFloat(anchor, 300), P("next")], Page(PageVerticalAlignment.Center));

        // The float's bottom (350) is the extent's: (380 − 350) / 2 = 15.
        var image = document.Pages[0].Items.OfType<PlacedImage>().Single();
        await Assert.That(image.Y).IsEqualTo(315f).Within(0.01f);
        await Assert.That(Lines(document, 0)[0].Y).IsEqualTo(top + 15).Within(0.01f);
    }

    [Test]
    public async Task A_float_above_the_text_does_not_widen_the_extent()
    {
        // Word's _probe_valign2 P3: a page float across the top margin, ending inside the text, left the
        // text centred on its own — and moved by the same amount.
        var anchor = P("anchored");
        var document = fragmenter.Layout([anchor, PageFloat(anchor, 5, 25), P("next")], Page(PageVerticalAlignment.Center));

        var lines = Lines(document, 0);
        var offset = lines[0].Y - top;
        await Assert.That(offset).IsEqualTo(bottom - Bottom(lines[^1])).Within(0.01f);
        await Assert.That(document.Pages[0].Items.OfType<PlacedImage>().Single().Y).IsEqualTo(5 + offset).Within(0.01f);
    }

    [Test]
    public async Task A_page_float_after_its_anchor_paragraph_stays_on_that_page()
    {
        // The drawing follows its paragraph's text, so the anchor has laid out before the float reaches
        // the flow. It used to wait for that anchor's first line forever and land on the last page.
        var anchor = P("anchored");
        var document = fragmenter.Layout(
            [anchor, PageFloat(anchor, 300), new PageBreakElement(), P("later")],
            Page(PageVerticalAlignment.Top));

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[0].Items.OfType<PlacedImage>().Count()).IsEqualTo(1);
        await Assert.That(document.Pages[1].Items.OfType<PlacedImage>().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task The_parser_reads_each_sections_alignment()
    {
        var path = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "page_vertical_alignment", "input.docx");
        var document = new DocumentParser().Parse(path);

        var alignments = document.Elements.OfType<SectionBreakElement>()
            .Select(_ => _.NewSectionSettings!.VerticalAlignment)
            .Prepend(document.PageSettings.VerticalAlignment)
            .Take(6);

        // A, B centre; C bottom; D both; E centre; F both — the fixture's first six sections.
        await Assert.That(string.Join(",", alignments)).IsEqualTo("Center,Center,Bottom,Justified,Center,Justified");
    }
}
