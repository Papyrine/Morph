using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// End-to-end regression for the int.MaxValue grid-index infinite-loop DoS. A row
/// (<c>&lt;row r="2147483647"&gt;</c>) or column (a cell such as <c>FXSHRXW1</c>, which parses
/// base-26 to int.MaxValue) index of exactly int.MaxValue used to drive SheetGridBuilder's
/// <c>for (var i = first; i &lt;= last; i++)</c> walk into a non-terminating loop — i++ overflows
/// past int.MaxValue back to int.MinValue — that exhausted memory in the ExcelDocument constructor
/// before any export. Only int.MaxValue triggers it; the neighbours 2147483646 / FXSHRXV are fine.
///
/// The fix rejects the out-of-range index at read (AssignImpliedReferences / CellReference.ColumnOf),
/// so the parse now throws InvalidOperationException quickly instead of looping. Example workbooks
/// live under <c>Inputs/excel-overflow/</c> — a sibling of <c>Inputs/excel/</c>, so the scenario
/// suite, which renders every <c>Inputs/excel/**/input.xlsx</c>, never picks them up.
/// </summary>
public class GridIndexBoundsTests
{
    [Test]
    public async Task RowIndex_AtIntMaxValue_IsRejected()
    {
        await using var workbook = File.OpenRead(Fixture("row-index-int-max.xlsx"));

        await Assert.That(Rejects(workbook)).IsTrue();
    }

    [Test]
    public async Task ColumnReference_AtIntMaxValue_IsRejected()
    {
        await using var workbook = File.OpenRead(Fixture("column-ref-int-max.xlsx"));

        await Assert.That(Rejects(workbook)).IsTrue();
    }

    /// <summary>The same defect synthesised in code, no fixture — mirrors DefaultOrientationTests.Sheet.</summary>
    [Test]
    public async Task RowIndex_AtIntMaxValue_Synthesised_IsRejected()
    {
        using var workbook = Workbook(rowIndex: int.MaxValue, cellReference: "A1");

        await Assert.That(Rejects(workbook)).IsTrue();
    }

    /// <summary>Positive control: a normal in-range workbook still parses, so the bound does not over-reject.</summary>
    [Test]
    public async Task InRangeWorkbook_IsAccepted()
    {
        using var workbook = Workbook(rowIndex: 1, cellReference: "A1");

        await Assert.That(Rejects(workbook)).IsFalse();
    }

    static string Fixture(string name) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel-overflow", name);

    // The fix rejects an out-of-range index with InvalidOperationException (Morph's malformed-input
    // convention). OutOfMemoryException is deliberately NOT caught — a regression to the unbounded
    // loop must fail the test rather than pass as "rejected".
    static bool Rejects(Stream workbook)
    {
        try
        {
            ExcelConverter.Parse(workbook, defaultFont: null, fontDirectory: null);
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    static MemoryStream Workbook(uint rowIndex, string cellReference)
    {
        var worksheet = new S.Worksheet(
            new S.SheetData(
                new S.Row(
                    new S.Cell
                    {
                        CellReference = cellReference,
                        DataType = S.CellValues.String,
                        CellValue = [with("x")]
                    })
                {
                    RowIndex = rowIndex
                }));

        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = worksheet;
            workbookPart.Workbook =
            [
                with(new S.Sheets(
                    new S.Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = 1,
                        Name = "Sheet1"
                    }))
            ];
        }

        stream.Position = 0;
        return stream;
    }
}
