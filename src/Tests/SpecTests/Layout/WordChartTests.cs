/// <summary>
/// A Word document's charts are read as the model a sheet's charts use (<see cref="ChartReader"/>) and composed
/// at layout (<see cref="ChartComposer"/>): an inline chart is a run whose line carries it, an anchored one a
/// float at its anchor's position. chart_column_line and chart_anchored were authored for this, with Word's
/// renders as their references.
/// </summary>
public class WordChartTests
{
    static ParsedDocument Parse(string scenario) =>
        new DocumentParser().Parse(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", scenario, "input.docx"));

    [Test]
    public async Task An_inline_chart_is_a_run_carrying_its_model()
    {
        var charts = Parse("chart_column_line").Elements.OfType<ParagraphElement>()
            .SelectMany(_ => _.Runs)
            .Where(_ => _.InlineChart != null)
            .ToList();

        await Assert.That(charts.Count).IsEqualTo(2);
        var column = charts[0].InlineChart!;
        await Assert.That(column.Kind).IsEqualTo(ChartKind.Bar);
        await Assert.That(column.Title!.Text).IsEqualTo("Quarterly Sales");
        await Assert.That(column.Series.Select(_ => _.Name ?? "")).IsEquivalentTo(["North", "South"]);
        await Assert.That(column.Legend!.Position).IsEqualTo(ChartLegendPosition.Bottom);
        await Assert.That(charts[0].InlineImageWidthPoints).IsEqualTo(432).Within(0.01);
        await Assert.That(charts[1].InlineChart!.Kind).IsEqualTo(ChartKind.Line);
    }

    [Test]
    public async Task An_inline_chart_is_composed_into_its_box_on_the_line()
    {
        var document = Parse("chart_column_line");
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility).Layout(document.Elements, document.PageSettings);
        var boxes = laidOut.Pages[0].Items.OfType<PlacedLine>().SelectMany(_ => _.Images).Where(_ => _.Composed != null).ToList();

        await Assert.That(boxes.Count).IsEqualTo(2);
        foreach (var box in boxes)
        {
            // Everything drawn stands inside the chart's box, and its text includes the title.
            await Assert.That(box.Composed!
                .All(_ => _.X >= box.X - 0.5f &&
                          _.X + _.Width <= box.X + box.Width + 0.5f)).IsTrue();
            await Assert.That(box.Composed!.OfType<PlacedLine>()
                .Any(_ => _.Runs.Any(_ => _.Text is "Quarterly Sales" or "Web Traffic"))).IsTrue();
        }
    }

    [Test]
    public async Task An_anchored_chart_floats_at_its_anchor()
    {
        var chart = Parse("chart_anchored").Elements.OfType<FloatingChartElement>().Single();

        await Assert.That(chart.Chart.Horizontal).IsTrue();
        await Assert.That(chart.HorizontalAnchor).IsEqualTo(HorizontalAnchor.Page);
        await Assert.That(chart.HorizontalPositionPoints).IsEqualTo(108).Within(0.01);
        await Assert.That(chart.VerticalPositionPoints).IsEqualTo(216).Within(0.01);
        await Assert.That(chart.WidthPoints).IsEqualTo(396).Within(0.01);
    }
}
