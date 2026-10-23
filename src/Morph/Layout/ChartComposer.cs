/// <summary>
/// Draws a <see cref="ChartModel"/> into its box as placed shapes and laid-out label paragraphs — the
/// items every painter already draws, so a chart needs nothing of its own downstream.
///
/// What it draws, in paint order: the chart area's fill and border, the plot area's fill, the value
/// axis's gridlines, the bars, lines or slices, the axis lines, the tick labels, the data labels, the
/// legend and the title.
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
        var stacked = chart.Grouping != ChartGrouping.Clustered && chart.Kind == ChartKind.Bar;
        var (dataMin, dataMax) = DataRange(chart, categoryCount, stacked, percent);
        var valueAxis = chart.ValueAxis;
        var (minimum, maximum, unit) = Scale(dataMin, dataMax, valueAxis);
        var valueFormat = valueAxis?.FormatCode is { } code && code != "General" ? code : percent ? "0%" : chart.Series[0].FormatCode ?? "General";

        var ticks = new List<double>();
        for (var tick = minimum; tick <= maximum + unit * 1e-6; tick += unit)
        {
            ticks.Add(Math.Abs(tick) < unit * 1e-9 ? 0 : tick);
        }

        var categoryAxis = chart.CategoryAxis;
        var showValueLabels = valueAxis is { Deleted: false, ShowLabels: true };
        var showCategoryLabels = categoryAxis is not { Deleted: true } && categoryAxis?.ShowLabels != false;
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
            var categoryWidth = showCategoryLabels && chart.Categories.Count > 0 ? chart.Categories.Max(_ => TextWidth(_, categoryStyle)) : 0;
            var categoryHeight = showCategoryLabels ? LineHeight("0", categoryStyle) : 0;
            var left = horizontal ? categoryWidth : valueWidth;
            var bottom = horizontal ? valueHeight : categoryHeight;
            var leftReserve = left > 0 ? left + labelGap : 0;
            var bottomReserve = bottom > 0 ? bottom + labelGap : 0;

            // Half a label overhangs each end of the axis that carries them along its length.
            var top = horizontal ? 0 : showValueLabels ? valueHeight / 2 : 0;
            var right = horizontal && showValueLabels ? TextWidth(valueLabels[^1], valueStyle) / 2 : 0;
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

        if (chart.Kind == ChartKind.Bar)
        {
            Bars(chart, categoryCount, band, stacked, percent, BandStart, ValueAt, horizontal, minimum, items);
        }
        else
        {
            Lines(chart, categoryCount, band, BandStart, ValueAt, horizontal, items);
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
                    items.AddRange(Label(text, categoryStyle, start, plot.Y + plot.Height + labelGap, band, labelHeight, TextAlignment.Center));
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

                var value = percent && total > 0 ? raw / total : raw;
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
                if (horizontal && !stacked)
                {
                    across = groupStart + (float) ((slots - 1 - slot) * barWidth * (1 - overlap));
                }

                var start = valueAt(from);
                var end = valueAt(to);
                var fill = PointColor(chart, series, index, category);
                items.Add(horizontal
                    ? Rectangle(Math.Min(start, end), across, Math.Abs(end - start), barWidth, fill, series.Line)
                    : Rectangle(across, Math.Min(start, end), barWidth, Math.Abs(end - start), fill, series.Line));

                if (series.Labels is { } labels && !labels.Deleted.Contains(category))
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

    static void Lines(ChartModel chart, int categoryCount, float band, Func<int, float> bandStart, Func<double, float> valueAt, bool horizontal, List<PlacedItem> items)
    {
        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var stroke = series.Line ?? new ChartStroke(series.FillHex ?? chart.Palette[index % chart.Palette.Count], 2.25, 1);
            (float X, float Y)? previous = null;
            for (var category = 0; category < categoryCount; category++)
            {
                if (category >= series.Values.Count || series.Values[category] is not { } value)
                {
                    previous = null;
                    continue;
                }

                var along = bandStart(category) + band / 2;
                var point = horizontal ? (valueAt(value), along) : (along, valueAt(value));
                if (previous is { } from)
                {
                    items.Add(Rule(from.X, from.Y, point.Item1, point.Item2, stroke));
                }

                previous = point;
            }
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
            if (series.Labels is { } dataLabels && !dataLabels.Deleted.Contains(point))
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
            : [.. chart.Series.Select((_, index) => (Text: _.Name ?? $"Series{index + 1}", Color: _.FillHex ?? _.Line?.ColorHex ?? chart.Palette[index % chart.Palette.Count]))];

        // Horizontal bars stack their series upwards, the first at the bottom of each category, and the
        // legend lists them the same way round: chart_anchored's North and South bars are keyed
        // "South, North" in Word's render. A legend lists them by series everywhere else.
        if (!byCategory && chart is
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
        // 10pt legend keys its 3pt lines with 20pt strokes in Word's render. Anything else takes a square
        // swatch of its fill.
        var lineKeys = chart.Kind == ChartKind.Line && !byCategory;
        var keyWidth = lineKeys ? (float) (style.SizePoints * 2) : key;
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
                var stroke = (float) (chart.Series[index].Line?.WidthPoints ?? 2.25);
                items.Add(Rectangle(x, y + (lineHeight - stroke) / 2, keyWidth, stroke, entries[index].Color, null));
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
        var maximum = axis?.Maximum ?? (dataMaximum <= 0 && dataMinimum < 0 ? 0 : dataMaximum);
        if (maximum <= minimum)
        {
            maximum = minimum + 1;
        }

        var padded = maximum;
        var paddedMinimum = minimum;
        if (axis?.Maximum == null && dataMaximum > 0)
        {
            padded = maximum + 0.05 * (maximum - minimum);
        }

        if (axis?.Minimum == null && dataMinimum < 0)
        {
            paddedMinimum = minimum - 0.05 * (maximum - minimum);
        }

        var unit = axis?.MajorUnit is > 0 ? axis.MajorUnit.Value : NiceUnit(padded - paddedMinimum);
        if (axis?.Maximum == null)
        {
            maximum = Math.Ceiling(padded / unit - 1e-9) * unit;
        }

        if (axis?.Minimum == null && dataMinimum < 0)
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
        if (labels.ShowCategory && point < chart.Categories.Count)
        {
            parts.Add(chart.Categories[point]);
        }

        if (labels.ShowValue)
        {
            parts.Add(NumberFormat.Format(value, labels.FormatCode is { } own && own != "General" ? own : sourceFormat).Text);
        }

        if (labels.ShowPercent && total > 0)
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
            style with { SizePoints = style.SizePoints * scale };

        ChartStroke? Stroke(ChartStroke? stroke) =>
            stroke == null ? null : stroke with { WidthPoints = stroke.WidthPoints * scale };

        ChartAxis? Axis(ChartAxis? axis) =>
            axis == null ? null : axis with { Text = Text(axis.Text), Line = Stroke(axis.Line), MajorGridlines = Stroke(axis.MajorGridlines) };

        return chart with
        {
            Text = Text(chart.Text),
            Title = chart.Title is { } title ? title with { Style = Text(title.Style) } : null,
            Legend = chart.Legend is { } legend ? legend with { Text = Text(legend.Text) } : null,
            CategoryAxis = Axis(chart.CategoryAxis),
            ValueAxis = Axis(chart.ValueAxis),
            Area = chart.Area with { Line = Stroke(chart.Area.Line) },
            Plot = chart.Plot with { Line = Stroke(chart.Plot.Line) },
            Series = [.. chart.Series
                .Select(_ => _ with
                {
                    Line = Stroke(_.Line),
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
