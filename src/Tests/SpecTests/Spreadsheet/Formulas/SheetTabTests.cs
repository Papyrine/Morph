using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Sheets added, renamed, deleted and moved, as Excel does it: what Excel made of the same workbook and
/// the same steps, read back over COM (Excel 16, 2026-11-17), is what is expected.
/// </summary>
public class SheetTabTests
{
    static readonly DateTime now = new(2026, 11, 17, 9, 0, 0);

    static byte[] Book(params string[] names)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new(new S.Sheets());
            uint id = 1;
            foreach (var name in names)
            {
                var sheet = workbook.AddNewPart<WorksheetPart>();
                sheet.Worksheet = new(new S.SheetData());
                workbook.Workbook.Sheets!.AppendChild(new S.Sheet
                {
                    Id = workbook.GetIdOfPart(sheet),
                    SheetId = id++,
                    Name = name
                });
            }
        }

        return stream.ToArray();
    }

    static byte[] Change(byte[] xlsx, Action<SpreadsheetDocument> change)
    {
        var stream = new MemoryStream();
        stream.Write(xlsx);
        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            change(document);
        }

        return stream.ToArray();
    }

    static T Look<T>(byte[] xlsx, Func<SpreadsheetDocument, T> look)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return look(document);
    }

    static byte[] Type(byte[] xlsx, int sheet, params (string Cell, string Input)[] cells) =>
        WorkbookEditor.SetCells(
            xlsx,
            sheet,
            [.. cells.Select(_ => new CellEdit(CellReference.RowOf(_.Cell), CellReference.ColumnOf(_.Cell), _.Input))],
            now);

    static FormulaWorkbook Read(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return FormulaWorkbook.Read(document.WorkbookPart!, now);
    }

    static string? Formula(byte[] xlsx, int sheet, string cell) =>
        Read(xlsx).Cell(sheet, CellReference.RowOf(cell), CellReference.ColumnOf(cell))?.Formula;

    static List<string> Names(byte[] xlsx) =>
        Look(xlsx, _ => _.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Select(sheet => sheet.Name!.Value!).ToList());

    static List<(string Name, uint? Scope, string Text)> Defined(byte[] xlsx) =>
        Look(xlsx, _ => _.WorkbookPart!.Workbook!.DefinedNames?.Elements<S.DefinedName>()
            .Select(defined => (defined.Name!.Value!, defined.LocalSheetId?.Value, defined.Text))
            .ToList() ?? []);

    static string Joined(IEnumerable<string> items) =>
        string.Join("|", items);

    static uint ActiveTab(byte[] xlsx) =>
        Look(xlsx, _ => _.WorkbookPart!.Workbook!.BookViews!.Elements<S.WorkbookView>().First().ActiveTab!.Value);

    // A new sheet is named Sheet and the first number no sheet has, whatever its capitals.
    [Test]
    [Arguments(new[] { "Sheet1" }, "Sheet2", "Sheet3")]
    [Arguments(new[] { "Sheet1", "Sheet3" }, "Sheet2", "Sheet4")]
    [Arguments(new[] { "Budget" }, "Sheet1", "Sheet2")]
    [Arguments(new[] { "Sheet5", "Data" }, "Sheet1", "Sheet2")]
    [Arguments(new[] { "sheet1", "Other" }, "Sheet2", "Sheet3")]
    public async Task ANewSheet_IsNamedAsExcelNamesIt(string[] names, string first, string second)
    {
        var xlsx = WorkbookEditor.AddSheet(Book(names), names.Length);
        xlsx = WorkbookEditor.AddSheet(xlsx, names.Length + 1);

        await Assert.That(Joined(Names(xlsx))).IsEqualTo(Joined([.. names, first, second]));
        await Assert.That(ActiveTab(xlsx)).IsEqualTo((uint) names.Length + 1);
    }

    // Added between sheets, what names the later ones by place moves on with them.
    [Test]
    public async Task ANewSheet_MovesTheLaterSheetsScopedNames()
    {
        var xlsx = Change(Book("A", "B"), _ => _.WorkbookPart!.Workbook!.AppendChild(new S.DefinedNames(
            new S.DefinedName("B!$A$1")
            {
                Name = "Here",
                LocalSheetId = 1
            })));

        xlsx = WorkbookEditor.AddSheet(xlsx, 1);

        await Assert.That(Joined(Names(xlsx))).IsEqualTo("A|Sheet1|B");
        await Assert.That(Defined(xlsx).Single().Scope).IsEqualTo(2u);
        await Assert.That(Errors(xlsx)).IsEqualTo(0);
    }

    // The workbook of the probe: a number on Data, formulas over it on Other, names, text that only looks
    // like a reference.
    static byte[] Probed()
    {
        var xlsx = Book("Data", "Other");
        xlsx = Type(xlsx, 0, ("A1", "5"), ("B1", "=A1+1"));
        xlsx = Type(xlsx, 1, ("A1", "=Data!A1*2"), ("A2", "=SUM(Data!A1:A3)+Data!$A$1"), ("A3", "=\"Data!A1\""));
        return Change(xlsx, _ => _.WorkbookPart!.Workbook!.AppendChild(new S.DefinedNames(
            new S.DefinedName("Data!$A$1")
            {
                Name = "Five"
            },
            new S.DefinedName("Data!$B$1")
            {
                Name = "Scoped",
                LocalSheetId = 0
            })));
    }

    // Excel spells the new name in quotes where a formula could read it as something else.
    [Test]
    [Arguments("My Data", "'My Data'")]
    [Arguments("Bob's", "'Bob''s'")]
    [Arguments("Q1-2024", "'Q1-2024'")]
    [Arguments("Sales", "Sales")]
    [Arguments("Ünïcode", "Ünïcode")]
    [Arguments("2024", "'2024'")]
    [Arguments("A1", "'A1'")]
    [Arguments("R1C1", "'R1C1'")]
    [Arguments("C", "'C'")]
    [Arguments("TRUE", "'TRUE'")]
    public async Task Renaming_RewritesWhatNamesTheSheet(string name, string spelled)
    {
        var xlsx = WorkbookEditor.RenameSheet(Probed(), 0, name);

        await Assert.That(Names(xlsx)[0]).IsEqualTo(name);
        await Assert.That(Formula(xlsx, 1, "A1")).IsEqualTo($"{spelled}!A1*2");
        await Assert.That(Formula(xlsx, 1, "A2")).IsEqualTo($"SUM({spelled}!A1:A3)+{spelled}!$A$1");
        // Text that only looks like a reference is text.
        await Assert.That(Formula(xlsx, 1, "A3")).IsEqualTo("\"Data!A1\"");
        await Assert.That(Defined(xlsx).Select(_ => _.Text)).IsEquivalentTo([$"{spelled}!$A$1", $"{spelled}!$B$1"]);
        await Assert.That(Read(xlsx).Cell(1, 1, 1)!.Value.Number).IsEqualTo(10);
    }

    [Test]
    [Arguments("")]
    [Arguments("a/b")]
    [Arguments("a\\b")]
    [Arguments("a:b")]
    [Arguments("a[b")]
    [Arguments("a]b")]
    [Arguments("a*b")]
    [Arguments("a?b")]
    [Arguments("'quoted")]
    [Arguments("end'")]
    [Arguments("History")]
    [Arguments("other")]
    [Arguments("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task ANameExcelRefuses_IsRefused(string name) =>
        await Assert.That(() => WorkbookEditor.RenameSheet(Probed(), 0, name)).Throws<InvalidOperationException>();

    [Test]
    public async Task ThirtyOneCharacters_AreEnough()
    {
        var name = new string('x', 31);

        await Assert.That(Names(WorkbookEditor.RenameSheet(Probed(), 0, name))[0]).IsEqualTo(name);
    }

    // Excel writes Data!A1*2 as #REF!*2 once Data is gone: the reference whole, the sheet's name with it.
    [Test]
    public async Task Deleting_LeavesREF_WhereTheSheetWasNamed()
    {
        var xlsx = WorkbookEditor.DeleteSheet(Probed(), 0, now);

        await Assert.That(Names(xlsx)).IsEquivalentTo(["Other"]);
        await Assert.That(Formula(xlsx, 0, "A1")).IsEqualTo("#REF!*2");
        await Assert.That(Formula(xlsx, 0, "A2")).IsEqualTo("SUM(#REF!)+#REF!");
        await Assert.That(Formula(xlsx, 0, "A3")).IsEqualTo("\"Data!A1\"");
        // The name scoped to it goes; the other is #REF!.
        await Assert.That(Defined(xlsx)).IsEquivalentTo([("Five", (uint?) null, "#REF!")]);
        await Assert.That(Read(xlsx).Cell(0, 1, 1)!.Value.IsError).IsTrue();
        await Assert.That(ActiveTab(xlsx)).IsEqualTo(0u);
    }

    [Test]
    public async Task TheLastVisibleSheet_IsNotDeleted()
    {
        var xlsx = Change(Book("A", "B"), _ => _.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().Last().State = S.SheetStateValues.Hidden);

        await Assert.That(() => WorkbookEditor.DeleteSheet(xlsx, 0, now)).Throws<InvalidOperationException>();
        // The hidden one can go.
        await Assert.That(Names(WorkbookEditor.DeleteSheet(xlsx, 1, now))).IsEquivalentTo(["A"]);
    }

    // back-to-school-planner's Budget breakdown totals the Shopping list's Checklist table: the sheet
    // deleted, its tables' structured references are #REF!, and the file is no less valid.
    [Test]
    public async Task ASheetsTables_GoWithIt()
    {
        var original = File.ReadAllBytes(Corpus("back-to-school-planner"));

        var xlsx = WorkbookEditor.DeleteSheet(original, 0, now);

        await Assert.That(Joined(Names(xlsx))).IsEqualTo("Budget breakdown|To-do list|Share list");
        await Assert.That(Formula(xlsx, 0, "C4")).IsEqualTo("IFERROR(SUMIFS(#REF!,#REF!,Category[[#This Row],[Category]]), \"\")");
        await Assert.That(Defined(xlsx).Single(_ => _.Name == "ChecklistTotal").Text).IsEqualTo("SUM(#REF!)");
        // Its print titles were scoped to it; the others' scopes moved back a place.
        await Assert.That(Joined(Defined(xlsx).Where(_ => _.Name == "_xlnm.Print_Titles").Select(_ => $"{_.Scope}").Order())).IsEqualTo("1|2");
        await Assert.That(Errors(xlsx)).IsLessThanOrEqualTo(Errors(original));
    }

    // Moved, a sheet keeps the names scoped to it, and the active tab follows it.
    [Test]
    public async Task Moving_KeepsWhatIsScopedToTheSheet()
    {
        var xlsx = Change(Book("A", "B", "C"), _ => _.WorkbookPart!.Workbook!.AppendChild(new S.DefinedNames(
            new S.DefinedName("A!$A$1")
            {
                Name = "Here",
                LocalSheetId = 0
            },
            new S.DefinedName("'C'!$1:$1")
            {
                Name = "_xlnm.Print_Titles",
                LocalSheetId = 2
            })));

        xlsx = WorkbookEditor.MoveSheet(xlsx, 0, 2);

        await Assert.That(Joined(Names(xlsx))).IsEqualTo("B|C|A");
        await Assert.That(Defined(xlsx).Select(_ => (_.Name, _.Scope))).IsEquivalentTo([("Here", (uint?) 2), ("_xlnm.Print_Titles", (uint?) 1)]);
        await Assert.That(ActiveTab(xlsx)).IsEqualTo(2u);

        xlsx = WorkbookEditor.MoveSheet(xlsx, 2, 0);
        await Assert.That(Joined(Names(xlsx))).IsEqualTo("A|B|C");
        await Assert.That(Defined(xlsx).Select(_ => (_.Name, _.Scope))).IsEquivalentTo([("Here", (uint?) 0), ("_xlnm.Print_Titles", (uint?) 2)]);
    }

    [Test]
    public async Task AProtectedStructure_IsLeftAsItIs()
    {
        var xlsx = Change(Book("A", "B"), _ => _.WorkbookPart!.Workbook!.InsertAfter(
            new S.WorkbookProtection
            {
                LockStructure = true
            },
            null));

        await Assert.That(() => WorkbookEditor.AddSheet(xlsx, 1)).Throws<InvalidOperationException>();
        await Assert.That(() => WorkbookEditor.RenameSheet(xlsx, 0, "Z")).Throws<InvalidOperationException>();
        await Assert.That(() => WorkbookEditor.DeleteSheet(xlsx, 0, now)).Throws<InvalidOperationException>();
        await Assert.That(() => WorkbookEditor.MoveSheet(xlsx, 0, 1)).Throws<InvalidOperationException>();
    }

    static string Corpus(string name) =>
        Path.Combine(ScenarioInputs.Root(ScenarioFormat.Excel), name, "input.xlsx");

    public static IEnumerable<string> Workbooks() =>
        FormulaCorpusTests.Workbooks();

    // In every corpus workbook: its first sheet renamed and back, moved to the end and back, puts every
    // formula and value back; a sheet added and deleted again too; and each step's file is no less valid.
    [Test]
    [MethodDataSource(nameof(Workbooks))]
    public async Task EachStepUndone_PutsEverythingBack(string name)
    {
        var original = WorkbookEditor.Recalculate(File.ReadAllBytes(Corpus(name)), now);
        var errors = Errors(original);
        var expected = Cells(original);
        var names = Names(original);
        var last = names.Count - 1;

        var renamed = WorkbookEditor.RenameSheet(original, 0, "Renamed sheet's");
        await Assert.That(Errors(renamed)).IsLessThanOrEqualTo(errors);
        await Assert.That(WorkbookGrid.Read(renamed, ProjectFonts.Directory).Sheets[0].Name).IsEqualTo("Renamed sheet's");
        var back = WorkbookEditor.RenameSheet(renamed, 0, names[0]);
        await Assert.That(Cells(back)).IsEquivalentTo(expected);

        var moved = WorkbookEditor.MoveSheet(original, 0, last);
        await Assert.That(Errors(moved)).IsLessThanOrEqualTo(errors);
        await Assert.That(Cells(WorkbookEditor.MoveSheet(moved, last, 0))).IsEquivalentTo(expected);

        var added = WorkbookEditor.AddSheet(original, 1);
        await Assert.That(Errors(added)).IsLessThanOrEqualTo(errors);
        await Assert.That(ExcelConverter.ConvertToMarkdown(new MemoryStream(added))).IsNotNull();
        var removed = WorkbookEditor.DeleteSheet(added, 1, now);
        await Assert.That(Joined(Names(removed))).IsEqualTo(Joined(names));
        await Assert.That(Cells(removed)).IsEquivalentTo(expected);

        if (names.Count > 1)
        {
            var deleted = WorkbookEditor.DeleteSheet(original, 0, now);
            await Assert.That(Errors(deleted)).IsLessThanOrEqualTo(errors);
            await Assert.That(WorkbookGrid.Read(deleted, ProjectFonts.Directory)).IsNotNull();
        }
    }

    static List<string> Cells(byte[] xlsx)
    {
        var cells = new List<string>();
        foreach (var sheet in Read(xlsx).Sheets)
        {
            foreach (var cell in sheet.Cells)
            {
                if (cell.Formula == null &&
                    cell.Value.IsBlank)
                {
                    continue;
                }

                var value = cell.Value;
                cells.Add($"{sheet.Name}!{cell.Row},{cell.Column}={cell.Formula}|{value.Kind}:{value.Number.ToString("R", CultureInfo.InvariantCulture)}:{value.Text}");
            }
        }

        return cells;
    }

    static int Errors(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).Count();
    }
}
