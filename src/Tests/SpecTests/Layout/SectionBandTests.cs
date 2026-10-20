/// <summary>
/// Headers, footers and page numbers per section, against the three Word-rendered fixtures the rules
/// were read from (2026-09-06): <c>section_header_inheritance</c> (a section takes the previous
/// section's part of any type it does not declare), <c>section_numbering_even_odd</c> (restarts,
/// formats, SECTIONPAGES, the bare filler page a restart of the wrong parity gets under
/// <c>w:evenAndOddHeaders</c>, and even pages showing nothing when their section has no even part) and
/// <c>section_numbering</c> (the same document without the setting: no filler, even parts ignored).
/// </summary>
public class SectionBandTests
{
    static string Fixture(string name) => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", name, "input.docx");

    static LaidOutDocument Layout(string name)
    {
        var document = new DocumentParser().Parse(Fixture(name));
        return new Fragmenter(LayoutTestFonts.Measurer).Layout(
            document.Elements,
            document.PageSettings,
            document.Header,
            document.Footer,
            document.FirstPageHeader,
            document.FirstPageFooter,
            document.EvenPageHeader,
            document.EvenPageFooter,
            DocumentNotes.From(document));
    }

    // The band text of a page: every run above the body's first line or below its last, joined.
    static string Bands(LaidOutPage page, bool header)
    {
        var lines = page.Items.OfType<PlacedLine>().ToList();
        var band = lines.Where(_ =>
        {
            if (header)
            {
                return _.Y < 60;
            }

            return _.Y > 700;
        });
        return string.Concat(band.SelectMany(_ => _.Runs).Select(_ => _.Text)).Trim();
    }

    [Test]
    public async Task A_section_inherits_each_undeclared_part_from_the_previous_section()
    {
        var laidOut = Layout("section_header_inheritance");

        await Assert.That(laidOut.Pages.Count).IsEqualTo(6);
        await Assert.That(Bands(laidOut.Pages[0], true)).IsEqualTo("S1 HEADER FIRST");
        await Assert.That(Bands(laidOut.Pages[0], false)).IsEqualTo("S1 FOOTER FIRST");
        await Assert.That(Bands(laidOut.Pages[1], true)).IsEqualTo("S1 HEADER EVEN");
        await Assert.That(Bands(laidOut.Pages[1], false)).IsEqualTo("S1 FOOTER EVEN");
        // Section 2 declares only a default header: its footer and even header come from section 1.
        await Assert.That(Bands(laidOut.Pages[2], true)).IsEqualTo("S2 HEADER DEFAULT");
        await Assert.That(Bands(laidOut.Pages[2], false)).IsEqualTo("S1 FOOTER DEFAULT");
        await Assert.That(Bands(laidOut.Pages[3], true)).IsEqualTo("S1 HEADER EVEN");
        await Assert.That(Bands(laidOut.Pages[3], false)).IsEqualTo("S1 FOOTER EVEN");
        // Section 3 declares nothing and sets w:titlePg: its first page takes section 1's first-page
        // parts through section 2, which never declared them.
        await Assert.That(Bands(laidOut.Pages[4], true)).IsEqualTo("S1 HEADER FIRST");
        await Assert.That(Bands(laidOut.Pages[4], false)).IsEqualTo("S1 FOOTER FIRST");
        await Assert.That(Bands(laidOut.Pages[5], true)).IsEqualTo("S1 HEADER EVEN");
    }

    [Test]
    public async Task Numbering_restarts_per_section_with_a_filler_page_for_a_wrong_parity_restart()
    {
        var laidOut = Layout("section_numbering_even_odd");

        // v, vi, vii | filler | 1, 2, 3 | 4, 5 — nine pages, the filler bare.
        await Assert.That(laidOut.Pages.Count).IsEqualTo(9);
        await Assert.That(Bands(laidOut.Pages[0], false)).IsEqualTo("FOOTER page=v numpages=9 sectionpages=3");
        await Assert.That(Bands(laidOut.Pages[1], false)).IsEqualTo("");
        await Assert.That(Bands(laidOut.Pages[1], true)).IsEqualTo("HEADER EVEN");
        await Assert.That(Bands(laidOut.Pages[2], false)).IsEqualTo("FOOTER page=vii numpages=9 sectionpages=3");
        await Assert.That(laidOut.Pages[3].Items.Count).IsEqualTo(0);
        await Assert.That(Bands(laidOut.Pages[4], true)).IsEqualTo("HEADER ODD");
        await Assert.That(Bands(laidOut.Pages[4], false)).IsEqualTo("FOOTER page=1 numpages=9 sectionpages=3");
        await Assert.That(Bands(laidOut.Pages[6], false)).IsEqualTo("FOOTER page=3 numpages=9 sectionpages=3");
        await Assert.That(Bands(laidOut.Pages[7], true)).IsEqualTo("HEADER EVEN");
        await Assert.That(Bands(laidOut.Pages[8], false)).IsEqualTo("FOOTER page=5 numpages=9 sectionpages=2");
    }

    [Test]
    public async Task Without_even_and_odd_headers_the_even_parts_are_ignored_and_no_filler_is_inserted()
    {
        var laidOut = Layout("section_numbering");

        await Assert.That(laidOut.Pages.Count).IsEqualTo(8);
        foreach (var page in laidOut.Pages)
        {
            await Assert.That(Bands(page, true)).IsEqualTo("HEADER ODD");
        }

        await Assert.That(Bands(laidOut.Pages[1], false)).IsEqualTo("FOOTER page=vi numpages=8 sectionpages=3");
        await Assert.That(Bands(laidOut.Pages[3], false)).IsEqualTo("FOOTER page=1 numpages=8 sectionpages=3");
        await Assert.That(Bands(laidOut.Pages[7], false)).IsEqualTo("FOOTER page=5 numpages=8 sectionpages=2");
    }

    static ParagraphElement Text(string text) =>
        new()
        {
            Runs = [new() {Text = text, Properties = new() {FontFamily = "Calibri", FontSizePoints = 12}}],
            Properties = new()
        };

    static HeaderFooterContent Band(int lines, string name) =>
        new() {Elements = [.. Enumerable.Range(1, lines).Select(DocumentElement (_) => Text($"{name} {_}"))]};

    static float BodyTop(LaidOutPage page, string text) =>
        page.Items.OfType<PlacedLine>().First(_ => string.Concat(_.Runs.Select(run => run.Text)) == text).Y;

    // A page's body clears the header THAT page shows. Six lines of header reach well past a 72pt top
    // margin from a 36pt header distance; one line stays inside it. menus/01 is the corpus case: its
    // third page's body sat a tall first-page header's worth out of place until each page asked.
    [Test]
    [Arguments(6, 1)]
    [Arguments(1, 6)]
    public async Task Each_page_reserves_room_for_the_header_it_shows(int firstLines, int laterLines)
    {
        var page = new PageSettings
        {
            WidthPoints = 612,
            HeightPoints = 792,
            MarginTop = 72,
            MarginBottom = 72,
            MarginLeft = 72,
            MarginRight = 72,
            HeaderDistance = 36,
            DifferentFirstPage = true
        };
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout(
            [Text("body one"), new PageBreakElement(), Text("body two"), new PageBreakElement(), Text("body three")],
            page,
            header: Band(laterLines, "later"),
            firstPageHeader: Band(firstLines, "first"));

        var tall = BodyTop(laidOut.Pages[firstLines > laterLines ? 0 : 1], firstLines > laterLines ? "body one" : "body two");
        var plain = BodyTop(laidOut.Pages[firstLines > laterLines ? 1 : 0], firstLines > laterLines ? "body two" : "body one");

        await Assert.That(plain).IsEqualTo(72f).Within(0.01f);
        await Assert.That(tall).IsGreaterThan(100f);
        await Assert.That(BodyTop(laidOut.Pages[2], "body three")).IsEqualTo(BodyTop(laidOut.Pages[1], "body two")).Within(0.01f);
    }
}
