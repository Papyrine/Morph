/// <summary>
/// Footnotes stack at the bottom of the page their reference lands on, under a separator rule, and
/// endnotes flow after the body under their own — the Word laws XPS-read on the <c>_probe_fn_*</c>
/// fixtures (2026-09-05): the separator is the mark font's strikethrough stroke, 2in long (the column
/// width for a continuation); a note that misses its page splits at a line and displaces the body after
/// its reference; the endnote block moves whole when its separator and first line miss the page.
/// </summary>
public class FootnoteLayoutTests
{
    static readonly PageSettings page = new() { WidthPoints = 300, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task A_cited_note_sits_at_the_page_bottom_under_a_two_inch_rule()
    {
        var notes = Notes(("1", ["Note one."]));
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([Paragraph("Body", footnote: "1")], page, notes: notes);

        await Assert.That(laidOut.Pages.Count).IsEqualTo(1);
        var items = laidOut.Pages[0].Items;
        var rule = items.OfType<PlacedShading>().Single();
        var note = items.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "Note one."));
        var body = items.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "Body"));

        // The note's line box ends on the content bottom; the rule is 144pt from the column left and
        // sits above the note, below the body.
        await Assert.That(note.Y + note.Height).IsEqualTo(280).Within(0.05f);
        await Assert.That(rule.X).IsEqualTo(20).Within(0.01f);
        await Assert.That(rule.Width).IsEqualTo(144).Within(0.01f);
        await Assert.That(rule.Y).IsLessThan(note.Y);
        await Assert.That(rule.Y).IsGreaterThan(body.Y + body.Height);
        await Assert.That(rule.Height).IsGreaterThan(0);
    }

    [Test]
    public async Task The_rule_is_the_mark_font_strikethrough_stroke()
    {
        // Calibri's OS/2 stroke: 0.25em above the baseline, 0.0654em thick — at 20pt 5.0pt above and
        // 1.31pt thick, which the 120-dpi grid makes 1.2pt (Word's XPS: 4.8 / 1.2 on _probe_fn_l).
        var separator = new ParagraphElement
        {
            Runs = [],
            Properties = new() { ParagraphMarkRunProperties = new() { FontFamily = "Calibri", FontSizePoints = 20 }, SpacingAfterPoints = 0, LineSpacingMultiplier = 1 }
        };
        var notes = Notes(("1", ["Note one."])) with { FootnoteSeparator = separator };
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([Paragraph("Body", footnote: "1")], page, notes: notes);

        var rule = laidOut.Pages[0].Items.OfType<PlacedShading>().Single();
        var note = laidOut.Pages[0].Items.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "Note one."));
        var separatorLine = LayoutTestFonts.Measurer.LayoutLineContents(separator, 260)[0];
        var separatorTop = note.Y - separatorLine.Height;
        var baseline = separatorTop + separatorLine.Ascent;

        await Assert.That(rule.Height).IsEqualTo(1.2f).Within(0.01f);
        await Assert.That(baseline - rule.Y).IsEqualTo(5.0f).Within(0.05f);
    }

    [Test]
    public async Task The_body_stops_where_the_note_area_begins()
    {
        // Twenty 13.43pt lines overfill the 260pt band on their own (19 fit); the note under line 1
        // takes the separator and one line, so two fewer body lines fit page 1.
        var fill = Enumerable.Range(1, 20).Select(_ => (DocumentElement) Paragraph($"line {_}", footnote: _ == 1 ? "1" : null)).ToList();
        var plain = new Fragmenter(LayoutTestFonts.Measurer).Layout(fill, page);
        var noted = new Fragmenter(LayoutTestFonts.Measurer).Layout(fill, page, notes: Notes(("1", ["Note one."])));

        var plainFirstPage = plain.Pages[0].Items.OfType<PlacedLine>().Count();
        var notedFirstPage = noted.Pages[0].Items.OfType<PlacedLine>().Count(_ => _.Paragraph.Runs[0].Text.StartsWith("line"));
        var noteLine = noted.Pages[0].Items.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "Note one."));
        var lastBody = noted.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Paragraph.Runs[0].Text.StartsWith("line")).Max(_ => _.Y + _.Height);

        await Assert.That(notedFirstPage).IsEqualTo(plainFirstPage - 2);
        await Assert.That(noteLine.Y + noteLine.Height).IsEqualTo(280).Within(0.05f);
        await Assert.That(lastBody).IsLessThanOrEqualTo(noteLine.Y - LayoutTestFonts.Measurer.LayoutLineContents(DocumentNotes.DefaultSeparator(), 260)[0].Height + 0.05f);
    }

    [Test]
    public async Task A_long_note_splits_and_displaces_the_body_after_its_reference()
    {
        // A twenty-line note cited on line 2 of a ten-line body (13.43pt Aptos lines in a 260pt band):
        // page 1 keeps lines 1-2 and the sixteen note lines that fit under them; lines 3-10 open page 2
        // with the note's last four at the bottom under a full-width continuation rule.
        var body = Enumerable.Range(1, 10).Select(_ => (DocumentElement) Paragraph($"line {_}", footnote: _ == 2 ? "1" : null)).ToList();
        var noteText = Enumerable.Range(1, 20).Select(_ => $"note {_}").ToArray();
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout(body, page, notes: Notes(("1", noteText)));

        await Assert.That(laidOut.Pages.Count).IsEqualTo(2);
        var first = laidOut.Pages[0].Items;
        var second = laidOut.Pages[1].Items;
        var firstBody = first.OfType<PlacedLine>().Where(_ => _.Paragraph.Runs[0].Text.StartsWith("line")).Select(_ => _.Paragraph.Runs[0].Text).ToList();
        var secondBody = second.OfType<PlacedLine>().Where(_ => _.Paragraph.Runs[0].Text.StartsWith("line")).Select(_ => _.Paragraph.Runs[0].Text).ToList();
        var firstNotes = first.OfType<PlacedLine>().Count(_ => _.Paragraph.Runs[0].Text.StartsWith("note"));
        var secondNotes = second.OfType<PlacedLine>().Count(_ => _.Paragraph.Runs[0].Text.StartsWith("note"));

        await Assert.That(firstBody).IsEquivalentTo(["line 1", "line 2"]);
        await Assert.That(secondBody).IsEquivalentTo(Enumerable.Range(3, 8).Select(_ => $"line {_}").ToList());
        await Assert.That(firstNotes).IsEqualTo(16);
        await Assert.That(secondNotes).IsEqualTo(4);

        // The continuation rule spans the column; the note's last line ends on page 2's content bottom.
        var continuation = second.OfType<PlacedShading>().Single();
        await Assert.That(continuation.Width).IsEqualTo(260).Within(0.01f);
        var lastNote = second.OfType<PlacedLine>().Where(_ => _.Paragraph.Runs[0].Text.StartsWith("note")).Max(_ => _.Y + _.Height);
        await Assert.That(lastNote).IsEqualTo(280).Within(0.05f);
    }

    [Test]
    public async Task A_reference_keeps_its_note_first_line_on_its_page()
    {
        // Nineteen 13.43pt lines fill the 260pt band; a note cited on line 19 needs the separator and a
        // line under it, so line 19 moves to page 2 with its note.
        var body = Enumerable.Range(1, 20).Select(_ => (DocumentElement) Paragraph($"line {_}", footnote: _ == 19 ? "1" : null)).ToList();
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout(body, page, notes: Notes(("1", ["Note one."])));

        var firstBody = laidOut.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Paragraph.Runs[0].Text.StartsWith("line")).Select(_ => _.Paragraph.Runs[0].Text).ToList();
        await Assert.That(firstBody).Contains("line 18");
        await Assert.That(firstBody).DoesNotContain("line 19");
        await Assert.That(laidOut.Pages[0].Items.OfType<PlacedShading>().Count()).IsEqualTo(0);
        await Assert.That(laidOut.Pages[1].Items.OfType<PlacedShading>().Count()).IsEqualTo(1);
        await Assert.That(laidOut.Pages[1].Items.OfType<PlacedLine>().Any(_ => _.Runs.Any(run => run.Text == "Note one."))).IsTrue();
    }

    [Test]
    public async Task A_repeat_citation_places_the_note_once_and_a_table_cell_citation_counts()
    {
        var table = new TableElement
        {
            Rows = [new() { Cells = [new() { Properties = new(), Content = [Paragraph("cell", footnote: "2")] }] }],
            Properties = new() { GridColumnWidths = [260] }
        };
        var elements = new List<DocumentElement> { Paragraph("Body", footnote: "1"), table, Paragraph("Again", footnote: "1") };
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout(elements, page, notes: Notes(("1", ["Note one."]), ("2", ["Note two."])));

        var noteLines = laidOut.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Runs.Any(run => run.Text.StartsWith("Note"))).OrderBy(_ => _.Y).Select(_ => _.Runs[0].Text).ToList();
        await Assert.That(noteLines).IsEquivalentTo(["Note one.", "Note two."]);
    }

    [Test]
    public async Task Endnotes_flow_after_the_body_and_move_whole_when_their_block_misses_the_page()
    {
        var notes = Notes() with { Endnotes = new Dictionary<string, IReadOnlyList<DocumentElement>> { ["1"] = [Paragraph("Endnote one.")] } };

        // A short body: the rule follows it and the note follows the rule, on page 1.
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([Paragraph("Body", endnote: "1")], page, notes: notes);
        var body = laidOut.Pages[0].Items.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "Body"));
        var rule = laidOut.Pages[0].Items.OfType<PlacedShading>().Single();
        var note = laidOut.Pages[0].Items.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "Endnote one."));
        await Assert.That(rule.Y).IsGreaterThan(body.Y + body.Height);
        await Assert.That(note.Y).IsGreaterThan(rule.Y);
        await Assert.That(rule.Width).IsEqualTo(144).Within(0.01f);

        // Eighteen lines leave one line of room: the separator and first note line do not both fit, so
        // the block opens page 2 at the margin top.
        var fill = Enumerable.Range(1, 18).Select(_ => (DocumentElement) Paragraph($"line {_}", endnote: _ == 1 ? "1" : null)).ToList();
        var moved = new Fragmenter(LayoutTestFonts.Measurer).Layout(fill, page, notes: notes);
        await Assert.That(moved.Pages.Count).IsEqualTo(2);
        await Assert.That(moved.Pages[0].Items.OfType<PlacedShading>().Count()).IsEqualTo(0);
        var movedRule = moved.Pages[1].Items.OfType<PlacedShading>().Single();
        await Assert.That(movedRule.Y).IsLessThan(20 + 15);
    }

    static DocumentNotes Notes(params (string Id, string[] Lines)[] footnotes)
    {
        var bodies = new Dictionary<string, IReadOnlyList<DocumentElement>>();
        foreach (var (id, lines) in footnotes)
        {
            bodies[id] = lines.Select(_ => (DocumentElement) Paragraph(_)).ToList();
        }

        return new(
            bodies,
            new Dictionary<string, IReadOnlyList<DocumentElement>>(),
            DocumentNotes.DefaultSeparator(),
            DocumentNotes.DefaultSeparator(),
            DocumentNotes.DefaultSeparator(),
            DocumentNotes.DefaultSeparator());
    }

    static ParagraphElement Paragraph(string text, string? footnote = null, string? endnote = null)
    {
        var runs = new List<Run> { new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } } };
        if (footnote != null)
        {
            runs.Add(new() { Text = "1", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11, VerticalAlignment = VerticalRunAlignment.Superscript }, FootnoteReferenceId = footnote });
        }

        if (endnote != null)
        {
            runs.Add(new() { Text = "i", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11, VerticalAlignment = VerticalRunAlignment.Superscript }, EndnoteReferenceId = endnote });
        }

        return new() { Runs = runs, Properties = new() { SpacingAfterPoints = 0, LineSpacingMultiplier = 1 } };
    }
}
