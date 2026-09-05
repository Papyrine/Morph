using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Where a table's left border sits, by compatibility mode — the XPS-read rule on
/// <c>DocumentParser.ResolveTableIndent</c>: mode 15 puts the border at margin + <c>w:tblInd</c>;
/// modes 12 and 14 put the TEXT there and hang the border left by the first cell's own left margin.
/// Word's built-in Normal Table declares <c>w:tblInd</c> 0, so under a styles part the indent is always
/// declared; only a package with no styles part reaches the undeclared branch.
/// </summary>
public class TableIndentCompatibilityTests
{
    [Test]
    [Arguments(15, null, 0d)]
    [Arguments(15, 720, 36d)]
    [Arguments(12, null, -5.4)]
    [Arguments(12, 720, 30.6)]
    [Arguments(14, 720, 30.6)]
    public async Task The_border_offset_follows_the_mode_under_a_Table_Normal_that_declares_zero(int mode, int? indentTwips, double expected)
    {
        using var stream = BuildDocument(mode, indentTwips, styles: true, tableMarginTwips: null, firstCellMarginTwips: null);
        var table = new DocumentParser().Parse(stream).Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.IndentPoints).IsEqualTo(expected).Within(0.001);
    }

    [Test]
    public async Task Modes_below_15_subtract_the_first_cell_own_margin_not_the_table_default()
    {
        // Table default 720 (36pt), first cell w:tcMar 1440 (72pt), tblInd 1440 (72pt): _probe_compats12
        // table K — border at the margin, 72 + 72 - 72.
        using var stream = BuildDocument(12, 1440, styles: true, tableMarginTwips: 720, firstCellMarginTwips: 1440);
        var table = new DocumentParser().Parse(stream).Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.IndentPoints).IsEqualTo(0d).Within(0.001);
    }

    [Test]
    public async Task Mode_15_ignores_cell_margins()
    {
        using var stream = BuildDocument(15, 1440, styles: true, tableMarginTwips: 720, firstCellMarginTwips: 1440);
        var table = new DocumentParser().Parse(stream).Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.IndentPoints).IsEqualTo(72d).Within(0.001);
    }

    [Test]
    public async Task Without_a_styles_part_an_undeclared_indent_puts_the_border_on_the_margin_when_the_table_declares_its_margins()
    {
        // _probe_compat12 table F: w:tblCellMar 720, no w:tblInd, no styles — border at the margin.
        using var stream = BuildDocument(12, null, styles: false, tableMarginTwips: 720, firstCellMarginTwips: null);
        var table = new DocumentParser().Parse(stream).Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.IndentPoints).IsEqualTo(0d).Within(0.001);
    }

    [Test]
    [Arguments(15, 0d)]
    [Arguments(12, 10.8)]
    [Arguments(14, 10.8)]
    public async Task Modes_below_15_let_the_border_box_run_both_cell_margins_past_the_column(int mode, double expected)
    {
        // Table Normal's 108-twip margins on both sides: _probe_pct12 table A reads 479.25 for a 100% table
        // on a 468pt column, _probe_pct15 468.
        using var stream = BuildDocument(mode, null, styles: true, tableMarginTwips: null, firstCellMarginTwips: null);
        var table = new DocumentParser().Parse(stream).Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.WidthOverhangPoints).IsEqualTo(expected).Within(0.001);
    }

    [Test]
    public async Task The_overhang_takes_the_first_cell_own_margin()
    {
        // _probe_pct12 table E: a 1in first-cell margin against the default last reads 545.4 = 468 + 72 + 5.4.
        using var stream = BuildDocument(12, null, styles: true, tableMarginTwips: null, firstCellMarginTwips: 1440);
        var table = new DocumentParser().Parse(stream).Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.WidthOverhangPoints).IsEqualTo(77.4).Within(0.001);
    }

    [Test]
    public async Task An_undeclared_indent_over_direct_margins_hangs_only_the_right_margin()
    {
        // table_autofit_no_widths: no styles part, direct margins — the border sits on the margin and the
        // box runs the right margin past the column (Word 474 on 468).
        using var stream = BuildDocument(12, null, styles: false, tableMarginTwips: 720, firstCellMarginTwips: null);
        var table = new DocumentParser().Parse(stream).Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.WidthOverhangPoints).IsEqualTo(36d).Within(0.001);
    }

    [Test]
    public async Task A_grid_that_fits_the_column_does_not_grow_into_the_overhang()
    {
        var table = new TableElement
        {
            Rows = [new() { Cells = [new() { Properties = new(), Content = [] }, new() { Properties = new(), Content = [] }] }],
            Properties = new() { GridColumnWidths = [234, 234], WidthOverhangPoints = 10.8 }
        };

        var widths = TableLayout.CalculateColumnWidths(table, 2, 468);

        await Assert.That(widths.Sum()).IsEqualTo(468f).Within(0.01f);
    }

    [Test]
    public async Task A_percentage_table_fills_the_outdented_box()
    {
        var table = new TableElement
        {
            Rows = [new() { Cells = [new() { Properties = new(), Content = [] }, new() { Properties = new(), Content = [] }] }],
            Properties = new() { GridColumnWidths = [100, 100], FillContainer = true, PreferredWidthFraction = 1.0, WidthOverhangPoints = 10.8 }
        };

        var widths = TableLayout.CalculateColumnWidths(table, 2, 468);

        await Assert.That(widths.Sum()).IsEqualTo(478.8f).Within(0.01f);
    }

    [Test]
    public async Task ResolveTableIndent_covers_the_probe_matrix()
    {
        // Mode 15: the declared indent, or zero.
        await Assert.That(DocumentParser.ResolveTableIndent(15, 36, true, 72)).IsEqualTo(36d);
        await Assert.That(DocumentParser.ResolveTableIndent(15, null, true, 72)).IsEqualTo(0d);

        // Mode 12, declared: indent minus the first cell margin (probe tables B, D, H, J, K).
        await Assert.That(DocumentParser.ResolveTableIndent(12, 36, true, 72)).IsEqualTo(-36d);
        await Assert.That(DocumentParser.ResolveTableIndent(12, 72, true, 0)).IsEqualTo(72d);

        // Mode 12, undeclared: on the margin with declared table margins (A, E, F), the built-in
        // margin to the left of it otherwise (O).
        await Assert.That(DocumentParser.ResolveTableIndent(12, null, true, 72)).IsEqualTo(0d);
        await Assert.That(DocumentParser.ResolveTableIndent(12, null, false, 5.4)).IsEqualTo(-5.4);
    }

    static MemoryStream BuildDocument(int mode, int? indentTwips, bool styles, int? tableMarginTwips, int? firstCellMarginTwips)
    {
        var tableProperties = new W.TableProperties();
        if (indentTwips is { } indent)
        {
            tableProperties.Append(new TableIndentation { Width = indent, Type = TableWidthUnitValues.Dxa });
        }

        if (tableMarginTwips is { } tableMargin)
        {
            tableProperties.Append(new TableCellMarginDefault(
                new TableCellLeftMargin { Width = (short) tableMargin, Type = TableWidthValues.Dxa },
                new TableCellRightMargin { Width = (short) tableMargin, Type = TableWidthValues.Dxa }));
        }

        var firstCellProperties = new W.TableCellProperties();
        if (firstCellMarginTwips is { } cellMargin)
        {
            firstCellProperties.Append(new TableCellMargin(
                new LeftMargin { Width = cellMargin.ToString(), Type = TableWidthUnitValues.Dxa }));
        }

        var table = new Table(
            tableProperties,
            new W.TableRow(
                new W.TableCell(firstCellProperties, new Paragraph(new W.Run(new Text("a1")))),
                new W.TableCell(new Paragraph(new W.Run(new Text("b1"))))));

        var body = new Body(table, new Paragraph(new W.Run(new Text("after"))));
        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = [with(body)];

            var settings = mainPart.AddNewPart<DocumentSettingsPart>();
            settings.Settings = new Settings(new Compatibility(new CompatibilitySetting
            {
                Name = CompatSettingNameValues.CompatibilityMode,
                Uri = "http://schemas.microsoft.com/office/word",
                Val = mode.ToString()
            }));

            if (styles)
            {
                // Word's built-in Normal Table: tblInd 0 and 108-twip start/end margins.
                var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
                stylesPart.Styles = new Styles(new Style
                {
                    Type = StyleValues.Table,
                    StyleId = "TableNormal",
                    Default = true,
                    StyleName = new StyleName { Val = "Normal Table" },
                    StyleTableProperties = new StyleTableProperties(
                        new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa },
                        new TableCellMarginDefault(
                            new TableCellLeftMargin { Width = 108, Type = TableWidthValues.Dxa },
                            new TableCellRightMargin { Width = 108, Type = TableWidthValues.Dxa }))
                });
            }
        }

        stream.Position = 0;
        return stream;
    }
}
