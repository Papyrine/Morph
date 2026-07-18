using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Builds the outline contours for the <c>a:prstGeom</c> presets the corpus uses beyond the
/// natively-rendered <c>rect</c>/<c>ellipse</c>/<c>line</c>: hexagon, roundRect, plaque, octagon,
/// star5, frame and round2SameRect. Each builder evaluates the ECMA-376 preset formulas (with the
/// shape's <c>a:avLst</c> adjust values over the spec defaults) in the shape's own width/height
/// space — corner radii derive from <c>min(width, height)</c>, so they stay circular on
/// non-square shapes — then normalizes to the same unit-square contour lists
/// <see cref="ShapeParser.ExtractSubpaths"/> produces for <c>a:custGeom</c>. Arcs are flattened
/// to short segments, matching the custom-geometry pipeline every backend already renders.
/// </summary>
static class PresetShapeGeometry
{
    /// <summary>
    /// Contours for a supported preset, or null when the preset is unknown (callers keep their
    /// rect fallback) or the box is degenerate. <paramref name="width"/>/<paramref name="height"/>
    /// only need a consistent unit (points and EMU both work) — the result is normalized.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>>? TryBuild(A.PresetGeometry? prstGeom, double width, double height)
    {
        if (prstGeom?.Preset?.Value is not { } preset || width <= 0 || height <= 0)
        {
            return null;
        }

        var adjustments = ReadAdjustments(prstGeom);

        var contours = preset switch
        {
            var value when value == A.ShapeTypeValues.Hexagon => Hexagon(width, height, adjustments),
            var value when value == A.ShapeTypeValues.RoundRectangle => RoundRect(width, height, adjustments),
            var value when value == A.ShapeTypeValues.Plaque => Plaque(width, height, adjustments),
            var value when value == A.ShapeTypeValues.Octagon => Octagon(width, height, adjustments),
            var value when value == A.ShapeTypeValues.Star5 => Star5(width, height, adjustments),
            var value when value == A.ShapeTypeValues.Frame => Frame(width, height, adjustments),
            var value when value == A.ShapeTypeValues.Round2SameRectangle => Round2SameRect(width, height, adjustments),
            _ => null
        };

        if (contours == null)
        {
            return null;
        }

        // Normalize into the unit square the renderers scale back into the shape's box.
        var normalized = new List<IReadOnlyList<(double X, double Y)>>(contours.Count);
        foreach (var contour in contours)
        {
            var points = new List<(double X, double Y)>(contour.Count);
            foreach (var (pointX, pointY) in contour)
            {
                points.Add((pointX / width, pointY / height));
            }

            normalized.Add(points);
        }

        return normalized;
    }

    /// <summary>Reads <c>a:avLst/a:gd</c> values, e.g. <c>fmla="val 16667"</c>, keyed by guide name.</summary>
    static Dictionary<string, double> ReadAdjustments(A.PresetGeometry prstGeom)
    {
        var adjustments = new Dictionary<string, double>(StringComparer.Ordinal);
        var avList = prstGeom.GetFirstChild<A.AdjustValueList>();
        if (avList == null)
        {
            return adjustments;
        }

        foreach (var guide in avList.Elements<A.ShapeGuide>())
        {
            var name = guide.Name?.Value;
            var formula = guide.Formula?.Value;
            if (name == null || formula == null || !formula.StartsWith("val ", StringComparison.Ordinal))
            {
                continue;
            }

            if (double.TryParse(formula[4..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                adjustments[name] = value;
            }
        }

        return adjustments;
    }

    static double Adjustment(Dictionary<string, double> adjustments, string name, double fallback) =>
        adjustments.TryGetValue(name, out var value) ? value : fallback;

    static double Pin(double min, double value, double max) => Math.Clamp(value, min, max);

    /// <summary>
    /// Appends a flattened circular arc. Angles are degrees in screen coordinates (y down,
    /// 0° = +x, 90° = +y); the sweep is clockwise-positive like the OOXML arc convention.
    /// </summary>
    static void AddArc(List<(double X, double Y)> contour, double centerX, double centerY, double radius, double startDegrees, double sweepDegrees)
    {
        const int segments = 8;
        for (var step = 0; step <= segments; step++)
        {
            var angle = (startDegrees + sweepDegrees * step / segments) * Math.PI / 180;
            contour.Add((centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)));
        }
    }

    static List<IReadOnlyList<(double X, double Y)>> Single(List<(double X, double Y)> contour) => [contour];

    // Flat-topped hexagon: left/right points on the vertical centre, top/bottom edges inset by
    // adj (of min-side). ECMA guides: a = pin(0, adj, 50000*w/ss); x1 = ss*a/100000;
    // dy1 = (h/2)*(vf/100000)*sin(60°) — dy1 equals h/2 at the default vf, so the flat edges sit
    // on the top/bottom of the box.
    static List<IReadOnlyList<(double X, double Y)>> Hexagon(double width, double height, Dictionary<string, double> adjustments)
    {
        var shortSide = Math.Min(width, height);
        var adj = Pin(0, Adjustment(adjustments, "adj", 25000), 50000 * width / shortSide);
        var verticalFactor = Adjustment(adjustments, "vf", 115470);

        var inset = shortSide * adj / 100000;
        var halfHeight = height / 2;
        var dy = halfHeight * (verticalFactor / 100000) * Math.Sin(Math.PI / 3);
        dy = Math.Min(dy, halfHeight);
        var top = halfHeight - dy;
        var bottom = halfHeight + dy;

        return Single(
        [
            (0, halfHeight),
            (inset, top),
            (width - inset, top),
            (width, halfHeight),
            (width - inset, bottom),
            (inset, bottom)
        ]);
    }

    // Rounded rectangle: convex quarter-circle corners of radius adj (of min-side, max 50%).
    static List<IReadOnlyList<(double X, double Y)>> RoundRect(double width, double height, Dictionary<string, double> adjustments)
    {
        var radius = Math.Min(width, height) * Pin(0, Adjustment(adjustments, "adj", 16667), 50000) / 100000;
        var contour = new List<(double X, double Y)>();
        AddArc(contour, radius, radius, radius, 180, 90);                          // top-left
        AddArc(contour, width - radius, radius, radius, 270, 90);                  // top-right
        AddArc(contour, width - radius, height - radius, radius, 0, 90);           // bottom-right
        AddArc(contour, radius, height - radius, radius, 90, 90);                  // bottom-left
        return Single(contour);
    }

    // Plaque: the roundRect's inverse — concave quarter-circle notches centred on the box
    // corners, scooping inward (Word's ticket/label chrome).
    static List<IReadOnlyList<(double X, double Y)>> Plaque(double width, double height, Dictionary<string, double> adjustments)
    {
        var radius = Math.Min(width, height) * Pin(0, Adjustment(adjustments, "adj", 16667), 50000) / 100000;
        var contour = new List<(double X, double Y)>();
        AddArc(contour, 0, 0, radius, 90, -90);                                    // top-left notch: (0,r) -> (r,0)
        AddArc(contour, width, 0, radius, 180, -90);                               // top-right notch: (w-r,0) -> (w,r)
        AddArc(contour, width, height, radius, 270, -90);                          // bottom-right notch: (w,h-r) -> (w-r,h)
        AddArc(contour, 0, height, radius, 0, -90);                                // bottom-left notch: (r,h) -> (0,h-r)
        return Single(contour);
    }

    // Octagon: straight 45° corner cuts of length adj (of min-side).
    static List<IReadOnlyList<(double X, double Y)>> Octagon(double width, double height, Dictionary<string, double> adjustments)
    {
        var cut = Math.Min(width, height) * Pin(0, Adjustment(adjustments, "adj1", 29289), 50000) / 100000;
        return Single(
        [
            (cut, 0),
            (width - cut, 0),
            (width, cut),
            (width, height - cut),
            (width - cut, height),
            (cut, height),
            (0, height - cut),
            (0, cut)
        ]);
    }

    // Five-point star, point up. Outer vertices on the (hf, vf)-scaled half-axes, inner vertices
    // at adj/50000 of the outer radii, alternating every 36°.
    static List<IReadOnlyList<(double X, double Y)>> Star5(double width, double height, Dictionary<string, double> adjustments)
    {
        var inner = Pin(0, Adjustment(adjustments, "adj", 19098), 50000) / 50000;
        var outerX = width / 2 * Adjustment(adjustments, "hf", 105146) / 100000;
        var outerY = height / 2 * Adjustment(adjustments, "vf", 110557) / 100000;
        var centerX = width / 2;
        var centerY = height / 2;

        var contour = new List<(double X, double Y)>(10);
        for (var point = 0; point < 5; point++)
        {
            var outerAngle = (point * 72) * Math.PI / 180;
            contour.Add((centerX + outerX * Math.Sin(outerAngle), centerY - outerY * Math.Cos(outerAngle)));
            var innerAngle = (point * 72 + 36) * Math.PI / 180;
            contour.Add((centerX + outerX * inner * Math.Sin(innerAngle), centerY - outerY * inner * Math.Cos(innerAngle)));
        }

        return Single(contour);
    }

    // Picture-frame ring: outer box with an inner box inset by adj1 (of min-side). The inner
    // contour winds the opposite way so both non-zero and even-odd fills leave the hole open.
    static List<IReadOnlyList<(double X, double Y)>> Frame(double width, double height, Dictionary<string, double> adjustments)
    {
        var inset = Math.Min(width, height) * Pin(0, Adjustment(adjustments, "adj1", 12500), 50000) / 100000;
        List<(double X, double Y)> outer = [(0, 0), (width, 0), (width, height), (0, height)];
        List<(double X, double Y)> innerReversed =
        [
            (inset, inset),
            (inset, height - inset),
            (width - inset, height - inset),
            (width - inset, inset)
        ];
        return [outer, innerReversed];
    }

    // Rectangle with the two top corners rounded (adj1) and the two bottom corners rounded
    // (adj2, default square) — Word's tab/card header chrome.
    static List<IReadOnlyList<(double X, double Y)>> Round2SameRect(double width, double height, Dictionary<string, double> adjustments)
    {
        var shortSide = Math.Min(width, height);
        var topRadius = shortSide * Pin(0, Adjustment(adjustments, "adj1", 16667), 50000) / 100000;
        var bottomRadius = shortSide * Pin(0, Adjustment(adjustments, "adj2", 0), 50000) / 100000;

        var contour = new List<(double X, double Y)>();
        if (topRadius > 0)
        {
            AddArc(contour, topRadius, topRadius, topRadius, 180, 90);
            AddArc(contour, width - topRadius, topRadius, topRadius, 270, 90);
        }
        else
        {
            contour.Add((0, 0));
            contour.Add((width, 0));
        }

        if (bottomRadius > 0)
        {
            AddArc(contour, width - bottomRadius, height - bottomRadius, bottomRadius, 0, 90);
            AddArc(contour, bottomRadius, height - bottomRadius, bottomRadius, 90, 90);
        }
        else
        {
            contour.Add((width, height));
            contour.Add((0, height));
        }

        return Single(contour);
    }
}
