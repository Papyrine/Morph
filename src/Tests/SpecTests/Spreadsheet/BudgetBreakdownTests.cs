/// <summary>
/// back-to-school-planner's Budget breakdown sheet, against Excel's page: its title runs on over
/// the empty columns beside it, and its chart ends at column E's right edge although its anchor's
/// end offset is wider than E.
/// </summary>
public class BudgetBreakdownTests
{
    static List<DocumentElement> Sheet()
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "back-to-school-planner", "input.xlsx"));
        var elements = ExcelConverter.Parse(stream, null, null, useLetterPageSize: false, onePagePerSheet: false).Elements.ToList();
        var start = elements.FindIndex(_ => _ is SectionBreakElement) + 1;
        var end = elements.FindIndex(start, _ => _ is SectionBreakElement);
        return elements.GetRange(start, end - start);
    }

    // The workbook's Normal style is filled white and the title's own style is not, so every empty
    // cell beside it is a change of fill: the title's box stops at B, and the text runs on past it.
    [Test]
    public async Task The_title_runs_on_over_the_empty_columns_beside_it()
    {
        var table = Sheet().OfType<TableElement>().Single();
        var widths = table.Properties.GridColumnWidths!;
        var title = table.Rows[0].Cells[1];

        await Assert.That(title.Properties.GridSpan).IsEqualTo(1);
        await Assert.That(title.Properties.ClipSpillRightPoints).IsEqualTo(widths.Skip(2).Sum()).Within(0.01);
    }

    // Every sheet asks for a different first page with no first footer, and Excel honours that on
    // the print job's first page only: page 2, the Budget breakdown's first, carries "Page 2 of 4".
    [Test]
    public async Task Only_the_first_sheet_keeps_a_different_first_page()
    {
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "back-to-school-planner", "input.xlsx"));
        var document = ExcelConverter.Parse(stream, null, null, useLetterPageSize: false, onePagePerSheet: false);
        var later = document.Elements.OfType<SectionBreakElement>().Select(_ => _.NewSectionSettings!.DifferentFirstPage).ToList();

        await Assert.That(document.PageSettings.DifferentFirstPage).IsTrue();
        await Assert.That(later).IsNotEmpty();
        await Assert.That(later).DoesNotContain(true);
    }

    // The chart's end marker stands 5048250 EMU into column E, which is narrower than that; Excel
    // reads the offset as E's right edge, the right edge of the grid. The grid is centred between
    // the margins, and the chart is placed against the left margin.
    [Test]
    public async Task The_chart_ends_at_its_end_cells_right_edge()
    {
        var sheet = Sheet();
        var gridWidth = sheet.OfType<TableElement>().Single().Properties.GridColumnWidths!.Sum();
        var chart = sheet.OfType<FloatingChartElement>().Single();
        const double contentWidth = 595.28 - 2 * 0.25 * 72;

        await Assert.That(chart.HorizontalPositionPoints + chart.WidthPoints)
            .IsEqualTo((contentWidth - gridWidth) / 2 + gridWidth).Within(0.05);
    }
}
