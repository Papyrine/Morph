/// <summary>
/// Which cell of a vertical merge supplies which edge. Word-read with 6pt edges on a three-row merge
/// (<c>_probe_vborders</c> / <c>_probe_vborders2</c>, <c>docs/word-features.md</c> Vertical Merge): the
/// top is the head's, the bottom the last cell's, and each row's stretch of the sides its own cell's.
/// </summary>
public class MergedCellBorderTests
{
    static readonly PageSettings page = new() { WidthPoints = 400, HeightPoints = 600, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task The_bottom_comes_from_the_last_cell_of_the_merge()
    {
        var merged = MergedCell(Table(null, null, Declares(BorderSides.Bottom, "00A000")));

        await Assert.That(merged.Borders!.Bottom.ColorHex).IsEqualTo("00A000");
    }

    [Test]
    public async Task The_heads_own_bottom_is_not_drawn()
    {
        var merged = MergedCell(Table(Declares(BorderSides.Bottom, "0000FF"), null, null));

        await Assert.That(merged.Borders!.Bottom.IsVisible).IsFalse();
    }

    [Test]
    public async Task A_continuations_top_is_not_drawn()
    {
        var merged = MergedCell(Table(Declares(BorderSides.Top, "0000FF"), null, Declares(BorderSides.Top, "00A000")));

        await Assert.That(merged.Borders!.Top.ColorHex).IsEqualTo("0000FF");
    }

    [Test]
    public async Task Each_row_draws_its_own_cells_sides()
    {
        var table = Table(Declares(BorderSides.Left, "0000FF"), Declares(BorderSides.Left, "00A000"), null);

        var rows = Rows(table);
        var merged = MergedCell(table);
        var segments = rows[0].Cells.Where(_ => _.Content.Count == 0).ToList();

        await Assert.That(merged.Borders).IsNull();
        await Assert.That(segments.Select(_ => _.Borders!.Left.IsVisible ? _.Borders.Left.ColorHex : null))
            .IsEquivalentTo(["0000FF", "00A000", null]);
        await Assert.That(segments.Select(_ => _.Y)).IsEquivalentTo(rows.Select(_ => _.Y));
    }

    static PlacedCell MergedCell(TableElement table) =>
        Rows(table)[0].Cells.First(_ => _.Content.Count > 0);

    static List<PlacedTableRow> Rows(TableElement table) =>
        new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page).Pages[0].Items.OfType<PlacedTableRow>().ToList();

    // A borderless two-column table whose first column merges down three 30pt rows.
    static TableElement Table(CellBorders? head, CellBorders? middle, CellBorders? last) => new()
    {
        Properties = new() { GridColumnWidths = [150, 150] },
        Rows =
        [
            Row(Cell("head", VerticalMergeType.Restart, head), Cell("R1")),
            Row(Cell("", VerticalMergeType.Continue, middle), Cell("R2")),
            Row(Cell("", VerticalMergeType.Continue, last), Cell("R3"))
        ]
    };

    static TableRow Row(params List<TableCell> cells) =>
        new() { HeightPoints = 30, Cells = cells };

    static TableCell Cell(string text, VerticalMergeType merge = VerticalMergeType.None, CellBorders? borders = null) => new()
    {
        Properties = new() { VerticalMerge = merge, Borders = borders },
        Content = [Paragraph(text)]
    };

    // A w:tcBorders naming one 6pt side, as a template's continuation cell does.
    static CellBorders Declares(BorderSides side, string color)
    {
        var edge = new BorderEdge { IsVisible = true, WidthPoints = 6, ColorHex = color };
        return side switch
        {
            BorderSides.Top => new() { Top = edge, Declared = side },
            BorderSides.Bottom => new() { Bottom = edge, Declared = side },
            _ => new() { Left = edge, Declared = side }
        };
    }

    static ParagraphElement Paragraph(string text) => new()
    {
        Runs = text.Length == 0 ? [] : [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = new() { SpacingAfterPoints = 0, LineSpacingMultiplier = 1 }
    };
}
