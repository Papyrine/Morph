/// <summary>
/// A <c>w:hRule="exact"</c> row is a verbatim box: a table carrying one that misses the space left flows
/// row by row — the rows that fit stay, the exact row that does not moves whole — and an exact row's
/// cells clip their overflow. Both read off Word's reference for <c>table_layout_tall_row</c> (an 80pt
/// company row that stays on page 1 with its third line hidden, a 530pt letter row that opens page 2).
/// </summary>
public class ExactRowTests
{
    static readonly PageSettings page = new() { WidthPoints = 300, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task The_rows_before_an_unfitting_exact_row_stay_and_the_exact_row_moves_whole()
    {
        // Five 13.43pt lines leave ~193pt; a 40pt exact row fits, the 200pt exact row after it does not.
        var fill = Enumerable.Range(1, 5).Select(_ => (DocumentElement) Paragraph($"line {_}")).ToList();
        var table = new TableElement
        {
            Rows = [ExactRow(40, "first"), ExactRow(200, "second")],
            Properties = new() { GridColumnWidths = [260] }
        };
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([.. fill, table], page);

        await Assert.That(laidOut.Pages.Count).IsEqualTo(2);
        var first = laidOut.Pages[0].Items.OfType<PlacedTableRow>().ToList();
        var second = laidOut.Pages[1].Items.OfType<PlacedTableRow>().ToList();
        await Assert.That(first.Select(_ => _.RowIndex)).IsEquivalentTo([0]);
        await Assert.That(second.Select(_ => _.RowIndex)).IsEquivalentTo([1]);
        await Assert.That(second[0].Y).IsEqualTo(20).Within(0.01f);
        await Assert.That(second[0].Height).IsEqualTo(200).Within(0.01f);
    }

    [Test]
    public async Task An_exact_row_clips_its_cells_and_an_at_least_row_does_not()
    {
        var exact = new TableElement { Rows = [ExactRow(10, "tall content")], Properties = new() { GridColumnWidths = [260] } };
        var atLeast = new TableElement
        {
            Rows = [new() { HeightPoints = 10, IsExactHeight = false, Cells = [new() { Properties = new(), Content = [Paragraph("tall content")] }] }],
            Properties = new() { GridColumnWidths = [260] }
        };

        var exactCell = new Fragmenter(LayoutTestFonts.Measurer).Layout([exact], page).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];
        var atLeastCell = new Fragmenter(LayoutTestFonts.Measurer).Layout([atLeast], page).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];

        await Assert.That(exactCell.ClipContent).IsTrue();
        await Assert.That(exactCell.Height).IsEqualTo(10).Within(0.01f);
        await Assert.That(atLeastCell.ClipContent).IsFalse();
        await Assert.That(atLeastCell.Height).IsGreaterThan(10);
    }

    static TableRow ExactRow(double height, string text) => new()
    {
        HeightPoints = height,
        IsExactHeight = true,
        Cells = [new() { Properties = new(), Content = [Paragraph(text)] }]
    };

    static ParagraphElement Paragraph(string text) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = new() { SpacingAfterPoints = 0, LineSpacingMultiplier = 1 }
    };
}
