using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Unwrapped text runs on over the empty cells to its right, and Excel prints as far as it reaches,
/// stopped by the next cell holding a value (<c>_probe_overflow</c>). How far it reaches is the text's
/// measured width in its own font, not its count of characters.
/// </summary>
public class TextOverflowRangeTests
{
    // A1 alone, in the default Calibri 11 at the default 8.43-character columns (64px).
    static int PrintedColumns(Action<S.SheetData> fill)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new(new S.Sheets(new S.Sheet { Id = "rId1", SheetId = 1, Name = "Sheet1" }));
            var sheetPart = workbookPart.AddNewPart<WorksheetPart>("rId1");
            var data = new S.SheetData();
            fill(data);
            sheetPart.Worksheet = new(data);
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = new(
                new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" })) { Count = 1 },
                new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None })) { Count = 1 },
                new S.Borders(new S.Border()) { Count = 1 },
                new S.CellFormats(new S.CellFormat()) { Count = 1 });
        }

        stream.Position = 0;
        return ExcelConverter.Parse(stream, null, ProjectFonts.Directory, useLetterPageSize: false, onePagePerSheet: false)
            .Elements.OfType<TableElement>().First()
            .Properties.GridColumnWidths!.Count;
    }

    static S.Cell Text(string reference, string text) =>
        new(new S.InlineString(new S.Text(text))) { CellReference = reference, DataType = S.CellValues.InlineString };

    // Fifteen "i"s are fifteen characters, nearly two columns by count, but 40pt at Calibri's 0.23 em
    // in a 48pt column; thirty "W"s are four columns and more.
    [Test]
    public async Task Text_reaches_as_far_as_its_measured_width()
    {
        var narrow = PrintedColumns(_ => _.Append(new S.Row(Text("A1", new('i', 15))) { RowIndex = 1 }));
        var wide = PrintedColumns(_ => _.Append(new S.Row(Text("A1", new('W', 30))) { RowIndex = 1 }));

        await Assert.That(narrow).IsEqualTo(1);
        await Assert.That(wide).IsGreaterThanOrEqualTo(5);
    }

    // The next value in the row stops it: B1's "x" leaves A's text clipped at A's edge, and B prints
    // for its own sake.
    [Test]
    public async Task Text_stops_at_the_next_cell_with_a_value()
    {
        var columns = PrintedColumns(_ => _.Append(new S.Row(Text("A1", new('W', 30)), Text("B1", "x")) { RowIndex = 1 }));

        await Assert.That(columns).IsEqualTo(2);
    }
}
