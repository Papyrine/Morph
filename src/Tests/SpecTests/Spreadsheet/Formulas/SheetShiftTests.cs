using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using XDR = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using XM = DocumentFormat.OpenXml.Office.Excel;

/// <summary>
/// Rows and columns inserted and deleted, as Excel does it: every formula, name, range and table that
/// names the cells moves with them. The expected formulas are what Excel made of the same workbook and
/// the same steps, read back over COM (Excel 16, 2026-11-17).
/// </summary>
public class SheetShiftTests
{
    static readonly DateTime now = new(2026, 11, 17, 9, 0, 0);

    // A workbook of empty sheets, named.
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

    // The workbook changed by hand, for what the editor does not make: a name, a merge, an array.
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

    static S.Worksheet Sheet(SpreadsheetDocument document, int index)
    {
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook!.Sheets!.Elements<S.Sheet>().ElementAt(index);
        return ((WorksheetPart) workbook.GetPartById(sheet.Id!.Value!)).Worksheet!;
    }

    static FormulaWorkbook Read(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return FormulaWorkbook.Read(document.WorkbookPart!, now);
    }

    static string? Formula(byte[] xlsx, int sheet, string cell) =>
        Read(xlsx).Cell(sheet, CellReference.RowOf(cell), CellReference.ColumnOf(cell))?.Formula;

    static byte[] Type(byte[] xlsx, int sheet, params (string Cell, string Input)[] cells) =>
        WorkbookEditor.SetCells(
            xlsx,
            sheet,
            [.. cells.Select(_ => new CellEdit(CellReference.RowOf(_.Cell), CellReference.ColumnOf(_.Cell), _.Input))],
            now);

    static string DefinedName(byte[] xlsx, string name) =>
        Look(xlsx, _ => _.WorkbookPart!.Workbook!.DefinedNames!.Elements<S.DefinedName>().First(defined => defined.Name == name).Text);

    static List<string> Merges(byte[] xlsx, int sheet) =>
        Look(xlsx, _ => Sheet(_, sheet).Descendants<S.MergeCell>().Select(merge => merge.Reference!.Value!).ToList());

    // The workbook of the probe: numbers in Data!A1:A5, formulas over them on both sheets, a name, merges.
    static byte[] Probed()
    {
        var xlsx = Book("Data", "Other");
        xlsx = Type(
            xlsx,
            0,
            ("A1", "1"),
            ("A2", "2"),
            ("A3", "3"),
            ("A4", "4"),
            ("A5", "5"),
            ("B1", "=SUM(A1:A5)"),
            ("B2", "=SUM(A3:A5)"),
            ("B3", "=A4+$A$5"),
            ("B4", "=SUM(A6:A9)"),
            ("B5", "=SUM(A:A)+SUM(4:5)"),
            ("C2", "=SUM(A3:A1048576)"));
        xlsx = Type(xlsx, 1, ("A1", "=Data!A3+Data!A2"), ("A2", "=SUM(Data!A2:A4)"), ("A3", "=A3+1"));
        return Change(xlsx, _ =>
        {
            var workbook = _.WorkbookPart!.Workbook!;
            workbook.AppendChild(new S.DefinedNames(new S.DefinedName("Data!$A$3")
            {
                Name = "Third"
            }));
            var data = Sheet(_, 0);
            data.InsertAfter(
                new S.MergeCells(
                    new S.MergeCell
                    {
                        Reference = "D2:E4"
                    },
                    new S.MergeCell
                    {
                        Reference = "D6:E7"
                    }),
                data.GetFirstChild<S.SheetData>());
        });
    }

    [Test]
    public async Task InsertedRows_MoveWhatIsBelow_AsExcelMovesIt()
    {
        var xlsx = WorkbookEditor.InsertRows(Probed(), 0, 3, 1, now);

        await Assert.That(Formula(xlsx, 0, "B1")).IsEqualTo("SUM(A1:A6)");
        await Assert.That(Formula(xlsx, 0, "B2")).IsEqualTo("SUM(A4:A6)");
        await Assert.That(Formula(xlsx, 0, "B4")).IsEqualTo("A5+$A$6");
        await Assert.That(Formula(xlsx, 0, "B5")).IsEqualTo("SUM(A7:A10)");
        await Assert.That(Formula(xlsx, 0, "B6")).IsEqualTo("SUM(A:A)+SUM(5:6)");
        // A range reaching the sheet's last row still reaches it.
        await Assert.That(Formula(xlsx, 0, "C2")).IsEqualTo("SUM(A4:A1048576)");
        await Assert.That(Formula(xlsx, 1, "A1")).IsEqualTo("Data!A4+Data!A2");
        await Assert.That(Formula(xlsx, 1, "A2")).IsEqualTo("SUM(Data!A2:A5)");
        // Another sheet's own cells are not this sheet's.
        await Assert.That(Formula(xlsx, 1, "A3")).IsEqualTo("A3+1");
        await Assert.That(DefinedName(xlsx, "Third")).IsEqualTo("Data!$A$4");
        await Assert.That(Merges(xlsx, 0)).IsEquivalentTo(["D2:E5", "D7:E8"]);
        // The values follow: B1 still sums the five numbers, now in A1:A6.
        await Assert.That(Read(xlsx).Cell(0, 1, 2)!.Value.Number).IsEqualTo(15);

        // Just past a range's end, the range is left as it was.
        xlsx = WorkbookEditor.InsertRows(xlsx, 0, 7, 1, now);
        await Assert.That(Formula(xlsx, 0, "B1")).IsEqualTo("SUM(A1:A6)");
    }

    [Test]
    public async Task DeletedRows_LeaveREF_WhereTheirCellsWere()
    {
        var xlsx = WorkbookEditor.InsertRows(Probed(), 0, 3, 1, now);
        xlsx = WorkbookEditor.InsertRows(xlsx, 0, 7, 1, now);
        xlsx = WorkbookEditor.DeleteRows(xlsx, 0, 3, 2, now);

        await Assert.That(Formula(xlsx, 0, "B1")).IsEqualTo("SUM(A1:A4)");
        await Assert.That(Formula(xlsx, 0, "B2")).IsEqualTo("SUM(A3:A4)");
        await Assert.That(Formula(xlsx, 0, "B3")).IsEqualTo("SUM(A6:A9)");
        await Assert.That(Formula(xlsx, 0, "B4")).IsEqualTo("SUM(A:A)+SUM(3:4)");
        await Assert.That(Formula(xlsx, 0, "C2")).IsEqualTo("SUM(A3:A1048576)");
        await Assert.That(Formula(xlsx, 1, "A1")).IsEqualTo("Data!#REF!+Data!A2");
        await Assert.That(Formula(xlsx, 1, "A2")).IsEqualTo("SUM(Data!A2:A3)");
        await Assert.That(DefinedName(xlsx, "Third")).IsEqualTo("Data!#REF!");
        // D6:E7 moved whole to D8:E9 with the second insert, at its first row.
        await Assert.That(Merges(xlsx, 0)).IsEquivalentTo(["D2:E3", "D6:E7"]);
        await Assert.That(Read(xlsx).Cell(1, 1, 1)!.Value.IsError).IsTrue();

        xlsx = WorkbookEditor.DeleteRows(xlsx, 0, 2, 1, now);
        await Assert.That(Formula(xlsx, 0, "B1")).IsEqualTo("SUM(A1:A3)");
        await Assert.That(Formula(xlsx, 0, "B2")).IsEqualTo("SUM(A5:A8)");
        await Assert.That(Formula(xlsx, 0, "B3")).IsEqualTo("SUM(A:A)+SUM(2:3)");
        await Assert.That(Formula(xlsx, 1, "A1")).IsEqualTo("Data!#REF!+Data!#REF!");
        await Assert.That(Merges(xlsx, 0)).IsEquivalentTo(["D2:E2", "D5:E6"]);
    }

    [Test]
    public async Task Columns_MoveAsRowsDo()
    {
        var xlsx = Type(Book("Data"), 0, ("A1", "1"), ("B1", "2"), ("C1", "3"), ("G1", "=SUM(A1:C1)+$A1"));

        xlsx = WorkbookEditor.InsertColumns(xlsx, 0, 2, 1, now);
        await Assert.That(Formula(xlsx, 0, "H1")).IsEqualTo("SUM(A1:D1)+$A1");
        await Assert.That(Read(xlsx).Cell(0, 1, 4)!.Value.Number).IsEqualTo(3);

        xlsx = WorkbookEditor.DeleteColumns(xlsx, 0, 1, 1, now);
        await Assert.That(Formula(xlsx, 0, "G1")).IsEqualTo("SUM(A1:C1)+#REF!");
    }

    [Test]
    public async Task InsertedRows_LookAsTheRowAbove()
    {
        var xlsx = Type(Book("Data"), 0, ("A2", "x"), ("C2", "y"));
        xlsx = WorkbookEditor.Format(xlsx, 0, new(2, 1, 2, 3), new()
        {
            Bold = true,
            FillHex = "FF0000"
        });
        xlsx = WorkbookEditor.SetRowHeight(xlsx, 0, 2, 2, 40);

        var inserted = WorkbookEditor.InsertRows(xlsx, 0, 3, 2, now);
        var sheet = WorkbookGrid.Read(inserted, ProjectFonts.Directory).Sheets[0];
        await Assert.That(sheet.RowHeight(3)).IsEqualTo(sheet.RowHeight(2));
        await Assert.That(sheet.RowHeight(4)).IsEqualTo(sheet.RowHeight(2));
        await Assert.That(sheet.Look(3, 2).FillHex).IsEqualTo("FF0000");
        await Assert.That(sheet.Look(4, 3).Bold).IsTrue();
        // What the row above held is not copied, only how it looks.
        await Assert.That(sheet.Cell(3, 1)?.Display ?? string.Empty).IsEqualTo(string.Empty);

        // Above the first row there is nothing to look like.
        var first = WorkbookGrid.Read(WorkbookEditor.InsertRows(xlsx, 0, 1, 1, now), ProjectFonts.Directory).Sheets[0];
        await Assert.That(first.RowHeight(1)).IsEqualTo(first.DefaultRowHeight);
        await Assert.That(first.RowHeight(3)).IsEqualTo(sheet.RowHeight(2));
    }

    [Test]
    public async Task InsertedColumns_LookAsTheColumnOnTheirLeft()
    {
        var xlsx = Type(Book("Data"), 0, ("F1", "x"));
        xlsx = WorkbookEditor.Format(xlsx, 0, new(1, 6, 1, 6), new()
        {
            FillHex = "FF0000"
        });
        xlsx = WorkbookEditor.SetColumnWidth(xlsx, 0, 6, 6, 25);
        xlsx = WorkbookEditor.SetColumnWidth(xlsx, 0, 1, 1, 4);

        var inserted = WorkbookEditor.InsertColumns(xlsx, 0, 7, 1, now);
        var sheet = WorkbookGrid.Read(inserted, ProjectFonts.Directory).Sheets[0];
        await Assert.That(sheet.ColumnWidth(7)).IsEqualTo(sheet.ColumnWidth(6));
        await Assert.That(sheet.Look(1, 7).FillHex).IsEqualTo("FF0000");
        await Assert.That(sheet.ColumnWidth(8)).IsEqualTo(sheet.DefaultColumnWidth);

        var first = WorkbookGrid.Read(WorkbookEditor.InsertColumns(xlsx, 0, 1, 1, now), ProjectFonts.Directory).Sheets[0];
        await Assert.That(first.ColumnWidth(1)).IsEqualTo(first.DefaultColumnWidth);
        await Assert.That(first.ColumnWidth(2)).IsEqualTo(sheet.ColumnWidth(1));
        await Assert.That(first.ColumnWidth(7)).IsEqualTo(sheet.ColumnWidth(6));
    }

    static string Corpus(string name) =>
        Path.Combine(ScenarioInputs.Root(ScenarioFormat.Excel), name, "input.xlsx");

    // back-to-school-planner's first sheet, Shopping list: its Checklist table B9:H25 (header row 9),
    // whose Total cost column (H) is calculated from Qty (D) and Cost (F).
    static byte[] Planner() =>
        File.ReadAllBytes(Corpus("back-to-school-planner"));

    static S.Table Table(SpreadsheetDocument document, string name) =>
        document.WorkbookPart!.WorksheetParts
            .SelectMany(_ => _.TableDefinitionParts)
            .Select(_ => _.Table!)
            .First(_ => _.DisplayName == name);

    static string TableRange(byte[] xlsx, string name) =>
        Look(xlsx, _ => Table(_, name).Reference!.Value!);

    [Test]
    public async Task RowsInsertedInATable_GrowIt_AndTakeItsCalculatedColumn()
    {
        var xlsx = WorkbookEditor.InsertRows(Planner(), 0, 12, 2, now);

        await Assert.That(TableRange(xlsx, "Checklist")).IsEqualTo("B9:H27");
        await Assert.That(Look(xlsx, _ => Table(_, "Checklist").AutoFilter!.Reference!.Value)).IsEqualTo("B9:H27");
        var calculated = "IFERROR(Checklist[[#This Row],[Qty]]*Checklist[[#This Row],[Cost]], \"\")";
        await Assert.That(Formula(xlsx, 0, "H12")).IsEqualTo(calculated);
        await Assert.That(Formula(xlsx, 0, "H13")).IsEqualTo(calculated);
        // Excel 2010's conditional formats over the table's columns grow with it.
        var sqrefs = Look(xlsx, _ => Sheet(_, 0).Descendants<XM.ReferenceSequence>().Select(sqref => sqref.Text).ToList());
        await Assert.That(sqrefs).IsEquivalentTo(["G10:G27", "E10:E27"]);
    }

    [Test]
    public async Task ColumnsInsertedInATable_AreItsColumns_NamedColumn1()
    {
        var xlsx = WorkbookEditor.InsertColumns(Planner(), 0, 5, 1, now);

        await Assert.That(TableRange(xlsx, "Checklist")).IsEqualTo("B9:I25");
        var names = Look(xlsx, _ => Table(_, "Checklist").TableColumns!.Elements<S.TableColumn>().Select(column => column.Name!.Value!).ToList());
        await Assert.That(names).IsEquivalentTo(["Item", "Category", "Qty", "Column1", "To buy", "Cost", "Bought", "Total cost"]);
        await Assert.That(Look(xlsx, _ => Table(_, "Checklist").TableColumns!.Count!.Value)).IsEqualTo(8u);
        await Assert.That(WorkbookGrid.Read(xlsx, ProjectFonts.Directory).Sheets[0].Cell(9, 5)!.Display).IsEqualTo("Column1");
    }

    [Test]
    public async Task AColumnDeletedFromATable_MakesItsReferencesREF()
    {
        var xlsx = WorkbookEditor.DeleteColumns(Planner(), 0, 4, 1, now);

        await Assert.That(TableRange(xlsx, "Checklist")).IsEqualTo("B9:G25");
        // Total cost, now in G, was Qty times Cost.
        await Assert.That(Formula(xlsx, 0, "G10")).IsEqualTo("IFERROR(#REF!*Checklist[[#This Row],[Cost]], \"\")");
        var calculated = Look(xlsx, _ => Table(_, "Checklist").TableColumns!.Elements<S.TableColumn>().Last().CalculatedColumnFormula!.Text);
        await Assert.That(calculated).IsEqualTo("IFERROR(#REF!*Checklist[[#This Row],[Cost]], \"\")");
    }

    // What Excel named the columns inserted one at a time at the same place in a table of three, its first
    // two named as given (Excel 16 over COM, 2026-11-17).
    public static IEnumerable<(string Left, string Right, string First, string Second)> ColumnNames() =>
    [
        ("Name", "Qty", "Column1", "Column2"),
        ("Item 7", "Price", "Item 8", "Item 72"),
        ("Column 1", "Column 2", "Column 12", "Column 13"),
        ("Column1", "Column2", "Column12", "Column13"),
        ("Column2", "X", "Column3", "Column22"),
        ("Total2", "Z", "Total3", "Total22"),
        ("Total", "Total2", "Column1", "Column2")
    ];

    [Test]
    [MethodDataSource(nameof(ColumnNames))]
    public async Task AColumnInsertedInATable_IsNamedAfterTheOneOnItsLeft(string left, string right, string first, string second)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { left, right, "Notes" };
        var made = WorkbookEditor.NewColumnName(left, names);
        names.Add(made);

        await Assert.That(made).IsEqualTo(first);
        await Assert.That(WorkbookEditor.NewColumnName(left, names)).IsEqualTo(second);
    }

    // Two inserted at once: each is named after the one on its left, the first of them included.
    [Test]
    public async Task ColumnsInsertedTogether_AreNamedInTurn()
    {
        var xlsx = WorkbookEditor.InsertColumns(Planner(), 0, 5, 2, now);

        var names = Look(xlsx, _ => Table(_, "Checklist").TableColumns!.Elements<S.TableColumn>().Select(column => column.Name!.Value!).ToList());
        await Assert.That(names).IsEquivalentTo(["Item", "Category", "Qty", "Column1", "Column2", "To buy", "Cost", "Bought", "Total cost"]);
    }

    // check-register's running balance, a calculated column: IF(ROW()=3,$H$3,I2-…) in its first data row
    // (row 3), the row above's balance in each row after. Rows inserted at row 3 take the formula as it
    // was, copied down; the rows that moved have theirs moved; and the table's own formula stays.
    [Test]
    public async Task RowsInsertedAtTheFirstDataRow_TakeTheCalculatedFormulaAsItWas()
    {
        var original = File.ReadAllBytes(Corpus("check-register"));
        var calculated = Look(original, _ => Table(_, "CheckRegister").TableColumns!.Elements<S.TableColumn>().Select(column => column.CalculatedColumnFormula?.Text).Last(text => text != null));

        var xlsx = WorkbookEditor.InsertRows(original, 0, 3, 2, now);

        await Assert.That(Formula(xlsx, 0, "I3")).Contains("$H$3,I2-");
        await Assert.That(Formula(xlsx, 0, "I4")).Contains("$H$3,I3-");
        await Assert.That(Formula(xlsx, 0, "I5")).Contains("$H$5,I2-");
        var after = Look(xlsx, _ => Table(_, "CheckRegister").TableColumns!.Elements<S.TableColumn>().Select(column => column.CalculatedColumnFormula?.Text).Last(text => text != null));
        await Assert.That(after).IsEqualTo(calculated);

        // Inserted lower down, the row takes the first data row's formula copied to it.
        xlsx = WorkbookEditor.InsertRows(xlsx, 0, 8, 1, now);
        await Assert.That(Formula(xlsx, 0, "I8")).Contains("$H$3,I7-");
    }

    // simple-black-purple-commercial-invoice's subtotal sums its Invoice_2 table's amounts, E20:E28 —
    // the table's data rows. Rows inserted at its first data row grow that range with the table, as
    // Excel does, where a range over only some of its rows moves down.
    [Test]
    public async Task ARangeOverATablesData_GrowsWithIt()
    {
        var original = Type(File.ReadAllBytes(Corpus("simple-black-purple-commercial-invoice")), 0, ("H5", "=SUM(E20:E22)"));

        var xlsx = WorkbookEditor.InsertRows(original, 0, 20, 2, now);

        await Assert.That(Formula(xlsx, 0, "E32")).IsEqualTo("IFERROR(SUM(E20:E30,E31),\"\")");
        await Assert.That(Formula(xlsx, 0, "H5")).IsEqualTo("SUM(E22:E24)");
        await Assert.That(TableRange(xlsx, "Invoice_2")).IsEqualTo("B19:E30");

        // So does a conditional format over a table's column: back-to-school-planner's over Bought.
        var planner = WorkbookEditor.InsertRows(Planner(), 0, 10, 1, now);
        var sqrefs = Look(planner, _ => Sheet(_, 0).Descendants<XM.ReferenceSequence>().Select(sqref => sqref.Text).ToList());
        await Assert.That(sqrefs).IsEquivalentTo(["G10:G26", "E10:E26"]);
    }

    // wedding-budget-template's tables stand side by side: TBL_Gifts (B32:E37) beside TBL_Flowers
    // (G30:J37). A row inserted through both, or deleted from both, would shift one's cells against the
    // other's, which Excel refuses; rows inserted at two headers move both tables whole, which it does.
    [Test]
    public async Task TablesSideBySide_AreNotResizedTogether()
    {
        var xlsx = File.ReadAllBytes(Corpus("wedding-budget-template"));

        await Assert.That(() => WorkbookEditor.InsertRows(xlsx, 0, 34, 2, now)).Throws<InvalidOperationException>();
        await Assert.That(() => WorkbookEditor.DeleteRows(xlsx, 0, 23, 2, now)).Throws<InvalidOperationException>();
        await Assert.That(() => WorkbookEditor.InsertColumns(xlsx, 0, 9, 1, now)).Throws<InvalidOperationException>();

        var moved = WorkbookEditor.InsertRows(xlsx, 0, 20, 1, now);
        await Assert.That(TableRange(moved, "TBL_Apparel")).IsEqualTo("B21:E30");
        await Assert.That(TableRange(moved, "TBL_Decorations")).IsEqualTo("G21:J28");
    }

    [Test]
    public async Task ATablesHeaderRow_IsNotDeletedAlone() =>
        await Assert.That(() => WorkbookEditor.DeleteRows(Planner(), 0, 9, 1, now)).Throws<InvalidOperationException>();

    [Test]
    public async Task EveryDataRowDeleted_LeavesTheHeaderWithARowUnderIt()
    {
        var xlsx = WorkbookEditor.DeleteRows(Planner(), 0, 10, 16, now);

        await Assert.That(TableRange(xlsx, "Checklist")).IsEqualTo("B9:H10");
    }

    [Test]
    public async Task ATableDeletedWhole_IsGone_AndWhatNamedItIsREF()
    {
        var xlsx = WorkbookEditor.DeleteRows(Planner(), 0, 9, 17, now);

        var tables = Look(xlsx, _ => _.WorkbookPart!.WorksheetParts.SelectMany(part => part.TableDefinitionParts).Select(part => part.Table!.DisplayName!.Value).ToList());
        await Assert.That(tables).DoesNotContain("Checklist");
        await Assert.That(DefinedName(xlsx, "ChecklistTotal")).IsEqualTo("SUM(#REF!)");
        // Budget breakdown's totals read the table.
        await Assert.That(Formula(xlsx, 1, "C4")).IsEqualTo("IFERROR(SUMIFS(#REF!,#REF!,Category[[#This Row],[Category]]), \"\")");
        await Assert.That(Errors(xlsx)).IsLessThanOrEqualTo(Errors(Planner()));
    }

    [Test]
    public async Task WhatElseNamesTheCells_MovesWithThem()
    {
        var xlsx = WorkbookEditor.InsertRows(Planner(), 0, 3, 2, now);

        // The chart's series, the print titles and a name.
        var series = Look(xlsx, _ => _.WorkbookPart!.WorksheetParts
            .SelectMany(part => part.DrawingsPart?.ChartParts ?? [])
            .SelectMany(chart => chart.ChartSpace!.Descendants<DocumentFormat.OpenXml.Drawing.Charts.Formula>())
            .Select(formula => formula.Text)
            .ToList());
        await Assert.That(series).Contains("'Shopping list'!$E$6");
        await Assert.That(series).Contains("'Shopping list'!$G$11");
        await Assert.That(series).Contains("'Shopping list'!$E$7");
        // Another sheet's chart is left alone.
        await Assert.That(series).Contains("'Budget breakdown'!$B$4:$B$6");
        await Assert.That(DefinedName(xlsx, "RowTitleRegion1..C7")).IsEqualTo("'Shopping list'!$B$7");
        var titles = Look(xlsx, _ => _.WorkbookPart!.Workbook!.DefinedNames!.Elements<S.DefinedName>()
            .First(defined => defined.Name == "_xlnm.Print_Titles" && defined.LocalSheetId?.Value == 0).Text);
        await Assert.That(titles).IsEqualTo("'Shopping list'!$11:$11");

        // The merges: B3:H3 moves down whole, as the insert is at its row.
        await Assert.That(Merges(xlsx, 0)).IsEquivalentTo(["B6:C6", "B5:H5"]);
        await Assert.That(TableRange(xlsx, "Checklist")).IsEqualTo("B11:H27");

        var (formats, validations) = Look(xlsx, _ =>
        {
            var sheet = Sheet(_, 0);
            return (
                sheet.Elements<S.ConditionalFormatting>().Select(format => format.SequenceOfReferences!.InnerText!).ToList(),
                sheet.Descendants<S.DataValidation>().Select(validation => validation.SequenceOfReferences!.InnerText!).ToList());
        });
        await Assert.That(formats).IsEquivalentTo(["E7:H9"]);
        await Assert.That(validations).Contains("B6:C6");
        await Assert.That(validations).Contains("B1");

        // The pictures move with their cells and keep their size: one stands below the insert, one above.
        var rows = Look(xlsx, _ => Sheet(_, 0).WorksheetPart!.DrawingsPart!.WorksheetDrawing!
            .Elements<XDR.TwoCellAnchor>()
            .Select(anchor => (From: int.Parse(anchor.FromMarker!.RowId!.Text), To: int.Parse(anchor.ToMarker!.RowId!.Text)))
            .ToList());
        var before = Look(Planner(), _ => Sheet(_, 0).WorksheetPart!.DrawingsPart!.WorksheetDrawing!
            .Elements<XDR.TwoCellAnchor>()
            .Select(anchor => (From: int.Parse(anchor.FromMarker!.RowId!.Text), To: int.Parse(anchor.ToMarker!.RowId!.Text)))
            .ToList());
        await Assert.That(rows[0]).IsEqualTo((before[0].From + 2, before[0].To + 2));
        await Assert.That(rows[1]).IsEqualTo(before[1]);
        await Assert.That(Errors(xlsx)).IsLessThanOrEqualTo(Errors(Planner()));
    }

    [Test]
    public async Task APictureThatSizesWithItsCells_GoesWithThem()
    {
        var xlsx = Change(Book("Data"), _ =>
        {
            var part = _.WorkbookPart!.WorksheetParts.First();
            var drawings = part.AddNewPart<DrawingsPart>();
            drawings.WorksheetDrawing = new(
                Anchor(null, 9, 14),
                Anchor(XDR.EditAsValues.OneCell, 9, 14),
                Anchor(XDR.EditAsValues.Absolute, 9, 14));
            part.Worksheet!.AppendChild(new S.Drawing
            {
                Id = part.GetIdOfPart(drawings)
            });
        });

        xlsx = WorkbookEditor.InsertRows(xlsx, 0, 11, 1, now);
        await Assert.That(Anchors(xlsx)).IsEquivalentTo([(9, 15), (9, 14), (9, 14)]);

        // Rows 9 to 20 deleted: the one that sizes goes, the one that moves stands at row 9.
        xlsx = WorkbookEditor.DeleteRows(xlsx, 0, 9, 12, now);
        await Assert.That(Anchors(xlsx)).IsEquivalentTo([(8, 13), (9, 14)]);
    }

    [Test]
    public async Task AShapeThatGoes_TakesItsLinkWithIt()
    {
        var xlsx = Change(Book("Data"), _ =>
        {
            var part = _.WorkbookPart!.WorksheetParts.First();
            var drawings = part.AddNewPart<DrawingsPart>();
            var link = drawings.AddHyperlinkRelationship(new("https://example.com/"), true);
            var anchor = Anchor(null, 9, 14);
            anchor.Descendants<XDR.NonVisualDrawingProperties>().Single()
                .AppendChild(new DocumentFormat.OpenXml.Drawing.HyperlinkOnClick
                {
                    Id = link.Id
                });
            drawings.WorksheetDrawing = new(anchor);
            part.Worksheet!.AppendChild(new S.Drawing
            {
                Id = part.GetIdOfPart(drawings)
            });
        });

        xlsx = WorkbookEditor.DeleteRows(xlsx, 0, 9, 12, now);
        var links = Look(xlsx, _ => Sheet(_, 0).WorksheetPart!.DrawingsPart!.HyperlinkRelationships.Count());
        await Assert.That(Anchors(xlsx)).IsEmpty();
        await Assert.That(links).IsEqualTo(0);
    }

    // A shape over rows from to to (zero-based, as the file has them), column B.
    static XDR.TwoCellAnchor Anchor(XDR.EditAsValues? mode, int from, int to)
    {
        var anchor = new XDR.TwoCellAnchor(
            new XDR.FromMarker(new XDR.ColumnId("1"), new XDR.ColumnOffset("0"), new XDR.RowId(from.ToString()), new XDR.RowOffset("0")),
            new XDR.ToMarker(new XDR.ColumnId("2"), new XDR.ColumnOffset("0"), new XDR.RowId(to.ToString()), new XDR.RowOffset("0")),
            new XDR.Shape(
                new XDR.NonVisualShapeProperties(
                    new XDR.NonVisualDrawingProperties
                    {
                        Id = 2,
                        Name = "Shape"
                    },
                    new XDR.NonVisualShapeDrawingProperties()),
                new XDR.ShapeProperties()),
            new XDR.ClientData());
        if (mode is { } value)
        {
            anchor.EditAs = value;
        }

        return anchor;
    }

    static List<(int From, int To)> Anchors(byte[] xlsx) =>
        Look(xlsx, _ => Sheet(_, 0).WorksheetPart!.DrawingsPart!.WorksheetDrawing!
            .Elements<XDR.TwoCellAnchor>()
            .Select(anchor => (int.Parse(anchor.FromMarker!.RowId!.Text), int.Parse(anchor.ToMarker!.RowId!.Text)))
            .ToList());

    [Test]
    public async Task AnArray_IsNotCutInto()
    {
        var xlsx = Change(Type(Book("Data"), 0, ("A1", "1"), ("A2", "2"), ("A3", "3")), _ =>
        {
            var cell = WorkbookEditor.CellAt(Sheet(_, 0), 1, 2);
            cell.CellFormula = new("A1:A3*2")
            {
                FormulaType = S.CellFormulaValues.Array,
                Reference = "B1:B3"
            };
        });

        await Assert.That(() => WorkbookEditor.InsertRows(xlsx, 0, 2, 1, now)).Throws<InvalidOperationException>();
        await Assert.That(() => WorkbookEditor.DeleteRows(xlsx, 0, 3, 1, now)).Throws<InvalidOperationException>();
        // Above it, it moves whole.
        var moved = WorkbookEditor.InsertRows(xlsx, 0, 1, 1, now);
        var block = Look(moved, _ => Sheet(_, 0).Descendants<S.CellFormula>().First(formula => formula.FormulaType?.Value == S.CellFormulaValues.Array).Reference!.Value);
        await Assert.That(block).IsEqualTo("B2:B4");
    }

    [Test]
    public async Task NothingIsPushedOffTheSheet()
    {
        var xlsx = Type(Book("Data"), 0, ("A1048576", "last"));

        await Assert.That(() => WorkbookEditor.InsertRows(xlsx, 0, 5, 1, now)).Throws<InvalidOperationException>();
        // Deleting makes room.
        await Assert.That(WorkbookEditor.DeleteRows(xlsx, 0, 5, 1, now)).IsNotNull();
    }

    public static IEnumerable<string> Workbooks() =>
        FormulaCorpusTests.Workbooks();

    // In every corpus workbook, on every sheet of cells: rows inserted halfway down and deleted again,
    // then columns, leave every formula and every value as it was, and the file no less valid on the way.
    [Test]
    [MethodDataSource(nameof(Workbooks))]
    public async Task InsertedThenDeleted_PutsEverythingBack(string name)
    {
        var original = WorkbookEditor.Recalculate(File.ReadAllBytes(Corpus(name)), now);
        var errors = Errors(original);
        var expected = Cells(original);
        var book = Read(original);
        foreach (var sheet in book.Sheets.Where(_ => _.MaxRow > 0))
        {
            foreach (var rows in new[] { true, false })
            {
                var at = Math.Max(2, (rows ? sheet.MaxRow : sheet.MaxColumn) / 2);
                byte[] inserted;
                try
                {
                    inserted = rows
                        ? WorkbookEditor.InsertRows(original, sheet.Index, at, 2, now)
                        : WorkbookEditor.InsertColumns(original, sheet.Index, at, 2, now);
                }
                catch (InvalidOperationException)
                {
                    // An array or a PivotTable there, which Excel would not cut into either.
                    continue;
                }

                await Assert.That(Errors(inserted)).IsLessThanOrEqualTo(errors);
                await Assert.That(WorkbookGrid.Read(inserted, ProjectFonts.Directory)).IsNotNull();
                await Assert.That(ExcelConverter.ConvertToMarkdown(new MemoryStream(inserted))).IsNotNull();

                // What was at and past the insert is two further on.
                var moved = Cells(inserted);
                foreach (var ((index, row, column), value) in expected)
                {
                    if (index != sheet.Index ||
                        value.Formula != null)
                    {
                        continue;
                    }

                    var target = rows ? (index, row >= at ? row + 2 : row, column) : (index, row, column >= at ? column + 2 : column);
                    await Assert.That(moved.GetValueOrDefault(target).Value).IsEqualTo(value.Value);
                }

                var back = rows
                    ? WorkbookEditor.DeleteRows(inserted, sheet.Index, at, 2, now)
                    : WorkbookEditor.DeleteColumns(inserted, sheet.Index, at, 2, now);
                await Assert.That(Errors(back)).IsLessThanOrEqualTo(errors);
                var after = Cells(back);
                foreach (var (place, value) in expected)
                {
                    await Assert.That(after.GetValueOrDefault(place)).IsEqualTo(value);
                }
            }
        }
    }

    // Every cell that holds something: its formula and its value, as text.
    static Dictionary<(int Sheet, int Row, int Column), (string? Formula, string Value)> Cells(byte[] xlsx)
    {
        var cells = new Dictionary<(int Sheet, int Row, int Column), (string? Formula, string Value)>();
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
                cells[(sheet.Index, cell.Row, cell.Column)] = (cell.Formula, $"{value.Kind}:{value.Number.ToString("R", CultureInfo.InvariantCulture)}:{value.Text}:{value.Error}");
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
