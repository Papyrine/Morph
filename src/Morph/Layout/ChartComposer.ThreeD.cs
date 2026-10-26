/// <summary>
/// 3-D charts and surface charts. A 3-D bar, line, area or surface chart is drawn in an oblique frame:
/// its front face is upright and undistorted, and its depth recedes up and to the right. PowerPoint's
/// 3-D clustered column (rotX 15, rotY 20) moves its depth sin(rotY) across and sin(rotX) up for each
/// unit of depth, a unit being one category's width: its 125px-deep floor recedes 43px across and 32px
/// up, against 45 and 30 measured, and its bars' 50px-deep tops 17 and 13 against 16 and 13
/// (chart-3d-column). A chart that turns its axes off right angles (c:rAngAx 0) PowerPoint draws in
/// perspective; it is drawn in the same oblique frame here, which keeps its front face square.
///
/// Faces are shaded as if lit from the front and above: a box's top a quarter darker than its front,
/// its side two fifths. A surface is split by value into bands at the value axis's major unit, each in
/// its band's colour, and a 2-D surface chart is the same bands seen from above as a contour plot.
/// </summary>
sealed partial class ChartComposer
{
    const double topShade = -0.25;
    const double sideShade = -0.4;

    // A surface's pieces are outlined in their own colour, this wide, so the antialiased edges where two
    // meet do not show the page through as a hairline.
    const double seam = 0.5;

    /// <summary>
    /// The oblique frame: the front face's bottom-left corner, the width of one category, the length the
    /// value axis runs, and how far the depth moves across and up for each category's width of depth.
    /// A horizontal bar chart runs its categories up the front face and its values across it.
    /// </summary>
    readonly record struct Frame(float Left, float Bottom, float Band, float Length, double DepthX, double DepthY, bool Horizontal)
    {
        /// <summary>
        /// The page point of a place in the chart: so many categories across, a fraction of the way up the
        /// value axis, and so many categories' widths back.
        /// </summary>
        public (float X, float Y) At(double across, double up, double back)
        {
            var depthX = back * Band * DepthX;
            var depthY = back * Band * DepthY;
            if (Horizontal)
            {
                return ((float) (Left + up * Length + depthX), (float) (Bottom - across * Band - depthY));
            }

            return ((float) (Left + across * Band + depthX), (float) (Bottom - up * Length - depthY));
        }
    }

    /// <summary>
    /// A 3-D bar, line, area or surface chart. Clustered bars stand in one row; a standard 3-D chart, and
    /// every 3-D line, area or surface, stands each series in a row of its own, the first at the front.
    /// </summary>
    void Plot3D(ChartModel chart, (float X, float Y, float Width, float Height) inner, float chartX, float chartY, float chartWidth, float chartHeight, List<PlacedItem> items)
    {
        var view = chart.View3D!;
        var categoryCount = Math.Max(chart.Categories.Count, chart.Series.Max(_ => _.Values.Count));
        if (categoryCount == 0)
        {
            return;
        }

        var surface = chart.Kind == ChartKind.Surface;
        var rows = view.SeriesInRows ? chart.Series.Count : 1;
        var stacked = Stacked(chart);
        var percent = Percent(chart);
        var (dataMin, dataMax) = DataRange(chart, categoryCount, stacked, percent);
        var valueAxis = chart.ValueAxis;
        var (minimum, maximum, unit) = Scale(dataMin, dataMax, valueAxis, false, true);
        var ticks = Ticks(minimum, maximum, unit);
        var valueFormat = AxisFormat(valueAxis, percent ? "0%" : chart.Series[0].FormatCode);
        var valueLabels = ticks.Select(_ => NumberFormat.Format(_, valueFormat).Text).ToList();
        var valueStyle = valueAxis?.Text ?? chart.Text;
        var categoryAxis = chart.CategoryAxis;
        var categoryStyle = categoryAxis?.Text ?? chart.Text;
        var seriesAxis = chart.SeriesAxis;
        var seriesStyle = seriesAxis?.Text ?? chart.Text;
        var showValueLabels = valueAxis is { Deleted: false, ShowLabels: true };
        var showCategoryLabels = categoryAxis is not { Deleted: true } &&
                                 categoryAxis?.ShowLabels != false &&
                                 chart.Categories.Count > 0;
        var showSeriesLabels = rows > 1 &&
                               seriesAxis is { Deleted: false, ShowLabels: true };
        var horizontal = chart is
        {
            Kind: ChartKind.Bar,
            Horizontal: true
        };

        // Points on the category ticks (an area's, a surface's) span one category fewer than bands do.
        var onTicks = (chart.PointsOnTicks || surface) &&
                      categoryCount > 1;
        var span = onTicks ? categoryCount - 1 : categoryCount;
        double Across(int category) =>
            onTicks ? category : category + 0.5;

        // How deep each row is, in categories' widths: a bar as deep as it is wide with the gap depth
        // behind it, a line's ribbon or an area's slab a whole category.
        var gap = Math.Max(0, chart.GapWidthPercent / 100);
        var slots = rows > 1 || stacked ? 1 : chart.Series.Count;
        var overlap = slots == 1 ? 1 : Math.Clamp(chart.OverlapPercent / 100, -1, 1);
        var barWidth = 1 / (slots - (slots - 1) * overlap + gap);
        var gapDepth = Math.Max(0, view.GapDepthPercent / 100);
        var rowDepth = chart.Kind == ChartKind.Bar ? barWidth * (1 + gapDepth) : 1;
        var thickness = chart.Kind == ChartKind.Bar ? barWidth : 1 / (1 + gapDepth);
        var depthScale = Math.Max(0.2, view.DepthPercent / 100);
        var depth = (surface ? Math.Max(1, rows - 1) : rows * rowDepth) * depthScale;
        var depthX = Math.Sin(view.RotateY * Math.PI / 180);
        var depthY = Math.Sin(view.RotateX * Math.PI / 180);

        var seriesLabels = chart.Series.Select((_, index) => _.Name ?? $"Series {index + 1}").ToList();
        var plot = chart.PlotLayout is { } manual ? Absolute(manual, chartX, chartY, chartWidth, chartHeight) : inner;
        if (!chart.PlotLayoutIsInner || chart.PlotLayout == null)
        {
            var valueWidth = showValueLabels ? valueLabels.Max(_ => TextWidth(_, valueStyle)) + labelGap : 0;
            var valueHeight = showValueLabels ? LineHeight("0", valueStyle) : 0;
            var categoryWidth = showCategoryLabels ? chart.Categories.Max(_ => TextWidth(_, categoryStyle)) + labelGap : 0;
            var categoryHeight = showCategoryLabels ? LineHeight("0", categoryStyle) + labelGap : 0;
            var seriesWidth = showSeriesLabels ? seriesLabels.Max(_ => TextWidth(_, seriesStyle)) + labelGap * 2 : 0;
            if (horizontal)
            {
                plot = (plot.X + categoryWidth, plot.Y, plot.Width - categoryWidth - seriesWidth, plot.Height - valueHeight - labelGap);
            }
            else
            {
                plot = (plot.X + valueWidth, plot.Y + valueHeight / 2, plot.Width - valueWidth - seriesWidth, plot.Height - categoryHeight - valueHeight / 2);
            }
        }

        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        // The front face takes what the depth leaves of the plot.
        Frame frame;
        if (horizontal)
        {
            var band = (float) (plot.Height / (span + depth * depthY));
            var length = (float) (plot.Width - depth * band * depthX);
            frame = new(plot.X, plot.Y + plot.Height, band, length, depthX, depthY, true);
        }
        else
        {
            var band = (float) (plot.Width / (span + depth * depthX));
            var length = (float) (plot.Height - depth * band * depthY);
            frame = new(plot.X, plot.Y + plot.Height, band, length, depthX, depthY, false);
        }

        if (frame.Length <= 0 || frame.Band <= 0)
        {
            return;
        }

        double Up(double value)
        {
            var fraction = (value - minimum) / (maximum - minimum);
            if (valueAxis?.Reversed == true)
            {
                fraction = 1 - fraction;
            }

            return Math.Clamp(fraction, 0, 1);
        }

        // The walls and floor behind it all, then the value axis's gridlines across them.
        if (view.Walls.FillHex != null || view.Walls.Line != null)
        {
            AddShape(items, Polygon([frame.At(0, 0, depth), frame.At(span, 0, depth), frame.At(span, 1, depth), frame.At(0, 1, depth)], view.Walls.FillHex, view.Walls.Line));
            AddShape(items, Polygon([frame.At(0, 0, 0), frame.At(0, 0, depth), frame.At(0, 1, depth), frame.At(0, 1, 0)], view.Walls.FillHex, view.Walls.Line));
        }

        if (view.Floor.FillHex != null || view.Floor.Line != null)
        {
            AddShape(items, Polygon([frame.At(0, 0, 0), frame.At(span, 0, 0), frame.At(span, 0, depth), frame.At(0, 0, depth)], view.Floor.FillHex, view.Floor.Line));
        }

        if (valueAxis?.MajorGridlines is { } gridlines)
        {
            foreach (var tick in ticks)
            {
                var up = Up(tick);
                var front = frame.At(0, up, 0);
                var corner = frame.At(0, up, depth);
                var back = frame.At(span, up, depth);
                items.Add(Rule(front.X, front.Y, corner.X, corner.Y, gridlines));
                items.Add(Rule(corner.X, corner.Y, back.X, back.Y, gridlines));
            }
        }

        if (categoryAxis is { Deleted: false, Line: { } categoryLine })
        {
            var from = frame.At(0, Up(Math.Clamp(0, minimum, maximum)), 0);
            var to = frame.At(span, Up(Math.Clamp(0, minimum, maximum)), 0);
            items.Add(Rule(from.X, from.Y, to.X, to.Y, categoryLine));
        }

        switch (chart.Kind)
        {
            case ChartKind.Bar:
                Bars3D(chart, frame, categoryCount, rows, barWidth, rowDepth * depthScale, thickness * depthScale, gapDepth, gap, slots, overlap, stacked, percent, Up, minimum, items);
                break;
            case ChartKind.Area:
                Areas3D(chart, frame, categoryCount, rows, rowDepth * depthScale, thickness * depthScale, stacked, percent, Across, Up, minimum, items);
                break;
            case ChartKind.Surface:
                Surface3D(chart, frame, categoryCount, depth, Up, minimum, maximum, unit, items);
                break;
            default:
                Lines3D(chart, frame, categoryCount, rows, rowDepth * depthScale, thickness * depthScale, Across, Up, items);
                break;
        }

        if (showValueLabels)
        {
            var labelHeight = LineHeight("0", valueStyle);
            for (var index = 0; index < ticks.Count; index++)
            {
                var at = frame.At(0, Up(ticks[index]), 0);
                var labelWidth = TextWidth(valueLabels[index], valueStyle) + 2;
                items.AddRange(horizontal
                    ? Label(valueLabels[index], valueStyle, at.X - labelWidth / 2, at.Y + labelGap, labelWidth, labelHeight, TextAlignment.Center)
                    : Label(valueLabels[index], valueStyle, at.X - labelGap - labelWidth, at.Y - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Right));
            }
        }

        if (showCategoryLabels)
        {
            var labelHeight = LineHeight("0", categoryStyle);
            for (var category = 0; category < Math.Min(categoryCount, chart.Categories.Count); category++)
            {
                var text = chart.Categories[category];
                var at = frame.At(Across(category), 0, 0);
                var labelWidth = TextWidth(text, categoryStyle) + 2;
                items.AddRange(horizontal
                    ? Label(text, categoryStyle, at.X - labelGap - labelWidth, at.Y - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Right)
                    : Label(text, categoryStyle, at.X - labelWidth / 2, at.Y + labelGap, labelWidth, labelHeight, TextAlignment.Center));
            }
        }

        if (showSeriesLabels)
        {
            // The depth axis's labels run back along the floor's right edge, one beside each row.
            var labelHeight = LineHeight("0", seriesStyle);
            for (var row = 0; row < rows; row++)
            {
                var back = surface ? row * depth / Math.Max(1, rows - 1) : (row + 0.5) * rowDepth * depthScale;
                var at = frame.At(span, 0, back);
                var labelWidth = TextWidth(seriesLabels[row], seriesStyle) + 2;
                items.AddRange(Label(seriesLabels[row], seriesStyle, at.X + labelGap * 2, at.Y - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Left));
            }
        }
    }

    // A 3-D chart's bars as boxes: front, top and side, the rows from the back and the categories from the
    // left (or from the bottom, running across), so each box is drawn over the ones behind it.
    static void Bars3D(
        ChartModel chart,
        Frame frame,
        int categoryCount,
        int rows,
        double barWidth,
        double rowDepth,
        double thickness,
        double gapDepth,
        double gap,
        int slots,
        double overlap,
        bool stacked,
        bool percent,
        Func<double, double> up,
        double minimum,
        List<PlacedItem> items)
    {
        var baseline = Math.Max(0, minimum);
        for (var row = rows - 1; row >= 0; row--)
        {
            var front = row * rowDepth + thickness * gapDepth / 2;
            var back = front + thickness;
            for (var category = 0; category < categoryCount; category++)
            {
                var total = percent ? chart.Series.Sum(_ => Math.Abs(At(_, category))) : 1;
                var below = baseline;
                for (var index = 0; index < chart.Series.Count; index++)
                {
                    if (rows > 1 && index != row)
                    {
                        continue;
                    }

                    var series = chart.Series[index];
                    if (category >= series.Values.Count || series.Values[category] is not { } raw)
                    {
                        continue;
                    }

                    var value = percent && total > 0 ? raw / total : raw;
                    var from = stacked ? below : baseline;
                    var to = stacked ? below + value : value;
                    if (stacked)
                    {
                        below = to;
                    }

                    var slot = slots == 1 ? 0 : index;
                    var left = category + barWidth * gap / 2 + slot * barWidth * (1 - overlap);
                    Box(frame, left, left + barWidth, up(Math.Min(from, to)), up(Math.Max(from, to)), front, back, PointColor(chart, series, index, category), items);
                }
            }
        }
    }

    // A box's three faces that face the viewer: its front, its top, and its right side.
    static void Box(Frame frame, double left, double right, double bottom, double top, double front, double back, string color, List<PlacedItem> items)
    {
        AddShape(items, Polygon([frame.At(right, bottom, front), frame.At(right, bottom, back), frame.At(right, top, back), frame.At(right, top, front)], Shade(color, sideShade), null));
        AddShape(items, Polygon([frame.At(left, top, front), frame.At(right, top, front), frame.At(right, top, back), frame.At(left, top, back)], Shade(color, topShade), null));
        AddShape(items, Polygon([frame.At(left, bottom, front), frame.At(right, bottom, front), frame.At(right, top, front), frame.At(left, top, front)], color, null));
    }

    // A 3-D line chart's series as ribbons, each lying in its row: a rising stretch faces away from the
    // light and is shaded as a box's top, a falling one faces it and keeps the series' colour.
    static void Lines3D(ChartModel chart, Frame frame, int categoryCount, int rows, double rowDepth, double thickness, Func<int, double> across, Func<double, double> up, List<PlacedItem> items)
    {
        for (var index = chart.Series.Count - 1; index >= 0; index--)
        {
            var series = chart.Series[index];
            var row = rows > 1 ? index : 0;
            var front = row * rowDepth + (rowDepth - thickness) / 2;
            var back = front + thickness;
            var color = SeriesColor(chart, series, index);
            for (var category = 1; category < categoryCount; category++)
            {
                if (category >= series.Values.Count ||
                    series.Values[category - 1] is not { } from ||
                    series.Values[category] is not { } to)
                {
                    continue;
                }

                var x0 = across(category - 1);
                var x1 = across(category);
                var fill = to > from ? Shade(color, topShade) : color;
                AddShape(items, Polygon([frame.At(x0, up(from), front), frame.At(x1, up(to), front), frame.At(x1, up(to), back), frame.At(x0, up(from), back)], fill, null));
            }
        }
    }

    // A 3-D area chart's series as slabs: the top of each slab, then its right end, then its front face,
    // the rows from the back. Stacked series share a row, each standing on the one before.
    static void Areas3D(ChartModel chart, Frame frame, int categoryCount, int rows, double rowDepth, double thickness, bool stacked, bool percent, Func<int, double> across, Func<double, double> up, double minimum, List<PlacedItem> items)
    {
        var baseline = Math.Max(0, minimum);
        var below = new double[categoryCount];
        Array.Fill(below, baseline);
        var order = rows > 1 ? Enumerable.Range(0, chart.Series.Count).Reverse() : Enumerable.Range(0, chart.Series.Count);
        foreach (var index in order)
        {
            var series = chart.Series[index];
            var row = rows > 1 ? index : 0;
            var front = row * rowDepth + (rowDepth - thickness) / 2;
            var back = front + thickness;
            var color = SeriesColor(chart, series, index);
            var top = new double[categoryCount];
            for (var category = 0; category < categoryCount; category++)
            {
                var value = At(series, category);
                if (percent)
                {
                    var total = chart.Series.Sum(_ => Math.Abs(At(_, category)));
                    value = total > 0 ? value / total : 0;
                }

                top[category] = stacked ? below[category] + value : value;
            }

            var bottom = stacked ? below : Enumerable.Repeat(baseline, categoryCount).ToArray();
            for (var category = 1; category < categoryCount; category++)
            {
                AddShape(items, Polygon([frame.At(across(category - 1), up(top[category - 1]), front), frame.At(across(category), up(top[category]), front), frame.At(across(category), up(top[category]), back), frame.At(across(category - 1), up(top[category - 1]), back)], Shade(color, topShade), null));
            }

            var last = categoryCount - 1;
            AddShape(items, Polygon([frame.At(across(last), up(bottom[last]), front), frame.At(across(last), up(bottom[last]), back), frame.At(across(last), up(top[last]), back), frame.At(across(last), up(top[last]), front)], Shade(color, sideShade), null));

            var face = new List<(float X, float Y)>();
            for (var category = 0; category < categoryCount; category++)
            {
                face.Add(frame.At(across(category), up(top[category]), front));
            }

            for (var category = last; category >= 0; category--)
            {
                face.Add(frame.At(across(category), up(bottom[category]), front));
            }

            AddShape(items, Polygon(face, color, null));
            if (stacked)
            {
                below = top;
            }
        }
    }

    // A 3-D surface: each cell of the grid of categories and series split into two triangles, and each
    // triangle into the parts of it in each band, the cells from the back.
    static void Surface3D(ChartModel chart, Frame frame, int categoryCount, double depth, Func<double, double> up, double minimum, double maximum, double unit, List<PlacedItem> items)
    {
        var rows = chart.Series.Count;
        var back = rows > 1 ? depth / (rows - 1) : depth;
        var bands = Bands(chart, minimum, maximum, unit);
        for (var row = rows - 2; row >= 0; row--)
        {
            for (var category = 0; category + 1 < categoryCount; category++)
            {
                foreach (var triangle in CellTriangles(chart, row, category))
                {
                    foreach (var (low, high, color) in bands)
                    {
                        var piece = Clip(triangle, low, high);
                        if (piece.Count >= 3)
                        {
                            AddShape(items, Polygon([.. piece.Select(_ => frame.At(_.Across, up(_.Value), _.Row * back))], chart.Wireframe ? null : color, new ChartStroke(color, chart.Wireframe ? 0.75 : seam, 1)));
                        }
                    }
                }
            }
        }
    }

    // A 2-D surface chart: the same bands seen from straight above, as a contour plot, with the categories
    // along the bottom and the series up the right.
    void Contour(ChartModel chart, (float X, float Y, float Width, float Height) inner, float chartX, float chartY, float chartWidth, float chartHeight, List<PlacedItem> items)
    {
        var categoryCount = Math.Max(chart.Categories.Count, chart.Series.Max(_ => _.Values.Count));
        var rows = chart.Series.Count;
        if (categoryCount < 2 || rows < 2)
        {
            return;
        }

        var (dataMin, dataMax) = DataRange(chart, categoryCount, false, false);
        var (minimum, maximum, unit) = Scale(dataMin, dataMax, chart.ValueAxis, false);
        var categoryStyle = chart.CategoryAxis?.Text ?? chart.Text;
        var seriesStyle = chart.SeriesAxis?.Text ?? chart.Text;
        var seriesLabels = chart.Series.Select((_, index) => _.Name ?? $"Series {index + 1}").ToList();
        var plot = chart.PlotLayout is { } manual ? Absolute(manual, chartX, chartY, chartWidth, chartHeight) : inner;
        if (!chart.PlotLayoutIsInner || chart.PlotLayout == null)
        {
            var categoryHeight = LineHeight("0", categoryStyle);
            var seriesWidth = seriesLabels.Max(_ => TextWidth(_, seriesStyle)) + labelGap;
            var firstWidth = chart.Categories.Count > 0 ? TextWidth(chart.Categories[0], categoryStyle) / 2 : 0;
            var seriesHeight = LineHeight("0", seriesStyle);
            plot = (plot.X + firstWidth, plot.Y + seriesHeight / 2, plot.Width - firstWidth - seriesWidth, plot.Height - categoryHeight - labelGap - seriesHeight / 2);
        }

        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        (float X, float Y) At(double across, double row) =>
            ((float) (plot.X + across / (categoryCount - 1) * plot.Width), (float) (plot.Y + plot.Height - row / (rows - 1) * plot.Height));

        var bands = Bands(chart, minimum, maximum, unit);
        for (var row = 0; row + 1 < rows; row++)
        {
            for (var category = 0; category + 1 < categoryCount; category++)
            {
                foreach (var triangle in CellTriangles(chart, row, category))
                {
                    foreach (var (low, high, color) in bands)
                    {
                        var piece = Clip(triangle, low, high);
                        if (piece.Count >= 3)
                        {
                            AddShape(items, Polygon([.. piece.Select(_ => At(_.Across, _.Row))], color, new ChartStroke(color, seam, 1)));
                        }
                    }
                }
            }
        }

        var gridlines = chart.ValueAxis?.MajorGridlines ?? chart.CategoryAxis?.Line;
        if (gridlines != null)
        {
            for (var category = 0; category < categoryCount; category++)
            {
                var (x, _) = At(category, 0);
                items.Add(Rule(x, plot.Y, x, plot.Y + plot.Height, gridlines));
            }

            for (var row = 0; row < rows; row++)
            {
                var (_, y) = At(0, row);
                items.Add(Rule(plot.X, y, plot.X + plot.Width, y, gridlines));
            }
        }

        if (chart.CategoryAxis is not { Deleted: true })
        {
            var labelHeight = LineHeight("0", categoryStyle);
            for (var category = 0; category < Math.Min(categoryCount, chart.Categories.Count); category++)
            {
                var text = chart.Categories[category];
                var (x, _) = At(category, 0);
                var labelWidth = TextWidth(text, categoryStyle) + 2;
                items.AddRange(Label(text, categoryStyle, x - labelWidth / 2, plot.Y + plot.Height + labelGap, labelWidth, labelHeight, TextAlignment.Center));
            }
        }

        if (chart.SeriesAxis is not { Deleted: true })
        {
            var labelHeight = LineHeight("0", seriesStyle);
            for (var row = 0; row < rows; row++)
            {
                var (_, y) = At(0, row);
                var labelWidth = TextWidth(seriesLabels[row], seriesStyle) + 2;
                items.AddRange(Label(seriesLabels[row], seriesStyle, plot.X + plot.Width + labelGap, y - labelHeight / 2, labelWidth, labelHeight, TextAlignment.Left));
            }
        }
    }

    /// <summary>
    /// A surface chart's bands: one for each major unit of its value axis, from the lowest, each in its
    /// <c>c:bandFmt</c> colour or else the theme's accents in turn, darker on each pass through them.
    /// </summary>
    internal static List<(double Low, double High, string Color)> Bands(ChartModel chart, double minimum, double maximum, double unit)
    {
        var bands = new List<(double Low, double High, string Color)>();
        var index = 0;
        for (var low = minimum; low < maximum - unit * 1e-6; low += unit, index++)
        {
            var color = chart.BandFills.TryGetValue(index, out var fill)
                ? fill
                : Shade(chart.Palette[index % chart.Palette.Count], -0.25 * (index / chart.Palette.Count));
            bands.Add((low, low + unit, color));
        }

        return bands;
    }

    // The two triangles a cell of a surface splits into, between two categories and two series; a cell
    // with a missing corner draws nothing.
    static IEnumerable<List<(double Across, double Row, double Value)>> CellTriangles(ChartModel chart, int row, int category)
    {
        double? Corner(int r, int c) =>
            c < chart.Series[r].Values.Count ? chart.Series[r].Values[c] : null;

        if (Corner(row, category) is not { } a ||
            Corner(row, category + 1) is not { } b ||
            Corner(row + 1, category + 1) is not { } c ||
            Corner(row + 1, category) is not { } d)
        {
            yield break;
        }

        yield return [(category, row, a), (category + 1, row, b), (category + 1, row + 1, c)];
        yield return [(category, row, a), (category + 1, row + 1, c), (category, row + 1, d)];
    }

    // The part of a triangle whose value lies between low and high, the value varying linearly across it:
    // clipped against each bound in turn (Sutherland-Hodgman).
    static List<(double Across, double Row, double Value)> Clip(List<(double Across, double Row, double Value)> polygon, double low, double high)
    {
        static List<(double Across, double Row, double Value)> Keep(List<(double Across, double Row, double Value)> points, Func<double, bool> inside, double bound)
        {
            var kept = new List<(double Across, double Row, double Value)>();
            for (var index = 0; index < points.Count; index++)
            {
                var current = points[index];
                var next = points[(index + 1) % points.Count];
                if (inside(current.Value))
                {
                    kept.Add(current);
                }

                if (inside(current.Value) != inside(next.Value))
                {
                    var t = (bound - current.Value) / (next.Value - current.Value);
                    kept.Add((current.Across + t * (next.Across - current.Across), current.Row + t * (next.Row - current.Row), bound));
                }
            }

            return kept;
        }

        var above = Keep(polygon, _ => _ >= low, low);
        return above.Count < 3 ? above : Keep(above, _ => _ <= high, high);
    }

    // A 3-D pie: the top an ellipse squashed by the tilt (sin rotX: PowerPoint's 30-degree pie stands
    // 422px tall on a 900px width), standing on a rim as deep as an eighth of its width foreshortened by
    // cos rotX (100px under that pie). The rim shows only on the near half, each slice's part of it shaded
    // as a box's side.
    static void Pie3D(ChartModel chart, (float X, float Y, float Width, float Height) box, List<PlacedItem> items)
    {
        var view = chart.View3D!;
        var series = chart.Series[0];
        var values = series.Values.Select(_ => _ is > 0 ? _.Value : 0).ToList();
        var total = values.Sum();
        var tilt = view.RotateX * Math.PI / 180;
        var squash = (float) Math.Max(0.1, Math.Sin(tilt));
        var rim = 0.128f * (float) Math.Cos(tilt) * (float) (view.DepthPercent / 100);

        // The pie's width as the box allows: its height is the squashed top plus the rim.
        var width = Math.Min(box.Width, box.Height / (squash + rim));
        if (total <= 0 || width <= 0)
        {
            return;
        }

        var height = width * squash;
        var depth = width * rim;
        var left = box.X + (box.Width - width) / 2;
        var top = box.Y + (box.Height - height - depth) / 2;

        (float X, float Y) Rim(double degrees, float drop)
        {
            var radians = degrees * Math.PI / 180;
            return (left + width * (float) (0.5 + 0.5 * Math.Sin(radians)), top + drop + height * (float) (0.5 - 0.5 * Math.Cos(radians)));
        }

        var angle = chart.FirstSliceDegrees;
        var sides = new List<PlacedItem>();
        var tops = new List<PlacedItem>();
        for (var point = 0; point < values.Count; point++)
        {
            if (values[point] <= 0)
            {
                continue;
            }

            var sweep = values[point] / total * 360;
            var fill = PointColor(chart, series, 0, point);

            // The near half of the rim runs from three o'clock round through six to nine, on the slice's
            // first turn of the circle or, for a slice that started past twelve, its second.
            for (var turn = 0; turn <= 360; turn += 360)
            {
                var start = Math.Max(angle, 90 + turn);
                var end = Math.Min(angle + sweep, 270 + turn);
                if (end <= start)
                {
                    continue;
                }

                var steps = Math.Max(2, (int) Math.Ceiling((end - start) / 2));
                var outline = new List<(float X, float Y)>();
                for (var step = 0; step <= steps; step++)
                {
                    outline.Add(Rim(start + (end - start) * step / steps, 0));
                }

                for (var step = steps; step >= 0; step--)
                {
                    outline.Add(Rim(start + (end - start) * step / steps, depth));
                }

                AddShape(sides, Polygon(outline, Shade(fill, sideShade), null));
            }

            var outlineStroke = series.PointLines.GetValueOrDefault(point) ?? series.Line;
            tops.Add(new PlacedShape(
                left,
                top,
                width,
                height,
                new()
                {
                    WidthPoints = width,
                    HeightPoints = height,
                    FillColorHex = fill,
                    LineColorHex = outlineStroke?.ColorHex,
                    LineWidthPoints = outlineStroke?.WidthPoints,
                    Subpaths = [SliceContour(angle, sweep, 0)]
                }));
            angle += sweep;
        }

        items.AddRange(sides);
        items.AddRange(tops);
    }

    // Adds a shape that may have come out empty (a polygon with no area).
    static void AddShape(List<PlacedItem> items, PlacedShape? shape)
    {
        if (shape != null)
        {
            items.Add(shape);
        }
    }
}
