/// <summary>
/// An Excel drawing's charts are read (<see cref="ChartReader"/>) and drawn at layout
/// (<see cref="ChartComposer"/>). personal-budget's first sheet carries the two kinds the corpus has: a
/// doughnut with one big data label moved into its hole, and a clustered column chart with a bottom
/// legend and a currency value axis that Excel scales to $4,000 in steps of $500.
/// </summary>
public class ChartTests
{
    static readonly string workbook = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "excel", "personal-budget", "input.xlsx");

    static List<FloatingChartElement> Charts()
    {
        using var stream = File.OpenRead(workbook);
        return ExcelConverter.Parse(stream, null, null, null, useLetterPageSize: false, onePagePerSheet: false)
            .Elements.OfType<FloatingChartElement>().ToList();
    }

    [Test]
    public async Task The_doughnut_reads_its_ring_its_colours_and_its_one_label()
    {
        var doughnut = Charts().Single(_ => _.Chart.Kind == ChartKind.Doughnut).Chart;
        var series = doughnut.Series.Single();

        await Assert.That(doughnut.HolePercent).IsEqualTo(55);
        await Assert.That(series.Values.Count).IsEqualTo(2);
        await Assert.That(series.PointFills.Count).IsEqualTo(2);
        await Assert.That(series.FormatCode).IsEqualTo("0%");
        await Assert.That(series.Labels!.Deleted).Contains(0);
        await Assert.That(series.Labels.Offsets.ContainsKey(1)).IsTrue();
        await Assert.That(series.Labels.Text.SizePoints).IsEqualTo(53);
    }

    // The label was resized to nearly the whole chart (c15:layout, 99.5% by 99.99%), so the chart's edges
    // hold its box wherever its slice and offset would put it, and its text is centred in the chart: in
    // the hole, where Excel draws it, rather than over the ring.
    [Test]
    public async Task A_resized_label_is_centred_in_its_box_and_its_box_kept_in_the_chart()
    {
        var doughnut = Charts().Single(_ => _.Chart.Kind == ChartKind.Doughnut).Chart;
        var size = doughnut.Series.Single().Labels!.Sizes[1];
        await Assert.That(size.Width).IsEqualTo(0.99457156090782772).Within(1e-12);
        await Assert.That(size.Height).IsEqualTo(0.99986227240785974).Within(1e-12);

        var boxes = new List<(float X, float Y, float Width, float Height)>();
        new ChartComposer(LayoutTestFonts.Measurer, (_, x, y, width, height, _) =>
            {
                boxes.Add((x, y, width, height));
                return [];
            })
            .Compose(doughnut, 0, 0, 200, 300);

        var label = boxes.Single();
        await Assert.That(label.X + label.Width / 2).IsEqualTo(100f).Within(0.6f);
        await Assert.That(label.Y + label.Height / 2).IsEqualTo(150f).Within(0.1f);
    }

    [Test]
    public async Task The_column_chart_reads_its_series_its_legend_and_its_axes()
    {
        var columns = Charts().Single(_ => _.Chart.Kind == ChartKind.Bar).Chart;

        await Assert.That(columns.Horizontal).IsFalse();
        await Assert.That(columns.Grouping).IsEqualTo(ChartGrouping.Clustered);
        await Assert.That(columns.Series.Select(_ => _.Name ?? "")).IsEquivalentTo(["Income", "Expenses"]);
        await Assert.That(columns.GapWidthPercent).IsEqualTo(100);
        await Assert.That(columns.OverlapPercent).IsEqualTo(-25);
        await Assert.That(columns.Legend!.Position).IsEqualTo(ChartLegendPosition.Bottom);
        await Assert.That(columns.ValueAxis!.MajorGridlines).IsNotNull();
        await Assert.That(columns.ValueAxis.FormatCode).IsEqualTo("\"$\"#,##0");
        await Assert.That(columns.CategoryAxis!.ShowLabels).IsFalse();
    }

    [Test]
    [Arguments(0.0, 3750.0, 4000.0, 500.0)]
    [Arguments(0.0, 5.0, 6.0, 1.0)]
    [Arguments(0.0, 5300.0, 6000.0, 1000.0)]
    [Arguments(0.0, 280.0, 300.0, 50.0)]
    public async Task The_value_axis_pads_the_maximum_and_rounds_it_up_to_a_major_unit(double minimum, double maximum, double axisMaximum, double unit)
    {
        var scale = ChartComposer.Scale(minimum, maximum, null);

        await Assert.That(scale.Minimum).IsEqualTo(0);
        await Assert.That(scale.Maximum).IsEqualTo(axisMaximum).Within(1e-9);
        await Assert.That(scale.Unit).IsEqualTo(unit).Within(1e-9);
    }

    [Test]
    public async Task A_fixed_axis_keeps_its_own_maximum_and_unit()
    {
        var axis = new ChartAxis { Maximum = 1, MajorUnit = 0.25, Text = new("Calibri", 9, "595959", false, false) };
        var scale = ChartComposer.Scale(0, 0.1667, axis);

        await Assert.That(scale.Maximum).IsEqualTo(1);
        await Assert.That(scale.Unit).IsEqualTo(0.25);
    }

    [Test]
    public async Task Clustered_columns_share_their_band_with_the_gap_and_overlap()
    {
        // One category, two series, a 100% gap and -25% overlap: the band holds 3.25 bar widths.
        var chart = new ChartModel
        {
            Kind = ChartKind.Bar,
            GapWidthPercent = 100,
            OverlapPercent = -25,
            Categories = ["only"],
            Series = [new() { Values = [3750], FillHex = "000080" }, new() { Values = [2336], FillHex = "FF8000" }],
            CategoryAxis = new() { ShowLabels = false, Text = new("Calibri", 9, "595959", false, false) },
            ValueAxis = new() { Deleted = true, Text = new("Calibri", 9, "595959", false, false) },
            PlotLayout = new(0, 0, 1, 1),
            PlotLayoutIsInner = true,
            Area = new(null, null),
            Text = new("Calibri", 9, "595959", false, false),
            Palette = ["4472C4"]
        };

        var bars = new ChartComposer(LayoutTestFonts.Measurer, (_, _, _, _, _, _) => [])
            .Compose(chart, 0, 0, 325, 400)
            .OfType<PlacedShape>()
            .Where(_ => _.Shape.FillColorHex != null)
            .ToList();

        await Assert.That(bars.Select(_ => _.Width)).IsEquivalentTo([100f, 100f]);
        await Assert.That(bars[0].X).IsEqualTo(50f).Within(0.01f);
        await Assert.That(bars[1].X).IsEqualTo(175f).Within(0.01f);

        // 3,750 on an axis to 4,000 stands 375pt of the plot's 400.
        await Assert.That(bars[0].Height).IsEqualTo(375f).Within(0.01f);
    }
}
