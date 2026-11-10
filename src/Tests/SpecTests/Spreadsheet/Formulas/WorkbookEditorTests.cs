using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// The workbook editor: typing into cells as Excel reads typing, the formulas worked out again and their
/// values written into the file, a table's header kept in step with its column, and a file that is no
/// less valid than it was.
/// </summary>
public class WorkbookEditorTests
{
    static readonly DateTime now = new(2026, 11, 10, 9, 0, 0);

    // A workbook of one empty sheet, as the SDK makes one.
    static byte[] Blank()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new(new S.Sheets());
            var sheet = workbook.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new(new S.SheetData());
            workbook.Workbook.Sheets!.AppendChild(new S.Sheet
            {
                Id = workbook.GetIdOfPart(sheet),
                SheetId = 1,
                Name = "Sheet1"
            });
        }

        return stream.ToArray();
    }

    static FormulaWorkbook Read(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return FormulaWorkbook.Read(document.WorkbookPart!, now);
    }

    static FormulaValue Cached(byte[] xlsx, int row, int column) =>
        Read(xlsx).Cell(0, row, column)?.Value ?? FormulaValue.Blank;

    static byte[] Type(byte[] xlsx, params (string Cell, string Input)[] cells) =>
        WorkbookEditor.SetCells(
            xlsx,
            0,
            [.. cells.Select(_ => new CellEdit(CellReference.RowOf(_.Cell), CellReference.ColumnOf(_.Cell), _.Input))],
            now);

    [Test]
    public async Task Typing_WritesValuesAndWorksFormulasOut()
    {
        var xlsx = Type(Blank(), ("A1", "2"), ("A2", "3"), ("A3", "=A1*A2"), ("B1", "hello"), ("B2", "TRUE"));

        await Assert.That(Cached(xlsx, 1, 1).Number).IsEqualTo(2);
        await Assert.That(Cached(xlsx, 3, 1).Number).IsEqualTo(6);
        await Assert.That(Cached(xlsx, 1, 2).Text).IsEqualTo("hello");
        await Assert.That(Cached(xlsx, 2, 2).IsBoolean).IsTrue();

        // A cell the formula reads changed: the formula's value with it.
        xlsx = Type(xlsx, ("A1", "10"));
        await Assert.That(Cached(xlsx, 3, 1).Number).IsEqualTo(30);
    }

    [Test]
    public async Task AFormula_IsStoredWithoutItsEquals_InCapitals()
    {
        var xlsx = Type(Blank(), ("A1", "1"), ("A2", "=sum(a1, 2)"), ("A3", "=ifs(a1=1,\"one\")"));
        var book = Read(xlsx);

        await Assert.That(book.Cell(0, 2, 1)!.Formula).IsEqualTo("SUM(A1, 2)");
        // Functions newer than 2007 carry Excel's prefix in the file, or Excel shows #NAME?.
        await Assert.That(book.Cell(0, 3, 1)!.Formula).IsEqualTo("_xlfn.IFS(A1=1,\"one\")");
        await Assert.That(Cached(xlsx, 3, 1).Text).IsEqualTo("one");
        await Assert.That(FormulaText.ForDisplay(book.Cell(0, 3, 1)!.Formula!)).IsEqualTo("=IFS(A1=1,\"one\")");
    }

    [Test]
    public async Task AFormulaThatCannotBeRead_IsRefused() =>
        await Assert.That(() => Type(Blank(), ("A1", "=SUM(1,"))).Throws<FormulaSyntaxException>();

    // Excel refuses a function given too few or too many arguments as it is typed.
    [Test]
    public async Task AFunctionsArguments_AreCounted()
    {
        await Assert.That(() => Type(Blank(), ("A1", "=SUM()"))).Throws<FormulaSyntaxException>();
        await Assert.That(() => Type(Blank(), ("A1", "=ROUND(1,2,3)"))).Throws<FormulaSyntaxException>();
        // A function the engine has not got is Excel's to judge.
        await Assert.That(Cached(Type(Blank(), ("A1", "=NOSUCH()")), 1, 1).IsBlank).IsTrue();
    }

    public static IEnumerable<(string Typed, double Value, string Format)> TypedNumbers() =>
    [
        ("50%", 0.5, "0%"),
        ("12.5%", 0.125, "0.00%"),
        ("$1,200", 1200, "\"$\"#,##0"),
        ("$3.50", 3.5, "\"$\"#,##0.00"),
        ("1,234", 1234, "#,##0"),
        ("1/31/2024", 45322, "m/d/yyyy"),
        ("2024-01-31", 45322, "m/d/yyyy"),
        ("10:30", 0.4375, "h:mm"),
        ("1E3", 1000, "0.00E+00")
    ];

    // Typing a number in a shape gives a General cell the format the shape says, as Excel does.
    [Test]
    [MethodDataSource(nameof(TypedNumbers))]
    public async Task ATypedNumber_TakesTheFormatItsShapeSays(string typed, double value, string format)
    {
        var xlsx = Type(Blank(), ("A1", typed));
        var grid = WorkbookGrid.Read(xlsx, ProjectFonts.Directory);
        var cell = grid.Sheets[0].Cell(1, 1)!;

        await Assert.That(cell.Number!.Value).IsEqualTo(value).Within(1e-9);
        await Assert.That(cell.Look.NumberFormat).IsEqualTo(format);
    }

    [Test]
    public async Task AnApostrophe_KeepsWhatFollowsAsText()
    {
        var xlsx = Type(Blank(), ("A1", "'00123"), ("A2", "00123"));

        await Assert.That(Cached(xlsx, 1, 1).Text).IsEqualTo("00123");
        await Assert.That(Cached(xlsx, 2, 1).Number).IsEqualTo(123);
    }

    [Test]
    public async Task TheSameText_IsSharedOnce()
    {
        var xlsx = Type(Blank(), ("A1", "same"), ("A2", "same"), ("A3", "other"));
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        var items = document.WorkbookPart!.SharedStringTablePart!.SharedStringTable!.Elements<S.SharedStringItem>().Count();

        await Assert.That(items).IsEqualTo(2);
    }

    [Test]
    public async Task Clearing_TakesAwayWhatACellHolds_AndTheFormulasFollow()
    {
        var xlsx = Type(Blank(), ("A1", "4"), ("A2", "=A1+1"));
        xlsx = WorkbookEditor.Clear(xlsx, 0, new(1, 1, 1, 1), contents: true, formats: false, now);

        await Assert.That(Cached(xlsx, 1, 1).IsBlank).IsTrue();
        await Assert.That(Cached(xlsx, 2, 1).Number).IsEqualTo(1);
    }

    [Test]
    public async Task Copying_MovesAFormulasRelativeReferences()
    {
        var xlsx = Type(Blank(), ("A1", "1"), ("A2", "2"), ("B1", "=A1*10"));
        xlsx = WorkbookEditor.Copy(xlsx, 0, new(1, 2, 1, 2), 0, 2, 2, cut: false, now);
        var book = Read(xlsx);

        await Assert.That(book.Cell(0, 2, 2)!.Formula).IsEqualTo("A2*10");
        await Assert.That(Cached(xlsx, 2, 2).Number).IsEqualTo(20);
    }

    [Test]
    public async Task Formatting_ChangesTheCellsLook()
    {
        var xlsx = Type(Blank(), ("A1", "x"));
        xlsx = WorkbookEditor.Format(xlsx, 0, new(1, 1, 2, 2), new()
        {
            Bold = true,
            FillHex = "FFFF00",
            Horizontal = "center"
        });
        var sheet = WorkbookGrid.Read(xlsx, ProjectFonts.Directory).Sheets[0];

        await Assert.That(sheet.Cell(1, 1)!.Look.Bold).IsTrue();
        await Assert.That(sheet.Cell(1, 1)!.Look.FillHex).IsEqualTo("FFFF00");
        await Assert.That(sheet.Cell(1, 1)!.Look.Horizontal).IsEqualTo(TextAlignment.Center);
        await Assert.That(sheet.Look(2, 2).FillHex).IsEqualTo("FFFF00");

        // The same look again is the same style, not another.
        var again = WorkbookEditor.Format(xlsx, 0, new(3, 3, 3, 3), new()
        {
            Bold = true,
            FillHex = "FFFF00",
            Horizontal = "center"
        });
        await Assert.That(Styles(again)).IsEqualTo(Styles(xlsx));
    }

    static int Styles(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.CellFormats!.Elements<S.CellFormat>().Count();
    }

    [Test]
    public async Task AColumnsWidth_IsSet()
    {
        var xlsx = WorkbookEditor.SetColumnWidth(Blank(), 0, 2, 3, 20);
        var sheet = WorkbookGrid.Read(xlsx, ProjectFonts.Directory).Sheets[0];

        await Assert.That(sheet.ColumnWidth(2)).IsEqualTo(WorkbookGrid.Pixels(20, sheet.MaxDigitWidth));
        await Assert.That(sheet.ColumnWidth(3)).IsEqualTo(sheet.ColumnWidth(2));
        await Assert.That(sheet.ColumnWidth(4)).IsEqualTo(sheet.DefaultColumnWidth);
    }

    static string Corpus(string name) =>
        Path.Combine(ScenarioInputs.Root(ScenarioFormat.Excel), name, "input.xlsx");

    // back-to-school-planner's checklist: Qty and Cost make Total, a table formula of its row.
    [Test]
    public async Task ATableFormula_FollowsWhatItsRowHolds()
    {
        var xlsx = File.ReadAllBytes(Corpus("back-to-school-planner"));
        var book = Read(xlsx);
        var table = book.Tables.First(_ => _.Name == "Checklist");
        var sheet = table.Sheet;
        var row = table.FirstDataRow;
        var qty = table.Range.FirstColumn + table.ColumnIndex("Qty");
        var cost = table.Range.FirstColumn + table.ColumnIndex("Cost");
        var total = book.Sheets[sheet].Cells.First(_ => _.Row == row && _.Formula?.Contains("[Qty]") == true);

        var edited = WorkbookEditor.SetCells(xlsx, sheet, [new(row, qty, "3"), new(row, cost, "2.5")], now);
        var after = Read(edited);

        await Assert.That(after.Cell(sheet, row, total.Column)!.Value.Number).IsEqualTo(7.5);
        // A file whose formulas changed has no stale calculation chain.
        using var stream = new MemoryStream(edited);
        using var document = SpreadsheetDocument.Open(stream, false);
        await Assert.That(document.WorkbookPart!.CalculationChainPart).IsNull();
    }

    // A header cell is its column's name: renamed, the column is, and the formulas that name it.
    [Test]
    public async Task AHeader_RenamesItsColumn_AndTheFormulasNamingIt()
    {
        var xlsx = File.ReadAllBytes(Corpus("back-to-school-planner"));
        var table = Read(xlsx).Tables.First(_ => _.Name == "Checklist");
        var qty = table.Range.FirstColumn + table.ColumnIndex("Qty");

        var edited = WorkbookEditor.SetCells(xlsx, table.Sheet, [new(table.Range.FirstRow, qty, "Quantity")], now);
        var book = Read(edited);
        var renamed = book.Tables.First(_ => _.Name == "Checklist");

        await Assert.That(renamed.ColumnIndex("Quantity")).IsEqualTo(table.ColumnIndex("Qty"));
        await Assert.That(renamed.ColumnIndex("Qty")).IsEqualTo(-1);
        var formulas = book.Sheets[table.Sheet].Cells.Where(_ => _.Formula != null).Select(_ => _.Formula!).ToList();
        await Assert.That(formulas.Any(_ => _.Contains("[Quantity]"))).IsTrue();
        await Assert.That(formulas.Any(_ => _.Contains("[Qty]"))).IsFalse();
    }

    public static IEnumerable<string> Workbooks() =>
        FormulaCorpusTests.Workbooks();

    // Typed into every corpus workbook's first sheet, the edited file is no less valid than it was, and
    // Morph still reads it.
    [Test]
    [MethodDataSource(nameof(Workbooks))]
    public async Task AnEditedWorkbook_IsNoLessValid(string name)
    {
        var xlsx = File.ReadAllBytes(Corpus(name));
        var book = Read(xlsx);
        var sheet = book.Sheets.FirstOrDefault(_ => _.MaxRow > 0);
        if (sheet == null)
        {
            return;
        }

        var row = sheet.MaxRow + 2;
        var edited = WorkbookEditor.SetCells(xlsx, sheet.Index, [new(row, 1, "42"), new(row, 2, "=A" + row + "*2"), new(row, 3, "text")], now);
        edited = WorkbookEditor.Format(edited, sheet.Index, new(row, 1, row, 3), new()
        {
            Bold = true,
            FillHex = "DDEBF7"
        });

        await Assert.That(Errors(edited)).IsLessThanOrEqualTo(Errors(xlsx));
        await Assert.That(Read(edited).Cell(sheet.Index, row, 2)!.Value.Number).IsEqualTo(84);
        // Read by the grid as typed, and by the pages without fault (where the print area may leave the
        // new row off them).
        await Assert.That(WorkbookGrid.Read(edited, ProjectFonts.Directory).Sheets[sheet.Index].Cell(row, 3)!.Display).IsEqualTo("text");
        await Assert.That(ExcelConverter.ConvertToMarkdown(new MemoryStream(edited))).IsNotNull();
    }

    static int Errors(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).Count();
    }
}
