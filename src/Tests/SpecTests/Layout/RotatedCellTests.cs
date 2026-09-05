/// <summary>
/// A <c>w:textDirection</c> cell lays its content out against the cell's height and the painters rotate
/// it into place (<see cref="PlacedRotatedGroup"/>) — the engine-flip orphan behind feature_capture/01's
/// horizontal "Header".
/// </summary>
public class RotatedCellTests
{
    [Test]
    public async Task A_btLr_cell_wraps_against_its_height_and_rotates_minus_ninety()
    {
        var page = new PageSettings { WidthPoints = 400, HeightPoints = 400, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };
        var table = new TableElement
        {
            Rows =
            [
                new()
                {
                    HeightPoints = 120,
                    Cells =
                    [
                        new() { Properties = new() { TextDirection = CellTextDirection.BottomToTop }, Content = [Paragraph("Header")] },
                        new() { Properties = new(), Content = [Paragraph("body")] }
                    ]
                }
            ],
            Properties = new() { GridColumnWidths = [60, 300] }
        };

        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page);
        var row = laidOut.Pages[0].Items.OfType<PlacedTableRow>().First();
        var group = row.Cells[0].Content.OfType<PlacedRotatedGroup>().Single();

        await Assert.That(group.RotationDegrees).IsEqualTo(-90d);
        // The unrotated box is the cell's height by its width, centred on the cell.
        await Assert.That(group.Width).IsEqualTo(row.Cells[0].Height).Within(0.01f);
        await Assert.That(group.Height).IsEqualTo(row.Cells[0].Width).Within(0.01f);
        await Assert.That(group.X + group.Width / 2).IsEqualTo(row.Cells[0].X + row.Cells[0].Width / 2).Within(0.01f);
        await Assert.That(group.Y + group.Height / 2).IsEqualTo(row.Cells[0].Y + row.Cells[0].Height / 2).Within(0.01f);
        await Assert.That(group.Items.OfType<PlacedLine>().Count()).IsEqualTo(1);
        await Assert.That(row.Cells[1].Content.OfType<PlacedRotatedGroup>().Count()).IsEqualTo(0);
    }

    static ParagraphElement Paragraph(string text) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = new()
    };
}
