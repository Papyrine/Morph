using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Rich text in a cell — <c>CT_Rst</c> carrying a sequence of formatted <c>r</c> runs instead of one
/// <c>t</c> (ECMA-376 §18.4.8). A producer bolding part of a label writes this, and it is the shape
/// an instruction banner above a header row usually takes.
///
/// The two places it appears share that content model but not the code that read it: the shared
/// string table flattened runs, and a cell's own <c>is</c> did not, taking its absent single
/// <c>t</c> and rendering blank. Both are covered here so they cannot drift apart again.
/// </summary>
public class RichCellTextTests
{
    [Test]
    public async Task InlineRichRuns_AreRead()
    {
        using var stream = Workbook(RichInline());

        var text = CellText(stream);

        await Assert.That(text).IsEqualTo("Instructions: fill in every field.");
    }

    [Test]
    public async Task SharedRichRuns_AreRead()
    {
        using var stream = SharedWorkbook();

        var text = CellText(stream);

        await Assert.That(text).IsEqualTo("Instructions: fill in every field.");
    }

    // The plain single-t form keeps working; the fallback must not shadow it.
    [Test]
    public async Task InlinePlainText_IsUnaffected()
    {
        using var stream = Workbook(
            new S.Cell
            {
                CellReference = "A1",
                DataType = S.CellValues.InlineString,
                InlineString = new(new S.Text("plain"))
            });

        var text = CellText(stream);

        await Assert.That(text).IsEqualTo("plain");
    }

    static string CellText(Stream stream)
    {
        var document = ExcelConverter.Parse(stream, new ImageExportOptions());
        var table = document.Elements.OfType<TableElement>().Single();
        return string.Concat(
            table.Rows[0].Cells[0].Content
                .OfType<ParagraphElement>()
                .SelectMany(_ => _.Runs)
                .Select(_ => _.Text));
    }

    static S.Cell RichInline() =>
        new()
        {
            CellReference = "A1",
            DataType = S.CellValues.InlineString,
            InlineString = new(
                new S.Run(
                    new S.RunProperties(new S.Bold()),
                    new S.Text("Instructions: ")),
                new S.Run(
                    new S.Text("fill in every field.")))
        };

    static MemoryStream Workbook(S.Cell cell, Action<WorkbookPart>? configure = null)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new(new S.SheetData(new S.Row(cell)));
            configure?.Invoke(workbookPart);
            workbookPart.Workbook = new(
                new S.Sheets(
                    new S.Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = 1,
                        Name = "Sheet1"
                    }));
        }

        stream.Position = 0;
        return stream;
    }

    static MemoryStream SharedWorkbook() =>
        Workbook(
            new S.Cell
            {
                CellReference = "A1",
                DataType = S.CellValues.SharedString,
                CellValue = new("0")
            },
            workbookPart =>
            {
                var part = workbookPart.AddNewPart<SharedStringTablePart>();
                part.SharedStringTable = new(
                    new S.SharedStringItem(
                        new S.Run(
                            new S.RunProperties(new S.Bold()),
                            new S.Text("Instructions: ")),
                        new S.Run(
                            new S.Text("fill in every field."))));
            });
}
