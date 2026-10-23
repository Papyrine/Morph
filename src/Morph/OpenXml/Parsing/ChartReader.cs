using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Reads a chart part (<c>c:chartSpace</c>) into a <see cref="ChartModel"/>: the first bar, line, pie or
/// doughnut plot of its plot area, the series' cached values and categories, and the look of the axes,
/// legend, title and data labels. Elements are found by local name, so a chart written with literals
/// (<c>c:numLit</c>) reads as one written with references, and one Excel wraps in
/// <c>mc:AlternateContent</c> as one it does not.
///
/// Absent text formatting takes Excel's chart style defaults: the theme's minor font, 9pt labels and
/// 14pt titles, in 65% black (595959).
/// </summary>
sealed class ChartReader(ThemeColors? themeColors, ThemeFonts? themeFonts)
{
    const string defaultTextColor = "595959";

    // Office 2013's chart extensions, where a resized data label keeps its box (c15:layout).
    const string c15Namespace = "http://schemas.microsoft.com/office/drawing/2012/chart";

    // The Office theme's accents, for a package with no theme.
    static readonly string[] defaultPalette = ["4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47"];

    public ChartModel? Read(OpenXmlElement? chartSpace)
    {
        var chart = Child(chartSpace, "chart");
        var plotArea = Child(chart, "plotArea");
        var plot = plotArea?.Elements().FirstOrDefault(_ => _.LocalName is "barChart" or "lineChart" or "pieChart" or "doughnutChart");
        if (chartSpace == null || plotArea == null || plot == null)
        {
            return null;
        }

        var text = TextStyle(Child(chartSpace, "txPr"), new(themeFonts?.MinorFont ?? "Calibri", 10, defaultTextColor, false, false));
        var labelText = text with {SizePoints = Child(chartSpace, "txPr") == null ? 9 : text.SizePoints};

        var kind = plot.LocalName switch
        {
            "lineChart" => ChartKind.Line,
            "pieChart" => ChartKind.Pie,
            "doughnutChart" => ChartKind.Doughnut,
            _ => ChartKind.Bar
        };

        var grouping = Attribute(Child(plot, "grouping"), "val") switch
        {
            "stacked" => ChartGrouping.Stacked,
            "percentStacked" => ChartGrouping.PercentStacked,
            _ => ChartGrouping.Clustered
        };

        var series = new List<ChartSeries>();
        IReadOnlyList<string> categories = [];
        foreach (var ser in plot.Elements().Where(_ => _.LocalName == "ser"))
        {
            if (categories.Count == 0)
            {
                categories = Categories(Child(ser, "cat"));
            }

            series.Add(Series(ser, labelText));
        }

        if (series.Count == 0)
        {
            return null;
        }

        var axes = plotArea.Elements().Where(_ => _.LocalName is "catAx" or "valAx" or "dateAx").ToList();
        var layout = Child(Child(plotArea, "layout"), "manualLayout");
        var autoTitleDeleted = On(Child(chart, "autoTitleDeleted"));

        return new()
        {
            Kind = kind,
            Horizontal = Attribute(Child(plot, "barDir"), "val") == "bar",
            Grouping = grouping,
            GapWidthPercent = Value(Child(plot, "gapWidth")) ?? 150,
            OverlapPercent = Value(Child(plot, "overlap")) ?? (grouping == ChartGrouping.Clustered ? 0 : 100),
            HolePercent = kind == ChartKind.Doughnut ? Value(Child(plot, "holeSize")) ?? 10 : 0,
            FirstSliceDegrees = Value(Child(plot, "firstSliceAng")) ?? 0,
            VaryColors = On(Child(plot, "varyColors")),
            Categories = categories,
            Series = series,
            CategoryAxis = axes.FirstOrDefault(_ => _.LocalName is "catAx" or "dateAx") is { } categoryAxis ? Axis(categoryAxis, labelText) : null,
            ValueAxis = axes.FirstOrDefault(_ => _.LocalName == "valAx") is { } valueAxis ? Axis(valueAxis, labelText) : null,
            Legend = Child(chart, "legend") is { } legend ? Legend(legend, labelText) : null,
            Title = Child(chart, "title") is { } title && !autoTitleDeleted ? Title(title, text, series) : null,
            PlotLayout = Rect(layout),
            PlotLayoutIsInner = Attribute(Child(layout, "layoutTarget"), "val") == "inner",
            Area = Fill(Child(chartSpace, "spPr"), defaultFill: "FFFFFF", defaultLine: new("D9D9D9", 0.75, 1)),
            Plot = Fill(Child(plotArea, "spPr"), defaultFill: null, defaultLine: null),
            Text = text,
            Palette = Enumerable.Range(1, 6).Select(_ => themeColors?.ResolveColor($"accent{_}") ?? defaultPalette[_ - 1]).ToList()
        };
    }

    ChartSeries Series(OpenXmlElement ser, ChartTextStyle labelText)
    {
        var properties = Child(ser, "spPr");
        var pointFills = new Dictionary<int, string>();
        var pointLines = new Dictionary<int, ChartStroke>();
        foreach (var point in ser.Elements().Where(_ => _.LocalName == "dPt"))
        {
            if (Value(Child(point, "idx")) is not { } index)
            {
                continue;
            }

            if (SolidColor(Child(point, "spPr")) is { } fill)
            {
                pointFills[(int) index] = fill;
            }

            if (Stroke(Child(Child(point, "spPr"), "ln")) is { } line)
            {
                pointLines[(int) index] = line;
            }
        }

        return new()
        {
            Name = Child(ser, "tx") is { } name ? CachedStrings(name).FirstOrDefault() ?? Child(name, "v")?.InnerText : null,
            Values = Numbers(Child(ser, "val") ?? Child(ser, "yVal")),
            FormatCode = (Child(ser, "val") ?? Child(ser, "yVal"))?.Descendants().FirstOrDefault(_ => _.LocalName == "formatCode")?.InnerText,
            FillHex = SolidColor(properties),
            Line = Stroke(Child(properties, "ln")),
            PointFills = pointFills,
            PointLines = pointLines,
            Labels = Child(ser, "dLbls") is { } labels ? DataLabels(labels, labelText) : null
        };
    }

    ChartDataLabels? DataLabels(OpenXmlElement labels, ChartTextStyle labelText)
    {
        var deleted = new HashSet<int>();
        var offsets = new Dictionary<int, (double X, double Y)>();
        var sizes = new Dictionary<int, (double Width, double Height)>();
        foreach (var label in labels.Elements().Where(_ => _.LocalName == "dLbl"))
        {
            if (Value(Child(label, "idx")) is not { } index)
            {
                continue;
            }

            if (On(Child(label, "delete")))
            {
                deleted.Add((int) index);
                continue;
            }

            if (Child(Child(label, "layout"), "manualLayout") is { } manual)
            {
                offsets[(int) index] = (Value(Child(manual, "x")) ?? 0, Value(Child(manual, "y")) ?? 0);
            }

            var sized = Child(label, "extLst")?
                .Elements()
                .SelectMany(_ => _.Elements())
                .FirstOrDefault(_ => _ is
                {
                    LocalName: "layout",
                    NamespaceUri: c15Namespace
                });
            if (Child(sized, "manualLayout") is { } size &&
                Value(Child(size, "w")) is > 0 and var width &&
                Value(Child(size, "h")) is > 0 and var height)
            {
                sizes[(int) index] = (width, height);
            }
        }

        // A point's own dLbl can carry the flags and text the series' block leaves out; the block's own
        // settles the rest.
        var styled = labels.Elements().FirstOrDefault(_ => _.LocalName == "dLbl" && Child(_, "txPr") != null);
        var result = new ChartDataLabels
        {
            ShowValue = Flag(labels, "showVal") || labels.Elements().Any(_ => _.LocalName == "dLbl" && Flag(_, "showVal")),
            ShowPercent = Flag(labels, "showPercent"),
            ShowCategory = Flag(labels, "showCatName"),
            FormatCode = Attribute(Child(labels, "numFmt"), "formatCode"),
            Text = TextStyle(Child(labels, "txPr") ?? Child(styled, "txPr"), labelText),
            Deleted = deleted,
            Offsets = offsets,
            Sizes = sizes
        };

        if (result is
            {
                ShowValue: false,
                ShowPercent: false,
                ShowCategory: false
            })
        {
            return null;
        }

        return result;
    }

    ChartAxis Axis(OpenXmlElement axis, ChartTextStyle labelText)
    {
        var scaling = Child(axis, "scaling");
        return new()
        {
            Deleted = On(Child(axis, "delete")),
            Reversed = Attribute(Child(scaling, "orientation"), "val") == "maxMin",
            Minimum = Value(Child(scaling, "min")),
            Maximum = Value(Child(scaling, "max")),
            MajorUnit = Value(Child(axis, "majorUnit")),
            ShowLabels = Attribute(Child(axis, "tickLblPos"), "val") != "none",
            FormatCode = Attribute(Child(axis, "numFmt"), "formatCode"),
            Text = TextStyle(Child(axis, "txPr"), labelText),
            Line = Stroke(Child(Child(axis, "spPr"), "ln")),
            MajorGridlines = Child(axis, "majorGridlines") is { } gridlines
                ? Stroke(Child(Child(gridlines, "spPr"), "ln")) ?? new ChartStroke("D9D9D9", 0.75, 1)
                : null
        };
    }

    ChartLegend Legend(OpenXmlElement legend, ChartTextStyle labelText)
    {
        var position = Attribute(Child(legend, "legendPos"), "val") switch
        {
            "l" => ChartLegendPosition.Left,
            "t" => ChartLegendPosition.Top,
            "b" => ChartLegendPosition.Bottom,
            _ => ChartLegendPosition.Right
        };
        return new(position, Rect(Child(Child(legend, "layout"), "manualLayout")), TextStyle(Child(legend, "txPr"), labelText));
    }

    ChartText? Title(OpenXmlElement title, ChartTextStyle text, List<ChartSeries> series)
    {
        var rich = Child(Child(title, "tx"), "rich");
        var style = TextStyle(Child(title, "txPr") ?? rich, text with {SizePoints = 14});
        var content = rich != null
            ? string.Join("\n", rich.Elements().Where(_ => _.LocalName == "p").Select(_ => string.Concat(_.Descendants<A.Text>().Select(run => run.Text))))
            : series.Count == 1
                ? series[0].Name
                : null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new(content, style, Rect(Child(Child(title, "layout"), "manualLayout")));
    }

    // The default run properties of a text body (c:txPr, or a rich title's first paragraph) over what the
    // label inherits.
    ChartTextStyle TextStyle(OpenXmlElement? body, ChartTextStyle inherited)
    {
        var defaults = body?.Descendants().FirstOrDefault(_ => _.LocalName == "defRPr");
        if (defaults == null)
        {
            return inherited;
        }

        var size = Attribute(defaults, "sz") is { } hundredths && double.TryParse(hundredths, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed / 100
            : inherited.SizePoints;
        var family = Attribute(Child(defaults, "latin"), "typeface") switch
        {
            null or "" => inherited.FontFamily,
            "+mn-lt" => themeFonts?.MinorFont ?? inherited.FontFamily,
            "+mj-lt" => themeFonts?.MajorFont ?? inherited.FontFamily,
            { } named => named
        };

        return new(
            family,
            size,
            SolidColor(defaults) ?? inherited.ColorHex,
            Attribute(defaults, "b") is { } bold ? bold == "1" : inherited.Bold,
            Attribute(defaults, "i") is { } italic ? italic == "1" : inherited.Italic);
    }

    ChartFill Fill(OpenXmlElement? properties, string? defaultFill, ChartStroke? defaultLine)
    {
        if (properties == null)
        {
            return new(defaultFill, defaultLine);
        }

        return new(SolidColor(properties), Stroke(Child(properties, "ln")));
    }

    // An outline's colour and width; null when it draws nothing.
    ChartStroke? Stroke(OpenXmlElement? line)
    {
        if (line == null ||
            Child(line, "noFill") != null ||
            Child(line, "solidFill") is not A.SolidFill solid ||
            ShapeParser.ExtractSolidFillColor(solid, themeColors) is not { } color)
        {
            return null;
        }

        var width = Attribute(line, "w") is { } emus && long.TryParse(emus, out var parsed) ? parsed / OoxmlUnits.EmusPerPoint : 0.75;
        return new(color, width, ShapeParser.ExtractSolidFillAlpha(solid));
    }

    string? SolidColor(OpenXmlElement? properties) =>
        Child(properties, "solidFill") is { } solid ? ShapeParser.ExtractSolidFillColor(solid, themeColors) : null;

    static IReadOnlyList<double?> Numbers(OpenXmlElement? values)
    {
        var cache = values?.Descendants().FirstOrDefault(_ => _.LocalName is "numCache" or "numLit");
        if (cache == null)
        {
            return [];
        }

        var count = (int) (Value(Child(cache, "ptCount")) ?? 0);
        var points = new SortedDictionary<int, double>();
        foreach (var point in cache.Elements().Where(_ => _.LocalName == "pt"))
        {
            if (int.TryParse(Attribute(point, "idx"), out var index) &&
                double.TryParse(Child(point, "v")?.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                points[index] = number;
            }
        }

        var length = Math.Max(count, points.Count == 0 ? 0 : points.Keys.Max() + 1);
        var result = new double?[length];
        foreach (var (index, number) in points)
        {
            result[index] = number;
        }

        return result;
    }

    // A category axis's labels: cached strings, or cached numbers in their own format.
    static IReadOnlyList<string> Categories(OpenXmlElement? categories)
    {
        if (categories == null)
        {
            return [];
        }

        var strings = CachedStrings(categories);
        if (strings.Count > 0)
        {
            return strings;
        }

        var cache = categories.Descendants().FirstOrDefault(_ => _.LocalName is "numCache" or "numLit");
        var format = Child(cache, "formatCode")?.InnerText;
        return Numbers(categories).Select(_ => _ is { } number ? NumberFormat.Format(number, format).Text : "").ToList();
    }

    static List<string> CachedStrings(OpenXmlElement holder)
    {
        var cache = holder.Descendants().FirstOrDefault(_ => _.LocalName is "strCache" or "strLit" or "multiLvlStrCache");
        if (cache == null)
        {
            return [];
        }

        // A multi-level cache lists its innermost level first, which is the one the axis labels.
        var level = cache.LocalName == "multiLvlStrCache" ? Child(cache, "lvl") ?? cache : cache;
        var count = (int) (Value(Child(cache, "ptCount")) ?? 0);
        var result = new string[count];
        Array.Fill(result, "");
        foreach (var point in level.Elements().Where(_ => _.LocalName == "pt"))
        {
            if (int.TryParse(Attribute(point, "idx"), out var index) && index >= 0)
            {
                if (index >= result.Length)
                {
                    Array.Resize(ref result, index + 1);
                    result[index] = "";
                }

                result[index] = Child(point, "v")?.InnerText ?? "";
            }
        }

        return [.. result.Select(_ => _ ?? "")];
    }

    static ChartRect? Rect(OpenXmlElement? manual)
    {
        if (manual == null ||
            Value(Child(manual, "x")) is not { } x ||
            Value(Child(manual, "y")) is not { } y)
        {
            return null;
        }

        return new(x, y, Value(Child(manual, "w")) ?? 1 - x, Value(Child(manual, "h")) ?? 1 - y);
    }

    static bool Flag(OpenXmlElement? parent, string localName) =>
        On(Child(parent, localName));

    // A CT_Boolean: present with no val means on.
    static bool On(OpenXmlElement? element) =>
        element != null && (Value(element) ?? 1) == 1;

    static OpenXmlElement? Child(OpenXmlElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(_ => _.LocalName == localName);

    static string? Attribute(OpenXmlElement? element, string localName)
    {
        if (element == null)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == localName)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    static double? Value(OpenXmlElement? element)
    {
        if (Attribute(element, "val") is not { } text)
        {
            return null;
        }

        if (text is "true")
        {
            return 1;
        }

        if (text is "false")
        {
            return 0;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return null;
    }
}
