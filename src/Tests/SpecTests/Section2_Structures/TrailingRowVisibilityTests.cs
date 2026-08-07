/// <summary>
/// A table absorbs a trailing run of rows that draw nothing rather than carrying them onto a
/// continuation page (<c>Fragmenter</c>'s <c>lastVisibleRow</c>, fed by
/// <see cref="TableLayout.RowHasVisibleContent"/>). The question this pins is what counts as drawing
/// nothing when a cell holds a nested table.
///
/// <para>The predicate short-circuits on any non-paragraph cell element, so a row whose only content
/// is an EMPTY nested table counts as visible. That reads like an oversight — an empty, unbordered
/// table puts no ink on the page — and recursing into it to decide looks like the obvious fix. It is
/// not: Word was asked directly (<c>_probe_trailing_*</c>, 2026-08-07) and refutes it.</para>
///
/// <para>Probed twice, because the first attempt was confounded: its fixtures ended
/// <c>&lt;/w:tbl&gt;&lt;w:sectPr&gt;</c> with no paragraph after the table, which Word silently
/// repairs — so its blank continuation page read equally as "the row was carried" and as "the row was
/// absorbed and the repaired final paragraph took the page". The second probe
/// (<c>_probe_trail2_*</c>) de-confounds by putting a text paragraph AFTER the table and giving the
/// trailing row an explicit 100pt <c>w:trHeight</c>: the row's fate is then read off AFTER's position
/// on the continuation page. Shared geometry: 108 borderless single-cell rows of one 12pt exact line
/// fill two 648pt bands exactly, keeping the table over the 110% threshold that routes it row by row
/// (at 55 rows the whole-table path takes it instead and simply overflows, testing nothing).</para>
///
/// <code>
///   trailing 100pt row holds    Word AFTER ink          reading
///   an empty paragraph          174.72pt                carried, height honoured
///   an empty nested table       174.72pt                carried, height honoured
///   a bordered nested table     72-85 ink + 174.72pt    carried, drawn
/// </code>
///
/// <para>Word carries ALL of them — 174.72 = 72pt margin + the 100pt row + the first-line ink offset,
/// where absorption predicts ~74.4. So the short-circuit reproduces Word for both nested cases, and
/// recursing would have turned a matching case into a failing one. The genuine divergences the probe
/// exposed are elsewhere and are recorded in <c>src/todo.md</c>: the engine absorbs the trailing
/// empty-PARAGRAPH row Word carries (AFTER at 75.36), and it drops the natural-overflow blank page
/// Word renders (<c>_probe_trail2_flowblank</c>: 54 exact lines plus a trailing empty paragraph is 2
/// pages in Word, page 2 blank — the engine's <c>FinishPage</c> comment claims Word does not render
/// that page, and the probe refutes it). Both touch corpus-motivated rules (letter-template spacer
/// rows, blank-page suppression), so they need their own adjudication.</para>
/// </summary>
public class TrailingRowVisibilityTests
{
    static TableRow RowWith(params DocumentElement[] content) =>
        new() {Cells = [new TableCell {Content = content}]};

    static ParagraphElement EmptyParagraph() => new() {Runs = []};

    static TableElement NestedTable(CellBorders? borders) =>
        new()
        {
            Rows = [RowWith(EmptyParagraph())],
            Properties = new() {DefaultBorders = borders}
        };

    /// <summary>An empty nested table keeps its row visible — Word carries such a row to a new page.</summary>
    [Test]
    public async Task A_row_holding_an_empty_nested_table_is_visible()
    {
        await Assert.That(TableLayout.RowHasVisibleContent(RowWith(NestedTable(borders: null)))).IsTrue();
        await Assert.That(TableLayout.RowHasVisibleContent(RowWith(NestedTable(CellBorders.All)))).IsTrue();
    }

    /// <summary>The trailing-spacer case the absorption rule exists for: no content, nothing drawn.</summary>
    [Test]
    public async Task A_row_holding_only_empty_paragraphs_is_not_visible() =>
        await Assert.That(TableLayout.RowHasVisibleContent(RowWith(EmptyParagraph(), EmptyParagraph()))).IsFalse();

    /// <summary>Shading draws even with no content, so a filled cell keeps its row.</summary>
    [Test]
    public async Task A_row_with_a_shaded_cell_is_visible()
    {
        var row = new TableRow
        {
            Cells = [new TableCell {Content = [EmptyParagraph()], Properties = new() {BackgroundColorHex = "000000"}}]
        };

        await Assert.That(TableLayout.RowHasVisibleContent(row)).IsTrue();
    }
}
