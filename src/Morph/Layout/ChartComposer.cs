/// <summary>
/// Draws a <see cref="ChartModel"/> into its box as placed shapes and laid-out label paragraphs — the
/// items every painter already draws, so a chart needs nothing of its own downstream.
///
/// What it draws, in paint order: the chart area's fill and border, the plot area's fill, the value
/// axis's gridlines, the bars, areas, lines, markers or slices, the axis lines, the tick labels, the data
/// labels, the legend and the title. A scatter chart scales both of its axes as values; a radar chart
/// lays its categories round a circle, with the value axis's gridlines drawn as a web.
///
/// The plot box is the manual layout's when the chart has one (<c>layoutTarget="inner"</c> is the box
/// the axes span; otherwise the box their labels are taken off). Without one it is the chart less a
/// padding, the title, the legend, and the widest value or category label beside the axis that carries
/// it. The value axis scales as Excel's automatic one does — 0 under all-positive data, the maximum
/// padded by 5% of the range and rounded up to a major unit of 1, 2 or 5 times a power of ten (see
/// <see cref="Scale"/>) — unless the axis fixes its own minimum, maximum or unit.
/// </summary>
sealed class ChartComposer(
    CanonicalParagraphMeasurer measurer,
    Func<IReadOnlyList<DocumentElement>, float, float, float, float, CellVerticalAlignment, IReadOnlyList<PlacedItem>> layoutText)
{
    // The space Excel leaves inside the chart area's edge, and between a label and what it labels, at the
    // chart's own scale.
    float padding = 5f;
    float labelGap = 4f;

    /// <summary>
    /// The chart in its box. <paramref name="scale"/> shrinks its text, rules and spacing with a sheet
    /// printed smaller than life (<see cref="FloatingChartElement.Scale"/>).
    /// </summary>
    public List<PlacedItem> Compose(ChartModel chart, float x, float y, float width, float height, double scale = 1)
    {
        var items = new List<PlacedItem>();
        if (width <= 0 || height <= 0)
        {
            return items;
        }

        chart = Scaled(chart, scale);
        padding = 5f * (float) scale;
        labelGap = 4f * (float) scale;

        if (chart.Area.FillHex != null || chart.Area.Line != null)
        {
            items.Add(Rectangle(x, y, width, height, chart.Area.FillHex, chart.Area.Line));
        }

        var inner = (X: x + padding, Y: y + padding, Width: width - 2 * padding, Height: height - 2 * padding);

        // The title is drawn last, over the plot, as Word layers it. A title with a manual layout stands
        // where that puts its box's top-left and takes nothing from the plot: business-plans/12's "SWOT"
        // sits in its doughnut's hole, which the plot, laid out over the whole chart, would otherwise paint
        // over. Without one it heads the chart and the plot goes below it.
        //
        // Word sets the text inside the title's box 2.9pt in and 1.4pt down, the same at 14pt and 36pt
        // (_probe_charttitle, read off the XPS against the box its manual layout names: glyphs at 182.90pt
        // for a box at 180, baselines 1.35 and 1.54pt under the render's); an automatic title takes the
        // same drop.
        var titleItems = new List<PlacedItem>();
        if (chart.Title is { } title)
        {
            var titleHeight = LineHeight(title.Text, title.Style);
            var insetX = 2.9f * (float) scale;
            var insetY = 1.4f * (float) scale;
            if (title.Layout is { } at)
            {
                var titleWidth = TextWidth(title.Text, title.Style) + 2;
                titleItems.AddRange(Label(title.Text, title.Style, x + (float) at.X * width + insetX, y + (float) at.Y * height + insetY, titleWidth, titleHeight, TextAlignment.Left));
            }
            else
            {
                titleItems.AddRange(Label(title.Text, title.Style, inner.X, inner.Y + insetY, inner.Width, titleHeight, TextAlignment.Center));
                inner = (inner.X, inner.Y + titleHeight + padding, inner.Width, inner.Height - titleHeight - padding);
            }
        }

        if (chart.Legend is { } legend)
        {
            inner = Legend(chart, legend, x, y, width, height, inner, items);
        }

        if (chart.Kind is ChartKind.Pie or ChartKind.Doughnut)
        {
            var box = chart.PlotLayout is { } manual ? Absolute(manual, x, y, width, height) : inner;
            Pie(chart, box, x, y, width, height, items);
            items.AddRange(titleItems);
            return items;
        }

        if (chart.Kind == ChartKind.Radar)
        {
            var box = chart.PlotLayout is { } manual ? Absolute(manual, x, y, width, height) : inner;
            Radar(chart, box, items);
            items.AddRange(titleItems);
            return items;
        }

        if (chart.Kind == ChartKind.Scatter)
        {
            Scatter(chart, inner, x, y, width, height, items);
            items.AddRange(titleItems);
            return items;
        }

        Plot(chart, inner, x, y, width, height, items);
        items.AddRange(titleItems);
        return items;
    }

    void Plot(ChartModel chart, (float X, float Y, float Width, float Height) inner, float chartX, float chartY, float chartWidth, float chartHeight, List<PlacedItem> items)
    {
        var categoryCount = Math.Max(chart.Categories.Count, chart.Series.Max(_ => _.Values.Count));
        if (categoryCount == 0)
        {
            return;
        }

        var percent = chart.Grouping == ChartGrouping.PercentStacked;
        var stacked = chart.Grouping != ChartGrouping.Clustered &&
                      chart.Kind is ChartKind.Bar or ChartKind.Area;
        var (dataMin, dataMax) = DataRange(chart, categoryCount, stacked, percent);
        var valueAxis = chart.ValueAxis;
        var (minimum, maximum, unit) = Scale(dataMin, dataMax, valueAxis);
        var valueFormat = AxisFormat(valueAxis, percent ? "0%" : chart.Series[0].FormatCode);
        var ticks = Ticks(minimum, maximum, unit);

        var categoryAxis = chart.CategoryAxis;
        var showValueLabels = valueAxis is { Deleted: false, ShowLabels: true };
        var showCategoryLabels = categoryAxis is not { Deleted: true } &&
                                 categoryAxis?.ShowLabels != false;
        var valueStyle = valueAxis?.Text ?? chart.Text;
        var categoryStyle = categoryAxis?.Text ?? chart.Text;
        var valueLabels = ticks.Select(_ => NumberFormat.Format(_, valueFormat).Text).ToList();
        var horizontal = chart is
        {
            Horizontal: true,
            Kind: ChartKind.Bar
        };

        // The axes' box: a manual inner layout is it; otherwise the labels come off the box.
        var plot = chart.PlotLayout is { } manual ? Absolute(manual, chartX, chartY, chartWidth, chartHeight) : inner;
        if (!chart.PlotLayoutIsInner || chart.PlotLayout == null)
        {
            var valueWidth = showValueLabels ? valueLabels.Max(_ => TextWidth(_, valueStyle)) : 0;
            var valueHeight = showValueLabels ? LineHeight("0", valueStyle) : 0;
            var categoryWidth = showCategoryLabels &&
                                chart.Categories.Count > 0 ? chart.Categories.Max(_ => TextWidth(_, categoryStyle)) : 0;
            var categoryHeight = showCategoryLabels ? LineHeight("0", categoryStyle) : 0;
            var left = horizontal ? categoryWidth : valueWidth;
            var bottom = horizontal ? valueHeight : categoryHeight;
            var leftReserve = left > 0 ? left + labelGap : 0;
            var bottomReserve = bottom > 0 ? bottom + labelGap : 0;

            // Half a label overhangs each end of the axis that carries them along its length.
            var top = horizontal ? 0 : showValueLabels ? valueHeight / 2 : 0;
            var right = horizontal &&
                        showValueLabels ? TextWidth(valueLabels[^1], valueStyle) / 2 : 0;
            plot = (plot.X + leftReserve, plot.Y + top, plot.Width - leftReserve - right, plot.Height - bottomReserve - top);
        }

        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        if (chart.Plot.FillHex != null || chart.Plot.Line != null)
        {
            items.Add(Rectangle(plot.X, plot.Y, plot.Width, plot.Height, chart.Plot.FillHex, chart.Plot.Line));
        }

        var valueReversed = valueAxis?.Reversed == true;

        // Where a value lands along the value axis, from the plot's left (horizontal) or bottom (vertical).
        float ValueOffset(double value)
        {
            var fraction = (float) ((value - minimum) / (maximum - minimum));
            if (valueReversed)
            {
                fraction = 1 - fraction;
            }

            return fraction * (horizontal ? plot.Width : plot.Height);
        }

        // The page coordinate of a value: x when the bars run across, y when they stand up.
        float ValueAt(double value) =>
            horizontal ? plot.X + ValueOffset(value) : plot.Y + plot.Height - ValueOffset(value);

        // Gridlines, at every tick across the plot.
        if (valueAxis?.MajorGridlines is { } gridlines)
        {
            foreach (var tick in ticks)
            {
                var at = ValueAt(tick);
                items.Add(horizontal
                    ? Rule(at, plot.Y, at, plot.Y + plot.Height, gridlines)
                    : Rule(plot.X, at, plot.X + plot.Width, at, gridlines));
            }
        }

        // Categories run from the axis origin: up the plot when the bars run across, unless reversed.
        var band = (horizontal ? plot.Height : plot.Width) / categoryCount;
        var categoryReversed = categoryAxis?.Reversed == true;

        float BandStart(int category)
        {
            var slot = categoryReversed != horizontal ? categoryCount - 1 - category : category;
            return horizontal ? plot.Y + slot * band : plot.X + slot * band;
        }

        // Where a category's point stands along the category axis: mid-band, or on the band's ticks when
        // the value axis crosses between categories (an area chart's points run edge to edge).
        var onTicks = chart.PointsOnTicks &&
                      categoryCount > 1;
        var pitch = onTicks ? plot.Width / (categoryCount - 1) : band;

        float Along(int category)
        {
            if (!onTicks)
            {
                return BandStart(category) + band / 2;
            }

            var slot = categoryReversed ? categoryCount - 1 - category : category;
            return plot.X + slot * pitch;
        }

        switch (chart.Kind)
        {
            case ChartKind.Bar:
                Bars(chart, categoryCount, band, stacked, percent, BandStart, ValueAt, horizontal, minimum, items);
                break;
            case ChartKind.Area:
                Areas(chart, categoryCount, stacked, percent, Along, ValueAt, minimum, items);
                break;
            default:
                Lines(chart, categoryCount, Along, ValueAt, horizontal, items);
                break;
        }

        // Axis lines: the category axis along the value origin, the value axis along the plot's edge.
        if (categoryAxis is { Deleted: false, Line: { } categoryLine })
        {
            var origin = ValueAt(Math.Clamp(0, minimum, maximum));
            items.Add(horizontal
                ? Rule(origin, plot.Y, origin, plot.Y + plot.Height, categoryLine)
                : Rule(plot.X, origin, plot.X + plot.Width, origin, categoryLine));
        }

        if (valueAxis is { Deleted: false, Line: { } valueLine })
        {
            items.Add(horizontal
                ? Rule(plot.X, plot.Y + plot.Height, plot.X + plot.Width, plot.Y + plot.Height, valueLine)
                : Rule(plot.X, plot.Y, plot.X, plot.Y + plot.Height, valueLine));
        }

        if (showValueLabels)
        {
            var labelHeight = LineHeight("0", valueStyle);
            for (var index = 0; index < ticks.Count; index++)
            {
                var at = ValueAt(ticks[index]);
                if (horizontal)
                {
                    var labelWidth = TextWidth(valueLabels[index], valueStyle) + 2;
                    items.AddRange(Label(valueLabels[index], valueStyle, at - labelWidth / 2, plot.Y + plot.Height + labelGap, labelWidth, labelHeight, TextAlignment.Center));
                }
                else
                {
                    var labelWidth = TextWidth(valueLabels[index], valueStyle) + 2;
                    items.AddRange(Label(valueLabels[index], valueStyle, plot.X - labelGap - labelWidth, at - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Right));
                }
            }
        }

        if (showCategoryLabels)
        {
            var labelHeight = LineHeight("0", categoryStyle);
            for (var category = 0; category < Math.Min(categoryCount, chart.Categories.Count); category++)
            {
                var text = chart.Categories[category];
                var start = BandStart(category);
                if (horizontal)
                {
                    var labelWidth = TextWidth(text, categoryStyle) + 2;
                    items.AddRange(Label(text, categoryStyle, plot.X - labelGap - labelWidth, start + band / 2 - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Right));
                }
                else
                {
                    var left = onTicks ? Along(category) - pitch / 2 : start;
                    items.AddRange(Label(text, categoryStyle, left, plot.Y + plot.Height + labelGap, pitch, labelHeight, TextAlignment.Center));
                }
            }
        }
    }

    void Bars(
        ChartModel chart,
        int categoryCount,
        float band,
        bool stacked,
        bool percent,
        Func<int, float> bandStart,
        Func<double, float> valueAt,
        bool horizontal,
        double minimum,
        List<PlacedItem> items)
    {
        // A category's band holds its bars and one gap: n bars overlapping by `overlap` of a bar, and a gap
        // of `gap` bars split either side.
        var slots = stacked ? 1 : chart.Series.Count;
        var overlap = stacked ? 1 : Math.Clamp(chart.OverlapPercent / 100, -1, 1);
        var gap = Math.Max(0, chart.GapWidthPercent / 100);
        var barWidth = (float) (band / (slots - (slots - 1) * overlap + gap));
        var baseline = Math.Max(0, minimum);

        for (var category = 0; category < categoryCount; category++)
        {
            var groupStart = bandStart(category) + (float) (barWidth * gap / 2);
            var total = percent ? chart.Series.Sum(_ => Math.Abs(At(_, category))) : 1;
            double positive = 0;
            double negative = 0;
            for (var index = 0; index < chart.Series.Count; index++)
            {
                var series = chart.Series[index];
                if (category >= series.Values.Count || series.Values[category] is not { } raw)
                {
                    continue;
                }

                var value = percent &&
                            total > 0 ? raw / total : raw;
                double from;
                double to;
                if (stacked)
                {
                    from = value >= 0 ? positive : negative;
                    to = from + value;
                    if (value >= 0)
                    {
                        positive = to;
                    }
                    else
                    {
                        negative = to;
                    }

                    from = Math.Max(from, minimum);
                }
                else
                {
                    from = baseline;
                    to = value;
                }

                var slot = stacked ? 0 : index;
                var across = groupStart + (float) (slot * barWidth * (1 - overlap));

                // In a horizontal chart the bars of one category stack downward in series order when
                // categories run up the axis, so the first series sits nearest the axis origin.
                if (horizontal &&
                    !stacked)
                {
                    across = groupStart + (float) ((slots - 1 - slot) * barWidth * (1 - overlap));
                }

                var start = valueAt(from);
                var end = valueAt(to);
                var fill = PointColor(chart, series, index, category);
                items.Add(horizontal
                    ? Rectangle(Math.Min(start, end), across, Math.Abs(end - start), barWidth, fill, series.Line)
                    : Rectangle(across, Math.Min(start, end), barWidth, Math.Abs(end - start), fill, series.Line));

                if (series.Labels is { } labels &&
                    !labels.Deleted.Contains(category))
                {
                    var text = DataLabelText(labels, series.FormatCode, raw, total, chart, category);
                    var labelWidth = TextWidth(text, labels.Text) + 2;
                    var labelHeight = LineHeight(text, labels.Text);
                    items.AddRange(horizontal
                        ? Label(text, labels.Text, Math.Max(start, end) + labelGap, across + barWidth / 2 - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Left)
                        : Label(text, labels.Text, across + barWidth / 2 - labelWidth / 2, Math.Min(start, end) - labelGap - labelHeight, labelWidth, labelHeight, TextAlignment.Center));
                }
            }
        }
    }

    static void Lines(ChartModel chart, int categoryCount, Func<int, float> along, Func<double, float> valueAt, bool horizontal, List<PlacedItem> items)
    {
        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var points = new List<(float X, float Y)?>();
            for (var category = 0; category < categoryCount; category++)
            {
                if (category >= series.Values.Count || series.Values[category] is not { } value)
                {
                    points.Add(null);
                    continue;
                }

                var at = along(category);
                points.Add(horizontal ? (valueAt(value), at) : (at, valueAt(value)));
            }

            SeriesLine(chart, series, index, points, false, items);
        }
    }

    // A line, scatter or radar series: its line through the points in turn (a gap breaks it; a radar's
    // closes on its first point), then a marker on each point, over the line.
    static void SeriesLine(ChartModel chart, ChartSeries series, int index, List<(float X, float Y)?> points, bool closed, List<PlacedItem> items)
    {
        var color = SeriesColor(chart, series, index);
        if (!series.NoLine)
        {
            var stroke = series.Line ?? new ChartStroke(color, 2.25, 1);
            var path = new List<(float X, float Y)?>(points);
            if (closed &&
                points.Count > 2)
            {
                path.Add(points[0]);
            }

            for (var point = 1; point < path.Count; point++)
            {
                if (path[point - 1] is { } from &&
                    path[point] is { } to)
                {
                    items.Add(Rule(from.X, from.Y, to.X, to.Y, stroke));
                }
            }
        }

        if (series.Marker is { } marker)
        {
            foreach (var point in points)
            {
                if (point is { } at)
                {
                    items.Add(Marker(marker, at.X, at.Y, color));
                }
            }
        }
    }

    // An area chart's series, each a polygon from its line down to the axis, or to the series under it
    // when stacked. Later series paint over earlier ones, as Office draws them.
    static void Areas(ChartModel chart, int categoryCount, bool stacked, bool percent, Func<int, float> along, Func<double, float> valueAt, double minimum, List<PlacedItem> items)
    {
        var baseline = Math.Max(0, minimum);
        var below = new double[categoryCount];
        Array.Fill(below, baseline);
        var totals = Enumerable.Range(0, categoryCount)
            .Select(category => percent ? chart.Series.Sum(_ => Math.Abs(At(_, category))) : 1)
            .ToList();
        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var top = new double[categoryCount];
            for (var category = 0; category < categoryCount; category++)
            {
                var value = At(series, category);
                if (percent &&
                    totals[category] > 0)
                {
                    value /= totals[category];
                }

                top[category] = stacked ? below[category] + value : value;
            }

            var outline = new List<(float X, float Y)>();
            for (var category = 0; category < categoryCount; category++)
            {
                outline.Add((along(category), valueAt(top[category])));
            }

            for (var category = categoryCount - 1; category >= 0; category--)
            {
                outline.Add((along(category), valueAt(stacked ? below[category] : baseline)));
            }

            if (Polygon(outline, PointColor(chart, series, index, 0), series.Line) is { } shape)
            {
                items.Add(shape);
            }

            if (stacked)
            {
                below = top;
            }
        }
    }

    // A scatter chart: both axes are value axes, scaled as the value axis of any other chart is, the
    // horizontal one (the model's category axis) by the series' x values, which default to 1, 2, 3….
    void Scatter(ChartModel chart, (float X, float Y, float Width, float Height) inner, float chartX, float chartY, float chartWidth, float chartHeight, List<PlacedItem> items)
    {
        var points = chart.Series
            .Select(_ =>
            {
                var xs = _.XValues.Count > 0 ? _.XValues : [.. Enumerable.Range(1, _.Values.Count).Select(number => (double?) number)];
                return xs.Zip(_.Values)
                    .Select(pair => pair is ({ } x, { } y) ? (X: x, Y: y) : ((double X, double Y)?) null)
                    .ToList();
            })
            .ToList();
        var present = points.SelectMany(_ => _).OfType<(double X, double Y)>().ToList();
        if (present.Count == 0)
        {
            return;
        }

        var xAxis = chart.CategoryAxis;
        var yAxis = chart.ValueAxis;
        var (xMinimum, xMaximum, xUnit) = Scale(Math.Min(0, present.Min(_ => _.X)), Math.Max(0, present.Max(_ => _.X)), xAxis);
        var (yMinimum, yMaximum, yUnit) = Scale(Math.Min(0, present.Min(_ => _.Y)), Math.Max(0, present.Max(_ => _.Y)), yAxis);
        var xTicks = Ticks(xMinimum, xMaximum, xUnit);
        var yTicks = Ticks(yMinimum, yMaximum, yUnit);
        var xStyle = xAxis?.Text ?? chart.Text;
        var yStyle = yAxis?.Text ?? chart.Text;
        var xLabels = xTicks.Select(_ => NumberFormat.Format(_, AxisFormat(xAxis, null)).Text).ToList();
        var yLabels = yTicks.Select(_ => NumberFormat.Format(_, AxisFormat(yAxis, chart.Series[0].FormatCode)).Text).ToList();
        var showX = xAxis is { Deleted: false, ShowLabels: true };
        var showY = yAxis is { Deleted: false, ShowLabels: true };

        var plot = chart.PlotLayout is { } manual ? Absolute(manual, chartX, chartY, chartWidth, chartHeight) : inner;
        if (!chart.PlotLayoutIsInner || chart.PlotLayout == null)
        {
            var yWidth = showY ? yLabels.Max(_ => TextWidth(_, yStyle)) : 0;
            var yHeight = showY ? LineHeight("0", yStyle) : 0;
            var xHeight = showX ? LineHeight("0", xStyle) : 0;
            var left = yWidth > 0 ? yWidth + labelGap : 0;
            var bottom = xHeight > 0 ? xHeight + labelGap : 0;
            var right = showX ? TextWidth(xLabels[^1], xStyle) / 2 : 0;
            plot = (plot.X + left, plot.Y + yHeight / 2, plot.Width - left - right, plot.Height - bottom - yHeight / 2);
        }

        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        if (chart.Plot.FillHex != null || chart.Plot.Line != null)
        {
            items.Add(Rectangle(plot.X, plot.Y, plot.Width, plot.Height, chart.Plot.FillHex, chart.Plot.Line));
        }

        float XAt(double value)
        {
            var fraction = (float) ((value - xMinimum) / (xMaximum - xMinimum));
            if (xAxis?.Reversed == true)
            {
                fraction = 1 - fraction;
            }

            return plot.X + fraction * plot.Width;
        }

        float YAt(double value)
        {
            var fraction = (float) ((value - yMinimum) / (yMaximum - yMinimum));
            if (yAxis?.Reversed == true)
            {
                fraction = 1 - fraction;
            }

            return plot.Y + plot.Height - fraction * plot.Height;
        }

        if (yAxis?.MajorGridlines is { } rows)
        {
            foreach (var tick in yTicks)
            {
                items.Add(Rule(plot.X, YAt(tick), plot.X + plot.Width, YAt(tick), rows));
            }
        }

        if (xAxis?.MajorGridlines is { } columns)
        {
            foreach (var tick in xTicks)
            {
                items.Add(Rule(XAt(tick), plot.Y, XAt(tick), plot.Y + plot.Height, columns));
            }
        }

        for (var index = 0; index < chart.Series.Count; index++)
        {
            var placed = points[index]
                .Select(_ => _ is { } point ? (XAt(point.X), YAt(point.Y)) : ((float X, float Y)?) null)
                .ToList();
            SeriesLine(chart, chart.Series[index], index, placed, false, items);
        }

        // Each axis line stands where the other axis is 0, as a category axis does.
        if (xAxis is { Deleted: false, Line: { } xLine })
        {
            var at = YAt(Math.Clamp(0, yMinimum, yMaximum));
            items.Add(Rule(plot.X, at, plot.X + plot.Width, at, xLine));
        }

        if (yAxis is { Deleted: false, Line: { } yLine })
        {
            var at = XAt(Math.Clamp(0, xMinimum, xMaximum));
            items.Add(Rule(at, plot.Y, at, plot.Y + plot.Height, yLine));
        }

        if (showY)
        {
            var labelHeight = LineHeight("0", yStyle);
            for (var index = 0; index < yTicks.Count; index++)
            {
                var labelWidth = TextWidth(yLabels[index], yStyle) + 2;
                items.AddRange(Label(yLabels[index], yStyle, plot.X - labelGap - labelWidth, YAt(yTicks[index]) - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Right));
            }
        }

        if (showX)
        {
            var labelHeight = LineHeight("0", xStyle);
            for (var index = 0; index < xTicks.Count; index++)
            {
                var labelWidth = TextWidth(xLabels[index], xStyle) + 2;
                items.AddRange(Label(xLabels[index], xStyle, XAt(xTicks[index]) - labelWidth / 2, plot.Y + plot.Height + labelGap, labelWidth, labelHeight, TextAlignment.Center));
            }
        }
    }

    // A radar chart: the categories are spokes clockwise from twelve o'clock, each value a distance out
    // along its spoke, with the value axis's labels up the first spoke and its gridlines a web of
    // polygons. The circle takes the box less room for the category labels round it.
    void Radar(ChartModel chart, (float X, float Y, float Width, float Height) box, List<PlacedItem> items)
    {
        var categoryCount = Math.Max(chart.Categories.Count, chart.Series.Max(_ => _.Values.Count));
        if (categoryCount < 3)
        {
            return;
        }

        var (dataMin, dataMax) = DataRange(chart, categoryCount, false, false);
        var valueAxis = chart.ValueAxis;
        var categoryAxis = chart.CategoryAxis;
        var (minimum, maximum, unit) = Scale(dataMin, dataMax, valueAxis);
        var ticks = Ticks(minimum, maximum, unit);
        var valueStyle = valueAxis?.Text ?? chart.Text;
        var categoryStyle = categoryAxis?.Text ?? chart.Text;
        var showCategoryLabels = categoryAxis is not { Deleted: true } &&
                                 categoryAxis?.ShowLabels != false &&
                                 chart.Categories.Count > 0;
        var categoryWidth = showCategoryLabels ? chart.Categories.Max(_ => TextWidth(_, categoryStyle)) + 2 : 0;
        var categoryHeight = showCategoryLabels ? LineHeight("0", categoryStyle) : 0;
        var radius = Math.Min(box.Width / 2 - categoryWidth - labelGap, box.Height / 2 - categoryHeight - labelGap);
        if (radius <= 0)
        {
            return;
        }

        var centreX = box.X + box.Width / 2;
        var centreY = box.Y + box.Height / 2;
        var direction = categoryAxis?.Reversed == true ? -1 : 1;

        double Angle(int category) =>
            direction * category * 2 * Math.PI / categoryCount;

        (float X, float Y) At(int category, double distance) =>
            (centreX + (float) (distance * Math.Sin(Angle(category))), centreY - (float) (distance * Math.Cos(Angle(category))));

        float Distance(double value)
        {
            var fraction = (float) Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
            if (valueAxis?.Reversed == true)
            {
                fraction = 1 - fraction;
            }

            return radius * fraction;
        }

        if (valueAxis?.MajorGridlines is { } web)
        {
            foreach (var tick in ticks)
            {
                for (var category = 0; category < categoryCount; category++)
                {
                    var from = At(category, Distance(tick));
                    var to = At((category + 1) % categoryCount, Distance(tick));
                    items.Add(Rule(from.X, from.Y, to.X, to.Y, web));
                }
            }
        }

        // Spokes are the category axis's gridlines; its line alone draws none, as PowerPoint renders it.
        if (categoryAxis is { Deleted: false, MajorGridlines: { } spokes })
        {
            for (var category = 0; category < categoryCount; category++)
            {
                var end = At(category, radius);
                items.Add(Rule(centreX, centreY, end.X, end.Y, spokes));
            }
        }

        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var placed = Enumerable.Range(0, categoryCount)
                .Select(category => category < series.Values.Count && series.Values[category] is { } value ? At(category, Distance(value)) : ((float X, float Y)?) null)
                .ToList();
            if (!chart.RadarFilled)
            {
                SeriesLine(chart, series, index, placed, true, items);
                continue;
            }

            if (Polygon([.. placed.Select(_ => _ ?? (centreX, centreY))], PointColor(chart, series, index, 0), series.Line) is { } shape)
            {
                items.Add(shape);
            }
        }

        if (valueAxis is { Deleted: false, ShowLabels: true })
        {
            var format = AxisFormat(valueAxis, chart.Series[0].FormatCode);
            var labelHeight = LineHeight("0", valueStyle);
            foreach (var tick in ticks)
            {
                var text = NumberFormat.Format(tick, format).Text;
                var labelWidth = TextWidth(text, valueStyle) + 2;
                var at = At(0, Distance(tick));
                items.AddRange(Label(text, valueStyle, at.X - labelGap - labelWidth, at.Y - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Right));
            }
        }

        if (!showCategoryLabels)
        {
            return;
        }

        // A category's label stands off its spoke's end, on the side the spoke points to.
        for (var category = 0; category < Math.Min(categoryCount, chart.Categories.Count); category++)
        {
            var text = chart.Categories[category];
            var labelWidth = TextWidth(text, categoryStyle) + 2;
            var end = At(category, radius + labelGap);
            var sine = Math.Sin(Angle(category));
            var cosine = Math.Cos(Angle(category));
            var left = end.X - labelWidth / 2;
            var alignment = TextAlignment.Center;
            if (sine > 0.1)
            {
                left = end.X;
                alignment = TextAlignment.Left;
            }
            else if (sine < -0.1)
            {
                left = end.X - labelWidth;
                alignment = TextAlignment.Right;
            }

            var top = end.Y - categoryHeight / 2;
            if (cosine > 0.1)
            {
                top = end.Y - categoryHeight;
            }
            else if (cosine < -0.1)
            {
                top = end.Y;
            }

            items.AddRange(Label(text, categoryStyle, left, top, labelWidth, categoryHeight, alignment));
        }
    }

    void Pie(ChartModel chart, (float X, float Y, float Width, float Height) box, float chartX, float chartY, float chartWidth, float chartHeight, List<PlacedItem> items)
    {
        var series = chart.Series[0];
        var values = series.Values.Select(_ => _ is > 0 ? _.Value : 0).ToList();
        var total = values.Sum();
        var diameter = Math.Min(box.Width, box.Height);
        if (total <= 0 || diameter <= 0)
        {
            return;
        }

        var circleX = box.X + (box.Width - diameter) / 2;
        var circleY = box.Y + (box.Height - diameter) / 2;
        var hole = Math.Clamp(chart.HolePercent / 100, 0, 0.9);
        var angle = chart.FirstSliceDegrees;
        var labels = new List<PlacedItem>();
        for (var point = 0; point < values.Count; point++)
        {
            if (values[point] <= 0)
            {
                continue;
            }

            var sweep = values[point] / total * 360;
            var fill = PointColor(chart, series, 0, point);
            var outline = series.PointLines.GetValueOrDefault(point) ?? series.Line;
            items.Add(
                new PlacedShape(
                    circleX,
                    circleY,
                    diameter,
                    diameter,
                    new()
                    {
                        WidthPoints = diameter,
                        HeightPoints = diameter,
                        FillColorHex = fill,
                        LineColorHex = outline?.ColorHex,
                        LineWidthPoints = outline?.WidthPoints,
                        Subpaths = [SliceContour(angle, sweep, hole)]
                    }));

            // A label sits mid-slice, halfway across the ring (a third of the way in from a pie's rim),
            // then moves by its manual offset, which is a fraction of the chart.
            if (series.Labels is { } dataLabels &&
                !dataLabels.Deleted.Contains(point))
            {
                var text = DataLabelText(dataLabels, series.FormatCode, values[point], total, chart, point);
                var radius = hole > 0 ? (1 + hole) / 4 : 1 / 3.0;
                var middle = (angle + sweep / 2) * Math.PI / 180;
                var centreX = circleX + diameter * (float) (0.5 + radius * Math.Sin(middle));
                var centreY = circleY + diameter * (float) (0.5 - radius * Math.Cos(middle));
                if (dataLabels.Offsets.TryGetValue(point, out var offset))
                {
                    centreX += (float) (offset.X * chartWidth);
                    centreY += (float) (offset.Y * chartHeight);
                }

                // A resized label is a box of its own, centred where the label would be and kept
                // inside the chart, with its text centred in it. personal-budget's "62%" is a box
                // 99.5% of the chart's width and height, so the chart's edges hold it and its text
                // sits in the doughnut's hole, where Excel draws it, whatever its slice.
                if (dataLabels.Sizes.TryGetValue(point, out var size))
                {
                    var boxWidth = (float) (size.Width * chartWidth);
                    var boxHeight = (float) (size.Height * chartHeight);
                    var boxX = Math.Clamp(centreX - boxWidth / 2, chartX, Math.Max(chartX, chartX + chartWidth - boxWidth));
                    var boxY = Math.Clamp(centreY - boxHeight / 2, chartY, Math.Max(chartY, chartY + chartHeight - boxHeight));
                    centreX = boxX + boxWidth / 2;
                    centreY = boxY + boxHeight / 2;
                }

                var labelWidth = TextWidth(text, dataLabels.Text) + 2;
                var labelHeight = LineHeight(text, dataLabels.Text);
                labels.AddRange(Label(text, dataLabels.Text, centreX - labelWidth / 2, centreY - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Center));
            }

            angle += sweep;
        }

        items.AddRange(labels);
    }

    // Lays the legend out and returns what is left of the chart beside it. Bars and lines key each
    // series; a pie, or a single series that varies its colours, keys each category.
    (float X, float Y, float Width, float Height) Legend(ChartModel chart, ChartLegend legend, float chartX, float chartY, float chartWidth, float chartHeight, (float X, float Y, float Width, float Height) inner, List<PlacedItem> items)
    {
        var byCategory = chart.Kind is ChartKind.Pie or ChartKind.Doughnut ||
                         chart is
                         {
                             VaryColors: true,
                             Series.Count: 1
                         };
        List<(string Text, string Color)> entries = byCategory
            ? [.. chart.Categories.Select((_, index) => (Text: _, Color: PointColor(chart, chart.Series[0], 0, index)))]
            : [.. chart.Series.Select((_, index) => (Text: _.Name ?? $"Series{index + 1}", Color: SeriesColor(chart, _, index)))];

        // Horizontal bars stack their series upwards, the first at the bottom of each category, and the
        // legend lists them the same way round: chart_anchored's North and South bars are keyed
        // "South, North" in Word's render. A legend lists them by series everywhere else.
        if (!byCategory &&
            chart is
            {
                Kind: ChartKind.Bar,
                Horizontal: true
            })
        {
            entries.Reverse();
        }

        if (entries.Count == 0)
        {
            return inner;
        }

        var style = legend.Text;
        var lineHeight = LineHeight("0", style);
        var key = (float) (style.SizePoints * 0.6);

        // A line series is keyed by a stroke of its line, twice the legend's font size long: chart_column_line's
        // 10pt legend keys its 3pt lines with 20pt strokes in Word's render. A scatter or unfilled radar
        // series is keyed as a line is, and a marked series carries its marker on the key; a scatter
        // series of markers alone is keyed by the marker. Anything else takes a square swatch of its fill.
        var lineKeys = !byCategory &&
                       (chart.Kind is ChartKind.Line or ChartKind.Scatter ||
                        chart is
                        {
                            Kind: ChartKind.Radar,
                            RadarFilled: false
                        });
        var strokeKeys = lineKeys &&
                         chart.Series.Any(_ => !_.NoLine);
        var keyWidth = strokeKeys ? (float) (style.SizePoints * 2) : key;
        var keyGap = 3f;
        var entryGap = (float) style.SizePoints;
        var widths = entries.Select(_ => keyWidth + keyGap + TextWidth(_.Text, style) + 2).ToList();
        var across = legend.Position is ChartLegendPosition.Top or ChartLegendPosition.Bottom;

        var blockWidth = across ? widths.Sum() + entryGap * (entries.Count - 1) : widths.Max();
        var blockHeight = across ? lineHeight : lineHeight * entries.Count;

        (float X, float Y, float Width, float Height) area;
        var remaining = inner;
        if (legend.Layout is { } manual)
        {
            area = Absolute(manual, chartX, chartY, chartWidth, chartHeight);
        }
        else
        {
            switch (legend.Position)
            {
                case ChartLegendPosition.Bottom:
                    area = (inner.X, inner.Y + inner.Height - blockHeight, inner.Width, blockHeight);
                    remaining = (inner.X, inner.Y, inner.Width, inner.Height - blockHeight - padding);
                    break;
                case ChartLegendPosition.Top:
                    area = (inner.X, inner.Y, inner.Width, blockHeight);
                    remaining = (inner.X, inner.Y + blockHeight + padding, inner.Width, inner.Height - blockHeight - padding);
                    break;
                case ChartLegendPosition.Left:
                    area = (inner.X, inner.Y, blockWidth, inner.Height);
                    remaining = (inner.X + blockWidth + padding, inner.Y, inner.Width - blockWidth - padding, inner.Height);
                    break;
                default:
                    area = (inner.X + inner.Width - blockWidth, inner.Y, blockWidth, inner.Height);
                    remaining = (inner.X, inner.Y, inner.Width - blockWidth - padding, inner.Height);
                    break;
            }
        }

        // The entries are centred in the legend's area, in a row or a column.
        var x = area.X + (area.Width - blockWidth) / 2;
        var y = area.Y + (area.Height - blockHeight) / 2;
        for (var index = 0; index < entries.Count; index++)
        {
            if (lineKeys)
            {
                var series = chart.Series[index];
                if (!series.NoLine)
                {
                    var stroke = (float) (series.Line?.WidthPoints ?? 2.25);
                    items.Add(Rectangle(x, y + (lineHeight - stroke) / 2, keyWidth, stroke, entries[index].Color, null));
                }

                if (series.Marker is { } marker)
                {
                    items.Add(Marker(marker, x + keyWidth / 2, y + lineHeight / 2, entries[index].Color));
                }
            }
            else
            {
                items.Add(Rectangle(x, y + (lineHeight - key) / 2, key, key, entries[index].Color, null));
            }

            items.AddRange(Label(entries[index].Text, style, x + keyWidth + keyGap, y, widths[index] - keyWidth - keyGap, lineHeight, TextAlignment.Left));
            if (across)
            {
                x += widths[index] + entryGap;
            }
            else
            {
                y += lineHeight;
            }
        }

        return remaining;
    }

    /// <summary>
    /// The value axis's minimum, maximum and major unit. Excel's automatic axis starts at 0 under data
    /// that is all positive (and ends at 0 over data all negative), pads the far end by 5% of the range,
    /// and steps by a power of ten divided by five when the padded range spans fewer than two of them,
    /// by two when fewer than five, and whole otherwise: a 3,750 maximum is padded to 3,937.5, steps by
    /// 500 and ends at 4,000 (personal-budget's column chart). A fixed minimum, maximum or unit on the
    /// axis stands.
    /// </summary>
    internal static (double Minimum, double Maximum, double Unit) Scale(double dataMinimum, double dataMaximum, ChartAxis? axis)
    {
        var minimum = axis?.Minimum ?? (dataMinimum >= 0 ? 0 : dataMinimum);
        var maximum = axis?.Maximum ?? (dataMaximum <= 0 &&
                                        dataMinimum < 0 ? 0 : dataMaximum);
        if (maximum <= minimum)
        {
            maximum = minimum + 1;
        }

        var padded = maximum;
        var paddedMinimum = minimum;
        if (axis?.Maximum == null &&
            dataMaximum > 0)
        {
            padded = maximum + 0.05 * (maximum - minimum);
        }

        if (axis?.Minimum == null &&
            dataMinimum < 0)
        {
            paddedMinimum = minimum - 0.05 * (maximum - minimum);
        }

        var unit = axis?.MajorUnit is > 0 ? axis.MajorUnit.Value : NiceUnit(padded - paddedMinimum);
        if (axis?.Maximum == null)
        {
            maximum = Math.Ceiling(padded / unit - 1e-9) * unit;
        }

        if (axis?.Minimum == null &&
            dataMinimum < 0)
        {
            minimum = Math.Floor(paddedMinimum / unit + 1e-9) * unit;
        }

        return (minimum, maximum, unit);
    }

    static double NiceUnit(double range)
    {
        if (range <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(range)));
        var ratio = range / magnitude;
        if (ratio < 2)
        {
            return magnitude / 5;
        }

        if (ratio < 5)
        {
            return magnitude / 2;
        }

        return magnitude;
    }

    static (double Minimum, double Maximum) DataRange(ChartModel chart, int categoryCount, bool stacked, bool percent)
    {
        if (percent)
        {
            return (0, 1);
        }

        double minimum = 0;
        double maximum = 0;
        for (var category = 0; category < categoryCount; category++)
        {
            if (stacked)
            {
                var positive = chart.Series.Sum(_ => Math.Max(0, At(_, category)));
                var negative = chart.Series.Sum(_ => Math.Min(0, At(_, category)));
                maximum = Math.Max(maximum, positive);
                minimum = Math.Min(minimum, negative);
                continue;
            }

            foreach (var series in chart.Series)
            {
                maximum = Math.Max(maximum, At(series, category));
                minimum = Math.Min(minimum, At(series, category));
            }
        }

        return (minimum, maximum);
    }

    static double At(ChartSeries series, int category) =>
        category < series.Values.Count ? series.Values[category] ?? 0 : 0;

    // The ticks from an axis's minimum to its maximum, a major unit apart.
    static List<double> Ticks(double minimum, double maximum, double unit)
    {
        var ticks = new List<double>();
        for (var tick = minimum; tick <= maximum + unit * 1e-6; tick += unit)
        {
            ticks.Add(Math.Abs(tick) < unit * 1e-9 ? 0 : tick);
        }

        return ticks;
    }

    // The format an axis's tick labels are in: its own, unless that is General, then what the data is cached in.
    static string AxisFormat(ChartAxis? axis, string? fallback)
    {
        if (axis?.FormatCode is { } code &&
            code != "General")
        {
            return code;
        }

        return fallback ?? "General";
    }

    // A series' own colour: its fill, else its line's, else its marker's (a scatter series with its line
    // off), else its turn in the theme's accents.
    static string SeriesColor(ChartModel chart, ChartSeries series, int index)
    {
        if (series.FillHex is { } fill)
        {
            return fill;
        }

        if (series is
            {
                NoLine: false,
                Line.ColorHex: { } line
            })
        {
            return line;
        }

        var marker = series.Marker;

        return marker?.FillHex ??
               marker?.Line?.ColorHex ??
               chart.Palette[index % chart.Palette.Count];
    }

    // A point's marker, centred on it, in its own fill and outline or else the series' colour.
    static PlacedShape Marker(ChartMarker marker, float x, float y, string color)
    {
        var size = (float) marker.SizePoints;
        var outline = marker.Line ?? new ChartStroke(color, 0.75, 1);
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> paths;
        var open = false;
        switch (marker.Symbol)
        {
            case "square":
                paths = [[(0, 0), (1, 0), (1, 1), (0, 1)]];
                break;
            case "diamond":
                paths = [[(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)]];
                break;
            case "triangle":
                paths = [[(0.5, 0), (1, 1), (0, 1)]];
                break;
            case "dash":
                paths = [[(0, 0.4), (1, 0.4), (1, 0.6), (0, 0.6)]];
                break;
            case "dot":
                paths = [Circle(0.25)];
                break;
            case "x":
                paths = [[(0, 0), (1, 1)], [(1, 0), (0, 1)]];
                open = true;
                break;
            case "plus":
                paths = [[(0.5, 0), (0.5, 1)], [(0, 0.5), (1, 0.5)]];
                open = true;
                break;
            case "star":
                paths = [[(0, 0), (1, 1)], [(1, 0), (0, 1)], [(0.5, 0), (0.5, 1)]];
                open = true;
                break;
            default:
                paths = [Circle(0.5)];
                break;
        }

        return new(
            x - size / 2,
            y - size / 2,
            size,
            size,
            new()
            {
                WidthPoints = size,
                HeightPoints = size,
                FillColorHex = open ? null : marker.FillHex ?? color,
                LineColorHex = outline.ColorHex,
                LineWidthPoints = outline.WidthPoints,
                LineAlpha = outline.Alpha,
                Subpaths = paths
            });

        static IReadOnlyList<(double X, double Y)> Circle(double radius) =>
            [.. Enumerable.Range(0, 24).Select(_ => (0.5 + radius * Math.Sin(_ * Math.PI / 12), 0.5 - radius * Math.Cos(_ * Math.PI / 12)))];
    }

    // A filled polygon through page points, in the box that bounds them; null where it has no area.
    static PlacedShape? Polygon(List<(float X, float Y)> points, string? fill, ChartStroke? line)
    {
        var left = points.Min(_ => _.X);
        var top = points.Min(_ => _.Y);
        var width = points.Max(_ => _.X) - left;
        var height = points.Max(_ => _.Y) - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new(
            left,
            top,
            width,
            height,
            new()
            {
                WidthPoints = width,
                HeightPoints = height,
                FillColorHex = fill,
                LineColorHex = line?.ColorHex,
                LineWidthPoints = line?.WidthPoints,
                LineAlpha = line?.Alpha ?? 1,
                Subpaths = [[.. points.Select(_ => ((double) (_.X - left) / width, (double) (_.Y - top) / height))]]
            });
    }

    static string PointColor(ChartModel chart, ChartSeries series, int seriesIndex, int point)
    {
        if (series.PointFills.TryGetValue(point, out var fill))
        {
            return fill;
        }

        if (chart is
            {
                VaryColors: true,
                Series.Count: 1
            })
        {
            return chart.Palette[point % chart.Palette.Count];
        }

        return series.FillHex ?? chart.Palette[seriesIndex % chart.Palette.Count];
    }

    static string DataLabelText(ChartDataLabels labels, string? sourceFormat, double value, double total, ChartModel chart, int point)
    {
        var parts = new List<string>();
        if (labels.ShowCategory &&
            point < chart.Categories.Count)
        {
            parts.Add(chart.Categories[point]);
        }

        if (labels.ShowValue)
        {
            parts.Add(NumberFormat.Format(
                value,
                labels.FormatCode is { } own &&
                own != "General"
                    ? own
                    : sourceFormat).Text);
        }

        if (labels.ShowPercent &&
            total > 0)
        {
            parts.Add(NumberFormat.Format(value / total, "0%").Text);
        }

        return string.Join(", ", parts);
    }

    // One slice's outline in the unit square of the plot circle: the outer arc clockwise from
    // startDegrees past twelve o'clock, then back along the hole's arc (or to the centre for a pie).
    static IReadOnlyList<(double X, double Y)> SliceContour(double startDegrees, double sweepDegrees, double hole)
    {
        var steps = Math.Max(2, (int) Math.Ceiling(sweepDegrees / 2));
        var points = new List<(double X, double Y)>(steps * 2 + 2);

        static (double X, double Y) At(double degrees, double radius)
        {
            var radians = degrees * Math.PI / 180;
            return (0.5 + radius * Math.Sin(radians), 0.5 - radius * Math.Cos(radians));
        }

        for (var step = 0; step <= steps; step++)
        {
            points.Add(At(startDegrees + sweepDegrees * step / steps, 0.5));
        }

        if (hole <= 0)
        {
            points.Add((0.5, 0.5));
            return points;
        }

        for (var step = steps; step >= 0; step--)
        {
            points.Add(At(startDegrees + sweepDegrees * step / steps, 0.5 * hole));
        }

        return points;
    }

    // The chart with every text size and rule width multiplied by the scale it is printed at.
    static ChartModel Scaled(ChartModel chart, double scale)
    {
        if (Math.Abs(scale - 1) < 1e-9)
        {
            return chart;
        }

        ChartTextStyle Text(ChartTextStyle style) =>
            style with
            {
                SizePoints = style.SizePoints * scale
            };

        ChartStroke? Stroke(ChartStroke? stroke)
        {
            if (stroke == null)
            {
                return null;
            }

            return stroke with
            {
                WidthPoints = stroke.WidthPoints * scale
            };
        }

        ChartAxis? Axis(ChartAxis? axis)
        {
            if (axis == null)
            {
                return null;
            }

            return axis with
            {
                Text = Text(axis.Text),
                Line = Stroke(axis.Line),
                MajorGridlines = Stroke(axis.MajorGridlines)
            };
        }

        return chart with
        {
            Text = Text(chart.Text),
            Title = chart.Title is { } title ? title with { Style = Text(title.Style) } : null,
            Legend = chart.Legend is { } legend ? legend with { Text = Text(legend.Text) } : null,
            CategoryAxis = Axis(chart.CategoryAxis),
            ValueAxis = Axis(chart.ValueAxis),
            Area = chart.Area with
            {
                Line = Stroke(chart.Area.Line)
            },
            Plot = chart.Plot with
            {
                Line = Stroke(chart.Plot.Line)
            },
            Series = [.. chart.Series
                .Select(_ => _ with
                {
                    Line = Stroke(_.Line),
                    Marker = _.Marker is { } marker ? marker with { SizePoints = marker.SizePoints * scale, Line = Stroke(marker.Line) } : null,
                    PointLines = _.PointLines.ToDictionary(point => point.Key, point => Stroke(point.Value)!),
                    Labels = _.Labels is { } labels ? labels with { Text = Text(labels.Text) } : null
                })]
        };
    }

    static (float X, float Y, float Width, float Height) Absolute(ChartRect rect, float x, float y, float width, float height) =>
        (x + (float) rect.X * width, y + (float) rect.Y * height, (float) rect.Width * width, (float) rect.Height * height);

    static PlacedShape Rectangle(float x, float y, float width, float height, string? fill, ChartStroke? line) =>
        new(
            x,
            y,
            width,
            height,
            new()
            {
                WidthPoints = width,
                HeightPoints = height,
                FillColorHex = fill,
                LineColorHex = line?.ColorHex,
                LineWidthPoints = line?.WidthPoints,
                LineAlpha = line?.Alpha ?? 1
            });

    // A straight rule from one point to another, as a stroked two-point path in the box between them.
    static PlacedShape Rule(float x0, float y0, float x1, float y1, ChartStroke stroke)
    {
        var left = Math.Min(x0, x1);
        var top = Math.Min(y0, y1);
        var width = Math.Max(Math.Abs(x1 - x0), 0.01f);
        var height = Math.Max(Math.Abs(y1 - y0), 0.01f);
        return new(
            left,
            top,
            width,
            height,
            new()
            {
                WidthPoints = width,
                HeightPoints = height,
                LineColorHex = stroke.ColorHex,
                LineWidthPoints = stroke.WidthPoints,
                LineAlpha = stroke.Alpha,
                Subpaths = [[((x0 - left) / width, (y0 - top) / height), ((x1 - left) / width, (y1 - top) / height)]]
            });
    }

    IReadOnlyList<PlacedItem> Label(string text, ChartTextStyle style, float x, float y, float width, float height, TextAlignment alignment) =>
        layoutText([Paragraph(text, style, alignment)], x, y, width, height, CellVerticalAlignment.Center);

    float TextWidth(string text, ChartTextStyle style) =>
        measurer.MeasureParagraphNaturalWidth(Paragraph(text, style, TextAlignment.Left), 10000);

    float LineHeight(string text, ChartTextStyle style)
    {
        var lines = measurer.LayoutLines(Paragraph(text, style, TextAlignment.Left), 10000);
        return lines.Count == 0 ? (float) style.SizePoints * 1.2f : lines.Sum(_ => _.Height);
    }

    static ParagraphElement Paragraph(string text, ChartTextStyle style, TextAlignment alignment) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = text,
                    Properties = new()
                    {
                        FontFamily = style.FontFamily,
                        FontSizePoints = style.SizePoints,
                        ColorHex = style.ColorHex,
                        Bold = style.Bold,
                        Italic = style.Italic
                    }
                }
            ],
            Properties = new()
            {
                Alignment = alignment,
                LineSpacingMultiplier = 1
            }
        };
}
