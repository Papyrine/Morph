using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using WordCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using static EditFixtures;

// The Table and Layout tabs end to end: each press acts on the table or the section the caret is in,
// and what it did is read back off the saved file. Their edits are spec-tested in core; what is tested
// here is the ribbon's glue — the caret's cell, the selection a merge spans, the menus and dialogs.
public partial class SnapshotTests
{
    // Two rows of three cells, "Cell one" to "Cell six", after the first paragraph.
    static byte[] TableDocument()
    {
        var options = new EditOptions("Ann", DateTimeOffset.UnixEpoch, Track: false);
        var docx = DocumentEditor.InsertTable(Paragraphs, 2, 15, 2, 3, options).Document;
        string[] cells = ["Cell one", "Cell two", "Cell three", "Cell four", "Cell five", "Cell six"];
        for (var index = 0; index < cells.Length; index++)
        {
            docx = DocumentEditor.Rewrite(docx, 3 + index, [new([new(-1, cells[index])])], options).Document;
        }

        return docx;
    }

    [Test]
    public async Task Table_RowsAndColumns_AreInsertedAndDeleted()
    {
        var page = await OpenEditAsync(TableDocument());

        // An empty row under the first and an empty column right of "Cell one"; then the row and the
        // column the caret is in go: the second row, and the column "Cell three" heads.
        await TablePressAsync(page, "Cell one", "Insert below");
        await WaitForTableAsync(page, _ => _.Rows == 3);
        await TablePressAsync(page, "Cell one", "Insert right");
        await WaitForTableAsync(page, _ => _.Columns == 4);
        await TablePressAsync(page, "Cell four", "Delete row");
        await WaitForTableAsync(page, _ => _.Rows == 2);
        await TablePressAsync(page, "Cell three", "Delete column");
        var shape = await WaitForTableAsync(page, _ => _.Columns == 3);
        await Assert.That(shape.Text).IsEqualTo("Cell one||Cell two/||");

        await TablePressAsync(page, "Cell two", "Delete table");
        await WaitForBodyAsync(page, _ => !_.Contains("<w:tbl>"));
    }

    // A selection from one cell to the next merges them; Split cells takes the merged cell back apart.
    [Test]
    public async Task Table_MergeAndSplit()
    {
        var page = await OpenEditAsync(TableDocument());

        await SelectOnPagesAsync(page, "Cell one", "Cell two");
        await ChooseTabAsync(page, "Table");
        await page.ClickAsync(".editor-tool:has-text('Merge cells')");
        await WaitForTableAsync(page, _ => _.Xml.Contains("<w:gridSpan w:val=\"2\" />"));

        await ClickInAsync(page, "Cell one");
        await page.ClickAsync(".editor-tool:has-text('Split cells')");
        await page.WaitForSelectorAsync("#editor-split-title");
        await page.FillAsync(".editor-dialog input >> nth=0", "2");
        await page.FillAsync(".editor-dialog input >> nth=1", "1");
        await page.ClickAsync(".editor-dialog .editor-button-primary");
        await WaitForTableAsync(page, _ => !_.Xml.Contains("w:gridSpan"));
    }

    // Borders, shading, a column's width and AutoFit, on the table the caret is in.
    [Test]
    public async Task Table_FormatsTheTableTheCaretIsIn()
    {
        var page = await OpenEditAsync(TableDocument());

        await ClickInAsync(page, "Cell one");
        await ChooseTabAsync(page, "Table");
        await page.ClickAsync(".editor-tool:has-text('Borders')");
        await page.ClickAsync(".editor-menu-item:text-is('No border')");
        await WaitForTableAsync(page, _ => _.Xml.Contains("w:val=\"nil\"") || _.Xml.Contains("w:val=\"none\""));

        await ClickInAsync(page, "Cell two");
        await page.ClickAsync("[aria-label='More shading colours']");
        await page.ClickAsync(".editor-swatch[aria-label='Yellow']");
        await WaitForTableAsync(page, _ => _.Xml.Contains("w:fill=\"FFFF00\""));

        await ClickInAsync(page, "Cell three");
        await page.FillAsync(".editor-width-input", "3");
        await page.PressAsync(".editor-width-input", "Tab");
        await WaitForTableAsync(page, _ => _.Xml.Contains("w:w=\"1701\""));

        await ClickInAsync(page, "Cell one");
        await page.ClickAsync(".editor-tool:has-text('AutoFit')");
        await page.ClickAsync(".editor-menu-item:text-is('Fixed column width')");
        await WaitForTableAsync(page, _ => _.Xml.Contains("<w:tblLayout w:type=\"fixed\" />"));

        await ClickInAsync(page, "Cell one");
        await page.ClickAsync(".editor-tool:has-text('Distribute columns')");
        // Alike to the twip the table's width divides into.
        await WaitForTableAsync(page, _ => _.Widths.Select(int.Parse).Max() - _.Widths.Select(int.Parse).Min() <= 1);
    }

    // The Layout tab sets the page of the caret's section: margins from the menu and the dialog,
    // orientation, paper, and a section break that starts a second section.
    [Test]
    public async Task Layout_SetsTheCaretsSection()
    {
        var page = await OpenEditAsync(Paragraphs);

        await ClickInAsync(page, "paragraph here");
        await ChooseTabAsync(page, "Layout");
        await page.ClickAsync(".editor-tool:has-text('Margins')");
        await page.ClickAsync(".editor-menu-item:has-text('Narrow')");
        await WaitForSectionAsync(page, _ => _.PageMargin?.Top?.Value == 720 &&
                                             _.PageMargin.Left?.Value == 720);

        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync(".editor-tool:has-text('Margins')");
        await page.ClickAsync(".editor-menu-item:text-is('Custom margins…')");
        await page.WaitForSelectorAsync("#editor-margins-title");
        await page.FillAsync(".editor-margin-fields input >> nth=0", "3");
        await page.ClickAsync(".editor-dialog .editor-button-primary");
        await WaitForSectionAsync(page, _ => _.PageMargin?.Top?.Value == 1701);

        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync(".editor-tool:has-text('Size')");
        await page.ClickAsync(".editor-menu-item:has-text('A5')");
        await WaitForSectionAsync(page, _ => _.PageSize?.Width?.Value == 8391u);

        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync(".editor-tool:has-text('Orientation')");
        await page.ClickAsync(".editor-menu-item:text-is('Landscape')");
        await WaitForSectionAsync(page, _ => _.PageSize?.Orient?.Value == PageOrientationValues.Landscape &&
                                             _.PageSize.Width?.Value == 11906u);

        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync(".editor-tool:has-text('Columns')");
        await page.ClickAsync(".editor-menu-item:text-is('Two')");
        await WaitForBodyAsync(page, _ => _.Contains("w:num=\"2\""));
        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync(".editor-tool:has-text('Columns')");
        await page.ClickAsync(".editor-menu-item:text-is('Line between')");
        await WaitForBodyAsync(page, _ => _.Contains("w:num=\"2\" w:sep=\"1\""));
        await ClickInAsync(page, "paragraph here");
        await page.ClickAsync(".editor-tool:has-text('Columns')");
        await page.ClickAsync(".editor-menu-item:text-is('One')");
        await WaitForBodyAsync(page, _ => !_.Contains("w:num="));

        await ClickInAsync(page, "bold one");
        await page.ClickAsync(".editor-tool:has-text('Breaks')");
        await page.ClickAsync(".editor-menu-item:text-is('Next page')");
        await WaitForBodyAsync(page, _ => _.Split("<w:sectPr").Length == 3);
        await page.WaitForSelectorAsync(".viewer-page[data-page-number='2']");
    }

    // Puts the caret in a cell and presses a Table tab button.
    static async Task TablePressAsync(IPage page, string cell, string button)
    {
        await ClickInAsync(page, cell);
        await ChooseTabAsync(page, "Table");
        await page.ClickAsync($".editor-tool:has-text('{button}')");
    }

    record TableShape(int Rows, int Columns, string Text, string Xml, IReadOnlyList<string> Widths);

    // Saves until the document's first table is as expected.
    static async Task<TableShape> WaitForTableAsync(IPage page, Func<TableShape, bool> expected)
    {
        TableShape? last = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            last = Shape(await SaveAsync(page));
            if (last != null &&
                expected(last))
            {
                return last;
            }

            await page.WaitForTimeoutAsync(250);
        }

        throw new($"The table did not change as expected: {last?.Rows}x{last?.Columns} {last?.Text}");
    }

    static TableShape? Shape(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var package = WordprocessingDocument.Open(stream, false);
        if (package.MainDocumentPart!.Document!.Body!.Descendants<WordTable>().FirstOrDefault() is not { } table)
        {
            return null;
        }

        var rows = table.Elements<WordRow>().ToList();
        var text = string.Join("/", rows.Select(row => string.Join("|", row.Elements<WordCell>().Select(_ => _.InnerText))));
        var widths = rows[0].Elements<WordCell>().Select(_ => _.TableCellProperties?.TableCellWidth?.Width?.Value ?? "").ToList();
        return new(rows.Count, table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Count() ?? 0, text, table.OuterXml, widths);
    }

    // Saves until the first section's properties are as expected.
    static async Task WaitForSectionAsync(IPage page, Func<(PageMargin? PageMargin, PageSize? PageSize), bool> expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var stream = new MemoryStream(await SaveAsync(page));
            using var package = WordprocessingDocument.Open(stream, false);
            var section = package.MainDocumentPart!.Document!.Body!.Descendants<SectionProperties>().First();
            if (expected((section.GetFirstChild<PageMargin>(), section.GetFirstChild<PageSize>())))
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }

        throw new("The section did not change as expected.");
    }
}
