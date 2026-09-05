/// <summary>
/// A cell's diagonal borders (<c>w:tl2br</c> / <c>w:tr2bl</c>) reach the placed cell and stroke at the
/// grid-floored width like every other cell edge — they were parsed and then dropped on the way to the
/// painters (<see cref="PlacedCell.Diagonals"/>).
/// </summary>
public class CellDiagonalPlacementTests
{
    [Test]
    public async Task A_diagonal_strokes_at_its_floored_width()
    {
        await Assert.That(BorderStroke.DiagonalThickness(new() { IsVisible = true, WidthPoints = 0.5 })).IsEqualTo(0.6).Within(0.001);
        await Assert.That(BorderStroke.DiagonalThickness(new() { IsVisible = true, WidthPoints = 3 })).IsEqualTo(3d).Within(0.001);
        await Assert.That(BorderStroke.DiagonalThickness(BorderEdge.None)).IsEqualTo(0d);
    }

    [Test]
    public async Task The_fragmenter_carries_the_diagonals_onto_the_placed_cell()
    {
        var diagonals = new CellDiagonals { Down = new() { IsVisible = true, WidthPoints = 0.5, ColorHex = "FF0000" } };
        var table = new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new() { Properties = new() { Diagonals = diagonals }, Content = [Paragraph("x")] },
                        new() { Properties = new(), Content = [Paragraph("y")] }
                    ]
                }
            ],
            Properties = new() { GridColumnWidths = [100, 100] }
        };
        var page = new PageSettings { WidthPoints = 300, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page);
        var row = laidOut.Pages[0].Items.OfType<PlacedTableRow>().First();

        await Assert.That(row.Cells[0].Diagonals).IsSameReferenceAs(diagonals);
        await Assert.That(row.Cells[1].Diagonals).IsNull();
    }

    static ParagraphElement Paragraph(string text) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = new()
    };
}
