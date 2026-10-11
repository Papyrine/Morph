/// <summary>
/// What a paragraph box (<c>w:pBdr</c>) takes of the flow beyond its own lines, read off Word's pages
/// for <c>border_style_variants</c> and Word-probed 2026-10-11 (<c>_probe_shadow</c>).
/// </summary>
public class ParagraphBoxFlowTests
{
    static readonly Lazy<LaidOutDocument> fixture = new(() =>
    {
        var path = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "border_style_variants", "input.docx");
        using var stream = File.OpenRead(path);
        var document = new DocumentParser().Parse(stream);
        return new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility).Layout(document.Elements, document.PageSettings);
    });

    static PlacedLine Line(LaidOutPage page, string text) =>
        page.Items.OfType<PlacedLine>().First(_ => string.Concat(_.Runs.Select(run => run.Text)) == text);

    // A box carried to the top of a page keeps its top rule and w:space in the flow: Word's rule is
    // at the margin and the text below it, where a plain paragraph's line would start at the margin.
    [Test]
    public async Task A_box_that_opens_a_page_starts_at_the_margin_with_its_text_below()
    {
        var page = fixture.Value.Pages[1];
        var line = Line(page, "thinThickMediumGap");
        var box = page.Items.OfType<PlacedBorder>().First();
        var top = (float) page.Settings.MarginTop;

        await Assert.That(page.Items.OfType<PlacedLine>().First()).IsEqualTo(line);
        await Assert.That(line.Y).IsGreaterThan(top + 8);
        await Assert.That(box.Y - (float) BorderStroke.Extent(box.Borders.Top.Style, box.Borders.Top.WidthPoints)).IsEqualTo(top).Within(0.01f);
    }

    // Three paragraphs alike but for w:shadow and w:frame on their edges are three boxes, not one
    // around the three; Word draws the heading that follows on the next page for the room they take.
    [Test]
    public async Task Edges_that_differ_in_shadow_or_frame_do_not_share_a_box()
    {
        var page = fixture.Value.Pages[3];
        var shadow = Line(page, "w:shadow=\"true\"");
        var frame = Line(page, "w:frame=\"true\"");
        var both = Line(page, "both");
        var boxes = page.Items.OfType<PlacedBorder>().Where(_ => _.Y > shadow.Y - 10 && _.Height > 0).ToList();

        await Assert.That(boxes.Count).IsEqualTo(3);
        await Assert.That(shadow.Paragraph.Properties.SharesBorderGroupWith(frame.Paragraph.Properties)).IsFalse();
        await Assert.That(frame.Paragraph.Properties.SharesBorderGroupWith(both.Paragraph.Properties)).IsFalse();
        await Assert.That(fixture.Value.Pages[4].Items.OfType<PlacedLine>().Any(_ => _.Runs.Any(run => run.Text.StartsWith("10. Table cell borders")))).IsTrue();
    }

    // The shadow: a black band the edge's width, outside the right and bottom rules, each starting
    // the opposite rule's width in. The frame-only box casts none.
    [Test]
    public async Task A_shadowed_box_casts_a_band_on_its_right_and_bottom()
    {
        var page = fixture.Value.Pages[3];
        var shadow = Line(page, "w:shadow=\"true\"");
        var frame = Line(page, "w:frame=\"true\"");
        var bands = page.Items.OfType<PlacedShading>().Where(_ => _.ColorHex == "000000").ToList();
        var box = page.Items.OfType<PlacedBorder>().First(_ => _.Y <= shadow.Y && _.Y + _.Height >= shadow.Y + shadow.Height);

        var own = bands.Where(_ => _.Y >= box.Y - 0.01f && _.Y < frame.Y - 2).ToList();
        await Assert.That(own.Count).IsEqualTo(2);

        var right = own.Single(_ => _.Height > _.Width);
        var bottom = own.Single(_ => _.Width > _.Height);
        const float rule = 1.5f;
        await Assert.That(right.Width).IsEqualTo(rule).Within(0.01f);
        await Assert.That(bottom.Height).IsEqualTo(rule).Within(0.01f);
        await Assert.That(right.X).IsEqualTo(box.X + box.Width + rule).Within(0.01f);
        await Assert.That(right.Y).IsEqualTo(box.Y).Within(0.01f);
        await Assert.That(bottom.Y).IsEqualTo(box.Y + box.Height + rule).Within(0.01f);
        await Assert.That(bottom.X).IsEqualTo(box.X).Within(0.01f);
        await Assert.That(right.Y + right.Height).IsEqualTo(bottom.Y + bottom.Height).Within(0.01f);

        // Two shadowed boxes on the page (shadow, both), two bands each.
        await Assert.That(bands.Count).IsEqualTo(4);
    }

    // A w:between rule takes its own room: the members' lines stand 31.2pt apart on Word's page —
    // the 14.1pt line, its 8pt after, and 4pt + the 1pt rule + 4pt — and each rule lies under the
    // after-spacing and one space.
    [Test]
    public async Task A_between_rule_and_its_space_on_either_side_take_flow()
    {
        var page = fixture.Value.Pages[3];
        var members = page.Items.OfType<PlacedLine>().Where(_ => _.Runs.Any(run => run.Text.StartsWith("Group member"))).ToList();
        var rules = page.Items.OfType<PlacedBorder>().Where(_ => _.Height == 0 && _.Y > members[0].Y && _.Y < members[2].Y).ToList();

        await Assert.That(members.Count).IsEqualTo(3);
        await Assert.That(members[1].Y - members[0].Y).IsEqualTo(31.14f).Within(0.1f);
        await Assert.That(members[2].Y - members[1].Y).IsEqualTo(31.14f).Within(0.1f);
        await Assert.That(rules.Count).IsEqualTo(2);

        // The rule's foot: line bottom + 8pt after + 4pt space + the 1pt rule; the next line 4pt on.
        await Assert.That(rules[0].Y).IsEqualTo(members[0].Y + members[0].Height + 8 + 4 + 1).Within(0.05f);
        await Assert.That(members[1].Y - rules[0].Y).IsEqualTo(4f).Within(0.05f);
    }
}
