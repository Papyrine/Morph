using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Spec tests for a sheet table's style (<c>tableStyleInfo</c>, ECMA-376 §18.5.1.5), read by
/// <c>TableStyles</c>. simple-basic-pink-blue-timesheet's three tables name the workbook's own
/// "TableStyleLight1 2": a whole-table fill of accent 6 lightened 80% with a thin rule along the
/// table's top, and a header row that states its edges bare, which Excel prints pink with no rule.
/// </summary>
public class TableStyleTests
{
    static TableStyles Labor(out SpreadsheetDocument document)
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "simple-basic-pink-blue-timesheet", "input.xlsx");
        document = SpreadsheetDocument.Open(inputFile, false);
        var workbookPart = document.WorkbookPart!;
        var styles = new CellStyles(workbookPart, ThemeParser.ExtractThemeColors(workbookPart.ThemePart));
        return new(workbookPart.WorksheetParts.Single(), workbookPart.WorkbookStylesPart?.Stylesheet, styles);
    }

    [Test]
    public async Task DataCell_TakesTheWholeTableFill()
    {
        var tables = Labor(out var document);
        using (document)
        {
            // LaborDetails is B19:E24, its header on row 19
            var overlay = tables.For(21, 3);

            await Assert.That(overlay?.BackgroundColorHex).IsEqualTo("FED5D3");
        }
    }

    [Test]
    public async Task HeaderRow_ClearsTheTopRule_ItStatesBare()
    {
        var tables = Labor(out var document);
        using (document)
        {
            var overlay = tables.For(19, 3);

            await Assert.That(overlay?.Top?.IsVisible).IsFalse();
            await Assert.That(overlay?.Bold).IsTrue();
        }
    }

    [Test]
    public async Task OutsideEveryTable_IsNull()
    {
        var tables = Labor(out var document);
        using (document)
        {
            await Assert.That(tables.For(2, 2)).IsNull();
        }
    }
}
