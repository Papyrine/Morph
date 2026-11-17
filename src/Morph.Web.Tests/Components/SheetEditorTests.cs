// An Excel workbook in MorphEditor: shown as a grid (SheetEditor) rather than as pages, and edited there.
// The keys, the typing and the mouse are the script's (morph-sheet.js); these tests pin what .NET does with
// what the script hands it — a cell handed in, a paste, a key — and that each edit becomes the editor's file.
//
// The sample is an invoice: C15 its subtotal (750), C16 the tax rate (8.75%), C17 other costs (0) and C18
// the total, =IFERROR(C15*(1+C16)+C17, "").
public class SheetEditorTests : BunitTestContext
{
    public SheetEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetupViewerController();
        var module = JSInterop.SetupModule($"./{MorphAssets.SheetScript}");
        module.SetupModule("attachSheet", _ => true).Mode = JSRuntimeMode.Loose;
    }

    async Task<IRenderedComponent<MorphEditor>> Open(byte[]? bytes = null, Action<ComponentParameterCollectionBuilder<MorphEditor>>? parameters = null)
    {
        var cut = Render(parameters ?? (_ => { }));
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(bytes ?? Sample.XlsxBytes, "invoice.xlsx", Sample.FontDirectory));
        return cut;
    }

    static FormulaValue Value(byte[] xlsx, int row, int column)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, false);
        return FormulaWorkbook.Read(document.WorkbookPart!, DateTime.Now).Cell(0, row, column)?.Value ?? FormulaValue.Blank;
    }

    [Test]
    public async Task AWorkbook_IsShownAsAGrid_InPlaceOfTheRibbonAndThePages()
    {
        var cut = await Open();

        await Assert.That(cut.FindAll(".sheet").Count).IsEqualTo(1);
        // The Word ribbon's panel and status bar give way to the grid's toolbar and status bar.
        await Assert.That(cut.FindAll("#editor-tabpanel")).IsEmpty();
        await Assert.That(cut.FindAll(".editor-status")).IsEmpty();
        await Assert.That(cut.Find(".viewer").ClassList.Contains("editor-sheet")).IsTrue();
        await Assert.That(string.Join(' ', cut.FindAll(".editor-tab").Select(_ => _.TextContent.Trim()))).IsEqualTo("Home");
        await Assert.That(cut.FindAll(".sheet-tab").Select(_ => _.TextContent).Single()).IsEqualTo("Invoice");
        await Assert.That(cut.Find(".sheet-mode").TextContent).IsEqualTo("Ready");
    }

    // The grid reaches a little past what the sheet holds and what is in view, never to the sheet's last
    // row: laid out that far it was a canvas tens of millions of pixels tall, which the browser left blank.
    [Test]
    public async Task TheGrid_ReachesOnlyALittlePastTheSheet()
    {
        var cut = await Open();
        var grid = cut.FindComponent<SheetEditor>().Instance;

        await Assert.That(grid.ExtentRows).IsLessThan(200);
        await Assert.That(grid.ExtentColumns).IsLessThan(100);
        await Assert.That(grid.tops[^1]).IsLessThan(20_000);

        // Scrolled far down, it reaches that far and a little more.
        await cut.InvokeAsync(() => grid.OnSheetScroll(50_000, 0, 1200, 800));
        await Assert.That(grid.tops[^1]).IsGreaterThan(50_800);
        await Assert.That(grid.tops[^1]).IsLessThan(200_000);
    }

    [Test]
    public async Task Edges_StopPastWhatIsInView()
    {
        var edges = SheetEditor.Edges(10, CellReference.MaxRow, _ => 40, 1000);

        await Assert.That(edges.Length - 1).IsEqualTo(43);
    }

    // The viewer shows a workbook as its pages, as before.
    [Test]
    public async Task TheViewer_ShowsAWorkbookAsPages()
    {
        var cut = Render<MorphViewer>();
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(Sample.XlsxBytes, "invoice.xlsx", Sample.FontDirectory));

        await Assert.That(cut.FindAll(".sheet")).IsEmpty();
    }

    [Test]
    public async Task ACellHandedIn_IsTheEditorsFile_ItsFormulasWorkedOut()
    {
        byte[]? changed = null;
        var cut = await Open(parameters: _ => _.Add(_ => _.OnDocumentChanged, (byte[] bytes) => changed = bytes));
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("C17"));

        var ok = await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("25", "down"));

        await Assert.That(ok).IsTrue();
        await Assert.That(changed).IsNotNull();
        await Assert.That(Value(changed!, 17, 3).Number).IsEqualTo(25);
        await Assert.That(Value(changed!, 18, 3).Number).IsEqualTo(750 * 1.0875 + 25).Within(1e-9);
        // The selection went on down, and the editor's title says the file is edited.
        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("C18");
        await Assert.That(cut.Find(".editor-name").TextContent).Contains("edited");
    }

    [Test]
    public async Task Undo_PutsTheFileBack_AsItWasSaved()
    {
        var cut = await Open();
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("C17"));
        await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("25", "none"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("Undo", false, true));

        await Assert.That(cut.Instance.Dirty).IsFalse();
        await Assert.That(cut.Find(".editor-name").TextContent).DoesNotContain("edited");
        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("C17");
    }

    // A formula that cannot be read is refused: the cell stays open, and the status bar says why.
    [Test]
    public async Task AFormulaThatCannotBeRead_KeepsTheCellOpen()
    {
        var cut = await Open();
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("E5"));
        await cut.InvokeAsync(() => grid.Instance.OnSheetEditStart("enter"));

        var ok = await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("=SUM(1,", "down"));

        await Assert.That(ok).IsFalse();
        await Assert.That(cut.Instance.Dirty).IsFalse();
        await Assert.That(cut.Find(".sheet-note").TextContent).StartsWith("There's a problem with this formula");
        await Assert.That(cut.Find(".sheet-mode").TextContent).IsEqualTo("Enter");
    }

    // Text pasted from elsewhere fills cells from the active one, a tab between columns.
    [Test]
    public async Task Pasting_Text_FillsTheCells()
    {
        byte[]? changed = null;
        var cut = await Open(parameters: _ => _.Add(_ => _.OnDocumentChanged, (byte[] bytes) => changed = bytes));
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("E20"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetPaste("1\t2\r\n3\t=E20+F21\r\n"));

        await Assert.That(Value(changed!, 20, 5).Number).IsEqualTo(1);
        await Assert.That(Value(changed!, 21, 6).Number).IsEqualTo(1 + 0);
        await Assert.That(Value(changed!, 21, 5).Number).IsEqualTo(3);
        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("E20:F21");
    }

    // What was copied in the grid is pasted as it was: a formula's references moved with it.
    [Test]
    public async Task Pasting_WhatWasCopied_MovesItsFormula()
    {
        byte[]? changed = null;
        var cut = await Open(parameters: _ => _.Add(_ => _.OnDocumentChanged, (byte[] bytes) => changed = bytes));
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("C18"));
        var text = await cut.InvokeAsync(() => grid.Instance.OnSheetCopy(false));
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("D18"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetPaste(text!));

        using var stream = new MemoryStream(changed!);
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, false);
        var formula = FormulaWorkbook.Read(document.WorkbookPart!, DateTime.Now).Cell(0, 18, 4)!.Formula;
        await Assert.That(formula).IsEqualTo("IFERROR(D15*(1+D16)+D17, \"\")");
    }

    [Test]
    public async Task Bold_FormatsTheSelection()
    {
        var cut = await Open();
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("E3:E4"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("Bold", false, true));

        var sheet = grid.Instance.Sheet!;
        await Assert.That(sheet.Look(3, 5).Bold).IsTrue();
        await Assert.That(sheet.Look(4, 5).Bold).IsTrue();
        await Assert.That(cut.Find("button[aria-label='Bold (Ctrl+B)']").GetAttribute("aria-pressed")).IsEqualTo("true");
    }

    // A read-only editor shows the grid and edits nothing.
    [Test]
    public async Task ReadOnly_EditsNothing()
    {
        var cut = await Open(parameters: _ => _.Add(_ => _.ReadOnly, true));
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("C17"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("25", "none"));

        await Assert.That(cut.Instance.Dirty).IsFalse();
        await Assert.That(cut.Find(".sheet-input").HasAttribute("readonly")).IsTrue();
    }

    [Test]
    public async Task ABlankWorkbook_OpensAsAGrid()
    {
        var cut = Render<MorphEditor>();

        await cut.InvokeAsync(() => cut.Instance.OpenAsync(WorkbookEditor.Blank(), "Book1.xlsx", Sample.FontDirectory));

        await Assert.That(cut.FindAll(".sheet-tab").Single().TextContent).IsEqualTo("Sheet1");
        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("A1");
    }

    // A row typed cell by cell with Tab, then Enter, goes on at the start of the next row, as Excel's does.
    [Test]
    public async Task Enter_AfterTabs_GoesBackToWhereTheyStarted()
    {
        var cut = Render<MorphEditor>();
        await cut.InvokeAsync(() => cut.Instance.OpenAsync(WorkbookEditor.Blank(), "Book1.xlsx", Sample.FontDirectory));
        var grid = cut.FindComponent<SheetEditor>();

        await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("Item", "right"));
        await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("Cost", "right"));
        await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("Tax", "down"));
        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("A2");

        // Moved anywhere else first, Enter just goes down.
        await cut.InvokeAsync(() => grid.Instance.OnSheetCommit("Paper", "right"));
        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("Right", false, false));
        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("Enter", false, false));
        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("C3");
    }

    // Keys the script sent before it heard a cell was opened wait for it: they do not move the cell.
    [Test]
    public async Task MovingKeys_WaitWhileACellIsOpen()
    {
        var cut = await Open();
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("E5"));
        await cut.InvokeAsync(() => grid.Instance.OnSheetEditStart("enter"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("Down", false, false));

        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("E5");
    }

    [Test]
    public async Task Rows_ReadsTabSeparatedText()
    {
        var rows = SheetEditor.Rows("a\tb\r\n\"c\td\"\t\"e \"\"q\"\"\"\r\n");

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(string.Join('|', rows[0])).IsEqualTo("a|b");
        await Assert.That(string.Join('|', rows[1])).IsEqualTo("c\td|e \"q\"");
    }

    static string? Formula(byte[] xlsx, int row, int column)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, false);
        return FormulaWorkbook.Read(document.WorkbookPart!, DateTime.Now).Cell(0, row, column)?.Formula;
    }

    // Whole rows selected, Ctrl+Plus inserts as many above them, and the total below moves down, its
    // formula's references with it.
    [Test]
    public async Task CtrlPlus_OnWholeRows_InsertsAsMany()
    {
        byte[]? changed = null;
        var cut = await Open(parameters: _ => _.Add(_ => _.OnDocumentChanged, (byte[] bytes) => changed = bytes));
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetHeader("row", 16, false, "down"));
        await cut.InvokeAsync(() => grid.Instance.OnSheetHeader("row", 17, true, "drag"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("InsertCells", true, true));

        await Assert.That(Formula(changed!, 20, 3)).IsEqualTo("IFERROR(C15*(1+C18)+C19, \"\")");
        await Assert.That(Value(changed!, 20, 3).Number).IsEqualTo(750 * 1.0875).Within(1e-9);
        // The rows inserted are what is selected, as in Excel.
        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("16:17");
    }

    // On a cell, Ctrl+Minus asks which, where Excel's dialog asks: the tax rate's row deleted, the total's
    // reference to it is #REF!.
    [Test]
    public async Task CtrlMinus_OnACell_AsksWhich()
    {
        byte[]? changed = null;
        var cut = await Open(parameters: _ => _.Add(_ => _.OnDocumentChanged, (byte[] bytes) => changed = bytes));
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("C16"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("DeleteCells", false, true));
        await Assert.That(changed).IsNull();
        await cut.FindAll(".editor-menu[aria-label='Delete'] .editor-menu-item").First(_ => _.TextContent == "Delete sheet rows").ClickAsync(new());

        await Assert.That(Formula(changed!, 17, 3)).IsEqualTo("IFERROR(C15*(1+#REF!)+C16, \"\")");
        await Assert.That(cut.FindAll(".editor-menu")).IsEmpty();
    }

    // A right-click on a row's number selects the row and offers Insert; a row inserted inside the line
    // items' table grows it, and the subtotal over its column is what it was.
    [Test]
    public async Task ARightClickOnARowsNumber_OffersToInsertThere()
    {
        byte[]? changed = null;
        var cut = await Open(parameters: _ => _.Add(_ => _.OnDocumentChanged, (byte[] bytes) => changed = bytes));
        var grid = cut.FindComponent<SheetEditor>();

        await cut.InvokeAsync(() => grid.Instance.OnSheetContextMenu("row", 12, 3, 40, 300));

        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("12:12");
        await Assert.That(cut.Find(".sheet-context-menu").GetAttribute("style")).IsEqualTo("left:40px;top:300px");
        await cut.FindAll(".sheet-context-menu .editor-menu-item").First(_ => _.TextContent == "Insert").ClickAsync(new());

        await Assert.That(Formula(changed!, 16, 3)).IsEqualTo("IFERROR(SUM(InvoiceDetails[AMOUNT]), \"\")");
        await Assert.That(Value(changed!, 16, 3).Number).IsEqualTo(750);
        await Assert.That(Value(changed!, 13, 3).Number).IsEqualTo(400);
        await Assert.That(cut.FindAll(".sheet-context-menu")).IsEmpty();
    }

    // A right-click inside the selection keeps it.
    [Test]
    public async Task ARightClickInTheSelection_KeepsIt()
    {
        var cut = await Open();
        var grid = cut.FindComponent<SheetEditor>();
        await cut.InvokeAsync(() => grid.Instance.OnSheetGoTo("B3:C5"));

        await cut.InvokeAsync(() => grid.Instance.OnSheetContextMenu("cell", 4, 3, 10, 10));

        await Assert.That(cut.Find(".sheet-name-box").GetAttribute("value")).IsEqualTo("B3:C5");
        var lines = cut.FindAll(".sheet-context-menu .editor-menu-item").Select(_ => _.TextContent).ToList();
        await Assert.That(lines).Contains("Insert sheet rows");
        await Assert.That(lines).Contains("Delete sheet columns");

        // Escape closes it.
        await cut.InvokeAsync(() => grid.Instance.OnSheetKey("Escape", false, false));
        await Assert.That(cut.FindAll(".sheet-context-menu")).IsEmpty();
    }
}
