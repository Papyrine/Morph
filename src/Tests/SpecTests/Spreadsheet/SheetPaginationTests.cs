using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using XDR = DocumentFormat.OpenXml.Drawing.Spreadsheet;

/// <summary>
/// Covers <see cref="SheetPagination.OnePagePerSheet"/>: a workbook drawn a sheet to an image
/// rather than as it prints.
///
/// Every sheet here gives its rows a height of their own, so how tall an image is follows from the
/// number of rows and nothing a font decides. Width is only ever compared, since a column's width is
/// a multiple of a glyph of the workbook's body font.
/// </summary>
public class SheetPaginationTests
{
    const double rowHeight = 20;

    // What SpreadsheetParser leaves around a sheet drawn whole, in points
    const int margin = 2;

    [Test]
    public async Task EachVisibleSheetIsOneImage()
    {
        using var stream = Workbook(
            new("Short", Rows: 5),
            new("Long", Rows: 300),
            new("Hidden", Rows: 5, Hidden: true),
            new("Empty", Rows: 0));

        var pages = Render(stream);

        // Short, Long and Empty. Long is thirty times the others and still the one image
        await Assert.That(pages.Count).IsEqualTo(3);
    }

    /// <summary>
    /// The same workbook as it prints, which is the default and is what the test above differs
    /// from: the long sheet runs over many pages, and the empty one is not printed.
    /// </summary>
    [Test]
    public async Task PrintLayoutStillPaginates()
    {
        using var stream = Workbook(
            new("Short", Rows: 5),
            new("Long", Rows: 300),
            new("Hidden", Rows: 5, Hidden: true),
            new("Empty", Rows: 0));

        var pages = new SkiaExcelConverter().ConvertToImageData(
            stream,
            new()
            {
                Dpi = 72,
                UseLetterPageSize = false
            });

        await Assert.That(pages.Count).IsGreaterThan(3);
    }

    [Test]
    public async Task AnImageIsAsTallAsItsRows()
    {
        using var stream = Workbook(
            new("Short", Rows: 5),
            new("Long", Rows: 300));

        var pages = Render(stream);

        // At 72 dpi a point is a pixel
        await Assert.That(PngHeight(pages[0])).IsEqualTo(5 * (int) rowHeight + margin * 2);
        await Assert.That(PngHeight(pages[1])).IsEqualTo(300 * (int) rowHeight + margin * 2);
    }

    [Test]
    public async Task AnImageIsAsWideAsItsColumns()
    {
        using var stream = Workbook(
            new("Narrow", Rows: 2, Columns: 2),
            new("Wide", Rows: 2, Columns: 40));

        var pages = Render(stream);

        var narrow = PngWidth(pages[0]) - margin * 2;
        var wide = PngWidth(pages[1]) - margin * 2;

        // Twenty times the columns, and nothing shrinking them to a page: the long edge of A4 is
        // 842 points, which forty default columns are more than twice
        await Assert.That(wide).IsGreaterThan(842);
        await Assert.That(wide).IsEqualTo(narrow * 20).Within(20);
    }

    /// <summary>
    /// The paper, the orientation, the scale and fit-to-page are how a sheet prints. Drawn whole it
    /// is the size it is.
    /// </summary>
    [Test]
    public async Task PageSetupDoesNotChangeTheImage()
    {
        using var plain = Workbook(new SheetSpec("Sheet", Rows: 60, Columns: 30));
        using var setUp = Workbook(
            new SheetSpec(
                "Sheet",
                Rows: 60,
                Columns: 30,
                FitToPage: true,
                Setup: new()
                {
                    PaperSize = 9,
                    Orientation = S.OrientationValues.Portrait,
                    Scale = 50,
                    FitToWidth = 1,
                    FitToHeight = 1
                }));

        var expected = Render(plain)[0];
        var actual = Render(setUp)[0];

        await Assert.That(PngWidth(actual)).IsEqualTo(PngWidth(expected));
        await Assert.That(PngHeight(actual)).IsEqualTo(PngHeight(expected));
        await Assert.That(PngHeight(actual)).IsEqualTo(60 * (int) rowHeight + margin * 2);
    }

    [Test]
    public async Task ThePrintAreaDoesNotCropTheImage()
    {
        using var stream = Workbook(new SheetSpec("Sheet", Rows: 10, PrintArea: "Sheet!$A$1:$A$2"));

        var pages = Render(stream);

        await Assert.That(PngHeight(pages[0])).IsEqualTo(10 * (int) rowHeight + margin * 2);
    }

    /// <summary>
    /// What makes the Nth image the Nth visible sheet: a sheet with nothing on it is not skipped,
    /// with a row element or without one.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AnEmptySheetIsStillAnImage(bool withSheetData)
    {
        using var stream = Workbook(
            new("First", Rows: 3),
            new("Empty", Rows: 0, OmitSheetData: !withSheetData),
            new("Last", Rows: 7));

        var pages = Render(stream);

        await Assert.That(pages.Count).IsEqualTo(3);
        await Assert.That(PngHeight(pages[0])).IsEqualTo(3 * (int) rowHeight + margin * 2);
        // The one cell, at the default height of a row
        await Assert.That(PngHeight(pages[1])).IsEqualTo(15 + margin * 2);
        await Assert.That(PngHeight(pages[2])).IsEqualTo(7 * (int) rowHeight + margin * 2);
    }

    /// <summary>
    /// A chart sheet is a tab like any other, and has no grid to draw. Left out, as it is when the
    /// workbook prints, it would put the image of every sheet after it against the sheet before.
    /// </summary>
    [Test]
    public async Task ATabThatIsNotAGridIsStillAnImage()
    {
        using var stream = Workbook(
            new("First", Rows: 3),
            new("Chart", Rows: 0, ChartSheet: true),
            new("Last", Rows: 7));

        var pages = Render(stream);

        await Assert.That(pages.Count).IsEqualTo(3);
        await Assert.That(PngHeight(pages[1])).IsEqualTo(15 + margin * 2);
        await Assert.That(PngHeight(pages[2])).IsEqualTo(7 * (int) rowHeight + margin * 2);
    }

    /// <summary>
    /// A page is a sheet, so the page range chooses sheets.
    /// </summary>
    [Test]
    public async Task PagesChoosesAmongTheSheets()
    {
        using var stream = Workbook(
            new("First", Rows: 3),
            new("Second", Rows: 9),
            new("Third", Rows: 5));

        var pages = new SkiaExcelConverter().ConvertToImageData(
            stream,
            Options() with
            {
                Pages = PageRange.Single(2)
            });

        await Assert.That(pages.Count).IsEqualTo(1);
        await Assert.That(PngHeight(pages[0])).IsEqualTo(9 * (int) rowHeight + margin * 2);
    }

    [Test]
    public async Task BothBackendsAgreeOnTheSizes()
    {
        using var skiaStream = Workbook(new("First", Rows: 4, Columns: 3), new("Second", Rows: 11, Columns: 6));
        using var sharpStream = Workbook(new("First", Rows: 4, Columns: 3), new("Second", Rows: 11, Columns: 6));

        var skia = new SkiaExcelConverter().ConvertToImageData(skiaStream, Options());
        var sharp = new ImageSharpExcelConverter().ConvertToImageData(sharpStream, Options());

        await Assert.That(sharp.Count).IsEqualTo(skia.Count);
        for (var index = 0; index < skia.Count; index++)
        {
            await Assert.That(PngWidth(sharp[index])).IsEqualTo(PngWidth(skia[index]));
            await Assert.That(PngHeight(sharp[index])).IsEqualTo(PngHeight(skia[index]));
        }
    }

    /// <summary>
    /// A sheet has no upper size, and an image has. Said, rather than left to an allocation that
    /// fails without saying which sheet or why.
    /// </summary>
    [Test]
    public async Task ASheetTooLargeForAnImageSaysSo()
    {
        using var stream = Workbook(new SheetSpec("Long", Rows: 2000));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new SkiaExcelConverter().ConvertToImageData(
                stream,
                Options() with
                {
                    Dpi = 2400
                }));

        await Assert.That(exception.Message).Contains("Page 1");
        await Assert.That(exception.Message).Contains("more than one image can hold");
    }

    /// <summary>
    /// The page is decided when the workbook is parsed: as wide as the grid, and marked to be cut
    /// down to what is laid out on it, with nothing of a printed page about it.
    /// </summary>
    [Test]
    public async Task TheParsedPageIsTheGridAndNoMore()
    {
        using var stream = Workbook(new SheetSpec("Sheet", Rows: 4, Columns: 3));

        var document = ExcelConverter.Parse(stream, Options());
        var settings = document.PageSettings;
        var table = document.Elements.OfType<TableElement>().Single();

        await Assert.That(settings.FitHeightToContent).IsTrue();
        await Assert.That(settings.ContentWidth).IsEqualTo(table.Properties.GridColumnWidths!.Sum()).Within(0.02);
        await Assert.That(settings.MarginTop).IsEqualTo(margin);
        await Assert.That(settings.MarginBottom).IsEqualTo(margin);
        await Assert.That(settings.HeaderDistance).IsEqualTo(0);
        await Assert.That(settings.FooterDistance).IsEqualTo(0);
    }

    /// <summary>
    /// And is not when it prints, where a page is the paper's.
    /// </summary>
    [Test]
    public async Task ThePrintedPageIsNotFitted()
    {
        using var stream = Workbook(new SheetSpec("Sheet", Rows: 4, Columns: 3));

        var settings = ExcelConverter.Parse(stream, new ImageExportOptions()).PageSettings;

        await Assert.That(settings.FitHeightToContent).IsFalse();
    }

    /// <summary>
    /// A chart beside the table it charts is over cells nothing has been typed in, so the used
    /// range stops short of it. Drawn whole, the sheet reaches to where its drawings end.
    /// </summary>
    [Test]
    public async Task ADrawingPastTheCellsWidensTheSheet()
    {
        // Cells in A1:A2, and a drawing from D1 to part way into G10: markers count from zero
        using var stream = Workbook(new SheetSpec("Sheet", Rows: 2, DrawingTo: (Column: 6, Row: 9, Offset: 63500)));

        var table = ExcelConverter.Parse(stream, Options())
            .Elements.OfType<TableElement>()
            .Single();

        await Assert.That(table.Properties.GridColumnWidths!.Count).IsEqualTo(7);
        await Assert.That(table.Rows.Count).IsEqualTo(10);
    }

    /// <summary>
    /// A drawing that ends on a cell boundary, or a hair past one, stops at that boundary: the cell
    /// past it is not part of the sheet. A quarter of a point over is what the corner art of the
    /// corpus's check-register has, and a whole empty column was drawn for it.
    /// </summary>
    [Test]
    [Arguments(0)]
    [Arguments(3319)]
    public async Task ADrawingEndingOnACellBoundaryStopsThere(long offset)
    {
        // The same marker with no offset is the near edge of G10, so F9 is the last cell it touches
        using var stream = Workbook(new SheetSpec("Sheet", Rows: 2, DrawingTo: (Column: 6, Row: 9, Offset: offset)));

        var table = ExcelConverter.Parse(stream, Options())
            .Elements.OfType<TableElement>()
            .Single();

        await Assert.That(table.Properties.GridColumnWidths!.Count).IsEqualTo(6);
        await Assert.That(table.Rows.Count).IsEqualTo(9);
    }

    static IReadOnlyList<byte[]> Render(Stream stream) =>
        new SkiaExcelConverter().ConvertToImageData(stream, Options());

    // 72 dpi, so that a point is a pixel and the sizes above read as the sheet's own
    static ImageExportOptions Options() =>
        new()
        {
            Dpi = 72,
            SheetPagination = SheetPagination.OnePagePerSheet
        };

    // IHDR is the first chunk: an 8-byte signature, then length and type, then the width and the
    // height as big-endian.
    static int PngWidth(byte[] png) =>
        (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

    static int PngHeight(byte[] png) =>
        (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];

    record SheetSpec(
        string Name,
        int Rows,
        int Columns = 1,
        bool Hidden = false,
        bool FitToPage = false,
        S.PageSetup? Setup = null,
        string? PrintArea = null,
        bool OmitSheetData = false,
        bool ChartSheet = false,
        (int Column, int Row, long Offset)? DrawingTo = null);

    static MemoryStream Workbook(params SheetSpec[] specs)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var sheets = new S.Sheets();
            var names = new S.DefinedNames();
            for (var index = 0; index < specs.Length; index++)
            {
                var spec = specs[index];
                OpenXmlPart part;
                if (spec.ChartSheet)
                {
                    var chartsheetPart = workbookPart.AddNewPart<ChartsheetPart>();
                    chartsheetPart.Chartsheet = new();
                    part = chartsheetPart;
                }
                else
                {
                    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                    worksheetPart.Worksheet = Worksheet(spec);

                    if (spec.DrawingTo is { } to)
                    {
                        worksheetPart.AddNewPart<DrawingsPart>().WorksheetDrawing = Drawing(to.Column, to.Row, to.Offset);
                    }

                    part = worksheetPart;
                }

                var sheet = new S.Sheet
                {
                    Id = workbookPart.GetIdOfPart(part),
                    SheetId = (uint) index + 1,
                    Name = spec.Name
                };
                if (spec.Hidden)
                {
                    sheet.State = S.SheetStateValues.Hidden;
                }

                sheets.AppendChild(sheet);

                if (spec.PrintArea != null)
                {
                    names.AppendChild(
                        new S.DefinedName(spec.PrintArea)
                        {
                            Name = "_xlnm.Print_Area",
                            LocalSheetId = (uint) index
                        });
                }
            }

            var workbook = new S.Workbook(sheets);
            if (names.HasChildren)
            {
                workbook.AppendChild(names);
            }

            workbookPart.Workbook = workbook;
        }

        stream.Position = 0;
        return stream;
    }

    static S.Worksheet Worksheet(SheetSpec spec)
    {
        var worksheet = new S.Worksheet();
        if (spec.FitToPage)
        {
            worksheet.AppendChild(
                new S.SheetProperties(
                    new S.PageSetupProperties
                    {
                        FitToPage = true
                    }));
        }

        if (!spec.OmitSheetData)
        {
            var data = new S.SheetData();
            for (var row = 1; row <= spec.Rows; row++)
            {
                // A height of the row's own, so it is neither grown to its text nor left to a font
                var element = new S.Row
                {
                    RowIndex = (uint) row,
                    Height = rowHeight,
                    CustomHeight = true
                };
                for (var column = 1; column <= spec.Columns; column++)
                {
                    element.AppendChild(
                        new S.Cell
                        {
                            CellReference = $"{ColumnName(column)}{row}",
                            DataType = S.CellValues.String,
                            CellValue = new("x")
                        });
                }

                data.AppendChild(element);
            }

            worksheet.AppendChild(data);
        }

        if (spec.Setup != null)
        {
            worksheet.AppendChild(spec.Setup);
        }

        return worksheet;
    }

    // A two-cell anchor with nothing in it: where a drawing ends is all the range is widened by
    static XDR.WorksheetDrawing Drawing(int toColumn, int toRow, long toOffset) =>
        new(
            new XDR.TwoCellAnchor(
                new XDR.FromMarker(
                    new XDR.ColumnId("3"),
                    new XDR.ColumnOffset("0"),
                    new XDR.RowId("0"),
                    new XDR.RowOffset("0")),
                new XDR.ToMarker(
                    new XDR.ColumnId(toColumn.ToString(CultureInfo.InvariantCulture)),
                    new XDR.ColumnOffset(toOffset.ToString(CultureInfo.InvariantCulture)),
                    new XDR.RowId(toRow.ToString(CultureInfo.InvariantCulture)),
                    new XDR.RowOffset(toOffset.ToString(CultureInfo.InvariantCulture))),
                new XDR.ClientData()));

    static string ColumnName(int column)
    {
        var name = "";
        while (column > 0)
        {
            column--;
            name = (char) ('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }
}
