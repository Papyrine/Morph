/// <summary>
/// A paragraph inside a split table row fits only when its after-spacing fits with it — XPS-read on
/// <c>_probe_cellheight2</c>, where the same 89 Normal paragraphs fill 28 lines of a flow page and 27
/// of a cell, the 28th moving overleaf with the 8pt it could not fit. The flow lets that spacing hang
/// past the page bottom; the cell does not.
/// </summary>
public class CellSplitAfterSpacingTests
{
    [Test]
    public async Task A_cell_line_moves_overleaf_when_its_after_spacing_would_not_fit()
    {
        const float after = 20f;
        var page = new PageSettings { WidthPoints = 300, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };
        var contentBottom = (float) (page.HeightPoints - page.MarginBottom);

        var paragraphs = Enumerable.Range(1, 40).Select(_ => Paragraph($"line {_}", after)).ToList<DocumentElement>();
        var table = new TableElement
        {
            Rows = [new() { Cells = [new() { Properties = new(), Content = paragraphs }] }],
            Properties = new() { GridColumnWidths = [260] }
        };

        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page);
        var firstPageRows = laidOut.Pages[0].Items.OfType<PlacedTableRow>().ToList();
        var cellLines = firstPageRows.SelectMany(_ => _.Cells).SelectMany(_ => _.Content).OfType<PlacedLine>().ToList();

        await Assert.That(laidOut.Pages.Count).IsGreaterThan(1);
        await Assert.That(cellLines.Count).IsGreaterThan(3);
        foreach (var line in cellLines)
        {
            await Assert.That(line.Y + line.Height + after).IsLessThanOrEqualTo(contentBottom + 0.01f);
        }

        // Paragraph n occupies n lines and n - 1 gaps before its own spacing must fit too: n × (line +
        // after) within the content band. Without the reserve one more paragraph would fit (its line
        // would, its spacing would not).
        var lineHeight = LayoutTestFonts.Measurer.LayoutLineContents(Paragraph("line", after), 260)[0].Height;
        var contentHeight = (float) (page.HeightPoints - page.MarginTop - page.MarginBottom);
        var withReserve = (int) Math.Floor(contentHeight / (lineHeight + after) + 0.001f);
        var withoutReserve = (int) Math.Floor((contentHeight + after) / (lineHeight + after) + 0.001f);
        await Assert.That(withoutReserve).IsEqualTo(withReserve + 1);
        await Assert.That(cellLines.Count).IsEqualTo(withReserve);
    }

    static ParagraphElement Paragraph(string text, float after) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = new() { SpacingAfterPoints = after }
    };
}
