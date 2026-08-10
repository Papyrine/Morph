/// <summary>
/// Covers <c>w:tl2br</c> / <c>w:tr2bl</c> diagonal cell-border parsing and end-to-end flow.
/// Diagonals are cell-level only — they don't appear in <c>w:tblBorders</c> or
/// table-style fallbacks, so the full coverage point is the cell-level <c>w:tcBorders</c>
/// path inside <c>DocumentParser</c>.
/// </summary>
public class TableDiagonalBordersTests
{
    [Test]
    public async Task CellDiagonals_Default_HasNoVisibleEdges()
    {
        var diagonals = new CellDiagonals();

        await Assert.That(diagonals.HasAny).IsFalse();
    }

    [Test]
    public async Task CellDiagonals_HasAny_TrueWhenOnlyDownSet()
    {
        var diagonals = new CellDiagonals
        {
            Down = new()
            {
                IsVisible = true,
                WidthPoints = 1,
                ColorHex = "000000"
            }
        };

        await Assert.That(diagonals.HasAny).IsTrue();
    }

    [Test]
    public async Task DocumentParser_ParsesDiagonalsAndKeepsTableCascade()
    {
        // The fixture's cells specify *only* diagonal children inside w:tcBorders;
        // the four sides come from the table's w:tblBorders and must still show up.
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "table_diagonal_borders", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var table = doc.Elements.OfType<TableElement>().First();
        var cells = table.Rows[0].Cells;

        // Cell 0: tl2br only — no side overrides, so cellProps.Borders stays null and
        // the table-level outer/inside borders cascade through.
        var c0 = cells[0].Properties;
        await Assert.That(c0.Borders).IsNull();
        await Assert.That(c0.Diagonals).IsNotNull();
        await Assert.That(c0.Diagonals!.Down.IsVisible).IsTrue();
        await Assert.That(c0.Diagonals.Up.IsVisible).IsFalse();

        // Cell 1: tr2bl only.
        var c1 = cells[1].Properties;
        await Assert.That(c1.Borders).IsNull();
        await Assert.That(c1.Diagonals!.Down.IsVisible).IsFalse();
        await Assert.That(c1.Diagonals.Up.IsVisible).IsTrue();

        // Cell 2: both diagonals, distinct colours.
        var both = cells[2].Properties.Diagonals!;
        await Assert.That(both.Down.IsVisible).IsTrue();
        await Assert.That(both.Down.ColorHex).IsEqualTo("FF0000");
        await Assert.That(both.Up.IsVisible).IsTrue();
        await Assert.That(both.Up.ColorHex).IsEqualTo("0000FF");
    }
}
