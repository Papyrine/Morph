using S = DocumentFormat.OpenXml.Spreadsheet;

static partial class WorkbookEditor
{
    /// <summary>
    /// A new workbook as Excel starts one: a single empty sheet, Sheet1, in Excel's own defaults — Calibri
    /// 11, 15-point rows, the Normal style and the two fills Excel reserves — and nothing else: no author,
    /// no theme, no document properties.
    /// </summary>
    public static byte[] Blank()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new(
                new S.BookViews(new S.WorkbookView { ActiveTab = 0 }),
                new S.Sheets());

            var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new(
                new S.Fonts(new S.Font(
                    new S.FontSize { Val = 11 },
                    new S.Color { Theme = 1 },
                    new S.FontName { Val = "Calibri" },
                    new S.FontFamilyNumbering { Val = 2 },
                    new S.FontScheme { Val = S.FontSchemeValues.Minor })) { Count = 1 },
                new S.Fills(
                    new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                    new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })) { Count = 2 },
                new S.Borders(new S.Border(new S.LeftBorder(), new S.RightBorder(), new S.TopBorder(), new S.BottomBorder(), new S.DiagonalBorder())) { Count = 1 },
                new S.CellStyleFormats(new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
                new S.CellFormats(new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 }) { Count = 1 },
                new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });

            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
            sheetPart.Worksheet = new(
                new S.SheetViews(new S.SheetView { TabSelected = true, WorkbookViewId = 0 }),
                new S.SheetFormatProperties { DefaultRowHeight = 15 },
                new S.SheetData(),
                new S.PageMargins { Left = 0.7, Right = 0.7, Top = 0.75, Bottom = 0.75, Header = 0.3, Footer = 0.3 });
            workbookPart.Workbook.Sheets!.AppendChild(new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(sheetPart),
                SheetId = 1,
                Name = "Sheet1"
            });
        }

        return stream.ToArray();
    }
}
