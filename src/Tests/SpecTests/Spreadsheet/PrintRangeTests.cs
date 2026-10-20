/// <summary>
/// What of a sheet prints, which is what a fit to page shrinks to the paper. Three rules, each read off
/// Excel's references:
/// * a print area of whole columns (<c>$A:$K</c>) or whole rows is a print area, not no print area;
/// * a cell that prints nothing — no value, no formula, no fill, no border — does not extend the range;
/// * a drawing extends it over the cells it stands on.
/// </summary>
public class PrintRangeTests
{
    static TableElement Grid(string scenario)
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", scenario, "input.xlsx"));
        return ExcelConverter.Parse(stream, null, null, null, useLetterPageSize: false, onePagePerSheet: false)
            .Elements.OfType<TableElement>().First();
    }

    [Test]
    [Arguments("'Wedding budget'!$A:$K", 1, 1, CellReference.MaxRow, 11)]
    [Arguments("$B:$D", 1, 2, CellReference.MaxRow, 4)]
    [Arguments("'Sheet 1'!$3:$40", 3, 1, 40, CellReference.MaxColumn)]
    [Arguments("$B$2:$F$19", 2, 2, 19, 6)]
    public async Task A_print_area_may_name_whole_columns_or_rows(string reference, int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        var area = CellReference.ParseArea(reference);

        await Assert.That(area).IsEqualTo(new SheetRange(firstRow, firstColumn, lastRow, lastColumn));
    }

    // Merges and dimensions are cell ranges only; whole columns are a print area's alone.
    [Test]
    public async Task A_range_of_whole_columns_is_still_no_cell_range() =>
        await Assert.That(CellReference.ParseRange("$A:$K")).IsNull();

    // The columns past K carry formatting but lie outside the print area.
    [Test]
    public async Task Wedding_budget_prints_its_print_area_of_columns_A_to_K() =>
        await Assert.That(Grid("wedding-budget-template").Properties.GridColumnWidths!.Count).IsEqualTo(11);

    // back-to-school-planner's second sheet charts its table in a column chart that reaches eleven
    // rows past the table's last; the grid runs to the chart's foot, or the chart is cut off there.
    [Test]
    public async Task A_drawing_extends_the_printed_rows_over_the_cells_it_stands_on()
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "back-to-school-planner", "input.xlsx"));
        var document = ExcelConverter.Parse(stream, null, null, null, useLetterPageSize: false, onePagePerSheet: false);
        var breakdown = document.Elements.OfType<TableElement>().Skip(1).First();

        await Assert.That(breakdown.Rows.Count).IsGreaterThan(10);
    }

    [Test]
    public async Task To_do_list_prints_its_print_area_of_columns_A_to_M() =>
        await Assert.That(Grid("to-do-list").Properties.GridColumnWidths!.Count).IsEqualTo(13);
}
