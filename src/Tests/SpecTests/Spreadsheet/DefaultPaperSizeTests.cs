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
    // These fixtures state no orientation either, and a sheet that does not is laid out LANDSCAPE
    // (see DefaultOrientationTests), so the page's WIDTH is the paper's long edge. A4 is
    // 841.89 x 595.28 that way round and Letter 792 x 612 — still two numbers apart, which is all
    // these tests need to tell the pinned papers apart.
    const double a4LongEdgePoints = 841.89;
    const double letterLongEdgePoints = 792;

    [Test]
    public async Task NoPaperSize_PinnedToA4_IgnoresRegion()
    {
        using var stream = SheetWithoutPageSetup();

        var document = ExcelConverter.Parse(stream, new ImageExportOptions
        {
            UseLetterPageSize = false
        });

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
    }

    [Test]
    public async Task NoPaperSize_PinnedToLetter_IgnoresRegion()
    {
        using var stream = SheetWithoutPageSetup();

        var document = ExcelConverter.Parse(stream, new ImageExportOptions
        {
            UseLetterPageSize = true
        });

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(letterLongEdgePoints).Within(0.01);
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

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
    }

    // Unset keeps the pre-existing region behaviour, so no caller that never asked is moved. The
    // harness pins DefaultPageSize.UseLetterSize = false, which is what "the region" resolves to
    // under test.
    [Test]
    public async Task Unset_KeepsTheRegionDefault()
    {
        using var stream = SheetWithoutPageSetup();

        var document = ExcelConverter.Parse(stream, new ImageExportOptions());

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
    }

    // The parse tests above go through ExcelConverter.Parse, which is the seam the option was
    // threaded into. Rendering is what callers actually use, and it reaches Parse by its own route —
    // so it needs its own guard, or the option can be plumbed correctly and still not reach a page.
    [Test]
    [Arguments(true, 1056)]
    [Arguments(false, 1122)]
    public async Task RenderedPageHonoursThePin(bool useLetter, int expectedWidth)
    {
        using var stream = SheetWithoutPageSetup();

        var pages = new SkiaExcelConverter().ConvertToImageData(
            stream,
            new()
            {
                Dpi = 96,
                UseLetterPageSize = useLetter
            });

        // Landscape, so the width is the long edge: Letter is 11in and A4 297mm, which truncate to
        // 1056px and 1122px at 96dpi.
        //
        // The cell carries an explicit CellReference, which is load-bearing. A sheet whose cells
        // declare no r attribute renders portrait A4 whatever this option or the region says — see
        // NoCellReference_IgnoresThePin below.
        await Assert.That(PngWidth(pages[0])).IsEqualTo(expectedWidth);
    }

    /// <summary>
    /// DEFECT, pinned as it behaves rather than as it should: a sheet whose cells declare no
    /// <c>r</c> attribute renders portrait A4 whatever the option — and whatever the region — says.
    /// The whole of <c>PageSettingsFor</c> is skipped rather than falling back to the default, so
    /// such a sheet cannot be moved onto Letter at all, and misses the landscape default too.
    ///
    /// Found from the other side, in Verify.OpenXml: fixtures built with the OOXML SDK and no
    /// explicit CellReference rendered A4 on a US-region CI agent where sibling fixtures rendered
    /// Letter, and the pin appeared to be ignored. Adding <c>r="A1"</c> to the one cell was enough
    /// to make both the option and the region take effect.
    ///
    /// Change this test when the defect is fixed; it exists so the fix cannot land silently.
    /// </summary>
    [Test]
    public async Task NoCellReference_IgnoresThePin()
    {
        using var stream = SheetWithoutPageSetup(cellReference: null);

        var pages = new SkiaExcelConverter().ConvertToImageData(
            stream,
            new()
            {
                Dpi = 96,
                UseLetterPageSize = true
            });

        // 793 is portrait A4. Landscape Letter — what the pin plus the orientation default should
        // have produced — would be 1056.
        await Assert.That(PngWidth(pages[0])).IsEqualTo(793);
    }

    // IHDR is the first chunk: an 8-byte signature, then length/type, then width as big-endian.
    static int PngWidth(byte[] png) =>
        (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

    static MemoryStream SheetWithoutPageSetup(uint? paperSize = null, string? cellReference = "A1")
    {
        var cell = new S.Cell
        {
            DataType = S.CellValues.String,
            CellValue = new("x")
        };

        if (cellReference != null)
        {
            cell.CellReference = cellReference;
        }

        var worksheet = new S.Worksheet(
            new S.SheetData(
                new S.Row(cell)));

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
