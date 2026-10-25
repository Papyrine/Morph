/// <summary>
/// A document's comments drawn as Word prints them with All Markup (<see cref="CommentMarkup"/>): the
/// page shrunk beside a grey pane, a balloon per comment. The numbers are Word's, XPS-read on the
/// <c>_probe_comment_*</c> fixtures (2026-10-25); comments/02 is the multi-comment one, kept.
/// </summary>
public class CommentMarkupTests
{
    static string Input(string scenario) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "comments", scenario, "input.docx");

    static LaidOutDocument Layout(ParsedDocument document) =>
        new Fragmenter(LayoutTestFonts.Measurer)
            .Layout(document.Elements, document.PageSettings)
            .WithComments(document, LayoutTestFonts.Measurer, true);

    static PlacedScaledGroup Group(LaidOutPage page) =>
        (PlacedScaledGroup) page.Items.Single();

    static List<PlacedLine> BalloonLines(PlacedScaledGroup group) =>
        group.Items.OfType<PlacedLine>().Where(_ => _.Runs[0].Properties.FontSizePoints == 9).ToList();

    [Test]
    public async Task Commented_runs_carry_their_comment()
    {
        var document = new DocumentParser().Parse(Input("02"));
        var runs = document.Elements
            .OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .ToList();

        await Assert.That(runs.Single(_ => _.Text == "Second commented paragraph.").Properties.CommentId).IsEqualTo("2");
        await Assert.That(runs.Single(_ => _.Text == "Filler 0.").Properties.CommentId).IsNull();
    }

    [Test]
    public async Task Comments_are_numbered_in_the_order_their_ranges_start()
    {
        var document = new DocumentParser().Parse(Input("02"));

        await Assert.That(document.Comments.Select(_ => _.Number ?? 0)).IsEquivalentTo([1, 2, 3, 4]);
        var second = document.Comments.Single(_ => _.Id == "2");
        await Assert.That(second.Initials).IsEqualTo("R");
        await Assert.That(second.Paragraphs).IsEquivalentTo(["Two paragraphs here.", "Second paragraph of the comment."]);
    }

    [Test]
    public async Task The_page_shrinks_to_make_room_for_the_pane()
    {
        // Letter with 1in margins: the pane starts 7.3pt past the right margin and is 259.2pt wide,
        // so the page and pane, 806.5pt across, are shrunk to the 612pt paper and centred down it.
        // Word: 0.7590, from the pane's 601.06pt height (95.47 to 696.53).
        var group = Group(Layout(new DocumentParser().Parse(Input("01"))).Pages[0]);

        await Assert.That(group.Scale).IsEqualTo(612f / 806.5f).Within(0.0001f);
        await Assert.That(group.X).IsEqualTo(0);
        await Assert.That(group.Y).IsEqualTo(95.47f).Within(0.2f);
        var pane = group.Items.OfType<PlacedShading>().First();
        await Assert.That(pane.X).IsEqualTo(547.3f).Within(0.01f);
        await Assert.That(pane.Width).IsEqualTo(259.2f).Within(0.01f);
    }

    [Test]
    public async Task A_balloon_stands_level_with_its_line_unless_the_one_above_pushes_it_down()
    {
        // Word (in the page's own points): R1 and R2 at the tops of their lines; R3's line is 25pt
        // below R2's, but R2 takes two lines, so R3 starts 2.4pt under R2's bottom.
        var group = Group(Layout(new DocumentParser().Parse(Input("02"))).Pages[0]);
        var balloons = group.Items
            .OfType<PlacedShape>()
            .Where(_ => _.Shape.FillColorHex != null)
            .OrderBy(_ => _.Y)
            .ToList();
        var lines = group.Items.OfType<PlacedLine>().ToList();
        float LineTop(string text) =>
            lines.Single(_ => _.Runs.Any(run => run.Text == text)).Y;

        await Assert.That(balloons.Count).IsEqualTo(3);
        await Assert.That(balloons[0].Y).IsEqualTo(LineTop("First paragraph.")).Within(0.01f);
        await Assert.That(balloons[1].Y).IsEqualTo(LineTop("Second commented paragraph.")).Within(0.01f);
        await Assert.That(balloons[2].Y).IsEqualTo(balloons[1].Y + balloons[1].Height + 2.4f).Within(0.01f);
        await Assert.That(balloons[2].Y).IsGreaterThan(LineTop("Third commented paragraph right after."));
    }

    [Test]
    public async Task Balloon_text_wraps_where_Words_does()
    {
        // Word set R1's comment in five lines, its fourth ending "in the balloon" (224.02pt of Calibri
        // 9pt) and its first refusing "should" (225.53pt).
        var group = Group(Layout(new DocumentParser().Parse(Input("02"))).Pages[0]);
        var first = BalloonLines(group)
            .Where(_ => _.Y < 120)
            .Select(_ => string.Concat(_.Runs.Select(run => run.Text)).Trim())
            .ToList();

        await Assert.That(first).IsEquivalentTo(
        [
            "Commented [R1]: This is a much longer comment that",
            "should wrap across several lines in the balloon so that its",
            "width can be read off the page. This is a much longer",
            "comment that should wrap across several lines in the balloon",
            "so that its width can be read off the page."
        ]);
    }

    [Test]
    public async Task Every_page_is_drawn_with_the_pane()
    {
        // comments/02's second page anchors one comment; Word draws the pane on both.
        var laidOut = Layout(new DocumentParser().Parse(Input("02")));

        await Assert.That(laidOut.Pages.Count).IsEqualTo(2);
        await Assert.That(laidOut.Pages.All(_ => _.Items is [PlacedScaledGroup])).IsTrue();
    }

    [Test]
    public async Task Without_the_option_nothing_changes()
    {
        var document = new DocumentParser().Parse(Input("01"));
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer)
            .Layout(document.Elements, document.PageSettings)
            .WithComments(document, LayoutTestFonts.Measurer, false);

        await Assert.That(laidOut.Pages[0].Items.OfType<PlacedScaledGroup>()).IsEmpty();
    }

    [Test]
    public async Task Html_highlights_the_range_and_lists_the_comment()
    {
        var html = DocumentConverter.ConvertToHtml(Input("01"), new HtmlExportOptions {ShowComments = true});

        await Assert.That(html).Contains("""<mark class="comment" style="background-color: #F8DCDD"><span style="font-size: 12pt;">Some text with a review note attached.</span></mark><sup class="comment-ref"><a href="#comment-1">[R1]</a></sup>""");
        await Assert.That(html).Contains("""<p id="comment-1"><strong>Commented [R1]:</strong> Looks good to me.</p>""");
        await Assert.That(DocumentConverter.ConvertToHtml(Input("01"))).DoesNotContain("Looks good");
    }

    [Test]
    public async Task Markdown_footnotes_the_comment()
    {
        var markdown = DocumentConverter.ConvertToMarkdown(Input("01"), new MarkdownExportOptions {ShowComments = true});

        await Assert.That(markdown).Contains("Some text with a review note attached.[^comment-1]");
        await Assert.That(markdown).Contains("[^comment-1]: **Commented [R1]:** Looks good to me.");
    }
}
