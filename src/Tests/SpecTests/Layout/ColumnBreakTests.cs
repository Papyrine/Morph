using static ProbeLayout;

/// <summary>
/// A column break in a single-column section. Word-probed on <c>_probe_colbreak</c> (2026-11-25,
/// compatibility modes 12 and 15 alike), read off its XPS.
/// </summary>
public class ColumnBreakTests
{
    static List<string> PageTexts(LaidOutDocument laidOut) =>
        laidOut.Pages
            .Select(_ => string.Join("|", Lines(_).Select(_ => _.Text).Where(_ => _.Length > 0)))
            .ToList();

    [Test]
    public async Task A_column_break_starts_a_page()
    {
        var laidOut = Layout(Text("before") + Paragraph(Break("column")) + Text("after"), Section());

        await Assert.That(PageTexts(laidOut)).IsEquivalentTo(["before", "after"]);
    }

    // Word left the section's first page blank and set "C4 after" on the next.
    [Test]
    public async Task A_column_break_at_the_top_of_a_page_still_starts_another()
    {
        var laidOut = Layout(Text("before") + SectionBreak() + Paragraph(Break("column")) + Text("after"), Section());

        await Assert.That(PageTexts(laidOut)).IsEquivalentTo(["before", "", "after"]);
    }

    [Test]
    public async Task Two_column_breaks_leave_a_blank_page_between()
    {
        var laidOut = Layout(Text("before") + Paragraph(Break("column")) + Paragraph(Break("column")) + Text("after"), Section());

        await Assert.That(PageTexts(laidOut)).IsEquivalentTo(["before", "", "after"]);
    }

    // Word drew "C5 cell before C5 cell after" as one line, and the paragraph after the table under it.
    [Test]
    public async Task A_column_break_in_a_table_cell_is_no_break()
    {
        var cell = Paragraph(Run("cell before ") + Break("column") + Run("cell after"));
        var laidOut = Layout(Table(cell) + Text("after table"), Section());

        await Assert.That(PageTexts(laidOut)).IsEquivalentTo(["cell before cell after|after table"]);
    }
}
