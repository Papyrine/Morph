/// <summary>
/// A table row that does not fit the foot of its page and is carried whole to the next is an ordinary
/// row there. Read off Word's XPS (copies of <c>table_multipage</c> and <c>header_row_repeat/01</c>,
/// 2026-10-11): the first row of a continuation page is pitched exactly as every other row — 28.2pt
/// behind 3pt rules, 25.8pt behind 0.5pt ones. The engine placed such a row as a FRAGMENT of a split
/// row, which closes itself with a bottom rule an unsplit row does not pay, so it stood one rule too
/// tall and the rest of its page sat that much low.
/// </summary>
public class CarriedRowHeightTests
{
    static LaidOutDocument Layout(string scenario)
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", scenario, "input.docx"));
        var document = DocumentConverter.Parse(stream, null, false);
        return new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(document.Elements, document.PageSettings);
    }

    [Test]
    [Arguments("table_multipage")]
    [Arguments("header_row_repeat/01")]
    public async Task The_first_row_of_a_continuation_page_is_as_tall_as_the_rows_around_it(string scenario)
    {
        var laidOut = Layout(scenario);
        await Assert.That(laidOut.Pages.Count).IsGreaterThan(1);

        // Every row of these tables holds one line, so every row but the table's last (which also
        // carries the table's bottom rule) is the same height.
        var rows = laidOut.Pages[0].Items.OfType<PlacedTableRow>().ToList();
        var ordinary = rows[1].Height;
        foreach (var page in laidOut.Pages.Skip(1))
        {
            var carried = page.Items.OfType<PlacedTableRow>().First(_ => !_.IsRepeatedHeader);
            var following = page.Items.OfType<PlacedTableRow>().Where(_ => !_.IsRepeatedHeader).ElementAt(1);

            await Assert.That(carried.Height).IsEqualTo(ordinary).Within(0.01f);
            await Assert.That(following.Y - carried.Y).IsEqualTo(ordinary).Within(0.01f);
        }
    }
}
