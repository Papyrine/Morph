using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Covers <see cref="ExportOptions.UseLetterPageSize"/> against a worksheet that states no
/// <c>pageSetup/@paperSize</c> — 72 of the 77 corpus sheets, so the common case rather than the
/// rare one.
///
/// Such a sheet falls back to <c>DefaultPageSize</c>, which reads the machine's region: Letter in
/// North America, A4 everywhere else, matching Excel. That is right for a person printing, and
/// wrong for anything that has to reproduce — a snapshot baselined at A4 on one machine comes back
/// 1275x1650 rather than 1240x1753 on a US-region CI agent, and the mismatch reads as a rendering
/// regression rather than as a page-size difference.
///
/// A docx carries w:pgSz and a deck carries p:sldSz, so this is the one format where the fallback
/// is routinely reached.
/// </summary>
public class DefaultPaperSizeTests
{
    const double a4WidthPoints = 595.28;
    const double letterWidthPoints = 612;

    [Test]
    public async Task NoPaperSize_PinnedToA4_IgnoresRegion()
    {
        using var stream = SheetWithoutPageSetup();

        var document = ExcelConverter.Parse(stream, new ImageExportOptions
        {
            UseLetterPageSize = false
        });

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(a4WidthPoints).Within(0.01);
    }

    [Test]
    public async Task NoPaperSize_PinnedToLetter_IgnoresRegion()
    {
        using var stream = SheetWithoutPageSetup();

        var document = ExcelConverter.Parse(stream, new ImageExportOptions
        {
            UseLetterPageSize = true
        });

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(letterWidthPoints).Within(0.01);
    }

    // The sheet's own paperSize still wins — the option is a fallback, not an override. 9 is A4.
    [Test]
    public async Task StatedPaperSize_WinsOverThePin()
    {
        using var stream = SheetWithoutPageSetup(paperSize: 9);

        var document = ExcelConverter.Parse(stream, new ImageExportOptions
        {
            UseLetterPageSize = true
        });

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(a4WidthPoints).Within(0.01);
    }

    // Unset keeps the pre-existing region behaviour, so no caller that never asked is moved. The
    // harness pins DefaultPageSize.UseLetterSize = false, which is what "the region" resolves to
    // under test.
    [Test]
    public async Task Unset_KeepsTheRegionDefault()
    {
        using var stream = SheetWithoutPageSetup();

        var document = ExcelConverter.Parse(stream, new ImageExportOptions());

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(a4WidthPoints).Within(0.01);
    }

    static MemoryStream SheetWithoutPageSetup(uint? paperSize = null)
    {
        var worksheet = new S.Worksheet(
            new S.SheetData(
                new S.Row(
                    new S.Cell
                    {
                        CellReference = "A1",
                        DataType = S.CellValues.String,
                        CellValue = new("x")
                    })));

        if (paperSize is { } code)
        {
            worksheet.AppendChild(
                new S.PageSetup
                {
                    PaperSize = code
                });
        }

        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = worksheet;
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
}
