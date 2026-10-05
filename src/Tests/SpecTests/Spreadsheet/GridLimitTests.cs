using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Excel's grid ends at row 1,048,576 and column XFD (16,384), so an index past either names no
/// real cell. A crafted reference at int.MaxValue used to flow straight into a SheetRange, where
/// every <c>&lt;= range.LastRow</c> walk overflowed its increment past the bound and never
/// terminated — an infinite-loop DoS on opening the workbook (#174). Out-of-grid references are
/// rejected at the CellReference seam instead, like any other unreadable reference.
///
/// Bounding the index is only half of it. An extent that stays INSIDE the grid still names up to
/// 16,384 x 1,048,576 cells, and a sheet's <c>dimension</c> and print area are strings no cell has
/// to substantiate — so the same 1.6KB file could still ask SheetGridBuilder for 17 billion cells.
/// ResolveRange caps a range nothing substantiates at <see cref="SpreadsheetParser.MaxDeclaredCells"/>.
/// </summary>
public class GridLimitTests
{
    [Test]
    public async Task LastGridColumn_IsRead() =>
        await Assert.That(CellReference.ColumnOf("XFD1")).IsEqualTo(16_384);

    [Test]
    public async Task ColumnPastTheGrid_IsRejected() =>
        await Assert.That(CellReference.ColumnOf("XFE1")).IsEqualTo(0);

    [Test]
    public async Task ColumnAtIntMaxValue_IsRejected() =>
        // FXSHRXW is bijective base-26 for exactly int.MaxValue — the crafted extent from #174.
        // One more letter would overflow the accumulator, which is why ColumnOf bails per letter.
        await Assert.That(CellReference.ColumnOf("FXSHRXW1")).IsEqualTo(0);

    [Test]
    public async Task LastGridRow_IsRead() =>
        await Assert.That(CellReference.RowOf("A1048576")).IsEqualTo(1_048_576);

    [Test]
    public async Task RowPastTheGrid_IsRejected() =>
        await Assert.That(CellReference.RowOf("A1048577")).IsEqualTo(0);

    [Test]
    public async Task RowAtIntMaxValue_IsRejected() =>
        await Assert.That(CellReference.RowOf("A2147483647")).IsEqualTo(0);

    [Test]
    public async Task RangeWithAnUnreadableEnd_IsNoRange() =>
        // Folding the 0 sentinel into Min/Max would fabricate a row or column 0, one off the top
        // of a grid that counts from 1.
        await Assert.That(CellReference.ParseRange("A1:A2147483647")).IsNull();

    /// <summary>The repro shape from #174: a row element stating row int.MaxValue.</summary>
    [Test]
    public async Task RowAtIntMaxValue_DoesNotHangTheParse()
    {
        using var stream = Workbook(
            new S.Row(Cell("A1", "kept")) { RowIndex = 1 },
            new S.Row(Cell("A2147483647", "dropped")) { RowIndex = int.MaxValue });

        var texts = CellTexts(stream);

        await Assert.That(texts).IsEquivalentTo(["kept"]);
    }

    /// <summary>The other #174 axis: a cell reference whose column is int.MaxValue.</summary>
    [Test]
    public async Task ColumnAtIntMaxValue_DoesNotHangTheParse()
    {
        using var stream = Workbook(new S.Row(Cell("A1", "kept"), Cell("FXSHRXW1", "beyond")) { RowIndex = 1 });

        var texts = CellTexts(stream);

        await Assert.That(texts).Contains("kept");
    }

    /// <summary>
    /// The extent can also arrive through <c>dimension</c> alone; an unreadable one falls back to
    /// the cells the sheet actually carries.
    /// </summary>
    [Test]
    public async Task DimensionAtIntMaxValue_DoesNotHangTheParse()
    {
        using var stream = Workbook("A1:FXSHRXW2147483647", new S.Row(Cell("A1", "kept")) { RowIndex = 1 });

        var texts = CellTexts(stream);

        await Assert.That(texts).IsEquivalentTo(["kept"]);
    }

    /// <summary>
    /// A print area overlapping none of the sheet's cells is not clipped by them, so it reached the
    /// grid builder at whatever extent it declared. Measured before the cap: this shape at 1,600
    /// rows allocated 25GB and took 52s, and the full grid it declares here is 655x that again.
    /// </summary>
    [Test]
    public async Task PrintAreaOutsideTheCells_IsBounded()
    {
        using var stream = Workbook(
            dimension: null,
            printArea: "Sheet1!$B$1:$XFD$1048576",
            new S.Row(Cell("A1", "kept")) { RowIndex = 1 });

        var table = Table(stream);

        await Assert.That(Cells(table)).IsLessThanOrEqualTo(SpreadsheetParser.MaxDeclaredCells);
    }

    /// <summary>
    /// The other unsubstantiated extent: a sheet with no cells at all, where <c>dimension</c> is
    /// the only thing that states one and there is nothing to intersect it with.
    /// </summary>
    [Test]
    public async Task DimensionWithoutCells_IsBounded()
    {
        using var stream = Workbook("A1:XFD1048576");

        var table = Table(stream);

        await Assert.That(Cells(table)).IsLessThanOrEqualTo(SpreadsheetParser.MaxDeclaredCells);
    }

    /// <summary>The cap narrows only what it must: a sheet's real extent is untouched by it.</summary>
    [Test]
    public async Task DimensionWithinTheCap_IsKeptWhole()
    {
        using var stream = Workbook("A1:C40");

        var table = Table(stream);

        await Assert.That(table.Rows.Count).IsEqualTo(40);
        await Assert.That(table.Rows[0].Cells.Count).IsEqualTo(3);
    }

    /// <summary>A print area the cells DO substantiate keeps clipping to them, cap or no cap.</summary>
    [Test]
    public async Task PrintAreaOverlappingTheCells_StillClips()
    {
        using var stream = Workbook(
            dimension: null,
            printArea: "Sheet1!$A$1:$B$1",
            new S.Row(Cell("A1", "kept"), Cell("B1", "kept too"), Cell("C1", "outside")) { RowIndex = 1 });

        var texts = CellTexts(stream);

        await Assert.That(texts).IsEquivalentTo(["kept", "kept too"]);
    }

    static long Cells(TableElement table) =>
        table.Rows.Sum(_ => (long) _.Cells.Count);

    static TableElement Table(Stream stream) =>
        ExcelConverter.Parse(stream, new ImageExportOptions())
            .Elements
            .OfType<TableElement>()
            .Single();

    static string[] CellTexts(Stream stream)
    {
        var document = ExcelConverter.Parse(stream, new ImageExportOptions());
        return document.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .SelectMany(_ => _.Content.OfType<ParagraphElement>())
            .SelectMany(_ => _.Runs)
            .Select(_ => _.Text)
            .Where(_ => _.Length > 0)
            .ToArray();
    }

    static S.Cell Cell(string reference, string text) =>
        new()
        {
            CellReference = reference,
            DataType = S.CellValues.InlineString,
            InlineString = [with(new S.Text(text))]
        };

    static MemoryStream Workbook(params S.Row[] rows) => Workbook(null, rows);

    static MemoryStream Workbook(string? dimension, params S.Row[] rows) =>
        Workbook(dimension, null, rows);

    static MemoryStream Workbook(string? dimension, string? printArea, params S.Row[] rows)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var worksheet = new S.Worksheet();
            if (dimension != null)
            {
                worksheet.SheetDimension = new()
                {
                    Reference = dimension
                };
            }

            worksheet.Append(new S.SheetData(rows));
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

            // The print area lives on the workbook as a built-in defined name, scoped to the sheet
            // by its index — sheet-level markup carries no such thing.
            if (printArea != null)
            {
                workbookPart.Workbook.Append(
                    new S.DefinedNames(
                        new S.DefinedName(printArea)
                        {
                            Name = "_xlnm.Print_Area",
                            LocalSheetId = 0
                        }));
            }
        }

        stream.Position = 0;
        return stream;
    }
}
