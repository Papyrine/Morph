/// <summary>
/// A chart (<c>c:chartSpace</c>) as <see cref="ChartComposer"/> draws it: the plot's kind, the cached
/// series values, and the look of its axes, legend, title and labels. <see cref="ChartReader"/> builds it
/// from a chart part; the chart is drawn at layout time, where label widths can be measured.
/// </summary>
sealed record ChartModel
{
    public required ChartKind Kind { get; init; }

    /// <summary><c>c:barDir="bar"</c>: bars run across from a vertical category axis.</summary>
    public bool Horizontal { get; init; }

    public ChartGrouping Grouping { get; init; }

    /// <summary><c>c:gapWidth</c>: the gap between category groups, as a percent of one bar.</summary>
    public double GapWidthPercent { get; init; } = 150;

    /// <summary><c>c:overlap</c>: how far the bars of one category overlap, as a percent of one bar.</summary>
    public double OverlapPercent { get; init; }

    /// <summary><c>c:holeSize</c>: a doughnut's hole, as a percent of its diameter.</summary>
    public double HolePercent { get; init; }

    /// <summary><c>c:firstSliceAng</c>: where a pie's first slice starts, clockwise from twelve.</summary>
    public double FirstSliceDegrees { get; init; }

    /// <summary><c>c:varyColors</c>: each point of a single series takes the next theme accent.</summary>
    public bool VaryColors { get; init; }

    public required IReadOnlyList<string> Categories { get; init; }

    public required IReadOnlyList<ChartSeries> Series { get; init; }

    public ChartAxis? CategoryAxis { get; init; }

    public ChartAxis? ValueAxis { get; init; }

    public ChartLegend? Legend { get; init; }

    public ChartText? Title { get; init; }

    /// <summary>The plot area's manual layout, as fractions of the chart; null lays it out automatically.</summary>
    public ChartRect? PlotLayout { get; init; }

    /// <summary><c>c:layoutTarget="inner"</c>: <see cref="PlotLayout"/> is the axes' box, not the box with their labels.</summary>
    public bool PlotLayoutIsInner { get; init; }

    public ChartFill Area { get; init; } = new(null, null);

    public ChartFill Plot { get; init; } = new(null, null);

    /// <summary>The chart's own text defaults (<c>c:chartSpace/c:txPr</c>), under every label.</summary>
    public required ChartTextStyle Text { get; init; }

    /// <summary>The theme's six accents, which colour a series or point that names no colour, in turn.</summary>
    public required IReadOnlyList<string> Palette { get; init; }
}

enum ChartKind
{
    Bar,
    Line,
    Pie,
    Doughnut
}

enum ChartGrouping
{
    Clustered,
    Stacked,
    PercentStacked
}

sealed record ChartSeries
{
    public string? Name { get; init; }

    /// <summary>The cached values by point index; null where the cache has no point.</summary>
    public required IReadOnlyList<double?> Values { get; init; }

    /// <summary>The format the values are cached in (<c>c:numCache/c:formatCode</c>), which a source-linked label shows them in.</summary>
    public string? FormatCode { get; init; }

    public string? FillHex { get; init; }

    public ChartStroke? Line { get; init; }

    /// <summary>Per-point fills (<c>c:dPt</c>).</summary>
    public IReadOnlyDictionary<int, string> PointFills { get; init; } = new Dictionary<int, string>();

    public ChartDataLabels? Labels { get; init; }
}

sealed record ChartDataLabels
{
    public bool ShowValue { get; init; }

    public bool ShowPercent { get; init; }

    public bool ShowCategory { get; init; }

    public string? FormatCode { get; init; }

    public required ChartTextStyle Text { get; init; }

    /// <summary>Points whose label is deleted (<c>c:dLbl/c:delete</c>).</summary>
    public IReadOnlySet<int> Deleted { get; init; } = new HashSet<int>();

    /// <summary>A point's label moved off its default place, as fractions of the chart (<c>c:dLbl/c:layout</c>).</summary>
    public IReadOnlyDictionary<int, (double X, double Y)> Offsets { get; init; } = new Dictionary<int, (double X, double Y)>();

    /// <summary>
    /// A point's label given a box of its own, as fractions of the chart (<c>c15:layout</c> in the
    /// label's extension list, Office 2013's resizable labels). The text is centred in the box.
    /// </summary>
    public IReadOnlyDictionary<int, (double Width, double Height)> Sizes { get; init; } = new Dictionary<int, (double Width, double Height)>();
}

sealed record ChartAxis
{
    /// <summary><c>c:delete</c>: the axis draws nothing, though it still scales the plot.</summary>
    public bool Deleted { get; init; }

    /// <summary><c>c:orientation="maxMin"</c>.</summary>
    public bool Reversed { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? MajorUnit { get; init; }

    /// <summary>Whether tick labels are drawn (<c>c:tickLblPos</c> other than none).</summary>
    public bool ShowLabels { get; init; } = true;

    public string? FormatCode { get; init; }

    public required ChartTextStyle Text { get; init; }

    public ChartStroke? Line { get; init; }

    public ChartStroke? MajorGridlines { get; init; }
}

sealed record ChartLegend(ChartLegendPosition Position, ChartRect? Layout, ChartTextStyle Text);

enum ChartLegendPosition
{
    Right,
    Left,
    Top,
    Bottom
}

sealed record ChartText(string Text, ChartTextStyle Style);

sealed record ChartTextStyle(string FontFamily, double SizePoints, string ColorHex, bool Bold, bool Italic);

sealed record ChartStroke(string ColorHex, double WidthPoints, double Alpha);

sealed record ChartFill(string? FillHex, ChartStroke? Line);

readonly record struct ChartRect(double X, double Y, double Width, double Height);
