/// <summary>
/// A table whose declared row floors miss the space left moves whole to the next region, as Word does
/// on every controlled floor fixture (<c>_probe_floorfit_single/_last/_mid/_enddoc</c>); a table of the
/// same height made of content alone keeps the shared rounding slack (todo #25 residue, closed 2026-09-05).
/// </summary>
public class TableFloorFitTests
{
    [Test]
    public async Task A_floored_table_that_misses_the_remainder_moves_whole()
    {
        var page = new PageSettings { WidthPoints = 300, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };
        var fill = Enumerable.Range(1, 16).Select(_ => (DocumentElement) Paragraph($"line {_}")).ToList();

        // Sixteen 14.5pt Aptos lines leave a remainder of roughly 28pt in the 260pt band; a 40pt floor
        // does not fit it, and its single 10pt line would fit with room to spare.
        var floored = Table(40, exact: false);
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([.. fill, floored], page);

        await Assert.That(laidOut.Pages.Count).IsEqualTo(2);
        await Assert.That(laidOut.Pages[0].Items.OfType<PlacedTableRow>().Count()).IsEqualTo(0);
        await Assert.That(laidOut.Pages[1].Items.OfType<PlacedTableRow>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task A_content_only_table_of_that_height_keeps_its_slack()
    {
        var page = new PageSettings { WidthPoints = 300, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };
        var fill = Enumerable.Range(1, 16).Select(_ => (DocumentElement) Paragraph($"line {_}")).ToList();

        var plain = Table(null, exact: false);
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([.. fill, plain], page);

        await Assert.That(laidOut.Pages.Count).IsEqualTo(1);
    }

    static TableElement Table(double? floor, bool exact) => new()
    {
        Rows =
        [
            new()
            {
                HeightPoints = floor,
                IsExactHeight = exact,
                Cells = [new() { Properties = new(), Content = [Paragraph("cell")] }]
            }
        ],
        Properties = new() { GridColumnWidths = [260] }
    };

    static ParagraphElement Paragraph(string text) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = new()
    };
}
