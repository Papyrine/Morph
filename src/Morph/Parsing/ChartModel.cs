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

    /// <summary>
    /// <c>c:crossBetween="midCat"</c> on the value axis of a line or area chart: its points sit on the
    /// category axis's ticks, the first at the plot's edge, rather than in the middle of each category.
    /// </summary>
    public bool PointsOnTicks { get; init; }

    /// <summary><c>c:radarStyle="filled"</c>: each radar series is a filled polygon rather than a line.</summary>
    public bool RadarFilled { get; init; }

    /// <summary><c>c:varyColors</c>: each point of a single series takes the next theme accent.</summary>
    public bool VaryColors { get; init; }

    /// <summary>
    /// The plots after the first in a combination chart (a column chart with a line over it), each drawn
    /// over this one's categories in the order the plot area lists them. An overlay keeps its own kind,
    /// grouping and series, and its own value axis where it is <see cref="Secondary"/>.
    /// </summary>
    public IReadOnlyList<ChartModel> Overlays { get; init; } = [];

    /// <summary>An overlay plotted against the secondary value axis, which stands at the plot's right.</summary>
    public bool Secondary { get; init; }

    /// <summary>A 3-D chart's view (<c>c:view3D</c> with <c>c:floor</c>, <c>c:sideWall</c>, <c>c:backWall</c>); null for a flat one.</summary>
    public ChartView3D? View3D { get; init; }

    /// <summary>A 3-D chart's depth axis (<c>c:serAx</c>), which labels the series front to back.</summary>
    public ChartAxis? SeriesAxis { get; init; }

    /// <summary><c>c:wireframe</c>: a surface chart draws its mesh's lines, not its bands.</summary>
    public bool Wireframe { get; init; }

    /// <summary>A surface chart's band colours (<c>c:bandFmts</c>), by band from the lowest; a band not listed takes the theme's accents in turn.</summary>
    public IReadOnlyDictionary<int, string> BandFills { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// <c>c:bubbleScale</c>: the largest bubble's size, as a percent of Office's default, which makes its
    /// diameter a quarter of the plot's shorter side.
    /// </summary>
    public double BubbleScalePercent { get; init; } = 100;

    /// <summary><c>c:sizeRepresents="w"</c>: a bubble's size is its diameter rather than its area.</summary>
    public bool BubbleSizeIsWidth { get; init; }

    /// <summary><c>c:bubble3D</c>: bubbles are shaded as spheres.</summary>
    public bool Bubble3D { get; init; }

    /// <summary>A stock chart's high-low lines (<c>c:hiLowLines</c>), from each category's highest value to its lowest.</summary>
    public ChartStroke? HighLowLines { get; init; }

    /// <summary>A stock chart's up-down bars (<c>c:upDownBars</c>), from its first series to its last.</summary>
    public ChartUpDownBars? UpDownBars { get; init; }

    public required IReadOnlyList<string> Categories { get; init; }

    public required IReadOnlyList<ChartSeries> Series { get; init; }

    /// <summary>The category axis; for a scatter chart, the horizontal value axis that carries the x values.</summary>
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
    Doughnut,
    Area,
    Scatter,
    Radar,
    Bubble,
    Stock,
    Surface
}

/// <summary>
/// A 3-D chart's view (<c>c:view3D</c>): its tilt back (<c>c:rotX</c>, degrees) and turn (<c>c:rotY</c>),
/// its perspective's field of view (<c>c:perspective</c>, half-degrees), and whether its axes stay at
/// right angles (<c>c:rAngAx</c>), which projects it obliquely without perspective. Depth and height are
/// percents of the plot's width (<c>c:depthPercent</c>, <c>c:hPercent</c>); the gap between series rows
/// (<c>c:gapDepth</c>) a percent of a bar. Clustered 3-D bars stand in one row; standard ones (and lines,
/// areas and surfaces) stand one row per series, front to back.
/// </summary>
sealed record ChartView3D
{
    public double RotateX { get; init; } = 15;

    public double RotateY { get; init; } = 20;

    public double Perspective { get; init; } = 30;

    public bool RightAngleAxes { get; init; } = true;

    public double DepthPercent { get; init; } = 100;

    public double? HeightPercent { get; init; }

    public double GapDepthPercent { get; init; } = 150;

    /// <summary>Series stand in rows, one behind another (a standard 3-D chart, and every 3-D line, area or surface).</summary>
    public bool SeriesInRows { get; init; }

    public ChartFill Floor { get; init; } = new(null, null);

    public ChartFill Walls { get; init; } = new(null, null);
}

sealed record ChartUpDownBars(double GapWidthPercent, ChartFill Up, ChartFill Down);

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

    /// <summary>A scatter series' x values (<c>c:xVal</c>); empty where they are not numbers, which plots the points at 1, 2, 3…</summary>
    public IReadOnlyList<double?> XValues { get; init; } = [];

    /// <summary>The series' line is turned off (<c>c:spPr/a:ln/a:noFill</c>): a scatter or line series of markers alone.</summary>
    public bool NoLine { get; init; }

    /// <summary>The marker drawn at each point of a line, scatter or radar series; null draws none.</summary>
    public ChartMarker? Marker { get; init; }

    /// <summary>The format the values are cached in (<c>c:numCache/c:formatCode</c>), which a source-linked label shows them in.</summary>
    public string? FormatCode { get; init; }

    /// <summary>A bubble series' sizes (<c>c:bubbleSize</c>), by point.</summary>
    public IReadOnlyList<double?> BubbleSizes { get; init; } = [];

    /// <summary>
    /// The series' place in the chart (<c>c:idx</c>), which picks its theme accent when it names no colour
    /// of its own. A combination chart's line plot numbers its series on from the column plot's, so its
    /// first line takes the second accent, not the first.
    /// </summary>
    public int? PaletteIndex { get; init; }

    public string? FillHex { get; init; }

    /// <summary>The fill's opacity (<c>a:alpha</c>): a bubble chart's bubbles are 75% opaque.</summary>
    public double FillAlpha { get; init; } = 1;

    public ChartStroke? Line { get; init; }

    /// <summary>Per-point fills (<c>c:dPt</c>).</summary>
    public IReadOnlyDictionary<int, string> PointFills { get; init; } = new Dictionary<int, string>();

    /// <summary>Per-point outlines (<c>c:dPt/c:spPr/a:ln</c>), over the series' own <see cref="Line"/>.</summary>
    public IReadOnlyDictionary<int, ChartStroke> PointLines { get; init; } = new Dictionary<int, ChartStroke>();

    public ChartDataLabels? Labels { get; init; }
}

/// <summary>
/// A point's marker (<c>c:marker</c>): its <c>c:symbol</c> (circle, square, diamond, triangle, x, plus,
/// star, dash or dot), its size in points, and its fill and outline.
/// </summary>
sealed record ChartMarker(string Symbol, double SizePoints, string? FillHex, ChartStroke? Line);

sealed record ChartDataLabels
{
    public bool ShowValue { get; init; }

    public bool ShowPercent { get; init; }

    public bool ShowCategory { get; init; }

    public string? FormatCode { get; init; }

    /// <summary>
    /// Where a label stands against its point (<c>c:dLblPos</c>: t, b, l, r, ctr, outEnd, inEnd, inBase,
    /// bestFit); null takes the kind's own place — right of a line or scatter point, centred in an area's
    /// band, out along a radar's spoke, past a bar's end.
    /// </summary>
    public string? Position { get; init; }

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

/// <summary>
/// A title's text and look, and where its manual layout puts it (<c>c:title/c:layout</c>): its box's top-left
/// as fractions of the chart, null where the title sits in its own place above the plot.
/// </summary>
sealed record ChartText(string Text, ChartTextStyle Style, ChartRect? Layout = null);

sealed record ChartTextStyle(string FontFamily, double SizePoints, string ColorHex, bool Bold, bool Italic);

sealed record ChartStroke(string ColorHex, double WidthPoints, double Alpha);

sealed record ChartFill(string? FillHex, ChartStroke? Line);

readonly record struct ChartRect(double X, double Y, double Width, double Height);
