using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Covers the orientation a worksheet does NOT state (<c>pageSetup/@orientation</c>,
/// ECMA-376 §18.18.55): Morph lays it out landscape.
///
/// This is a deliberate divergence from Excel, which reads <c>default</c> — and an absent
/// pageSetup — as "ask the printer", and so portrait on essentially every driver. A grid is wide,
/// and the fit-to-page scale most sheets ask for shrinks it far harder against a portrait page than
/// a landscape one, so landscape is the better answer for a sheet that expressed no preference.
///
/// Only sheets that leave orientation open are affected: <c>portrait</c> is still honoured. Every
/// one of the 77 corpus sheets states an orientation, so no reference image moves.
/// </summary>
public class DefaultOrientationTests
{
    const double a4ShortEdgePoints = 595.28;
    const double a4LongEdgePoints = 841.89;

    [Test]
    public async Task NoPageSetup_IsLandscape()
    {
        using var stream = Sheet(setup: null);

        var settings = ExcelConverter.Parse(stream, Pinned()).PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
        await Assert.That(settings.HeightPoints).IsEqualTo(a4ShortEdgePoints).Within(0.01);
    }

    // A pageSetup carrying other settings but no orientation attribute at all.
    [Test]
    public async Task PageSetupWithoutOrientation_IsLandscape()
    {
        using var stream = Sheet(new()
        {
            PaperSize = 9
        });

        var settings = ExcelConverter.Parse(stream, Pinned()).PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
    }

    // "default" means "whatever the printer says", which is a preference Morph has no printer to
    // answer with — so it lands on the same default as saying nothing.
    [Test]
    public async Task OrientationDefault_IsLandscape()
    {
        using var stream = Sheet(new()
        {
            Orientation = S.OrientationValues.Default
        });

        var settings = ExcelConverter.Parse(stream, Pinned()).PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
    }

    // The stated orientation still wins, in both directions — the landscape default is a fallback,
    // not an override.
    [Test]
    public async Task StatedPortrait_WinsOverTheDefault()
    {
        using var stream = Sheet(new()
        {
            Orientation = S.OrientationValues.Portrait
        });

        var settings = ExcelConverter.Parse(stream, Pinned()).PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(a4ShortEdgePoints).Within(0.01);
        await Assert.That(settings.HeightPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
    }

    [Test]
    public async Task StatedLandscape_IsUnchanged()
    {
        using var stream = Sheet(new()
        {
            Orientation = S.OrientationValues.Landscape
        });

        var settings = ExcelConverter.Parse(stream, Pinned()).PageSettings;

        await Assert.That(settings.WidthPoints).IsEqualTo(a4LongEdgePoints).Within(0.01);
    }

    /// <summary>
    /// Parsing is where the orientation is decided, but rendering is what callers use and it reaches
    /// the parser by its own route — so the rotated page needs its own guard.
    /// </summary>
    [Test]
    public async Task RenderedPageIsLandscape()
    {
        using var stream = Sheet(setup: null);

        var pages = new SkiaExcelConverter().ConvertToImageData(
            stream,
            new()
            {
                Dpi = 96,
                UseLetterPageSize = false
            });

        // A4's 297mm long edge truncates to 1122px at 96dpi; portrait's 210mm would be 793px.
        await Assert.That(PngWidth(pages[0])).IsEqualTo(1122);
    }

    // A4 rather than the region's paper, so the numbers above hold wherever this runs.
    static ImageExportOptions Pinned() =>
        new()
        {
            UseLetterPageSize = false
        };

    // IHDR is the first chunk: an 8-byte signature, then length/type, then width as big-endian.
    static int PngWidth(byte[] png) =>
        (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

    // The explicit CellReference is load-bearing — without it the range never resolves and
    // PageSettingsFor is skipped entirely. See DefaultPaperSizeTests.NoCellReference_IgnoresThePin.
    static MemoryStream Sheet(S.PageSetup? setup)
    {
        var worksheet = new S.Worksheet(
            new S.SheetData(
                new S.Row(
                    new S.Cell
                    {
                        CellReference = "A1",
                        DataType = S.CellValues.String,
                        CellValue = [with("x")]
                    })));

        if (setup != null)
        {
            worksheet.AppendChild(setup);
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
