/// <summary>
/// What a table cell does to the things its flow placed, beyond lines of text.
///
/// <para>A picture wider than its cell, Word-probed on <c>_probe_widepic</c> (a 300pt picture in a
/// 150pt cell of a fixed-layout table, left, centred and right aligned): Word draws the picture at
/// its own size from the cell's left content edge whatever the alignment and cuts it at the cell's
/// right border. It is not scaled to the cell. In a body paragraph the same picture runs past the
/// margin to the page's edge.</para>
///
/// <para>Vertical alignment moves everything the flow placed, the frame of an inline text box
/// included: brochures/08's "Contoso Logo" stood 39pt below its frame in a bottom-aligned
/// cell.</para>
/// </summary>
public class CellContentPlacementTests
{
    static readonly byte[] pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    static readonly PageSettings page = new() { WidthPoints = 400, HeightPoints = 400, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task A_picture_wider_than_its_cell_is_cut_at_the_cells_right_edge()
    {
        var row = Row(Table(Picture(300)));
        var cell = row.Cells[0];
        var image = cell.Content.OfType<PlacedLine>().Single().Images.Single();

        await Assert.That(image.X + image.Width).IsEqualTo(cell.X + cell.Width).Within(0.01f);
        await Assert.That(image.Height).IsEqualTo(40f).Within(0.01f);

        // The part past the edge is cropped off the source, so what is left is drawn at the
        // picture's own scale.
        var kept = image.Width / 300f;
        await Assert.That(image.Crop!.Right).IsEqualTo(1 - kept).Within(0.0001);
        await Assert.That(image.Crop.Left).IsEqualTo(0d);
    }

    [Test]
    public async Task An_earlier_crop_is_cut_further_in_proportion()
    {
        // Half the source is already hidden on the left, so the cut takes its share of the half
        // that shows.
        var row = Row(Table(Picture(300, new() { Left = 0.5 })));
        var cell = row.Cells[0];
        var image = cell.Content.OfType<PlacedLine>().Single().Images.Single();

        var kept = image.Width / 300f;
        await Assert.That(image.Crop!.Left).IsEqualTo(0.5);
        await Assert.That(image.Crop.Right).IsEqualTo(0.5 * (1 - kept)).Within(0.0001);
    }

    [Test]
    public async Task A_picture_that_fits_its_cell_is_left_whole()
    {
        var row = Row(Table(Picture(100)));
        var image = row.Cells[0].Content.OfType<PlacedLine>().Single().Images.Single();

        await Assert.That(image.Width).IsEqualTo(100f).Within(0.01f);
        await Assert.That(image.Crop).IsNull();
    }

    [Test]
    public async Task A_picture_wider_than_the_body_runs_past_the_margin()
    {
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([Picture(500)], page);
        var image = laidOut.Pages[0].Items.OfType<PlacedLine>().Single().Images.Single();

        await Assert.That(image.Width).IsEqualTo(500f).Within(0.01f);
        await Assert.That(image.Crop).IsNull();
    }

    [Test]
    public async Task Bottom_alignment_moves_an_inline_text_boxs_frame_with_its_text()
    {
        TableElement Boxed(CellVerticalAlignment alignment) => new()
        {
            Rows =
            [
                new()
                {
                    HeightPoints = 200,
                    Cells =
                    [
                        new()
                        {
                            Properties = new() { VerticalAlignment = alignment },
                            Content =
                            [
                                new WordArtElement
                                {
                                    Text = "LOGO",
                                    WidthPoints = 100,
                                    HeightPoints = 30,
                                    BoxLineColorHex = "000000",
                                    BoxLineWidthPoints = 1,
                                    Transform = WordArtTransform.None
                                }
                            ]
                        }
                    ]
                }
            ],
            Properties = new() { GridColumnWidths = [200] }
        };

        var top = Row(Boxed(CellVerticalAlignment.Top)).Cells[0].Content;
        var bottom = Row(Boxed(CellVerticalAlignment.Bottom)).Cells[0].Content;

        var frameMove = bottom.OfType<PlacedShape>().Single().Y - top.OfType<PlacedShape>().Single().Y;
        var textMove = bottom.OfType<PlacedLine>().First().Y - top.OfType<PlacedLine>().First().Y;

        await Assert.That(textMove).IsGreaterThan(100f);
        await Assert.That(frameMove).IsEqualTo(textMove).Within(0.01f);
    }

    static PlacedTableRow Row(TableElement table) =>
        new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page).Pages[0].Items.OfType<PlacedTableRow>().First();

    static TableElement Table(ParagraphElement content) => new()
    {
        Rows =
        [
            new()
            {
                Cells =
                [
                    new() { Properties = new() { WidthPoints = 150 }, Content = [content] },
                    new() { Properties = new() { WidthPoints = 150 }, Content = [new ParagraphElement { Runs = [], Properties = new() }] }
                ]
            }
        ],
        Properties = new() { GridColumnWidths = [150, 150], IsAutoFit = false }
    };

    static ParagraphElement Picture(double width, ImageCrop? crop = null) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = "",
                    Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 },
                    InlineImageData = pixel,
                    InlineImageContentType = "image/png",
                    InlineImageWidthPoints = width,
                    InlineImageHeightPoints = 40,
                    InlineImageCrop = crop
                }
            ],
            Properties = new() { LineSpacingMultiplier = 1 }
        };
}
