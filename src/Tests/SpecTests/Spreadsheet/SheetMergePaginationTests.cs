/// <summary>
/// Excel breaks a sheet's pages between rows whatever merges cross them, and prints a merge the break
/// parts on both pages, each cut at the rows its page holds. weekly-lesson-planner's first sheet is the
/// case: its five-row "FRI" label (B25:B29) and the notes column beside the week (I25:I29) cross the
/// page's foot, and Excel prints rows 28 and 29 overleaf, the label's fill resumed there without its text.
/// </summary>
public class SheetMergePaginationTests
{
    static LaidOutDocument Layout()
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "weekly-lesson-planner", "input.xlsx"));
        var document = ExcelConverter.Parse(stream, null, null, null, useLetterPageSize: false, onePagePerSheet: false);
        return new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(document.Elements, document.PageSettings);
    }

    static List<PlacedTableRow> Rows(LaidOutPage page) =>
        page.Items.OfType<PlacedTableRow>().Where(_ => !_.IsRepeatedHeader).ToList();

    [Test]
    public async Task A_merge_the_break_parts_is_cut_at_the_pages_foot()
    {
        var rows = Rows(Layout().Pages[0]);
        var foot = rows.Max(_ => _.Y + _.Height);

        // Rows 28 and 29 go overleaf though they continue a merge on this page.
        await Assert.That(rows[^1].RowIndex).IsEqualTo(26);
        var cells = rows.SelectMany(_ => _.Cells).ToList();
        await Assert.That(cells.All(_ => _.Y + _.Height <= foot + 0.01f)).IsTrue();

        // The label is cut there, its text clipped where the whole merge centres it, no edge at the cut.
        var label = rows.Single(_ => _.RowIndex == 24).Cells.First(_ => _.BackgroundColorHex != null);
        await Assert.That(label.Y + label.Height).IsEqualTo(foot).Within(0.01f);
        await Assert.That(label.ClipContent).IsTrue();
        await Assert.That(label.Borders?.Bottom ?? BorderEdge.None).IsEqualTo(BorderEdge.None);
    }

    [Test]
    public async Task The_page_overleaf_resumes_the_merge()
    {
        var rows = Rows(Layout().Pages[1]);
        var first = rows[0];

        await Assert.That(first.RowIndex).IsEqualTo(27);

        // The label's last two rows, its fill alone: the text stood above this page's top.
        var label = first.Cells.First(_ => _.BackgroundColorHex != null);
        await Assert.That(label.Y).IsEqualTo(first.Y).Within(0.01f);
        await Assert.That(label.Height).IsEqualTo(first.Height + rows[1].Height).Within(0.01f);
        await Assert.That(label.Content.All(_ => _.Y + _.Height <= first.Y + 0.01f)).IsTrue();
    }
}
