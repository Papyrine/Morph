using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Sheet rules read off Excel's own prints of the corpus workbooks, or probed in Excel 16 over COM
/// (2026-11-22). Each test names the workbook whose reference shows the rule.
/// </summary>
public class SheetFidelityTests
{
    static IReadOnlyList<DocumentElement> Sheet(string workbook)
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", workbook, "input.xlsx"));
        return ExcelConverter.Parse(stream, null, ProjectFonts.Directory, useLetterPageSize: false, onePagePerSheet: false).Elements;
    }

    static TableElement Grid(string workbook) =>
        Sheet(workbook).OfType<TableElement>().First();

    static string Text(TableCell cell) =>
        string.Concat(cell.Content.OfType<ParagraphElement>().SelectMany(_ => _.Runs).Select(_ => _.Text));

    static IEnumerable<(TableRow Row, TableCell Cell)> Cells(TableElement grid) =>
        grid.Rows.SelectMany(row => row.Cells.Select(cell => (row, cell)));

    static TableCell Cell(TableElement grid, string text) =>
        Cells(grid).First(_ => Text(_.Cell) == text).Cell;

    // An indent level is three spaces of the body font, each rounded to a 600-dpi printer dot and
    // unscaled by the print zoom: the calendar's five-level "Week 1" stands 50.4pt in under an Arial 12
    // body, printed at 59%.
    [Test]
    public async Task An_indent_is_three_spaces_of_the_body_font_whatever_the_zoom()
    {
        var paragraph = Cell(Grid("social-media-editorial-theme-calendar"), "Week 1").Content.OfType<ParagraphElement>().First();

        // Arial 12's space is 27.78 of the printer's 600-dpi dots: 28, three to a level, 84 dots
        await Assert.That(paragraph.Properties.LeftIndentPoints).IsEqualTo(5 * 84 * 72 / 600.0).Within(0.001);
    }

    // A rich string keeps its runs' own look: "Strengths:" is bold before a regular remainder.
    [Test]
    public async Task A_rich_strings_runs_keep_their_own_look()
    {
        var runs = Cells(Grid("business-plan-checklist"))
            .Select(_ => _.Cell.Content.OfType<ParagraphElement>().SelectMany(paragraph => paragraph.Runs).ToList())
            .First(_ => _.Count > 1 && _[0].Text.StartsWith("Strengths:"));

        await Assert.That(runs[0].Properties.Bold).IsTrue();
        await Assert.That(runs[1].Properties.Bold).IsFalse();
    }

    // With no print area Excel prints to the last cell that shows something: construction-proposal's
    // I, J and P36 carry a font and an alignment only, and A to H print.
    [Test]
    public async Task With_no_print_area_only_cells_that_show_something_extend_the_range() =>
        await Assert.That(Grid("construction-proposal").Properties.GridColumnWidths!.Count).IsEqualTo(8);

    // Its $B:$B print titles are fitted in twice: 61%, where the range alone fits at 71%.
    [Test]
    public async Task Print_title_columns_count_again_in_the_fit()
    {
        var header = Cell(Grid("construction-proposal"), "QUANTITY").Content.OfType<ParagraphElement>().First().Runs[0];

        await Assert.That(header.Properties.FontSizePoints).IsEqualTo(14 * 0.61).Within(0.01);
    }

    // An expression rule is worked out: $F6="completed" fills the completed task.
    [Test]
    public async Task An_expression_rule_is_worked_out()
    {
        var grid = Grid("to-do-list");

        await Assert.That(Cell(grid, "Completed").Properties.BackgroundColorHex)!
            .IsNotEqualTo(Cell(grid, "Pending").Properties.BackgroundColorHex);
    }

    // A data bar runs from 10% of the cell to 90% between the range's least and greatest values.
    [Test]
    public async Task A_data_bar_fills_its_share_of_the_cell()
    {
        var bars = Cells(Grid("to-do-list-for-projects"))
            .Select(_ => _.Cell.Properties.DataBar)
            .OfType<CellDataBar>()
            .Select(_ => _.Fraction)
            .ToList();

        await Assert.That(bars.Max()).IsEqualTo(0.9).Within(0.0001);
        await Assert.That(bars.Min()).IsEqualTo(0.1).Within(0.0001);
    }

    // A data bar with showValue="0" hides the value: to-do-list's progress is the bar alone.
    [Test]
    public async Task A_data_bar_can_hide_its_value()
    {
        var cell = Cells(Grid("to-do-list")).First(_ => _.Cell.Properties.DataBar != null).Cell;

        await Assert.That(Text(cell)).IsEmpty();
    }

    // The progress bar's merge, I5:L5, runs past the first page's edge at K: Excel lays the bar's third
    // over all four columns and prints the part on the page, so it is a third of I:L, not of I:K.
    [Test]
    public async Task A_data_bar_in_a_cut_merge_spans_the_whole_merge()
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "to-do-list", "input.xlsx"));
        var document = ExcelConverter.Parse(stream, null, ProjectFonts.Directory, useLetterPageSize: false, onePagePerSheet: false);
        var bar = Cells(document.Elements.OfType<TableElement>().First()).Select(_ => _.Cell.Properties.DataBar).OfType<CellDataBar>().First();

        await Assert.That(bar.CutAfterPoints).IsGreaterThan(0);
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility).Layout(document.Elements, document.PageSettings);
        var cell = laidOut.Pages[0].Items.OfType<PlacedTableRow>().SelectMany(_ => _.Cells)
            .First(_ => _.Content.OfType<PlacedShading>().Any(shading => shading.ColorHex == bar.ColorHex));
        var placed = cell.Content.OfType<PlacedShading>().First(_ => _.ColorHex == bar.ColorHex);
        var pixel = 0.75 * bar.InsetScale;
        var whole = cell.Width + bar.CutAfterPoints - 5 * pixel;
        await Assert.That((double) placed.Width).IsEqualTo(whole * bar.Fraction).Within(0.01);
    }

    // An icon set flags an item to reorder with a red flag, the value hidden, and gives the rest none.
    [Test]
    public async Task An_icon_set_shows_its_icon_alone()
    {
        var icons = Cells(Grid("inventory-list-with-highlighting"))
            .Select(_ => _.Cell.Properties.Icon)
            .OfType<CellIcon>()
            .ToList();

        await Assert.That(icons).IsNotEmpty();
        await Assert.That(icons.All(_ => _ is { Set: "3Flags", Id: 0, Alignment: TextAlignment.Center })).IsTrue();
    }

    // A position no cell is written for takes its column's style: address-and-phone-list's cream.
    [Test]
    public async Task An_unwritten_cell_takes_its_columns_style()
    {
        var grid = Grid("address-and-phone-list");

        await Assert.That(grid.Rows[1].Cells[0].Properties.BackgroundColorHex).IsNotNull();
    }

    // An accounting format's * fill sets the currency sign at the left and the number at the right.
    [Test]
    public async Task An_accounting_fill_spans_the_cell()
    {
        var runs = Cells(Grid("back-to-school-planner"))
            .Select(_ => _.Cell.Content.OfType<ParagraphElement>().First())
            .First(_ => _.Runs.Any(run => run.IsTab));

        await Assert.That(runs.Runs[0].Text.Trim()).IsEqualTo("$");
        await Assert.That(runs.Properties.TabStops.Single().Alignment).IsEqualTo(TabAlignment.Right);
    }

    // Number formats keep the fill's place only when asked; elsewhere it is dropped as before.
    [Test]
    public async Task A_fill_is_marked_only_where_it_is_asked_for()
    {
        const string accounting = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";

        await Assert.That(NumberFormat.FormatWithFill(55, accounting).Text).Contains(NumberFormat.FillMark);
        await Assert.That(NumberFormat.Format(55, accounting).Text).DoesNotContain(NumberFormat.FillMark);
    }

    // A bottom edge is handed to the cell beneath when that cell states no top edge, so its fill
    // cannot paint over it: the rule under Tax stands over the Total row's white.
    [Test]
    public async Task A_bottom_edge_is_handed_to_the_cell_beneath()
    {
        var total = Cell(Grid("simple-blue-timesheet-invoice"), "Total");

        await Assert.That(total.Properties.Borders?.Top.IsVisible).IsTrue();
    }

    // A shape's text shrinks with the sheet: the 16pt navigation labels print at the sheet's scale.
    [Test]
    public async Task A_shapes_text_shrinks_with_the_sheet()
    {
        var label = Sheet("inventory-list-accessibility-guide").OfType<FloatingTextBoxElement>()
            .SelectMany(_ => _.Content.OfType<ParagraphElement>())
            .SelectMany(_ => _.Runs)
            .First(_ => _.Text == "Inventory list");

        await Assert.That(label.Properties.FontSizePoints).IsLessThan(16);
    }

    // home-contents-inventory-list's blender leans: a drawing's turn is kept.
    [Test]
    public async Task A_drawings_turn_is_kept() =>
        await Assert.That(Sheet("home-contents-inventory-list").OfType<FloatingImageElement>().Any(_ => _.RotationDegrees != 0)).IsTrue();

    // inventory-list-accessibility-guide's screenshot casts a blurred shadow in Excel's print (#333333
    // at 65%, blur 23pt, 11pt off at 45 degrees): layers under the picture that climb to the shadow's
    // alpha, reaching three sigmas (a third of the blur each) past the moved box on every side.
    [Test]
    public async Task A_pictures_blurred_shadow_is_drawn_under_it()
    {
        var elements = Sheet("inventory-list-accessibility-guide");
        var picture = elements.OfType<FloatingImageElement>()
            .First(_ => _.Description == "Screenshot of Inventory list worksheet");
        var index = elements.ToList().IndexOf(picture);
        var layers = elements.Take(index).Reverse()
            .TakeWhile(_ => _ is FloatingShapeElement {FillColorHex: "333333"})
            .Cast<FloatingShapeElement>()
            .Reverse()
            .ToList();

        await Assert.That(layers.Count).IsEqualTo(32);
        var covered = 1 - layers.Aggregate(1.0, (remaining, layer) => remaining * (1 - layer.FillAlpha));
        await Assert.That(covered).IsEqualTo(0.65).Within(0.001);

        // The outermost layer is the picture's box moved 11pt along 45 degrees and grown by three sigmas,
        // 23pt, both at the sheet's scale: moved as far down as across, and grown 23/11 times as far as
        // the move's length
        var outer = layers[0];
        var reach = (outer.WidthPoints - picture.WidthPoints) / 2;
        var across = outer.HorizontalPositionPoints + reach - picture.HorizontalPositionPoints;
        var down = outer.VerticalPositionPoints + reach - picture.VerticalPositionPoints;
        await Assert.That(across).IsGreaterThan(0);
        await Assert.That(down).IsEqualTo(across).Within(0.001);
        await Assert.That(reach / Math.Sqrt(across * across + down * down)).IsEqualTo(23.0 / 11).Within(0.001);
        await Assert.That(outer.HeightPoints - picture.HeightPoints).IsEqualTo(2 * reach).Within(0.001);
        await Assert.That(layers.All(_ => _.RelativeHeight == picture.RelativeHeight)).IsTrue();
    }

    // basic-business-invoice's "LOGO GOES HERE" names no colour; its shape style's fontRef is lt1, white
    // on its blue square in Excel's print.
    [Test]
    public async Task A_shapes_text_takes_its_styles_font_colour()
    {
        var label = Sheet("basic-business-invoice").OfType<FloatingTextBoxElement>()
            .SelectMany(_ => _.Content.OfType<ParagraphElement>())
            .SelectMany(_ => _.Runs)
            .First(_ => _.Text.Contains("LOGO"));

        await Assert.That(label.Properties.ColorHex).IsEqualTo("FFFFFF");
    }

    // simple-basic-pink-blue-timesheet's "Email" overflows across C13 and D13, so its cell spans the
    // three: the rule under them is handed down to the three cells of the spacer row beneath, whose fill
    // would otherwise cover it, as a one-column cell's is.
    [Test]
    public async Task A_rule_under_overflowing_text_is_handed_to_every_cell_beneath()
    {
        var grid = Grid("simple-basic-pink-blue-timesheet");
        var index = 0;
        while (!grid.Rows[index].Cells.Any(cell => Text(cell) == "Email" && cell.Properties.GridSpan == 3))
        {
            index++;
        }

        var email = grid.Rows[index].Cells.First(_ => Text(_) == "Email" && _.Properties.GridSpan == 3);
        var ruled = grid.Rows[index + 1].Cells.Count(_ => _.Properties.Borders is {Top.IsVisible: true});

        // The three cells under "Email", and the one under the right-hand "Email" beside them
        await Assert.That(ruled).IsGreaterThanOrEqualTo(4);
        await Assert.That(email.Properties.Borders is {Bottom.IsVisible: true}).IsFalse();
    }

    // simple-list's blank rows 1 and 3 are 8.2pt at its print scale; their empty cells measure at their own
    // font, scaled, so they do not grow the rows to the document default's line and stand the grid low.
    [Test]
    public async Task An_empty_cell_measures_at_its_own_font()
    {
        var empty = (ParagraphElement) Grid("simple-list").Rows[0].Cells[0].Content[0];

        await Assert.That(empty.Runs.Count).IsEqualTo(0);
        await Assert.That(empty.Properties.ParagraphMarkRunProperties).IsNotNull();
        await Assert.That(empty.Properties.ParagraphMarkRunProperties!.FontSizePoints).IsLessThan(8);
    }

    // Excel prints a sheet's header and footer in its margins and never moves the grid for them.
    [Test]
    public async Task A_sheets_header_does_not_move_its_grid()
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "simple-list", "input.xlsx"));
        var settings = ExcelConverter.Parse(stream, null, ProjectFonts.Directory, useLetterPageSize: false, onePagePerSheet: false).PageSettings;

        await Assert.That(settings.TopMarginIsAbsolute).IsTrue();
        await Assert.That(settings.BottomMarginIsAbsolute).IsTrue();
    }

    // modern-corporate-green-standard-invoice's 50pt "INVOICE" merges two custom-height rows (30 and
    // 20.1pt); Excel keeps both at their heights and clips the text, so the merge grows neither.
    [Test]
    public async Task A_merge_does_not_grow_an_exact_row()
    {
        var table = new TableElement
        {
            Rows =
            [
                new() {HeightPoints = 20, IsExactHeight = true, Cells = [Tall(VerticalMergeType.Restart)]},
                new() {HeightPoints = 20, IsExactHeight = true, Cells = [Tall(VerticalMergeType.Continue)]}
            ]
        };
        var heights = TableHeightCalculator.CalculateRowHeights(table, [200], LayoutTestFonts.Measurer);

        await Assert.That(heights[0]).IsEqualTo(20).Within(0.5f);
        await Assert.That(heights[1]).IsEqualTo(20).Within(0.5f);

        static TableCell Tall(VerticalMergeType merge) =>
            new()
            {
                Content = [new ParagraphElement {Runs = [new() {Text = merge == VerticalMergeType.Restart ? "INVOICE" : "", Properties = new() {FontFamily = "Arial", FontSizePoints = 48}}]}],
                Properties = new() {VerticalMerge = merge, WidthPoints = 200}
            };
    }

    // A sheet's text is drawn at its linear advances, as Excel prints it, not at Word's pixels.
    [Test]
    public async Task A_sheets_text_is_drawn_at_its_linear_advances()
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "simple-list", "input.xlsx"));
        var document = ExcelConverter.Parse(stream, null, ProjectFonts.Directory, useLetterPageSize: false, onePagePerSheet: false);

        await Assert.That(document.Compatibility.DrawsAtLinearAdvances).IsTrue();
    }

    // to-do-list's leaf is a group turned 344 degrees: its members turn with it.
    [Test]
    public async Task A_turned_groups_members_turn_with_it() =>
        await Assert.That(Sheet("to-do-list").OfType<FloatingShapeElement>().Count(_ => Math.Abs(_.RotationDegrees - 344.1) < 1)).IsGreaterThan(0);

    // to-do-list's first page ends at K, and its title is merged over B:L: the text lays out over the
    // whole merge, column L past the page's edge.
    [Test]
    public async Task A_merge_cut_by_the_page_lays_out_over_the_whole_merge()
    {
        var title = Cell(Grid("to-do-list"), "TO-DO LIST").Content.OfType<ParagraphElement>().First();

        await Assert.That(title.Properties.RightIndentPoints).IsLessThan(0);
    }

    // The HTML export shows a data bar as a gradient across the cell, and an icon as its character.
    [Test]
    public async Task The_html_export_shows_data_bars_and_icons()
    {
        using var projects = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "to-do-list-for-projects", "input.xlsx"));
        using var inventory = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "inventory-list-with-highlighting", "input.xlsx"));

        await Assert.That(ExcelConverter.ConvertToHtml(projects)).Contains("linear-gradient(to right");
        await Assert.That(ExcelConverter.ConvertToHtml(inventory)).Contains("⚑");
    }

    // A three-colour scale passes through its middle colour at its middle stop, its stops worked out
    // over the range: red at the least, yellow at the 50th percentile, green at the greatest.
    [Test]
    public async Task A_three_colour_scale_passes_through_its_middle_colour()
    {
        static S.Cell Number(string reference, double value) => new(new S.CellValue(value)) { CellReference = reference };
        var worksheet = new S.Worksheet(
            new S.SheetData(
                new S.Row(Number("A1", 0)) { RowIndex = 1 },
                new S.Row(Number("A2", 10)) { RowIndex = 2 },
                new S.Row(Number("A3", 100)) { RowIndex = 3 }),
            new S.ConditionalFormatting(
                new S.ConditionalFormattingRule(
                    new S.ColorScale(
                        new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Min },
                        new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Percentile, Val = "50" },
                        new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Max },
                        new S.Color { Rgb = "FFFF0000" },
                        new S.Color { Rgb = "FFFFFF00" },
                        new S.Color { Rgb = "FF00FF00" }))
                {
                    Type = S.ConditionalFormatValues.ColorScale,
                    Priority = 1
                })
            {
                SequenceOfReferences = new() { InnerText = "A1:A3" }
            });
        var formats = new ConditionalFormats(worksheet, null, null);

        await Assert.That(formats.For(1, 1, "0", 0)?.BackgroundColorHex).IsEqualTo("FF0000");
        await Assert.That(formats.For(2, 1, "10", 10)?.BackgroundColorHex).IsEqualTo("FFFF00");
        await Assert.That(formats.For(3, 1, "100", 100)?.BackgroundColorHex).IsEqualTo("00FF00");
    }

    // A hidden row prints nothing and takes no room, its text never growing it back.
    [Test]
    public async Task A_hidden_row_takes_no_room()
    {
        using var stream = new MemoryStream();
        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new(new S.Sheets(new S.Sheet { Id = "rId1", SheetId = 1, Name = "Sheet1" }));
            var sheetPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>("rId1");

            static S.Row Row(uint index, bool hidden) =>
                new(new S.Cell(new S.InlineString(new S.Text($"Row{index}"))) { CellReference = $"A{index}", DataType = S.CellValues.InlineString })
                {
                    RowIndex = index,
                    Height = 30,
                    CustomHeight = true,
                    Hidden = hidden ? true : null
                };
            sheetPart.Worksheet = new(new S.SheetData(Row(1, false), Row(2, true), Row(3, false)));
        }

        stream.Position = 0;
        var grid = ExcelConverter.Parse(stream, null, ProjectFonts.Directory, useLetterPageSize: false, onePagePerSheet: false)
            .Elements.OfType<TableElement>().First();

        await Assert.That(grid.Rows[1].HeightPoints).IsEqualTo(0);
        await Assert.That(grid.Rows[1].IsExactHeight).IsTrue();
        await Assert.That(grid.Rows[0].HeightPoints!.Value).IsGreaterThan(0);
    }

    // A donut is a ring and a block arc a band of one: each draws its hole.
    [Test]
    public async Task Donut_and_block_arc_presets_are_built()
    {
        var donut = PresetShapeGeometry.TryBuild(
            new()
            {
                Preset = A.ShapeTypeValues.Donut
            },
            100,
            100);
        var arc = PresetShapeGeometry.TryBuild(
            new()
            {
                Preset = A.ShapeTypeValues.BlockArc
            },
            100,
            100);

        await Assert.That(donut!.Count).IsEqualTo(2);
        // The default block arc is the top half: nothing of it below the centre line
        await Assert.That(arc!.Single().All(_ => _.Y <= 0.5 + 1e-9)).IsTrue();
    }
}
