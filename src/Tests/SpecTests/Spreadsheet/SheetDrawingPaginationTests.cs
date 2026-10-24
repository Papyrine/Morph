/// <summary>
/// A sheet's drawings follow its rows down the pages. Excel prints a drawing with the rows it stands
/// over, so one that a page break parts comes out in two pieces, each cut where its page's rows end.
/// invoice-accessibility-guide's first sheet is the case to hand: a banner, a contents list and a
/// thumbnail over a grid of a dozen narrow cells, which Excel prints on two landscape pages, the lower
/// part of the banner and its logo at the head of the second.
/// </summary>
public class SheetDrawingPaginationTests
{
    static readonly string workbook = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "invoice-accessibility-guide", "input.xlsx");

    static LaidOutDocument Layout(ParsedDocument document) =>
        new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(document.Elements, document.PageSettings);

    static ParsedDocument Parse(bool onePagePerSheet = false)
    {
        using var stream = File.OpenRead(workbook);
        return ExcelConverter.Parse(stream, null, null, null, useLetterPageSize: false, onePagePerSheet);
    }

    static IEnumerable<PlacedTableRow> Rows(LaidOutPage page) =>
        page.Items.OfType<PlacedTableRow>().Where(_ => !_.IsRepeatedHeader);

    [Test]
    public async Task The_second_page_carries_the_drawings_over_its_own_rows()
    {
        var laidOut = Layout(Parse());
        var first = laidOut.Pages[0];
        var second = laidOut.Pages[1];

        // Both pages of the sheet hold rows of the one grid, and drawings clipped to those rows.
        var table = Rows(first).First().Table;
        await Assert.That(Rows(second).All(_ => ReferenceEquals(_.Table, table))).IsTrue();
        foreach (var page in new[] {first, second})
        {
            var rows = Rows(page).ToList();
            var top = rows.Min(_ => _.Y);
            var bottom = rows.Max(_ => _.Y + _.Height);
            var clips = page.Items.OfType<PlacedClipGroup>().ToList();

            await Assert.That(clips.Count).IsGreaterThan(0);
            foreach (var clip in clips)
            {
                await Assert.That(clip.Y).IsEqualTo(top).Within(0.01f);
                await Assert.That(clip.Y + clip.Height).IsEqualTo(bottom).Within(0.01f);
            }
        }

        // The banner stands over both pages' rows: the same picture, cut at the foot of the first
        // page's rows and carried on from the top of the second's.
        static IEnumerable<PlacedItem> Drawn(LaidOutPage page) =>
            page.Items.OfType<PlacedClipGroup>().SelectMany(_ => _.Items);

        var firstBottom = Rows(first).Max(_ => _.Y + _.Height);
        var secondTop = Rows(second).Min(_ => _.Y);
        var parted = Drawn(first).Where(_ => _.Y + _.Height > firstBottom + 1).ToList();
        await Assert.That(parted.Count).IsGreaterThan(0);
        foreach (var piece in parted)
        {
            var overhang = piece.Y + piece.Height - firstBottom;
            var carried = Drawn(second)
                .Any(_ => Math.Abs(_.Height - piece.Height) < 0.01f &&
                          Math.Abs(_.Y + _.Height - secondTop - overhang) < 0.01f);
            await Assert.That(carried).IsTrue();
        }
    }

    // fPrintsWithSheet="0" on a drawing's anchor: it is on the sheet and not on the paper. The
    // sheet's "Let's go >" button is one. The model keeps it, marked, so the text exports still
    // carry it; the pages do not; and drawn whole rather than printed, the sheet shows it.
    [Test]
    public async Task A_drawing_that_does_not_print_with_the_sheet_is_left_off_the_page()
    {
        static FloatingTextBoxElement Button(ParsedDocument document) =>
            document.Elements.OfType<FloatingTextBoxElement>().First(_ => _.Content.OfType<ParagraphElement>().Any(paragraph => paragraph.Runs.Any(run => run.Text.Contains("go"))));

        static bool Drawn(LaidOutDocument laidOut) =>
            laidOut.Pages.Take(2).Any(_ => DocumentConverter.Lines(_.Items).Any(line => string.Concat(line.Runs.Select(run => run.Text)).Contains("go >")));

        var printed = Parse();
        await Assert.That(Button(printed).NotPrinted).IsTrue();
        await Assert.That(Drawn(Layout(printed))).IsFalse();

        var whole = Parse(onePagePerSheet: true);
        await Assert.That(Button(whole).NotPrinted).IsFalse();
        await Assert.That(Drawn(Layout(whole))).IsTrue();
    }
}
