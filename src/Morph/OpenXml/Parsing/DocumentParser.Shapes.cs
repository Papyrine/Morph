using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

/// <summary>
/// DrawingML shapes: text boxes, filled shapes, lines and WordArt.
/// </summary>
sealed partial class DocumentParser
{
    /// <summary>
    /// A pie or doughnut chart as an inline group of its slices: each slice a filled outline in the plot
    /// circle's box, coloured and stroked as its <c>c:dPt</c> says (or from the theme's accents in turn
    /// when the chart varies its colours and the point names none). Slices run clockwise from
    /// <c>c:firstSliceAng</c> degrees past twelve o'clock, each a share of the circle proportional to its
    /// cached value; the plot circle is the largest that fits the plot area's manual layout (the whole
    /// chart when there is none), and a doughnut's hole is <c>c:holeSize</c> percent of its diameter.
    /// business-plans/12's SWOT ring is the case: four quarters around a half-width hole. Null for any
    /// other chart kind.
    /// </summary>
    Run? PieSliceRun(OpenXmlPartRootElement? chartSpace, long extentWidth, long extentHeight, RunProperties runProps)
    {
        var plotArea = chartSpace?.Descendants().FirstOrDefault(_ => _.LocalName == "plotArea");
        var chart = plotArea?.Elements().FirstOrDefault(_ => _.LocalName is "doughnutChart" or "pieChart");
        var series = chart?.Elements().FirstOrDefault(_ => _.LocalName == "ser");
        if (plotArea == null ||
            chart == null ||
            series == null)
        {
            return null;
        }

        static OpenXmlElement? Child(OpenXmlElement? parent, string localName) =>
            parent?.Elements().FirstOrDefault(_ => _.LocalName == localName);

        static double? Value(OpenXmlElement? element)
        {
            if (element?.GetAttributes().FirstOrDefault(_ => _.LocalName == "val").Value is { } text &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            return null;
        }

        // The cached values, by point index.
        var values = new SortedDictionary<int, double>();
        foreach (var point in Child(series, "val")?.Descendants().Where(_ => _.LocalName == "pt") ?? [])
        {
            if (int.TryParse(point.GetAttributes().FirstOrDefault(_ => _.LocalName == "idx").Value, out var pointIndex) &&
                double.TryParse(Child(point, "v")?.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var pointValue) &&
                pointValue > 0)
            {
                values[pointIndex] = pointValue;
            }
        }

        var total = values.Values.Sum();
        if (total <= 0)
        {
            return null;
        }

        var pointProperties = new Dictionary<int, OpenXmlElement>();
        foreach (var dataPoint in series.Elements().Where(_ => _.LocalName == "dPt"))
        {
            if (Value(Child(dataPoint, "idx")) is { } dataPointIndex &&
                Child(dataPoint, "spPr") is { } properties)
            {
                pointProperties[(int) dataPointIndex] = properties;
            }
        }

        // The plot area's manual layout, as fractions of the chart; the whole chart without one.
        var layout = Child(Child(plotArea, "layout"), "manualLayout");
        var plotX = (Value(Child(layout, "x")) ?? 0) * extentWidth;
        var plotY = (Value(Child(layout, "y")) ?? 0) * extentHeight;
        var plotWidth = (Value(Child(layout, "w")) ?? 1) * extentWidth;
        var plotHeight = (Value(Child(layout, "h")) ?? 1) * extentHeight;
        var diameter = Math.Min(plotWidth, plotHeight);
        if (diameter <= 0)
        {
            return null;
        }

        var circleX = plotX + (plotWidth - diameter) / 2;
        var circleY = plotY + (plotHeight - diameter) / 2;
        var hole = chart.LocalName == "doughnutChart" ? Math.Clamp((Value(Child(chart, "holeSize")) ?? 10) / 100, 0, 0.9) : 0;
        var varyColors = Value(Child(chart, "varyColors")) != 0;
        var seriesProperties = Child(series, "spPr");

        var shapes = new List<GroupShape>();
        var angle = Value(Child(chart, "firstSliceAng")) ?? 0;
        foreach (var (pointIndex, pointValue) in values)
        {
            var sweep = pointValue / total * 360;
            var properties = pointProperties.GetValueOrDefault(pointIndex) ?? seriesProperties;
            var fill = ExtractFirstFillColor(Child(properties, "solidFill"))
                       ?? (varyColors ? currentThemeColors?.ResolveColor($"accent{pointIndex % 6 + 1}") : null)
                       ?? currentThemeColors?.ResolveColor("accent1")
                       ?? "4472C4";
            var outline = Child(properties, "ln");
            var stroked = outline != null &&
                          Child(outline, "noFill") == null;
            shapes.Add(
                new()
                {
                    X = circleX,
                    Y = circleY,
                    Width = diameter,
                    Height = diameter,
                    Geometry = GroupShapeGeometry.Rectangle,
                    Subpaths = [SliceContour(angle, sweep, hole)],
                    FillColorHex = fill,
                    ColorHex = stroked ? ExtractFirstFillColor(Child(outline, "solidFill")) ?? "FFFFFF" : "FFFFFF",
                    LineWidthEmu = stroked &&
                                   long.TryParse(outline!.GetAttributes().FirstOrDefault(_ => _.LocalName == "w").Value, out var lineWidth) ? lineWidth : 0
                });
            angle += sweep;
        }

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageWidthPoints = extentWidth / OoxmlUnits.EmusPerPoint,
            InlineImageHeightPoints = extentHeight / OoxmlUnits.EmusPerPoint,
            InlineShapeGroup = new()
            {
                ChildExtentX = extentWidth,
                ChildExtentY = extentHeight,
                Shapes = shapes
            }
        };
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

    /// <summary>
    /// Where a member of an inline group stands in the group's own child space, and what its outline
    /// is there. A member inside nested <c>wpg:grpSp</c> groups is declared in the innermost one's
    /// child space, and each group on the way out scales that into its own box and turns it about
    /// its centre — newsletters/03's banner hexagons sit in a nested group drawn at 65% (one of them
    /// turned half way round). A turn — the groups', or
    /// the member's own <c>a:xfrm/@rot</c> — is baked into the outline, so the painters need know
    /// nothing of it: the box becomes the turned outline's bounds, and the outline is restated
    /// inside that. A picture cannot be turned this way and keeps its upright box at the place the
    /// groups put its centre.
    /// </summary>
    static (double X, double Y, double Width, double Height, IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Subpaths, bool FlipHorizontal, bool FlipVertical)
        PlaceGroupMember(OpenXmlElement element, A.Transform2D xfrm, GroupShapeGeometry geometry, IReadOnlyList<IReadOnlyList<(double X, double Y)>>? subpaths, bool turnable)
    {
        var flipHorizontal = xfrm.HorizontalFlip?.Value == true;
        var flipVertical = xfrm.VerticalFlip?.Value == true;
        var (x, y, width, height, groupDegrees, mirrored) = AccumulatedTransform.Of(element, 0, 0, 1, 1, composeFlips: true)
            .MapRectangle(xfrm.Offset?.X ?? 0, xfrm.Offset?.Y ?? 0, xfrm.Extents?.Cx ?? 0, xfrm.Extents?.Cy ?? 0);
        var ownDegrees = (xfrm.Rotation?.Value ?? 0) / 60000.0;

        static bool IsTurn(double degrees) => Math.Abs(Math.IEEERemainder(degrees, 360)) > 0.01;

        if (!turnable ||
            (!mirrored &&
             !IsTurn(groupDegrees) &&
             !IsTurn(ownDegrees)))
        {
            return (x, y, width, height, subpaths, flipHorizontal, flipVertical);
        }

        // A point of the member, measured from its centre: its own flips, its own turn, then the
        // groups' mirror and turn.
        var ownRadians = ownDegrees * Math.PI / 180;
        var groupRadians = groupDegrees * Math.PI / 180;

        (double X, double Y) Turn(double pointX, double pointY)
        {
            if (flipHorizontal)
            {
                pointX = -pointX;
            }

            if (flipVertical)
            {
                pointY = -pointY;
            }

            (pointX, pointY) = (pointX * Math.Cos(ownRadians) - pointY * Math.Sin(ownRadians), pointX * Math.Sin(ownRadians) + pointY * Math.Cos(ownRadians));
            if (mirrored)
            {
                pointY = -pointY;
            }

            return (pointX * Math.Cos(groupRadians) - pointY * Math.Sin(groupRadians), pointX * Math.Sin(groupRadians) + pointY * Math.Cos(groupRadians));
        }

        var centerX = x + width / 2;
        var centerY = y + height / 2;

        // A line is its two ends, and stays a line: the flips say which way it now runs.
        if (subpaths == null &&
            geometry == GroupShapeGeometry.Line)
        {
            var start = Turn(-width / 2, -height / 2);
            var end = Turn(width / 2, height / 2);
            return (
                centerX + Math.Min(start.X, end.X),
                centerY + Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y),
                null,
                end.X < start.X,
                end.Y < start.Y);
        }

        var turned = (subpaths ?? [PrimitiveContour(geometry)])
            .Select(_ => _.Select(point => Turn((point.X - 0.5) * width, (point.Y - 0.5) * height)).ToList())
            .ToList();
        var points = turned.SelectMany(_ => _).ToList();
        if (points.Count == 0)
        {
            return (x, y, width, height, subpaths, flipHorizontal, flipVertical);
        }

        var left = points.Min(_ => _.X);
        var top = points.Min(_ => _.Y);
        var turnedWidth = points.Max(_ => _.X) - left;
        var turnedHeight = points.Max(_ => _.Y) - top;
        if (turnedWidth <= 0 || turnedHeight <= 0)
        {
            return (x, y, width, height, subpaths, flipHorizontal, flipVertical);
        }

        return (
            centerX + left,
            centerY + top,
            turnedWidth,
            turnedHeight,
            [.. turned.Select(_ => (IReadOnlyList<(double X, double Y)>) [.. _.Select(point => ((point.X - left) / turnedWidth, (point.Y - top) / turnedHeight))])],
            false,
            false);
    }

    // A rect's or an ellipse's outline in the unit square, for a primitive that has to be turned.
    static IReadOnlyList<(double X, double Y)> PrimitiveContour(GroupShapeGeometry geometry)
    {
        if (geometry != GroupShapeGeometry.Ellipse)
        {
            return [(0, 0), (1, 0), (1, 1), (0, 1)];
        }

        const int segments = 72;
        var points = new List<(double X, double Y)>(segments);
        for (var index = 0; index < segments; index++)
        {
            var angle = 2 * Math.PI * index / segments;
            points.Add((0.5 + 0.5 * Math.Cos(angle), 0.5 + 0.5 * Math.Sin(angle)));
        }

        return points;
    }

    /// <summary>
    /// Resolves a group member's stroke — colour, width (EMU) and opacity. DrawingML layers the
    /// shape's own <c>a:ln</c> over the theme line style that <c>wps:style/a:lnRef/@idx</c> names,
    /// so an <c>a:ln</c> carrying only a colour still strokes, at the theme's width. An explicit
    /// <c>a:noFill</c> on the outline means no stroke; so does a shape with neither a width nor a
    /// resolvable <c>a:lnRef</c>.
    /// </summary>
    (string ColorHex, double WidthEmu, double Alpha, IReadOnlyList<double>? DashPattern) ReadGroupStroke(OpenXmlElement shapeProperties, A.LineReference? lineReference)
    {
        var outline = shapeProperties.GetFirstChild<A.Outline>();
        if (outline?.GetFirstChild<A.NoFill>() != null)
        {
            return ("000000", 0, 1, null);
        }

        var solidFill = outline?.GetFirstChild<A.SolidFill>();

        // The lnRef's own colour child overrides the theme line style's phClr placeholder, so it
        // is the colour to fall back on when the shape doesn't name one itself.
        var colorSource = (OpenXmlElement?) solidFill ?? lineReference;

        return (
            ExtractFirstFillColor(colorSource) ?? "000000",
            outline?.Width?.Value ?? ResolveLineReferenceWidthEmu(lineReference),
            solidFill != null ? ShapeParser.ExtractSolidFillAlpha(solidFill) : 1,
            MapPresetDashPattern(outline?.GetFirstChild<A.PresetDash>()?.Val));
    }

    /// <summary>
    /// Reads a group member's <c>a:effectLst/a:outerShdw</c>. <c>@dist</c> is the offset length in
    /// EMU and <c>@dir</c> its angle in 60,000ths of a degree, measured clockwise from the +x axis
    /// in screen space (y grows downward), so the two resolve straight into an x/y offset. A shadow
    /// with no distance is a blur halo rather than a drop shadow, and we have nothing to offset.
    /// </summary>
    GroupShadow? ReadOuterShadow(OpenXmlElement shapeProperties)
    {
        var shadow = shapeProperties.GetFirstChild<A.EffectList>()?.GetFirstChild<A.OuterShadow>();
        if (shadow?.Distance?.Value is not { } distance || distance <= 0)
        {
            return null;
        }

        var radians = (shadow.Direction?.Value ?? 0) / 60000.0 * Math.PI / 180.0;

        // Word writes shadow colours as <a:prstClr val="black">. Only the monochrome presets show
        // up in practice, so the rest fall back to black rather than carrying a 140-name table.
        var colorHex = ExtractFirstFillColor(shadow);
        if (colorHex == null &&
            shadow.GetFirstChild<A.PresetColor>()?.Val?.Value is { } preset)
        {
            colorHex = preset == A.PresetColorValues.White ? "FFFFFF" : "000000";
        }

        var alpha = shadow.Descendants<A.Alpha>().FirstOrDefault()?.Val?.Value is { } opacity
            ? opacity / 100000.0
            : 1.0;

        return new()
        {
            OffsetX = distance * Math.Cos(radians),
            OffsetY = distance * Math.Sin(radians),
            ColorHex = colorHex ?? "000000",
            Alpha = alpha
        };
    }

    /// <summary>Stroke width an <c>a:lnRef/@idx</c> selects from the theme's 1-based <c>lnStyleLst</c>.</summary>
    long ResolveLineReferenceWidthEmu(A.LineReference? lineReference)
    {
        if (lineReference?.Index?.Value is not { } index || index == 0)
        {
            return 0;
        }

        var widths = (currentThemeColors ?? new()).LineStyleWidthsEmu;
        if (index < widths.Count)
        {
            return widths[(int) index];
        }

        return 0;
    }

    /// <summary>
    /// Reads the <c>a:prstGeom</c> of a group member's shape properties. Line, rect and ellipse
    /// cover icon-style groups; anything fancier (curves, arcs, custGeom) collapses to a rectangle
    /// so we render at least the bounding box.
    /// </summary>
    static GroupShapeGeometry MapGroupGeometry(OpenXmlElement shapeProperties)
    {
        var preset = shapeProperties.GetFirstChild<A.PresetGeometry>()?.Preset?.Value;
        if (preset == A.ShapeTypeValues.Line)
        {
            return GroupShapeGeometry.Line;
        }

        if (preset == A.ShapeTypeValues.Ellipse)
        {
            return GroupShapeGeometry.Ellipse;
        }

        return GroupShapeGeometry.Rectangle;
    }

    /// <summary>
    /// True when <paramref name="shape"/> shares a <c>wpg:wgp</c> with other shapes or pictures.
    /// Parsers that read the drawing-level <c>wp:extent</c> and treat the first <c>wps:wsp</c> as
    /// the whole drawing (WordArt, text boxes) must decline those: the extent describes the group,
    /// and claiming the drawing would drop the shape's siblings. Word's icon groups are exactly
    /// this — an oval carrying a hidden descriptive text box, with the icon picture on top.
    /// </summary>
    static bool HasGroupSiblings(OpenXmlElement shape) =>
        shape.Ancestors<WPG.WordprocessingGroup>().FirstOrDefault() is { } group &&
        GroupDrawables(group).Skip(1).Any();

    /// <summary>
    /// True when any nested <c>wpg:grpSp</c> in the drawing remaps its child space — offset,
    /// scale (chOff/chExt differing from off/ext) or rotation. ShapeParser's flat scaling is
    /// only valid without such remapping.
    /// </summary>
    static bool HasNonIdentityNestedGroup(Drawing drawing)
    {
        foreach (var nested in drawing.Descendants())
        {
            if (nested.LocalName != "grpSp")
            {
                continue;
            }

            var xfrm = nested.Elements().FirstOrDefault(_ => _.LocalName == "grpSpPr")
                ?.Elements().FirstOrDefault(_ => _.LocalName == "xfrm");
            if (xfrm == null)
            {
                continue;
            }

            if (xfrm.GetAttributes().AttributeValue("rot") is {Length: > 0})
            {
                return true;
            }

            var off = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "off");
            var ext = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "ext");
            var chOff = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chOff");
            var chExt = xfrm.Elements().FirstOrDefault(_ => _.LocalName == "chExt");
            if (off == null || ext == null || chOff == null || chExt == null)
            {
                continue;
            }

            var offAttributes = off.GetAttributes();
            var chOffAttributes = chOff.GetAttributes();
            var extAttributes = ext.GetAttributes();
            var chExtAttributes = chExt.GetAttributes();
            if (offAttributes.AttributeValue("x") != chOffAttributes.AttributeValue("x") ||
                offAttributes.AttributeValue("y") != chOffAttributes.AttributeValue("y") ||
                extAttributes.AttributeValue("cx") != chExtAttributes.AttributeValue("cx") ||
                extAttributes.AttributeValue("cy") != chExtAttributes.AttributeValue("cy"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The shapes and pictures a <c>wpg:wgp</c> paints, in document (back-to-front) order,
    /// flattening nested <c>wpg:grpSp</c> groups. Deliberately does not use <c>Descendants</c>:
    /// a shape's <c>wps:txbx</c> can hold its own drawings, which belong to the text box rather
    /// than to this group's coordinate space.
    /// </summary>
    static IEnumerable<OpenXmlElement> GroupDrawables(OpenXmlElement group)
    {
        foreach (var child in group.ChildElements)
        {
            switch (child)
            {
                case WPS.WordprocessingShape:
                case PIC.Picture:
                    yield return child;
                    break;
                case WPG.GroupShape nested:
                    foreach (var descendant in GroupDrawables(nested))
                    {
                        yield return descendant;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Wraps a single <c>prstGeom prst="line"</c> shape sitting directly under
    /// <c>a:graphicData</c> (no <c>wpg:wgp</c>) into a one-element <see cref="InlineShapeGroup"/>.
    /// The shape's own <c>a:xfrm</c> defines the line in its local 0..cx × 0..cy box, so the
    /// group's child coord space is just the wp:extent itself.
    /// </summary>
    Run? ParseInlineSingleLineRun(Drawing drawing, WPS.WordprocessingShape wsp, RunProperties runProps)
    {
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        if (extent?.Cx == null || extent.Cy == null)
        {
            return null;
        }

        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        var shapeXfrm = shapeProps?.GetFirstChild<A.Transform2D>();
        if (shapeProps == null || shapeXfrm?.Extents == null)
        {
            return null;
        }

        var ln = shapeProps.GetFirstChild<A.Outline>();
        var lineWidth = ln?.Width?.Value ?? 0;
        var stroke = ExtractFirstFillColor(ln) ?? "000000";

        // wp:extent of an inline connector with cy=0 collapses to a zero-height layout box —
        // the rendered line falls on the baseline. Give the group a one-EMU child height so
        // GroupShape coordinates stay in-range when the renderer scales them.
        var childExtentX = (double) (shapeXfrm.Extents.Cx ?? extent.Cx.Value);
        var childExtentY = Math.Max(1.0, (double) (shapeXfrm.Extents.Cy ?? extent.Cy.Value));

        var widthPoints = extent.Cx.Value / OoxmlUnits.EmusPerPoint;
        var heightPoints = Math.Max(lineWidth / OoxmlUnits.EmusPerPoint, extent.Cy.Value / OoxmlUnits.EmusPerPoint);
        if (widthPoints <= 0)
        {
            return null;
        }

        List<GroupShape> shapes =
        [
            new()
            {
                X = 0,
                Y = 0,
                Width = shapeXfrm.Extents.Cx ?? 0,
                Height = shapeXfrm.Extents.Cy ?? 0,
                ColorHex = stroke,
                LineWidthEmu = lineWidth,
                LineDashPattern = MapPresetDashPattern(ln?.GetFirstChild<A.PresetDash>()?.Val),
                Geometry = GroupShapeGeometry.Line
            }
        ];

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            InlineShapeGroup = new()
            {
                ChildExtentX = childExtentX,
                ChildExtentY = childExtentY,
                Shapes = shapes
            }
        };
    }

    /// <summary>
    /// Wraps a single filled <c>wps:wsp</c> sitting directly under <c>a:graphicData</c>
    /// (no <c>wpg:wgp</c>) into a one-element <see cref="InlineShapeGroup"/>. A blip fill is
    /// Word's "fill a shape with a picture" (cover-letters/09's circular profile photo — the
    /// group route keeps the preset geometry, so an ellipse photo renders clipped to its
    /// circle); a solid fill is an inline logo/accent graphic (cover-letters/10's custGeom
    /// letterhead moons).
    /// </summary>
    Run? ParseInlineSingleShapeRun(Drawing drawing, WPS.WordprocessingShape wsp, OpenXmlPart hostPart, RunProperties runProps)
    {
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        if (extent?.Cx == null || extent.Cy == null)
        {
            return null;
        }

        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        var shapeXfrm = shapeProps?.GetFirstChild<A.Transform2D>();
        if (shapeProps == null || shapeXfrm?.Extents == null)
        {
            return null;
        }

        var blipFill = shapeProps.GetFirstChild<A.BlipFill>();
        var image = blipFill != null ? ReadBlipImage(blipFill, hostPart) : null;
        var solidFill = shapeProps.GetFirstChild<A.SolidFill>();
        var fillColorHex = solidFill != null ? ExtractFirstFillColor(solidFill) : null;

        // A shape can take its fill from the style's a:fillRef instead of naming one in spPr —
        // Word writes exactly that for a template's accent rules, e.g. resumes/12's coral bar under
        // "Manager" is a rect with <a:ln><a:noFill/> and its colour only in
        // <wps:style><a:fillRef><a:schemeClr val="accent1">, as the stroke reads its lnRef (see
        // ReadGroupStroke). An explicit a:noFill overrides the style's fill.
        if (shapeProps.GetFirstChild<A.NoFill>() == null)
        {
            fillColorHex ??= ExtractFirstFillColor(wsp.GetFirstChild<WPS.ShapeStyle>()?.FillReference);
        }

        var stroke = ReadGroupStroke(shapeProps, wsp.GetFirstChild<WPS.ShapeStyle>()?.LineReference);
        if (image == null &&
            fillColorHex == null &&
            stroke.WidthEmu <= 0)
        {
            return null;
        }

        var widthPoints = extent.Cx.Value / OoxmlUnits.EmusPerPoint;
        var heightPoints = extent.Cy.Value / OoxmlUnits.EmusPerPoint;
        var childExtentX = (double) (shapeXfrm.Extents.Cx ?? extent.Cx.Value);
        var childExtentY = (double) (shapeXfrm.Extents.Cy ?? extent.Cy.Value);
        if (widthPoints <= 0 || heightPoints <= 0 || childExtentX <= 0 || childExtentY <= 0)
        {
            return null;
        }

        var custGeom = shapeProps.GetFirstChild<A.CustomGeometry>();
        List<GroupShape> shapes =
        [
            new()
            {
                X = 0,
                Y = 0,
                Width = shapeXfrm.Extents.Cx ?? 0,
                Height = shapeXfrm.Extents.Cy ?? 0,
                ColorHex = stroke.ColorHex,
                LineWidthEmu = stroke.WidthEmu,
                LineAlpha = stroke.Alpha,
                LineDashPattern = stroke.DashPattern,
                OpenOutline = custGeom != null &&
                              !custGeom.Descendants<A.CloseShapePath>().Any(),
                RoundCap = shapeProps.GetFirstChild<A.Outline>()?.CapType?.Value == A.LineCapValues.Round,
                Geometry = MapGroupGeometry(shapeProps),
                // Contours drive the SOLID fill's silhouette (custGeom logo art). Picture
                // fills keep them null so the ellipse clip stays the smooth geometric path.
                Subpaths = image == null
                    ? ShapeParser.ExtractSubpaths(shapeProps)
                      ?? PresetShapeGeometry.TryBuild(
                          shapeProps.GetFirstChild<A.PresetGeometry>(),
                          shapeXfrm.Extents.Cx ?? 0,
                          shapeXfrm.Extents.Cy ?? 0)
                    : null,
                FillColorHex = image == null ? fillColorHex : null,
                FillAlpha = solidFill != null ? ShapeParser.ExtractSolidFillAlpha(solidFill) : 1,
                Shadow = ReadOuterShadow(shapeProps),
                ImageData = image?.Data,
                ImageContentType = image?.ContentType,
                ImageRasterFallbackData = image?.RasterFallbackData,
                ImageCrop = blipFill != null &&
                            image != null ? BlipReader.ReadCrop(blipFill) : null
            }
        ];

        return new()
        {
            Text = "",
            Properties = runProps,
            InlineImageWidthPoints = widthPoints,
            InlineImageHeightPoints = heightPoints,
            // The line reserves the drawing's layout box (extent + wp:effectExtent), as for a picture —
            // the room Word keeps for the chevrons' 5pt round-capped stroke spilling past the extent.
            InlineImageEffectExtent = ReadEffectExtent(drawing),
            InlineShapeGroup = new()
            {
                ChildExtentX = childExtentX,
                ChildExtentY = childExtentY,
                // Turned about the centre of its extent, as a group is: menus/07's mint waves are
                // custGeom rules at rot -4.55°, whose spill is the effectExtent the line reserves.
                RotationDegrees = ReadRotationDegrees(shapeXfrm),
                Shapes = shapes
            }
        };
    }

    /// <summary>
    /// Walks an <c>a:ln</c> / <c>a:solidFill</c> for the first solid colour we can resolve, in
    /// document order — an outline carries exactly one. Each colour flavour applies its own
    /// <c>lumMod</c>/<c>lumOff</c>/<c>tint</c>/<c>shade</c> children: ignoring them turns Word's
    /// "Lighter 80%" tints back into the saturated base colour.
    /// </summary>
    string? ExtractFirstFillColor(OpenXmlElement? element)
    {
        if (element == null)
        {
            return null;
        }

        foreach (var color in element.Descendants())
        {
            switch (color)
            {
                case A.RgbColorModelHex {Val.HasValue: true} rgb:
                    return ShapeParser.ApplyLiteralColorTransforms(rgb.Val.Value!, rgb);

                // Word caches the resolved value of a system colour in @lastClr; @val ("window",
                // "windowText") names a host UI colour a document renderer must not look up.
                case A.SystemColor {LastColor.HasValue: true} system:
                    return ShapeParser.ApplyLiteralColorTransforms(system.LastColor.Value!, system);

                case A.SchemeColor {Val.HasValue: true} scheme when currentThemeColors != null:
                    var schemeValue = ((IEnumValue) scheme.Val.Value).Value;
                    return currentThemeColors.ResolveColor(schemeValue, ShapeParser.ExtractColorTransforms(scheme));
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a Drawing element to extract a text box (shape with text content, without WordArt transform).
    /// </summary>
    FloatingTextBoxElement? ParseTextBox(Drawing drawing, MainDocumentPart mainPart)
    {
        long widthEmu = 0;
        long heightEmu = 0;

        var inline = drawing.GetFirstChild<DW.Inline>();
        var anchor = drawing.GetFirstChild<DW.Anchor>();

        if (inline != null)
        {
            var extent = inline.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }
        else if (anchor != null)
        {
            var extent = anchor.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }

        if (widthEmu == 0 || heightEmu == 0)
        {
            return null;
        }

        var widthPoints = widthEmu / OoxmlUnits.EmusPerPoint;
        var heightPoints = heightEmu / OoxmlUnits.EmusPerPoint;

        var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
        if (wsp == null)
        {
            return null;
        }

        var txbx = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (txbx == null)
        {
            return null;
        }

        var txbxContent = txbx.GetFirstChild<TextBoxContent>();
        if (txbxContent == null)
        {
            return null;
        }

        if (HasGroupSiblings(wsp))
        {
            return null;
        }

        // Check if this is WordArt (has text transform) - if so, skip it for this parser
        var bodyPr = wsp.GetFirstChild<WPS.TextBodyProperties>();
        if (bodyPr != null)
        {
            var prstTxWarp = bodyPr.GetFirstChild<A.PresetTextWarp>();
            if (prstTxWarp?.Preset?.HasValue == true &&
                prstTxWarp.Preset.Value != A.TextShapeValues.TextNoShape)
            {
                // This is WordArt, let ParseWordArt handle it
                return null;
            }
        }

        // Parse the content as document elements
        var content = new List<DocumentElement>();
        foreach (var element in txbxContent.ChildElements)
        {
            if (element is Paragraph para)
            {
                var parsedElements = ParseParagraph(para, mainPart);
                content.AddRange(parsedElements);
            }
            else if (element is Table table)
            {
                var parsedTable = ParseTable(table, mainPart);
                if (parsedTable != null)
                {
                    content.Add(parsedTable);
                }
            }
        }

        if (content.Count == 0)
        {
            return null;
        }

        // Get position and wrap info from anchor
        var positioning = anchor?.ParsePositioning(page: anchorAlignmentPage) ?? default;
        var wrapType = WrapType.None;
        string? bgColor = null;

        if (anchor != null)
        {
            if (anchor.GetFirstChild<DW.WrapNone>() != null)
            {
                wrapType = WrapType.None;
            }
            else if (anchor.GetFirstChild<DW.WrapSquare>() != null)
            {
                wrapType = WrapType.Square;
            }
            else if (anchor.GetFirstChild<DW.WrapTight>() != null)
            {
                wrapType = WrapType.Tight;
            }
            else if (anchor.GetFirstChild<DW.WrapTopBottom>() != null)
            {
                wrapType = WrapType.TopAndBottom;
            }
        }

        // Parse background color from shape properties
        var spPr = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (spPr != null)
        {
            var solidFill = spPr.GetFirstChild<A.SolidFill>();
            if (solidFill != null)
            {
                var rgbColor = solidFill.GetFirstChild<A.RgbColorModelHex>();
                if (rgbColor?.Val?.HasValue == true)
                {
                    bgColor = ShapeParser.ApplyLiteralColorTransforms(rgbColor.Val.Value!, rgbColor);
                }
            }
        }

        return new()
        {
            Content = content,
            HidesOverflow = HidesOverflow(wsp),
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            RelativeHeight = positioning.RelativeHeight,
            LayoutInCell = positioning.LayoutInCell,
            HorizontalPositionPoints = positioning.HorizontalPositionPoints,
            VerticalPositionPoints = positioning.VerticalPositionPoints,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            WrapType = wrapType,
            BehindText = positioning.BehindText,
            BackgroundColorHex = bgColor,
            Insets = ReadTextInsets(bodyPr),
            VerticalAlignment = ShapeParser.TextAnchor(bodyPr?.Anchor?.Value)
        };
    }

    // True when the shape carries no renderable text: no txbx at all, or only Word's empty
    // placeholder paragraph (whitespace).
    static bool TextBoxHasNoText(WPS.WordprocessingShape wsp)
    {
        var textBox = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (textBox == null)
        {
            return true;
        }

        return !textBox.Descendants<Text>().Any(static _ => !string.IsNullOrWhiteSpace(_.Text));
    }

    /// <summary>
    /// Parses ALL shapes from a drawing (handles groups with multiple shapes).
    /// Returns both text boxes and solid fill shapes.
    /// </summary>
    List<DocumentElement> ParseAllShapesFromDrawing(Drawing drawing, MainDocumentPart mainPart, Dictionary<DocumentElement, OpenXmlElement>? childSources = null)
    {
        var result = new List<DocumentElement>();

        var anchor = drawing.GetFirstChild<DW.Anchor>();
        if (anchor == null)
        {
            return result;
        }

        // Get base positioning from anchor
        var positioning = anchor.ParsePositioning(page: anchorAlignmentPage);
        var behindText = anchor.BehindDoc?.Value ?? false;

        // Check for a WordprocessingGroup
        var wgp = drawing.Descendants<WPG.WordprocessingGroup>().FirstOrDefault();
        if (wgp != null)
        {
            // Get root group transform info
            var grpSpPr = wgp.GetFirstChild<WPG.GroupShapeProperties>();
            var grpXfrm = grpSpPr?.GetFirstChild<A.TransformGroup>();

            long chOffX = 0, chOffY = 0;
            long chExtCx = 1, chExtCy = 1;

            var chOff = grpXfrm?.ChildOffset;
            var chExt = grpXfrm?.ChildExtents;

            if (chOff != null)
            {
                chOffX = chOff.X ?? 0;
                chOffY = chOff.Y ?? 0;
            }

            if (chExt != null)
            {
                chExtCx = chExt.Cx ?? 1;
                chExtCy = chExt.Cy ?? 1;
            }

            var extent = anchor.Extent;
            // A group of zero-width vertical connectors legitimately has cx=0 in BOTH the
            // anchor extent and its child space (cards/06's page-2 fold lines). Clamp each
            // degenerate length to 1 like AccumulatedTransform.Of does for nested groups, so the
            // degenerate axis maps through unscaled rather than as 0/0.
            double rootExtCx = extent?.Cx ?? 1;
            double rootExtCy = extent?.Cy ?? 1;
            var rootScaleX = Math.Max(rootExtCx, 1) / Math.Max(chExtCx, 1);
            var rootScaleY = Math.Max(rootExtCy, 1) / Math.Max(chExtCy, 1);

            // The group's frame in anchor-relative points — children clip to it (Word cuts a
            // group's children at the group's extent box). A zero-area frame must not clip
            // (it would eat every child of a degenerate connector-only group).
            (double Left, double Top, double Width, double Height)? groupFrame =
                extent?.Cx != null &&
                extent.Cy != null &&
                extent.Cx.Value > 0 &&
                extent.Cy.Value > 0
                    ? (positioning.HorizontalPositionPoints,
                        positioning.VerticalPositionPoints,
                        extent.Cx.Value / OoxmlUnits.EmusPerPoint,
                        extent.Cy.Value / OoxmlUnits.EmusPerPoint)
                    : null;

            // Process all shapes in the group (including nested grpSp groups)
            foreach (var wsp in wgp.Descendants<WPS.WordprocessingShape>())
            {
                // Get accumulated transform from all ancestor grpSp groups
                var accumTransform = AccumulatedTransform.Of(wsp, chOffX, chOffY, rootScaleX, rootScaleY);

                var textBox = ParseTextBoxFromShape(wsp, positioning, accumTransform, behindText, mainPart);
                if (textBox != null)
                {
                    result.Add(textBox);
                    childSources?[textBox] = wsp;
                }
                else if (TextBoxHasNoText(wsp))
                {
                    // Fall back to solid-fill parsing for shapes without TEXT: no txbx at all, or a
                    // txbx that is only Word's empty placeholder (templated artwork stores one in
                    // every decorative shape — labels/14's navy card bodies are roundRects with an
                    // empty txbx). Shape geometry mirrors with its ancestors' flips (cards/03's streamer sits in
                    // a flipH'd nested group); the text-box path above keeps the flip-free
                    // transform.
                    var shapeTransform = AccumulatedTransform.Of(wsp, chOffX, chOffY, rootScaleX, rootScaleY, composeFlips: true);
                    var solidShape = ParseSolidFillShape(wsp, positioning, shapeTransform, behindText, mainPart, groupFrame: groupFrame);
                    if (solidShape != null)
                    {
                        result.Add(solidShape);
                        childSources?[solidShape] = wsp;
                    }
                }
            }
        }
        else
        {
            // Single shape
            var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
            if (wsp != null)
            {
                var extent = anchor.Extent;
                var widthPoints = (extent?.Cx ?? 0) / OoxmlUnits.EmusPerPoint;
                var heightPoints = (extent?.Cy ?? 0) / OoxmlUnits.EmusPerPoint;

                var textBox = ParseTextBoxFromShape(wsp, positioning, AccumulatedTransform.Identity, behindText, mainPart, widthPoints, heightPoints);
                if (textBox != null)
                {
                    result.Add(textBox);
                }
                else if (TextBoxHasNoText(wsp))
                {
                    // See note above — only text-free shapes feed the solid-fill fallback.
                    var solidShape = ParseSolidFillShape(wsp, positioning, AccumulatedTransform.Identity, behindText, mainPart, widthPoints, heightPoints);
                    if (solidShape != null)
                    {
                        result.Add(solidShape);
                    }
                }
            }
        }

        return result;
    }

    static bool HasRenderableContent(List<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is ParagraphElement para)
            {
                if (para.Runs.Any(_ => !string.IsNullOrEmpty(_.Text)))
                {
                    return true;
                }
            }
            else
            {
                // Tables, images, etc. always count as renderable.
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves <c>a:grpFill</c>: a group child deferring its fill to the group takes the fill of
    /// the nearest ancestor group that carries one. Wrapper groups without any fill element are
    /// looked through (labels/07 nests its collage clusters two wrappers deep); an ancestor with a
    /// concrete non-solid fill (gradient/blip/noFill) stops the walk — the kind is unsupported by
    /// this path and inheriting from higher up would paint the wrong colour.
    /// </summary>
    static A.SolidFill? ResolveGroupFill(WPS.WordprocessingShape wsp)
    {
        foreach (var ancestor in wsp.Ancestors())
        {
            if (ancestor is not WPG.GroupShape &&
                ancestor is not WPG.WordprocessingGroup)
            {
                continue;
            }

            var groupProperties = ancestor.GetFirstChild<WPG.GroupShapeProperties>();
            if (groupProperties == null)
            {
                continue;
            }

            if (groupProperties.GetFirstChild<A.SolidFill>() is { } groupFill)
            {
                return groupFill;
            }

            if (groupProperties.GetFirstChild<A.GradientFill>() != null ||
                groupProperties.GetFirstChild<A.BlipFill>() != null ||
                groupProperties.GetFirstChild<A.NoFill>() != null)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a solid fill shape (no text box) as a FloatingShapeElement.
    /// </summary>
    FloatingShapeElement? ParseSolidFillShape(
        WPS.WordprocessingShape wsp,
        AnchorPositioning positioning,
        AccumulatedTransform accumTransform,
        bool behindText,
        MainDocumentPart mainPart,
        double? overrideWidth = null,
        double? overrideHeight = null,
        (double Left, double Top, double Width, double Height)? groupFrame = null)
    {
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (shapeProps == null)
        {
            return null;
        }

        // A blip fill is an image texture. ShapeParser's behindDoc sweep parses these too, but
        // for walk-owned groups (cell-anchored, non-identity nesting) this walk is the only
        // parser whose output survives the merge (cover-letters/09's circular profile photo
        // lives in a cell-anchored group). Identity
        // body groups still dual-parse; the position/size dedup collapses the twins.
        byte[]? imageData = null;
        string? imageContentType = null;
        ImageCrop? imageCrop = null;
        ImageTile? imageTile = null;
        if (shapeProps.GetFirstChild<A.BlipFill>() is { } blipFill)
        {
            (imageData, imageContentType) = ShapeParser.ExtractBlipFillImage(blipFill, mainPart, GetPartBytes);
            if (imageData == null)
            {
                return null;
            }

            imageCrop = BlipReader.ReadCrop(blipFill);
            imageTile = BlipReader.ReadTile(blipFill, imageData);
        }

        // prstGeom prst="line" — stroke-only connector. Has no fill (and typically cy=0
        // for a horizontal line, cx=0 for a vertical), so the rest of the fill-driven
        // pipeline can't render it. Emit a FloatingShapeElement with the line color/width
        // and let the renderer's stroke branch draw it. Must run before the noFill bail-out
        // because line connectors carry an explicit <a:noFill/> alongside the stroke.
        if (ShapeParser.IsLineShape(shapeProps))
        {
            return ParseLineShape(wsp, shapeProps, positioning, accumTransform, behindText);
        }

        // An explicit <a:noFill/> in spPr means the shape has no fill, even if the
        // wps:style contains a fillRef — the direct property always wins. It does not mean
        // invisible: a visible stroke still draws the frame, and in walk-owned groups this parser
        // is the only one whose output survives the merge (cards/13's white card outlines sit in
        // translation-remapped nested groups). Text-box carriers keep ShapeParser's rule and stay
        // skipped — their chrome belongs to the text-box path.
        var explicitNoFill = shapeProps.GetFirstChild<A.NoFill>() != null;
        if (explicitNoFill &&
            wsp.GetFirstChild<WPS.TextBoxInfo2>() != null)
        {
            return null;
        }

        // Check for solid fill in shape properties. a:grpFill defers to the ancestor group's
        // fill (labels/07's 27 collage pieces inherit their cluster group's accent colour).
        var solidFill = explicitNoFill ? null : shapeProps.GetFirstChild<A.SolidFill>();
        if (solidFill == null &&
            !explicitNoFill &&
            shapeProps.GetFirstChild<A.GroupFill>() != null)
        {
            solidFill = ResolveGroupFill(wsp);
        }

        string? fillColorHex = null;
        var fillAlpha = 1.0;

        if (solidFill != null)
        {
            // Parse alpha transform (a:alpha val="X" where X is in thousandths of a percent)
            var alphaElement = solidFill.Descendants<A.Alpha>().FirstOrDefault();
            if (alphaElement?.Val?.HasValue == true)
            {
                fillAlpha = alphaElement.Val.Value / 100000.0;
            }

            var srgbClr = solidFill.GetFirstChild<A.RgbColorModelHex>();
            if (srgbClr?.Val?.HasValue == true)
            {
                fillColorHex = ShapeParser.ApplyLiteralColorTransforms(srgbClr.Val.Value!, srgbClr);
            }
            else
            {
                var schemeClr = solidFill.GetFirstChild<A.SchemeColor>();
                if (schemeClr?.Val?.HasValue == true)
                {
                    var transforms = ShapeParser.ExtractColorTransforms(schemeClr);
                    if (transforms.HasTransforms &&
                        currentThemeColors != null)
                    {
                        var schemeValue = ((IEnumValue) schemeClr.Val.Value).Value;
                        fillColorHex = currentThemeColors.ResolveColor(schemeValue, transforms);
                    }

                    // No transforms, or ResolveColor failed.
                    fillColorHex ??= ThemeParser.ResolveSchemeColor(schemeClr.Val.Value, mainPart.ThemePart);
                }
            }
        }

        // Linear gradient fill — every corpus gradient sits on a GROUP child (labels/04's
        // 90° accent bars, cover-letters/06's banner segments), reaching this walk or
        // ShapeParser's grouped path. A linear gradient that extracts suppresses the fillRef
        // fallback below (the direct property wins); unmodelled radial/path gradients fall
        // through to it.
        GradientFill? gradient = null;
        if (fillColorHex == null &&
            !explicitNoFill &&
            shapeProps.GetFirstChild<A.GradientFill>() is { } gradFillElement)
        {
            gradient = ShapeParser.ExtractGradientFill(gradFillElement, currentThemeColors);
            if (gradient != null)
            {
                fillColorHex = gradient.StartColorHex;
            }
        }

        // If no direct fill, check for style reference (fillRef in wps:style)
        if (fillColorHex == null &&
            !explicitNoFill)
        {
            var shapeStyle = wsp.GetFirstChild<WPS.ShapeStyle>();
            var fillRef = shapeStyle?.FillReference;
            if (fillRef != null)
            {
                // fillRef idx="1" with a scheme color means solid fill with that color
                var schemeClr = fillRef.GetFirstChild<A.SchemeColor>();
                if (schemeClr?.Val?.HasValue == true)
                {
                    fillColorHex = ThemeParser.ResolveSchemeColor(schemeClr.Val.Value, mainPart.ThemePart);
                }
            }
        }

        // Resolve the outline once. A shape can be stroke-only (no fill) — e.g. the thin accent
        // rules down the left and across the top of the Agenda template, which are stroked
        // custom-geometry line segments (moveTo + lnTo) with no fill. Those must still render, so
        // don't bail purely because there's no fill; only bail when there's neither fill nor stroke.
        var (lineColorHex, lineWidthPoints, lineAlpha) = ShapeParser.ExtractLineStyle(wsp, shapeProps, currentThemeColors);
        // Dashed outlines render through the same LineDashPattern the line connectors use
        // (letters/05's dashed letterhead rules). A preset the dash table doesn't map draws solid.
        var strokeDashPattern = MapPresetDashPattern(
            shapeProps.GetFirstChild<A.Outline>()?.GetFirstChild<A.PresetDash>()?.Val);
        var hasStroke = lineColorHex != null &&
                        lineWidthPoints is > 0;
        if (fillColorHex == null &&
            imageData == null &&
            !hasStroke)
        {
            return null;
        }

        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();

        var xPt = positioning.HorizontalPositionPoints;
        var yPt = positioning.VerticalPositionPoints;
        var widthPt = overrideWidth ?? 0;
        var heightPt = overrideHeight ?? 0;
        double rotationDegrees = 0;
        var flipHorizontal = false;
        var flipVertical = false;

        if (xfrm != null)
        {
            var off = xfrm.Offset;
            var ext = xfrm.Extents;
            var mapped = accumTransform.MapRectangle(
                off?.X ?? 0,
                off?.Y ?? 0,
                ext?.Cx ?? 0,
                ext?.Cy ?? 0);

            if (off != null)
            {
                xPt = positioning.HorizontalPositionPoints + mapped.X.EmuToPoints();
                yPt = positioning.VerticalPositionPoints + mapped.Y.EmuToPoints();
            }

            if (ext != null)
            {
                widthPt = mapped.Width.EmuToPoints();
                heightPt = mapped.Height.EmuToPoints();
            }

            // The shape's own rotation composed with the rotation its ancestor groups apply
            // (e.g. the label sheets rotate each card's wave sub-group 270°). A mirroring
            // ancestor reverses the sense of the shape's own rotation (F·R(θ) = R(−θ)·F) and
            // XORs into its vertical flip — MapRectangle's canonical det<0 decomposition is
            // rotation + flipV.
            rotationDegrees = mapped.RotationDegrees;
            double ownRotation = 0;
            if (xfrm.Rotation?.HasValue == true)
            {
                ownRotation = xfrm.Rotation.Value / 60000.0;
            }

            rotationDegrees += mapped.FlipVertical ? -ownRotation : ownRotation;
            (_, flipHorizontal, flipVertical) = ShapeParser.ExtractTransform(xfrm);
            flipVertical ^= mapped.FlipVertical;
        }

        if (widthPt <= 0 || heightPt <= 0)
        {
            return null;
        }

        // Without this, custGeom shapes (e.g. half-circle decorations with cubic-bezier arcs)
        // render as their bounding rect instead of the actual curved silhouette. Stroke-only
        // shapes allow an open two-point line segment through (minContourPoints: 2) so the
        // renderer strokes the actual rule rather than the bounding box; filled shapes keep the
        // 3-point minimum, so their geometry is unchanged.
        var subpaths = ShapeParser.ExtractSubpaths(
            shapeProps,
            minContourPoints: fillColorHex == null &&
                              imageData == null ? 2 : 3);

        // An image fill clips to the shape's real silhouette, so a preset beyond the enum's
        // Rect/Ellipse needs its built contours (newsletters/13's arch-top photo is a
        // round2SameRect). Stroke-only shapes need them too — the outline IS the preset's
        // silhouette (letters/05's triangle) — and so do GRADIENT fills, which the guard
        // below otherwise drops for want of geometry (labels/04's light-blue hexagon
        // accents). Solid fills keep the Preset fast path when the preset IS a rect or
        // ellipse; any other solid preset needs its contours too, or it fills its bounding
        // box (cards/02's ticket stars and notched plaque tickets).
        if (subpaths == null &&
            (imageData != null || fillColorHex == null || gradient != null || !ShapeParser.HasStrokeablePresetOutline(shapeProps)))
        {
            subpaths = PresetShapeGeometry.TryBuild(
                shapeProps.GetFirstChild<A.PresetGeometry>(), widthPt, heightPt);
        }

        // A stroke-only shape whose geometry produced no contours can only stroke faithfully
        // when it is a plain rect/ellipse preset (business/06's LOGO frame, labels/02's label
        // borders); a custGeom (menus/04's arc-path doodles) or an unbuilt preset would stroke
        // a bounding box Word doesn't draw.
        if (fillColorHex == null &&
            imageData == null &&
            subpaths == null &&
            !ShapeParser.HasStrokeablePresetOutline(shapeProps))
        {
            return null;
        }

        // Gradients render through FillShape's geometry the same way: parsed contours or a
        // plain rect/ellipse preset. An unbuilt preset would paint its BOUNDING BOX (labels/04's
        // hexagon accents), and the stroke would box the same way, so the shape drops entirely.
        if (gradient != null &&
            subpaths == null &&
            !ShapeParser.HasStrokeablePresetOutline(shapeProps))
        {
            return null;
        }

        // Word cuts group children at the group's frame. Both boxes share the anchor's
        // coordinate space, so the clip is plain geometry on the unit-square contours.
        // Rotated and percent-positioned/sized shapes resolve differently at render and are
        // left unclipped (none of the affected templates rotate their out-of-frame pieces).
        // Image-textured shapes are exempt like pictures are: group photos legitimately
        // overflow their frame (clipping them measured +0.99 worse).
        if (groupFrame is { } frame &&
            imageData == null &&
            rotationDegrees == 0 &&
            positioning.HorizontalPositionPercent == null &&
            positioning.VerticalPositionPercent == null)
        {
            var contours = subpaths ?? [[(0d, 0d), (1d, 0d), (1d, 1d), (0d, 1d)]];
            var clipped = GroupFrameClipper.Clip(
                contours, xPt, yPt, widthPt, heightPt,
                frame.Left, frame.Top, frame.Width, frame.Height);
            if (clipped.Count == 0)
            {
                return null;
            }

            if (!ReferenceEquals(clipped, contours))
            {
                subpaths = clipped;
            }
        }

        return new()
        {
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            RelativeHeight = positioning.RelativeHeight,
            LayoutInCell = positioning.LayoutInCell,
            HorizontalPositionPoints = xPt,
            VerticalPositionPoints = yPt,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            BehindText = behindText,
            FillColorHex = fillColorHex,
            FillAlpha = fillAlpha,
            Gradient = gradient,
            ImageData = imageData,
            ImageContentType = imageContentType,
            ImageCrop = imageCrop,
            ImageTile = imageTile,
            // Solid-filled shapes stroke their <a:ln> exactly like ShapeParser's solid branch
            // (letters/05's p1 triangle is a WHITE-filled custGeom whose thick teal stroke is
            // the only visible ink). Image fills stroke like ShapeParser's blip branch does
            // (a photo shape's border ring).
            LineColorHex = lineColorHex,
            LineWidthPoints = lineWidthPoints,
            LineAlpha = lineAlpha,
            LineDashPattern = strokeDashPattern,
            Preset = ShapeParser.ExtractPresetShape(shapeProps),
            Subpaths = subpaths,
            RotationDegrees = rotationDegrees,
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical
        };
    }

    FloatingShapeElement? ParseLineShape(
        WPS.WordprocessingShape wsp,
        WPS.ShapeProperties shapeProps,
        AnchorPositioning positioning,
        AccumulatedTransform accumTransform,
        bool behindText)
    {
        var (lineColor, lineWidth, _) = ShapeParser.ExtractLineStyle(wsp, shapeProps, currentThemeColors);
        if (lineColor == null || lineWidth is not > 0)
        {
            return null;
        }

        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();
        if (xfrm?.Offset is not { } off ||
            xfrm.Extents is not { } ext)
        {
            return null;
        }

        // A line segment is symmetric under 180-degree rotation, and a 90/270-degree rotation
        // about the box centre is a transpose of the box — both render on the rect-stroke
        // path (labels/03's tear lines are 90-degree sysDot connectors). Anything oblique
        // still bails: the rect-stroke path can't draw a diagonal.
        var offX = (long) (off.X ?? 0);
        var offY = (long) (off.Y ?? 0);
        var extCx = (long) (ext.Cx ?? 0);
        var extCy = (long) (ext.Cy ?? 0);
        if (xfrm.Rotation?.Value is { } rot &&
            rot != 0)
        {
            var ownDegrees = NormalizeDegrees(rot / 60000.0);
            if (IsQuarterTurn(ownDegrees))
            {
                (offX, offY, extCx, extCy) = TransposeAboutCentre(offX, offY, extCx, extCy);
            }
            else if (!IsHalfTurn(ownDegrees))
            {
                return null;
            }
        }

        // Dash presets render through the stroke dash pattern; anything unmapped stays solid.
        var dashPattern = MapPresetDashPattern(
            shapeProps.GetFirstChild<A.Outline>()?.GetFirstChild<A.PresetDash>()?.Val);

        var mapped = accumTransform.MapRectangle(offX, offY, extCx, extCy);

        // Group-sourced rotation: same quarter/half-turn policy as the shape's own rotation.
        // MapRectangle already sizes by the rotated basis vectors, so a quarter-turn arrives
        // with the box axes ALREADY swapped — only oblique angles bail.
        var groupDegrees = NormalizeDegrees(mapped.RotationDegrees);
        if (Math.Abs(groupDegrees) > 0.01 &&
            !IsQuarterTurn(groupDegrees) &&
            !IsHalfTurn(groupDegrees))
        {
            return null;
        }

        var finalX = mapped.X;
        var finalY = mapped.Y;
        var widthPt = mapped.Width.EmuToPoints();
        var heightPt = mapped.Height.EmuToPoints();

        // Lines have a zero extent on the perpendicular axis. Bail if both are zero
        // (degenerate point) — there's nothing to draw.
        if (widthPt <= 0 &&
            heightPt <= 0)
        {
            return null;
        }

        return new()
        {
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            RelativeHeight = positioning.RelativeHeight,
            LayoutInCell = positioning.LayoutInCell,
            HorizontalPositionPoints = positioning.HorizontalPositionPoints + finalX.EmuToPoints(),
            VerticalPositionPoints = positioning.VerticalPositionPoints + finalY.EmuToPoints(),
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            BehindText = behindText,
            LineColorHex = lineColor,
            LineWidthPoints = lineWidth,
            LineDashPattern = dashPattern
        };
    }

    static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized;
    }

    static bool IsQuarterTurn(double degrees) =>
        Math.Abs(degrees - 90) < 0.01 || Math.Abs(degrees - 270) < 0.01;

    static bool IsHalfTurn(double degrees) =>
        Math.Abs(degrees - 180) < 0.01;

    static (long OffX, long OffY, long ExtCx, long ExtCy) TransposeAboutCentre(long offX, long offY, long extCx, long extCy)
    {
        var centreX = offX + extCx / 2;
        var centreY = offY + extCy / 2;
        return (centreX - extCy / 2, centreY - extCx / 2, extCy, extCx);
    }

    /// <summary>
    /// A preset dash as alternating on/off lengths in multiples of the line width — the
    /// DrawingML convention (ECMA-376 ST_PresetLineDashVal). Null = solid (or an unmapped
    /// preset, which draws solid rather than inventing a pattern).
    /// </summary>
    static IReadOnlyList<double>? MapPresetDashPattern(EnumValue<A.PresetLineDashValues>? dash)
    {
        if (dash?.Value is not { } value || value == A.PresetLineDashValues.Solid)
        {
            return null;
        }

        if (value == A.PresetLineDashValues.Dot)
        {
            return [1, 3];
        }

        if (value == A.PresetLineDashValues.SystemDot)
        {
            return [1, 1];
        }

        if (value == A.PresetLineDashValues.Dash)
        {
            return [4, 3];
        }

        if (value == A.PresetLineDashValues.SystemDash)
        {
            return [3, 1];
        }

        if (value == A.PresetLineDashValues.LargeDash)
        {
            return [8, 3];
        }

        if (value == A.PresetLineDashValues.DashDot)
        {
            return [4, 3, 1, 3];
        }

        if (value == A.PresetLineDashValues.LargeDashDot)
        {
            return [8, 3, 1, 3];
        }

        if (value == A.PresetLineDashValues.LargeDashDotDot)
        {
            return [8, 3, 1, 3, 1, 3];
        }

        if (value == A.PresetLineDashValues.SystemDashDot)
        {
            return [3, 1, 1, 1];
        }

        if (value == A.PresetLineDashValues.SystemDashDotDot)
        {
            return [3, 1, 1, 1, 1, 1];
        }

        return null;
    }

    // A text box that resizes to its text (wps:bodyPr/a:spAutoFit) never overflows; any other hides
    // what does not fit.
    static bool HidesOverflow(WPS.WordprocessingShape wsp) =>
        wsp.GetFirstChild<WPS.TextBodyProperties>()?.GetFirstChild<A.ShapeAutoFit>() == null;

    /// <summary>
    /// Parses a text box from a WordprocessingShape, mapping its box through the accumulated
    /// transform of the groups above it — the identity for a shape standing alone. The fallbacks
    /// size a shape whose <c>a:xfrm</c> carries no extents, from the anchor's own extent.
    /// </summary>
    FloatingTextBoxElement? ParseTextBoxFromShape(
        WPS.WordprocessingShape wsp,
        AnchorPositioning positioning,
        AccumulatedTransform accumTransform,
        bool behindText,
        MainDocumentPart mainPart,
        double? fallbackWidth = null,
        double? fallbackHeight = null)
    {
        var txbx = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (txbx == null)
        {
            return null;
        }

        var txbxContent = txbx.GetFirstChild<TextBoxContent>();
        if (txbxContent == null)
        {
            return null;
        }

        // Skip WordArt (has text transform)
        var bodyPr = wsp.GetFirstChild<WPS.TextBodyProperties>();
        if (bodyPr != null)
        {
            var prstTxWarp = bodyPr.GetFirstChild<A.PresetTextWarp>();
            if (prstTxWarp?.Preset?.HasValue == true &&
                prstTxWarp.Preset.Value != A.TextShapeValues.TextNoShape)
            {
                return null;
            }
        }

        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        var xfrm = shapeProps?.GetFirstChild<A.Transform2D>();

        var xPt = positioning.HorizontalPositionPoints;
        var yPt = positioning.VerticalPositionPoints;
        var widthPt = fallbackWidth ?? 0;
        var heightPt = fallbackHeight ?? 0;
        double rotationDegrees = 0;

        if (xfrm != null)
        {
            var off = xfrm.Offset;
            var ext = xfrm.Extents;
            var mapped = accumTransform.MapRectangle(
                off?.X ?? 0,
                off?.Y ?? 0,
                ext?.Cx ?? 0,
                ext?.Cy ?? 0);

            if (off != null)
            {
                xPt = positioning.HorizontalPositionPoints + mapped.X.EmuToPoints();
                yPt = positioning.VerticalPositionPoints + mapped.Y.EmuToPoints();
            }

            if (ext != null)
            {
                widthPt = mapped.Width.EmuToPoints();
                heightPt = mapped.Height.EmuToPoints();
            }

            // The shape's own rotation (in 60,000ths of a degree) composed with the rotation
            // its ancestor groups apply
            rotationDegrees = mapped.RotationDegrees;
            if (xfrm.Rotation?.HasValue == true)
            {
                rotationDegrees += xfrm.Rotation.Value / 60000.0;
            }
        }

        if (widthPt <= 0 || heightPt <= 0)
        {
            return null;
        }

        var content = new List<DocumentElement>();
        foreach (var element in txbxContent.ChildElements)
        {
            if (element is Paragraph para)
            {
                var paragraphElements = ParseParagraph(para, mainPart);
                content.AddRange(paragraphElements);
            }
            else if (element is Table table)
            {
                var tableElement = ParseTable(table, mainPart);
                if (tableElement != null)
                {
                    content.Add(tableElement);
                }
            }
        }

        if (!HasRenderableContent(content))
        {
            // Word documents commonly use a wsp with an empty txbx as the carrier for a behind-text
            // decorative shape (e.g. a full-page coloured rectangle plus an anchored picture). The
            // FloatingShapeElement emitted by ShapeParser already covers the fill; emitting a
            // FloatingTextBoxElement here too would draw the bg again on top of any same-anchor
            // image overlay, masking it. Skip when there's no actual text.
            return null;
        }

        string? bgColor = null;
        var solidFill = shapeProps?.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            bgColor = ShapeParser.ExtractSolidFillColor(solidFill, currentThemeColors);
        }

        // The shape's own chrome: a:ln outline plus any non-rect geometry (roundRect ticket
        // outlines, plaque frames, custGeom) — drawn behind the text content.
        var (lineColor, lineWidth, lineAlpha) = shapeProps == null
            ? (null, null, 1)
            : ShapeParser.ExtractLineStyle(wsp, shapeProps, currentThemeColors);
        var subpaths = shapeProps == null
            ? null
            : ShapeParser.TextBoxOutline(shapeProps, widthPt, heightPt);

        return new()
        {
            Content = content,
            HidesOverflow = HidesOverflow(wsp),
            WidthPoints = widthPt,
            HeightPoints = heightPt,
            RelativeHeight = positioning.RelativeHeight,
            LayoutInCell = positioning.LayoutInCell,
            HorizontalPositionPoints = xPt,
            VerticalPositionPoints = yPt,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            HorizontalPositionPercent = positioning.HorizontalPositionPercent,
            VerticalPositionPercent = positioning.VerticalPositionPercent,
            WrapType = WrapType.None,
            BehindText = behindText,
            BackgroundColorHex = bgColor,
            LineColorHex = lineColor,
            LineWidthPoints = lineWidth ?? 0,
            LineAlpha = lineAlpha,
            Subpaths = subpaths,
            RotationDegrees = rotationDegrees,
            VerticalAlignment = ShapeParser.TextAnchor(bodyPr?.Anchor?.Value)
        };
    }

    /// <summary>
    /// True when the run's resolved colour comes from an EXPLICIT <c>w:color</c> — on the run
    /// itself, its character style chain, or its paragraph style chain — rather than the document
    /// default. Distinguishes "the author asked for black" from "nothing said otherwise", which is
    /// the boundary the <c>a:fontRef</c> fallback in <see cref="ParseWordArt"/> sits on.
    /// </summary>
    static bool DeclaresRunColor(OoxmlRunProperties? runProperties, MainDocumentPart mainPart, string? paragraphStyleId)
    {
        // w:color w:val="auto" is not a declared colour — "automatic" is precisely the value that
        // defers to context, which for a DrawingML text box is the a:fontRef (wedding/08's
        // Ampersand style carries an auto colour and Word still paints the fontRef's white).
        static bool IsExplicit(Color? color) =>
            color?.Val?.HasValue == true &&
            !string.Equals(color.Val.Value, "auto", StringComparison.OrdinalIgnoreCase);

        if (IsExplicit(runProperties?.GetFirstChild<Color>()))
        {
            return true;
        }

        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles == null)
        {
            return false;
        }

        bool ChainDeclares(string? styleId)
        {
            for (var depth = 0; styleId != null &&
                                depth < 12; depth++)
            {
                var style = styles.Elements<Style>().FirstOrDefault(_ => _.StyleId?.Value == styleId);
                if (style == null)
                {
                    return false;
                }

                if (IsExplicit(style.StyleRunProperties?.Color))
                {
                    return true;
                }

                styleId = style.BasedOn?.Val?.Value;
            }

            return false;
        }

        return ChainDeclares(runProperties?.RunStyle?.Val?.Value) || ChainDeclares(paragraphStyleId);
    }

    /// <summary>
    /// Parses a Drawing element to extract a WordArt shape.
    /// Returns WordArtElement for inline WordArt, FloatingWordArtElement for anchored WordArt.
    /// </summary>
    DocumentElement? ParseWordArt(Drawing drawing, MainDocumentPart mainPart, TextAlignment alignment = TextAlignment.Left, double spacingBefore = 0, double spacingAfter = 0, ParagraphProperties? hostParagraph = null)
    {
        // Get dimensions from Inline or Anchor
        long widthEmu = 0;
        long heightEmu = 0;
        var isAnchored = false;

        var inline = drawing.GetFirstChild<DW.Inline>();
        var anchor = drawing.GetFirstChild<DW.Anchor>();

        if (inline != null)
        {
            var extent = inline.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }
        else if (anchor != null)
        {
            isAnchored = true;
            var extent = anchor.Extent;
            if (extent != null)
            {
                widthEmu = extent.Cx ?? 0;
                heightEmu = extent.Cy ?? 0;
            }
        }

        if (widthEmu == 0 || heightEmu == 0)
        {
            return null;
        }

        var widthPoints = widthEmu / OoxmlUnits.EmusPerPoint;
        var heightPoints = heightEmu / OoxmlUnits.EmusPerPoint;

        // Find WordprocessingShape element (wps:wsp)
        var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
        if (wsp == null)
        {
            return null;
        }

        // Get text content from text box (wps:txbx/w:txbxContent)
        var txbx = wsp.GetFirstChild<WPS.TextBoxInfo2>();
        if (txbx == null)
        {
            return null;
        }

        var txbxContent = txbx.GetFirstChild<TextBoxContent>();
        if (txbxContent == null)
        {
            return null;
        }

        if (HasGroupSiblings(wsp))
        {
            return null;
        }

        // Extract text from paragraphs in text box
        var textBuilder = new StringBuilder();
        foreach (var para in txbxContent.Descendants<Paragraph>())
        {
            foreach (var run in para.Descendants<OoxmlRun>())
            {
                foreach (var text in run.Descendants<Text>())
                {
                    textBuilder.Append(text.Text.ReplaceSeparatorsWithSpace());
                }
            }
        }

        var wordArtText = textBuilder.TrimmedToString();
        if (wordArtText.Length == 0)
        {
            return null;
        }

        // Parse font properties from the first run
        var fontFamily = effectiveDefaultFont;
        double fontSize = 36;
        var bold = false;
        var italic = false;
        string? fillColor = null;

        var firstRun = txbxContent.Descendants<OoxmlRun>().FirstOrDefault();
        if (firstRun?.RunProperties != null)
        {
            var runProps = firstRun.RunProperties;

            var runFonts = runProps.GetFirstChild<RunFonts>();
            if (runFonts != null)
            {
                if (runFonts.AsciiTheme?.HasValue == true &&
                    currentThemeFonts != null)
                {
                    var themeValue = ((IEnumValue) runFonts.AsciiTheme.Value).Value;
                    var resolvedFont = currentThemeFonts.ResolveFont(themeValue);
                    if (resolvedFont != null)
                    {
                        fontFamily = resolvedFont;
                    }
                }
                else if (runFonts.Ascii?.HasValue == true)
                {
                    fontFamily = runFonts.Ascii.Value!;
                }
            }

            var fontSizeElement = runProps.GetFirstChild<FontSize>();
            if (OoxmlUnits.FontSizeHalfPointsToPoints(fontSizeElement?.Val?.Value) is { } declaredSize)
            {
                fontSize = declaredSize;
            }

            var boldElement = runProps.GetFirstChild<Bold>();
            if (boldElement != null)
            {
                bold = boldElement.IsOn();
            }

            var italicElement = runProps.GetFirstChild<Italic>();
            if (italicElement != null)
            {
                italic = italicElement.IsOn();
            }

            var colorElement = runProps.GetFirstChild<Color>();
            if (colorElement?.Val?.HasValue == true)
            {
                fillColor = colorElement.Val.Value;
            }
        }

        // Parse text transform preset from body properties
        var transform = WordArtTransform.None;
        double? warpAdjust = null;
        string? outlineColor = null;
        double outlineWidth = 0;
        var hasShadow = false;
        var hasReflection = false;
        var hasGlow = false;

        var bodyPr = wsp.GetFirstChild<WPS.TextBodyProperties>();

        // Parse preset text warp (prstTxWarp)
        var prstTxWarp = bodyPr?.GetFirstChild<A.PresetTextWarp>();
        if (prstTxWarp?.Preset?.HasValue == true)
        {
            transform = ParseTextWarpPreset(prstTxWarp.Preset.Value);
            warpAdjust = ParseTextWarpAdjust(prstTxWarp);
        }

        // An unwarped floating box holding more than one paragraph, or a table, is a text box rather than
        // a line of WordArt: the dispatch below draws one line of all its text (issue #199). ParseTextBox
        // lays it out paragraph by paragraph.
        if (isAnchored &&
            transform == WordArtTransform.None &&
            txbxContent.ChildElements.Count(_ => _ is Paragraph or Table) > 1)
        {
            return null;
        }

        // An arched warp lays its path in the text rect, so the insets set the arch's width.
        var insets = ReadTextInsets(bodyPr);

        // How the text sits inside the box — its first paragraph's resolved w:jc. An arched warp lays the
        // text along its path by it.
        var firstTextParagraph = txbxContent.Descendants<Paragraph>().FirstOrDefault();
        var textAlignment = ParseParagraphProperties(
            firstTextParagraph?.ParagraphProperties,
            mainPart,
            firstTextParagraph?.ParagraphProperties?.ParagraphStyleId?.Val?.Value).Alignment;

        // Check for effects in the shape style
        string? boxLineColor = null;
        double boxLineWidth = 0;
        double boxLineAlpha = 1;
        string? boxFillColor = null;
        IReadOnlyList<IReadOnlyList<(double X, double Y)>>? boxSubpaths = null;
        var boxIsEllipse = false;
        var spPr = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (spPr != null)
        {
            if (transform == WordArtTransform.None)
            {
                // The shape's own fill paints BEHIND an inline text box; it does not colour the
                // glyphs. ExtractFirstFillColor resolves scheme colours and their lumMod/tint
                // transforms.
                if (spPr.GetFirstChild<A.SolidFill>() is { } boxFill)
                {
                    boxFillColor = ExtractFirstFillColor(boxFill);
                }

                // The box is not always a rectangle: brochures/08's logo frame is a prst="frame"
                // RING and wedding/08's badge an ellipse. Fill the real geometry or the frame
                // becomes a solid block Word never draws.
                boxSubpaths = ShapeParser.ExtractSubpaths(spPr)
                              ?? PresetShapeGeometry.TryBuild(
                                  spPr.GetFirstChild<A.PresetGeometry>(), widthPoints, heightPoints);
                boxIsEllipse = spPr.GetFirstChild<A.PresetGeometry>()?.Preset?.Value ==
                               A.ShapeTypeValues.Ellipse;
            }

            if (transform == WordArtTransform.None)
            {
                // No real text warp means this is Word's inline text BOX wearing the WordArt
                // dispatch (business/06's LOGO frame): its a:ln is the box border, not a glyph
                // stroke. ExtractLineStyle resolves theme colours and the wps:style lnRef fallback.
                (boxLineColor, var boxWidthPts, boxLineAlpha) = ShapeParser.ExtractLineStyle(wsp, spPr, currentThemeColors);
                boxLineWidth = boxWidthPts ?? 0;

                // An unwarped inline text box (Word routes these through the WordArt dispatch)
                // renders as an ordinary text box: its glyph colour, size, weight and font follow
                // the run's RESOLVED properties — direct run rPr over the paragraph style chain over
                // the document defaults — not the WordArt defaults the first-run read above used.
                // menus/03's "EVENT INTRO"/"EVENT DATE" labels carry NO run rPr, so their formatting
                // lives entirely in the Heading1 -> Normal chain (10pt, w:color FFFFFF/background1).
                // ParseRunProperties is the body path's cascade (theme fonts, the auto-colour sentinel).
                var firstParagraph = txbxContent.Descendants<Paragraph>().FirstOrDefault();
                var firstParagraphStyleId = firstParagraph?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                var firstRunProperties = txbxContent.Descendants<OoxmlRun>().FirstOrDefault()?.RunProperties;
                var resolvedRun = ParseRunProperties(firstRunProperties, mainPart, firstParagraphStyleId);
                fillColor = resolvedRun.ColorHex;

                // wps:style/a:fontRef is DrawingML's glyph-colour fallback and sits BELOW the
                // w:rPr/style chain: it colours the glyphs only when neither the run nor any style
                // in its chain declares w:color, in which case the cascade above resolved to the
                // document default rather than anything the author asked for.
                if (!DeclaresRunColor(firstRunProperties, mainPart, firstParagraphStyleId) &&
                    ShapeParser.ExtractFontReferenceColor(wsp, currentThemeColors) is { } fontRefColor)
                {
                    fillColor = fontRefColor;
                }

                fontSize = resolvedRun.FontSizePoints;
                bold = resolvedRun.Bold;
                italic = resolvedRun.Italic;
                fontFamily = resolvedRun.FontFamily;
            }
            else
            {
                // Warped WordArt keeps the legacy interpretation: spPr a:ln strokes the glyphs.
                var outline = spPr.GetFirstChild<A.Outline>();
                if (outline != null)
                {
                    var solidFill = outline.GetFirstChild<A.SolidFill>();
                    if (solidFill != null)
                    {
                        var rgbColor = solidFill.GetFirstChild<A.RgbColorModelHex>();
                        if (rgbColor?.Val?.HasValue == true)
                        {
                            outlineColor = ShapeParser.ApplyLiteralColorTransforms(rgbColor.Val.Value!, rgbColor);
                        }

                        var schemeColor = solidFill.GetFirstChild<A.SchemeColor>();
                        if (schemeColor != null)
                        {
                            // Map common scheme colors
                            outlineColor = ThemeParser.MapSchemeColor(schemeColor.Val?.Value);
                        }
                    }

                    if (outline.Width?.HasValue == true)
                    {
                        outlineWidth = outline.Width.Value / OoxmlUnits.EmusPerPoint;
                    }
                }
            }

            // Warped WordArt keeps the legacy reading where the shape fill colours the GLYPHS; for
            // an unwarped inline text box that fill is the box background (boxFillColor above).
            if (transform != WordArtTransform.None)
            {
                var shapeSolidFill = spPr.GetFirstChild<A.SolidFill>();
                if (shapeSolidFill != null &&
                    fillColor == null)
                {
                    var rgbColor = shapeSolidFill.GetFirstChild<A.RgbColorModelHex>();
                    if (rgbColor?.Val?.HasValue == true)
                    {
                        fillColor = ShapeParser.ApplyLiteralColorTransforms(rgbColor.Val.Value!, rgbColor);
                    }
                }
            }

            var effectList = spPr.GetFirstChild<A.EffectList>();
            if (effectList != null)
            {
                hasShadow = effectList.GetFirstChild<A.OuterShadow>() != null ||
                            effectList.GetFirstChild<A.InnerShadow>() != null;
                hasReflection = effectList.GetFirstChild<A.Reflection>() != null;
                hasGlow = effectList.GetFirstChild<A.Glow>() != null;
            }
        }

        // For anchored WordArt, return a floating element with position info
        if (isAnchored &&
            anchor != null)
        {
            var positioning = anchor.ParsePositioning(page: anchorAlignmentPage);

            return new FloatingWordArtElement
            {
                Text = wordArtText,
                WidthPoints = widthPoints,
                HeightPoints = heightPoints,
                RelativeHeight = positioning.RelativeHeight,
                LayoutInCell = positioning.LayoutInCell,
                HorizontalPositionPoints = positioning.HorizontalPositionPoints,
                VerticalPositionPoints = positioning.VerticalPositionPoints,
                HorizontalAnchor = positioning.HorizontalAnchor,
                VerticalAnchor = positioning.VerticalAnchor,
                HorizontalPositionPercent = positioning.HorizontalPositionPercent,
                VerticalPositionPercent = positioning.VerticalPositionPercent,
                BehindText = positioning.BehindText,
                FontFamily = fontFamily,
                FontSizePoints = fontSize,
                Bold = bold,
                Italic = italic,
                FillColorHex = fillColor,
                OutlineColorHex = outlineColor,
                OutlineWidthPoints = outlineWidth,
                HasShadow = hasShadow,
                HasReflection = hasReflection,
                HasGlow = hasGlow,
                Transform = transform,
                WarpAdjust = warpAdjust,
                Insets = insets,
                TextAlignment = textAlignment
            };
        }

        // For inline WordArt, return a regular element
        return new WordArtElement
        {
            Text = wordArtText,
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            FontFamily = fontFamily,
            FontSizePoints = fontSize,
            Bold = bold,
            Italic = italic,
            FillColorHex = fillColor,
            OutlineColorHex = outlineColor,
            OutlineWidthPoints = outlineWidth,
            BoxLineColorHex = boxLineColor,
            BoxLineWidthPoints = boxLineWidth,
            BoxLineAlpha = boxLineAlpha,
            BoxFillColorHex = boxFillColor,
            BoxSubpaths = boxSubpaths,
            BoxIsEllipse = boxIsEllipse,
            Alignment = alignment,
            SpacingBeforePoints = spacingBefore,
            SpacingAfterPoints = spacingAfter,
            HostParagraph = hostParagraph,
            EffectExtent = ReadEffectExtent(drawing),
            HasShadow = hasShadow,
            HasReflection = hasReflection,
            HasGlow = hasGlow,
            Transform = transform,
            WarpAdjust = warpAdjust,
            Insets = insets,
            TextAlignment = textAlignment
        };
    }

    /// <summary>
    /// The warp a <c>prstTxWarp</c> preset names. A preset with no entry (the rings, the button, the
    /// multi-line deflate-inflates) is drawn unwarped.
    /// </summary>
    static readonly Dictionary<A.TextShapeValues, WordArtTransform> textWarpPresets = new()
    {
        [A.TextShapeValues.TextArchUp] = WordArtTransform.ArchUp,
        [A.TextShapeValues.TextArchUpPour] = WordArtTransform.ArchUp,
        [A.TextShapeValues.TextArchDown] = WordArtTransform.ArchDown,
        [A.TextShapeValues.TextArchDownPour] = WordArtTransform.ArchDown,
        [A.TextShapeValues.TextCircle] = WordArtTransform.Circle,
        [A.TextShapeValues.TextCirclePour] = WordArtTransform.Circle,
        [A.TextShapeValues.TextPlain] = WordArtTransform.Plain,
        [A.TextShapeValues.TextStop] = WordArtTransform.Stop,
        [A.TextShapeValues.TextTriangle] = WordArtTransform.Triangle,
        [A.TextShapeValues.TextTriangleInverted] = WordArtTransform.TriangleInverted,
        [A.TextShapeValues.TextChevron] = WordArtTransform.ChevronUp,
        [A.TextShapeValues.TextChevronInverted] = WordArtTransform.ChevronDown,
        [A.TextShapeValues.TextWave1] = WordArtTransform.Wave,
        [A.TextShapeValues.TextWave2] = WordArtTransform.WaveInverted,
        [A.TextShapeValues.TextDoubleWave1] = WordArtTransform.DoubleWave,
        [A.TextShapeValues.TextWave4] = WordArtTransform.DoubleWaveInverted,
        [A.TextShapeValues.TextInflate] = WordArtTransform.Inflate,
        [A.TextShapeValues.TextDeflate] = WordArtTransform.Deflate,
        [A.TextShapeValues.TextInflateTop] = WordArtTransform.InflateTop,
        [A.TextShapeValues.TextInflateBottom] = WordArtTransform.InflateBottom,
        [A.TextShapeValues.TextDeflateTop] = WordArtTransform.DeflateTop,
        [A.TextShapeValues.TextDeflateBottom] = WordArtTransform.DeflateBottom,
        [A.TextShapeValues.TextCanUp] = WordArtTransform.CanUp,
        [A.TextShapeValues.TextCanDown] = WordArtTransform.CanDown,
        [A.TextShapeValues.TextFadeRight] = WordArtTransform.FadeRight,
        [A.TextShapeValues.TextFadeLeft] = WordArtTransform.FadeLeft,
        [A.TextShapeValues.TextFadeUp] = WordArtTransform.FadeUp,
        [A.TextShapeValues.TextFadeDown] = WordArtTransform.FadeDown,
        [A.TextShapeValues.TextSlantUp] = WordArtTransform.SlantUp,
        [A.TextShapeValues.TextSlantDown] = WordArtTransform.SlantDown,
        [A.TextShapeValues.TextCurveUp] = WordArtTransform.CurveUp,
        [A.TextShapeValues.TextCurveDown] = WordArtTransform.CurveDown,
        [A.TextShapeValues.TextCascadeUp] = WordArtTransform.CascadeUp,
        [A.TextShapeValues.TextCascadeDown] = WordArtTransform.CascadeDown
    };

    /// <summary>
    /// The text rect's insets from <c>wps:bodyPr</c>, each falling back to DrawingML's default where it is
    /// not declared.
    /// </summary>
    static WordArtInsets ReadTextInsets(WPS.TextBodyProperties? bodyPr)
    {
        var insets = WordArtInsets.Default;
        if (bodyPr == null)
        {
            return insets;
        }

        static double Inset(Int32Value? emu, double fallback) =>
            emu?.HasValue == true ? emu.Value / OoxmlUnits.EmusPerPoint : fallback;

        return new(
            Inset(bodyPr.LeftInset, insets.Left),
            Inset(bodyPr.TopInset, insets.Top),
            Inset(bodyPr.RightInset, insets.Right),
            Inset(bodyPr.BottomInset, insets.Bottom));
    }

    static WordArtTransform ParseTextWarpPreset(A.TextShapeValues preset) =>
        textWarpPresets.GetValueOrDefault(preset, WordArtTransform.None);

    /// <summary>
    /// A warp's first adjust value as a fraction — <c>a:avLst/a:gd</c> named <c>adj</c> or <c>adj1</c>, whose
    /// formula is <c>val N</c> in hundred-thousandths — or null when the preset's default stands.
    /// </summary>
    static double? ParseTextWarpAdjust(A.PresetTextWarp warp)
    {
        var guide = warp.GetFirstChild<A.AdjustValueList>()?
            .Elements<A.ShapeGuide>()
            .FirstOrDefault(_ => _.Name?.Value is "adj" or "adj1");
        var formula = guide?.Formula?.Value;
        if (formula == null ||
            !formula.StartsWith("val ", StringComparison.Ordinal) ||
            !double.TryParse(formula.AsSpan(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return value / 100000.0;
    }
}
