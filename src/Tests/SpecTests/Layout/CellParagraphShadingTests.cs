/// <summary>
/// Paragraph shading (<c>w:shd</c>) inside a table cell, which the cell layout never drew: brochures/06's
/// quote box is an olive-shaded, bordered pair of paragraphs in a cell, and rendered as a bare outline
/// with its white quote invisible.
/// </summary>
public class CellParagraphShadingTests
{
    static readonly PageSettings page = new() { WidthPoints = 400, HeightPoints = 600, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task A_bordered_run_of_shaded_paragraphs_fills_its_whole_box_behind_the_lines()
    {
        var shaded = new ParagraphProperties
        {
            BackgroundColorHex = "454C02",
            Borders = CellBorders.Uniform(new() { IsVisible = true, WidthPoints = 0.25 }),
            BorderTopSpacePoints = 10,
            BorderBottomSpacePoints = 10,
            SpacingAfterPoints = 0,
            LineSpacingMultiplier = 1
        };
        var content = Cell([Paragraph("quote", shaded), Paragraph("source", shaded)]);

        var border = content.OfType<PlacedBorder>().Single();
        var box = content.OfType<PlacedShading>().Single(_ => _.Height > 20);

        await Assert.That(box.ColorHex).IsEqualTo("454C02");
        await Assert.That((box.X, box.Y, box.Width, box.Height)).IsEqualTo((border.X, border.Y, border.Width, border.Height));
        await Assert.That(content.IndexOf(box)).IsLessThan(content.IndexOf(content.OfType<PlacedLine>().First()));
    }

    [Test]
    public async Task A_shaded_paragraph_without_borders_shades_its_lines()
    {
        var shaded = new ParagraphProperties
        {
            BackgroundColorHex = "FFCC00",
            SpacingAfterPoints = 0,
            LineSpacingMultiplier = 1
        };
        var content = Cell([Paragraph("one", shaded)]);

        var line = content.OfType<PlacedLine>().Single();
        var band = content.OfType<PlacedShading>().Single();

        await Assert.That(band.ColorHex).IsEqualTo("FFCC00");
        await Assert.That((band.Y, band.Height)).IsEqualTo((line.Y, line.Height));
    }

    static List<PlacedItem> Cell(List<DocumentElement> content)
    {
        var table = new TableElement
        {
            Properties = new() { GridColumnWidths = [300] },
            Rows = [new() { Cells = [new() { Content = content }] }]
        };
        return new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page).Pages[0].Items
            .OfType<PlacedTableRow>().Single().Cells.Single().Content.ToList();
    }

    static ParagraphElement Paragraph(string text, ParagraphProperties properties) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = properties
    };
}
