/// <summary>
/// Spec tests for <c>printOptions</c> (ECMA-376 §18.3.1.70) — the print-time placement of a sheet's
/// grid, which is invisible in the XML of any one cell but moves the whole page.
///
/// <c>horizontalCentered</c> is not an exotic setting: 38 of the 40 corpus workbooks declare it,
/// because Excel's own templates do. Ignoring it put every one of those grids hard against the left
/// margin — measured on <c>modern-corporate-blue-timesheet-invoice</c>, whose shaded band sits at
/// x 92→699 in Excel's reference against 50→665 in ours, the same WIDTH to within 1.3% but an origin
/// 31.5pt too far left.
/// </summary>
public class PrintOptionsTests
{
    static TableElement FirstTable(string scenario)
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", scenario, "input.xlsx");
        using var stream = File.OpenRead(inputFile);

        return ExcelConverter.Parse(stream, defaultFont: null, fontDirectory: null)
            .Elements
            .OfType<TableElement>()
            .First();
    }

    [Test]
    public async Task HorizontalCentered_CentresTheGrid()
    {
        // <printOptions horizontalCentered="1"/>, and the grid is narrower than the printable width,
        // so the alignment has slack to spend.
        var table = FirstTable("modern-corporate-blue-timesheet-invoice");

        await Assert.That(table.Properties.Alignment).IsEqualTo(TextAlignment.Center);
    }

    [Test]
    public async Task NoPrintOptions_LeavesTheGridAtTheMargin()
    {
        // One of the two corpus workbooks with no printOptions element at all.
        var table = FirstTable("probate-inventory");

        await Assert.That(table.Properties.Alignment).IsEqualTo(TextAlignment.Left);
    }

    static PageSettings Settings(string scenario)
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", scenario, "input.xlsx");
        using var stream = File.OpenRead(inputFile);

        return ExcelConverter.Parse(stream, defaultFont: null, fontDirectory: null).PageSettings;
    }

    /// <summary>
    /// The vertical half rides on the PAGE rather than the table, because nothing knows how much
    /// room is left until the page is full — <c>Fragmenter.FinishPage</c> spends it.
    /// </summary>
    [Test]
    public async Task VerticalCentered_IsCarriedOnThePageSettings()
    {
        await Assert.That(Settings("simple-green-black-timesheet-invoice").VerticallyCentered).IsTrue();

        // horizontalCentered on its own must not imply the vertical one.
        await Assert.That(Settings("modern-corporate-blue-timesheet-invoice").VerticallyCentered).IsFalse();
        await Assert.That(Settings("probate-inventory").VerticallyCentered).IsFalse();
    }

    /// <summary>
    /// A DOCX page is never centred — <c>w:vAlign</c> on a section is a separate, unmodelled
    /// feature, and the flag exists only for the spreadsheet path.
    /// </summary>
    [Test]
    public async Task VerticalCentered_DefaultsOffSoWordIsUntouched() =>
        await Assert.That(new PageSettings().VerticallyCentered).IsFalse();
}
