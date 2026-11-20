using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// The heights Excel prints rows at (<see cref="ExcelRowHeights"/>). Every expected value is what
/// Excel printed, read off its XPS export's gridlines in 600-dpi pixels (one is 0.12pt), on a 120-dpi
/// screen to Microsoft Print to PDF (2026-11-20). The heights declared are the ones asked of Excel,
/// before it held them to its quarter pixel.
/// </summary>
public class RowHeightTests
{
    static readonly string fontDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts");

    static readonly Func<string, bool, bool, FontMetrics?> resolve = LayoutFonts.ToDelegate(LayoutFonts.CreateResolver(fontDirectory, null));

    static double Pixels(double points) => Math.Round(points * 600 / 72, 6);

    static ExcelRowHeights Body(string family, double size) => new(family, size, resolve);

    // Arial 10's standard heights are 22px on the screen and 103px on the printer, so its rows print
    // at 103/600 over 22/120 = 0.936 of what they declare.
    [Test]
    [Arguments(30, 234)]
    [Arguments(60, 468)]
    [Arguments(12, 93)]
    [Arguments(15.75, 123)]
    [Arguments(14.3, 111)]
    [Arguments(13.9, 108)]
    [Arguments(33.33, 259)]
    [Arguments(100, 780)]
    [Arguments(7.5, 58)]
    public async Task A_declared_row_prints_through_the_body_fonts_ratio(double declared, double printed) =>
        await Assert.That(Pixels(Body("Arial", 10).Declared(declared, 100))).IsEqualTo(printed);

    // 15.75pt and 33.33pt cannot both fit one ratio on the points they declare: only Excel's quarter
    // pixel (0.15pt) fits them, 15.75 staying 15.75 and 33.33 becoming 33.3.
    [Test]
    public async Task Rows_are_held_to_a_quarter_pixel_before_they_print()
    {
        var heights = Body("Arial", 10);

        await Assert.That(Pixels(heights.Declared(15.75, 100))).IsEqualTo(123);
        await Assert.That(Pixels(heights.Declared(33.33, 100))).IsEqualTo(259);
    }

    [Test]
    public async Task A_standard_row_prints_at_the_printers_standard_height()
    {
        await Assert.That(Pixels(Body("Arial", 10).Standard(100))).IsEqualTo(103);
        await Assert.That(Pixels(Body("Calibri", 11).Standard(100))).IsEqualTo(121);
    }

    // The zoom scales the row as printed at 100%, to the nearest pixel.
    [Test]
    [Arguments("Arial", 10, 63, 30, 147)]
    [Arguments("Arial", 10, 63, 15.75, 77)]
    [Arguments("Arial", 10, 63, 100, 491)]
    [Arguments("Calibri", 11, 75, 30, 189)]
    [Arguments("Calibri", 11, 75, 7.5, 47)]
    [Arguments("Calibri", 11, 75, 100, 630)]
    public async Task The_zoom_scales_the_printed_row(string family, double size, int zoom, double declared, double printed) =>
        await Assert.That(Pixels(Body(family, size).Declared(declared, zoom))).IsEqualTo(printed);

    [Test]
    public async Task A_standard_row_is_scaled_the_same_way()
    {
        await Assert.That(Pixels(Body("Arial", 10).Standard(63))).IsEqualTo(65);
        await Assert.That(Pixels(Body("Calibri", 11).Standard(75))).IsEqualTo(91);
    }

    // A row Excel sizes itself is the screen standard height of its tallest font, printed through the
    // BODY font's ratio: Arial 20's 41px and Arial 30's 62px under an Arial 10 body.
    [Test]
    [Arguments(20, 192)]
    [Arguments(30, 290)]
    public async Task A_row_Excel_sizes_prints_from_its_tallest_fonts_screen_height(double size, double printed) =>
        await Assert.That(Pixels(Body("Arial", 10).Auto([("Arial", size)], 100))).IsEqualTo(printed);

    // Drawn rather than printed, a declared row is exactly what it declares, and one Excel sizes is
    // its standard height on the 120-dpi screen: Arial 10's 22px, 13.2pt.
    [Test]
    [Arguments(15.75)]
    [Arguments(14.3)]
    [Arguments(100)]
    public async Task A_drawn_row_is_as_declared(double declared) =>
        await Assert.That(Body("Arial", 10).Declared(declared, null)).IsEqualTo(declared);

    [Test]
    public async Task A_drawn_standard_row_is_the_screens() =>
        await Assert.That(Body("Arial", 10).Standard(null)).IsEqualTo(13.2).Within(1e-9);

    // The table holds Excel's own numbers. Verdana 11's printer height is one more than GDI's cell plus
    // 8, and Segoe UI's is seven more, which is why the table exists.
    [Test]
    [Arguments("Arial", 10, 22, 103)]
    [Arguments("Calibri", 11, 24, 121)]
    [Arguments("Verdana", 11, 23, 122)]
    [Arguments("Segoe UI", 11, 28, 138)]
    [Arguments("Consolas", 11, 24, 116)]
    public async Task The_table_holds_Excels_standard_heights(string family, double size, int screen, int printer) =>
        await Assert.That(ExcelStandardHeights.Lookup(family, size)!.Value).IsEqualTo((screen, printer));

    // GDI's cell, which a face the table lacks is measured by: VDMX where the font has a charset-1
    // group (Arial's ascent at ppem 83 is 74, not the 75 its usWinAscent scales to), the scaled Windows
    // metrics otherwise (Tahoma's charset-0 group is ignored by GDI), each read off GetTextMetrics.
    [Test]
    [Arguments("Arial", 83, 92, 3)]
    [Arguments("Arial", 17, 19, 1)]
    [Arguments("Calibri", 92, 113, 0)]
    [Arguments("Tahoma", 12, 14, 0)]
    [Arguments("Century Gothic", 83, 99, 3)]
    public async Task GDIs_cell_is_read_off_the_font(string family, int ppem, int height, int leading) =>
        await Assert.That(resolve(family, false, false)!.GdiCell(ppem)).IsEqualTo((height, leading));

    static IReadOnlyList<TableRow> Rows(S.SheetFormatProperties format, params S.Row[] rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new(new S.Sheets(new S.Sheet { Name = "Sheet1", SheetId = 1, Id = "rId1" }));
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new(
                new S.Fonts(new S.Font(new S.FontSize { Val = 10 }, new S.FontName { Val = "Arial" })),
                new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None })),
                new S.Borders(new S.Border()),
                new S.CellFormats(new S.CellFormat()));
            var sheet = workbook.AddNewPart<WorksheetPart>("rId1");
            sheet.Worksheet = new(format, new S.SheetData(rows));
        }

        stream.Position = 0;
        return ExcelConverter.Parse(stream, null, fontDirectory, useLetterPageSize: false, onePagePerSheet: false)
            .Elements.OfType<TableElement>().Single().Rows;
    }

    static S.Row Row(uint index, double? height = null, bool custom = false)
    {
        var row = new S.Row(new S.Cell { CellReference = $"A{index}", DataType = S.CellValues.String, CellValue = new("x") }) { RowIndex = index };
        if (height is { } declared)
        {
            row.Height = declared;
        }

        if (custom)
        {
            row.CustomHeight = true;
        }

        return row;
    }

    // Excel sizes a row without customHeight again when it opens the file, whatever ht it declares:
    // ht="40" came back as Arial 10's 13.2pt and printed as a standard row.
    [Test]
    public async Task A_height_without_customHeight_is_sized_again()
    {
        var rows = Rows(new() { DefaultRowHeight = 13.2 }, Row(1, 40), Row(2, 40, custom: true));

        await Assert.That(Pixels(rows[0].HeightPoints!.Value)).IsEqualTo(103);
        await Assert.That(Pixels(rows[1].HeightPoints!.Value)).IsEqualTo(312);
    }

    // A row the file leaves out takes defaultRowHeight only when the sheet says customHeight; without
    // it Excel ignored a stored 30 and printed the standard height.
    [Test]
    public async Task A_missing_row_is_the_sheets_default_only_when_that_is_custom()
    {
        var plain = Rows(new() { DefaultRowHeight = 30 }, Row(1), Row(3));
        var custom = Rows(new() { DefaultRowHeight = 30, CustomHeight = true }, Row(1), Row(3));

        await Assert.That(Pixels(plain[1].HeightPoints!.Value)).IsEqualTo(103);
        await Assert.That(Pixels(custom[1].HeightPoints!.Value)).IsEqualTo(234);
    }
}
