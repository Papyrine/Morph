using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using WPG = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using WPS = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

/// <summary>
/// Parses shape elements from Word documents.
/// </summary>
static class ShapeParser
{
    /// <summary>
    /// Parses a Drawing element to extract background shapes (solid fill or image fill shapes behind text).
    /// Filters out decorative shapes (those with complex bezier paths) and returns remaining shapes.
    /// </summary>
    public static List<FloatingShapeElement> ParseBackgroundShapes(Drawing drawing, ThemeColors? themeColors, MainDocumentPart? mainPart = null, double paragraphSpacingBeforePoints = 0, Func<OpenXmlPart, byte[]>? partBytes = null, Dictionary<DocumentElement, OpenXmlElement>? childSources = null)
    {
        var result = new List<FloatingShapeElement>();

        // Must be an anchored drawing with behindDoc attribute
        var anchor = drawing.GetFirstChild<DW.Anchor>();
        if (anchor == null || anchor.BehindDoc?.Value != true)
        {
            return result;
        }

        // Get anchor dimensions (target size after transform)
        var extent = anchor.Extent;
        if (extent == null)
        {
            return result;
        }

        var anchorDimensions = extent.GetDimensions();
        if (anchorDimensions == null)
        {
            return result;
        }

        var (anchorWidthPt, anchorHeightPt) = anchorDimensions.Value;

        // Parse base positioning from anchor
        var positioning = anchor.ParsePositioning();

        // Check for WordprocessingGroup
        var wgp = drawing.Descendants<WPG.WordprocessingGroup>().FirstOrDefault();
        if (wgp != null)
        {
            // Get group transform info for applying to individual shapes
            var grpSpPr = wgp.GetFirstChild<WPG.GroupShapeProperties>();
            var grpXfrm = grpSpPr?.GetFirstChild<A.TransformGroup>();

            // Child coordinate space (source)
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

            // Calculate scale factors (anchor extent / child extent)
            var scaleX = (extent.Cx ?? 1) / (double) chExtCx;
            var scaleY = (extent.Cy ?? 1) / (double) chExtCy;

            // The group's frame in anchor-relative points — children clip to it (Word cuts a
            // group's children at the group's extent box).
            (double Left, double Top, double Width, double Height)? groupFrame =
                extent.Cx != null && extent.Cy != null
                    ? (positioning.HorizontalPositionPoints,
                        positioning.VerticalPositionPoints,
                        ((long) extent.Cx.Value).EmuToPoints(),
                        ((long) extent.Cy.Value).EmuToPoints())
                    : null;

            // Process ALL non-decorative shapes in the group
            foreach (var wsp in wgp.Descendants<WPS.WordprocessingShape>())
            {
                var shapeElement = ParseGroupedShape(wsp, themeColors, positioning,
                    chOffX, chOffY, scaleX, scaleY, mainPart, partBytes, groupFrame);
                if (shapeElement != null)
                {
                    result.Add(shapeElement);
                    childSources?[shapeElement] = wsp;
                }
            }
        }
        else
        {
            // Standalone shape
            var wsp = drawing.Descendants<WPS.WordprocessingShape>().FirstOrDefault();
            if (wsp != null)
            {
                var shapeElement = ParseStandaloneShape(wsp, themeColors, positioning,
                    anchorWidthPt, anchorHeightPt, mainPart, partBytes);
                if (shapeElement != null)
                {
                    result.Add(shapeElement);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a standalone shape using anchor dimensions directly.
    /// </summary>
    static FloatingShapeElement? ParseStandaloneShape(
        WPS.WordprocessingShape wsp,
        ThemeColors? themeColors,
        AnchorPositioning positioning,
        double widthPoints,
        double heightPoints,
        MainDocumentPart? mainPart,
        Func<OpenXmlPart, byte[]>? partBytes)
    {
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (shapeProps == null)
        {
            return null;
        }

        var (lineColor, lineWidth, lineAlpha) = ExtractLineStyle(wsp, shapeProps, themeColors);
        var preset = ExtractPresetShape(shapeProps);
        var subpaths = ExtractSubpaths(shapeProps)
                       ?? PresetShapeGeometry.TryBuild(shapeProps.GetFirstChild<A.PresetGeometry>(), widthPoints, heightPoints);
        var (rotation, flipH, flipV) = ExtractTransform(shapeProps.GetFirstChild<A.Transform2D>());

        // Try solid fill first
        var solidFill = shapeProps.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            var fillColorHex = ExtractSolidFillColor(solidFill, themeColors);
            if (fillColorHex != null)
            {
                return new()
                {
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    RelativeHeight = positioning.RelativeHeight,
                    LayoutInCell = positioning.LayoutInCell,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints,
                    VerticalPositionPoints = positioning.VerticalPositionPoints,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    WidthPercent = positioning.WidthPercent,
                    WidthRelativeFrom = positioning.WidthRelativeFrom,
                    HeightPercent = positioning.HeightPercent,
                    HeightRelativeFrom = positioning.HeightRelativeFrom,
                    HorizontalPositionPercent = positioning.HorizontalPositionPercent,
                    VerticalPositionPercent = positioning.VerticalPositionPercent,
                    FillColorHex = fillColorHex,
                    FillAlpha = ExtractSolidFillAlpha(solidFill),
                    LineColorHex = lineColor,
                    LineWidthPoints = lineWidth,
                LineAlpha = lineAlpha,
                    Preset = preset,
                    Subpaths = subpaths,
                    RotationDegrees = rotation,
                    FlipHorizontal = flipH,
                    FlipVertical = flipV
                };
            }
        }

        // Try gradient fill (linear only — radial/path fall through to no fill)
        var gradFill = shapeProps.GetFirstChild<A.GradientFill>();
        if (gradFill != null)
        {
            var gradient = ExtractGradientFill(gradFill, themeColors);
            if (gradient != null)
            {
                return new()
                {
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    RelativeHeight = positioning.RelativeHeight,
                    LayoutInCell = positioning.LayoutInCell,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints,
                    VerticalPositionPoints = positioning.VerticalPositionPoints,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    WidthPercent = positioning.WidthPercent,
                    WidthRelativeFrom = positioning.WidthRelativeFrom,
                    HeightPercent = positioning.HeightPercent,
                    HeightRelativeFrom = positioning.HeightRelativeFrom,
                    HorizontalPositionPercent = positioning.HorizontalPositionPercent,
                    VerticalPositionPercent = positioning.VerticalPositionPercent,
                    Gradient = gradient,
                    FillColorHex = gradient.StartColorHex,
                    LineColorHex = lineColor,
                    LineWidthPoints = lineWidth,
                LineAlpha = lineAlpha,
                    Preset = preset,
                    Subpaths = subpaths,
                    RotationDegrees = rotation,
                    FlipHorizontal = flipH,
                    FlipVertical = flipV
                };
            }
        }

        // Try blip fill (image fill)
        var blipFill = shapeProps.GetFirstChild<A.BlipFill>();
        if (blipFill != null &&
            mainPart != null)
        {
            var (imageData, contentType) = ExtractBlipFillImage(blipFill, mainPart, partBytes);
            if (imageData != null)
            {
                return new()
                {
                    WidthPoints = widthPoints,
                    HeightPoints = heightPoints,
                    RelativeHeight = positioning.RelativeHeight,
                    LayoutInCell = positioning.LayoutInCell,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints,
                    VerticalPositionPoints = positioning.VerticalPositionPoints,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    WidthPercent = positioning.WidthPercent,
                    WidthRelativeFrom = positioning.WidthRelativeFrom,
                    HeightPercent = positioning.HeightPercent,
                    HeightRelativeFrom = positioning.HeightRelativeFrom,
                    HorizontalPositionPercent = positioning.HorizontalPositionPercent,
                    VerticalPositionPercent = positioning.VerticalPositionPercent,
                    ImageData = imageData,
                    ImageContentType = contentType,
                    LineColorHex = lineColor,
                    LineWidthPoints = lineWidth,
                LineAlpha = lineAlpha,
                    Preset = preset,
                    Subpaths = subpaths,
                    RotationDegrees = rotation,
                    FlipHorizontal = flipH,
                    FlipVertical = flipV
                };
            }
        }

        return TryBuildOutlineOnlyShape(wsp, shapeProps, positioning, widthPoints, heightPoints,
            0, 0, lineColor, lineWidth, lineAlpha, preset, subpaths, rotation, flipH, flipV);
    }

    /// <summary>
    /// Outline-only shape (<c>a:noFill</c> + a visible <c>a:ln</c>): Word draws the frame
    /// (business/06's LOGO box, labels/02's label borders, letters/05's teal triangle).
    /// Guarded: a wsp that carries a text box strokes its chrome on the text-box path
    /// (labels/04's outline shapes double-drew), and a CUSTOM geometry that produced no
    /// contours (arc-path doodles) would stroke a bounding box Word doesn't draw.
    /// </summary>
    static FloatingShapeElement? TryBuildOutlineOnlyShape(
        WPS.WordprocessingShape wsp,
        WPS.ShapeProperties shapeProps,
        AnchorPositioning positioning,
        double widthPoints,
        double heightPoints,
        double offsetX,
        double offsetY,
        string? lineColor,
        double? lineWidth,
        double lineAlpha,
        PresetShape preset,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>>? subpaths,
        double rotation,
        bool flipH,
        bool flipV)
    {
        if (lineColor == null || lineWidth is not > 0)
        {
            return null;
        }

        if (wsp.GetFirstChild<WPS.TextBoxInfo2>() != null)
        {
            return null;
        }

        if (subpaths == null && !HasStrokeablePresetOutline(shapeProps))
        {
            return null;
        }

        return new()
        {
            WidthPoints = widthPoints,
            HeightPoints = heightPoints,
            RelativeHeight = positioning.RelativeHeight,
            LayoutInCell = positioning.LayoutInCell,
            HorizontalPositionPoints = positioning.HorizontalPositionPoints + offsetX,
            VerticalPositionPoints = positioning.VerticalPositionPoints + offsetY,
            HorizontalAnchor = positioning.HorizontalAnchor,
            VerticalAnchor = positioning.VerticalAnchor,
            BehindText = true,
            LineColorHex = lineColor,
            LineWidthPoints = lineWidth,
            LineAlpha = lineAlpha,
            Preset = preset,
            Subpaths = subpaths,
            RotationDegrees = rotation,
            FlipHorizontal = flipH,
            FlipVertical = flipV
        };
    }

    /// <summary>
    /// Extracts the shape outline (color + width in points) from a wsp's <c>spPr/a:ln</c> and
    /// <c>wps:style/a:lnRef</c>. DrawingML layers the direct <c>a:ln</c> over the theme line style
    /// that <c>a:lnRef/@idx</c> names, so colour and width fall back independently: an <c>a:ln</c>
    /// naming only a colour still strokes, at the theme's width, and one naming only a width
    /// strokes in the lnRef's colour. Returns (null, null) when the shape has no stroke — an
    /// <c>a:noFill</c> line, or nothing left to fall back on.
    /// </summary>
    public static (string? color, double? widthPoints, double alpha) ExtractLineStyle(WPS.WordprocessingShape wsp, WPS.ShapeProperties shapeProps, ThemeColors? themeColors)
    {
        var directLn = shapeProps.GetFirstChild<A.Outline>();

        // Explicit no-fill on the line means no stroke, whatever the style reference says.
        if (directLn?.GetFirstChild<A.NoFill>() != null)
        {
            return (null, null, 1);
        }

        var (refColor, refWidthPoints) = ExtractLineReferenceStyle(wsp, themeColors);

        var directSolid = directLn?.GetFirstChild<A.SolidFill>();
        var color = directSolid != null
            ? ExtractSolidFillColor(directSolid, themeColors)
            : refColor;

        var widthPoints = directLn?.Width?.Value is { } emu
            ? ((long) emu).EmuToPoints()
            : refWidthPoints;

        // Word's decorative frames rely on line opacity: menus/09's 6.75pt octagon border is a
        // light grey at 16% alpha over a dark card — opaque it reads as a fat silver band.
        var alpha = directSolid != null ? ExtractSolidFillAlpha(directSolid) : 1;

        return (color, widthPoints, alpha);
    }

    /// <summary>
    /// The theme line style a <c>wps:style/a:lnRef</c> selects: the width comes from the theme's
    /// <c>lnStyleLst</c> (1-based by <c>@idx</c>), the colour from the lnRef's own colour child,
    /// which overrides the theme line's <c>phClr</c> placeholder.
    /// </summary>
    static (string? color, double? widthPoints) ExtractLineReferenceStyle(WPS.WordprocessingShape wsp, ThemeColors? themeColors)
    {
        var lnRef = wsp.GetFirstChild<WPS.ShapeStyle>()?.LineReference;
        if (lnRef?.Index?.Value is not { } refIdx || refIdx == 0)
        {
            return (null, null);
        }

        var widths = (themeColors ?? new()).LineStyleWidthsEmu;
        var refEmu = refIdx < widths.Count ? widths[(int) refIdx] : 0;
        if (refEmu <= 0)
        {
            return (null, null);
        }

        string? refColor = null;
        var refScheme = lnRef.GetFirstChild<A.SchemeColor>();
        if (refScheme?.Val?.HasValue == true && themeColors != null)
        {
            var schemeValue = ((IEnumValue) refScheme.Val.Value).Value;
            var transforms = ExtractColorTransforms(refScheme);
            refColor = themeColors.ResolveColor(schemeValue, transforms);
        }
        else
        {
            var refRgb = lnRef.GetFirstChild<A.RgbColorModelHex>();
            if (refRgb?.Val?.HasValue == true)
            {
                refColor = refRgb.Val.Value;
            }
        }

        return (refColor, refEmu.EmuToPoints());
    }

    /// <summary>Maps <c>a:prstGeom/@prst</c> values to our <see cref="PresetShape"/> enum.</summary>
    public static PresetShape ExtractPresetShape(WPS.ShapeProperties shapeProps)
    {
        var prstGeom = shapeProps.GetFirstChild<A.PresetGeometry>();
        if (prstGeom?.Preset?.Value == A.ShapeTypeValues.Ellipse)
        {
            return PresetShape.Ellipse;
        }
        return PresetShape.Rect;
    }

    /// <summary>True when the shape is a <c>prstGeom prst="line"</c> connector — these are
    /// stroke-only with no fill and typically have a zero <c>cx</c> or <c>cy</c>.</summary>
    public static bool IsLineShape(WPS.ShapeProperties? shapeProps)
    {
        var prstGeom = shapeProps?.GetFirstChild<A.PresetGeometry>();
        return prstGeom?.Preset?.Value == A.ShapeTypeValues.Line;
    }

    /// <summary>
    /// Whether an outline-only shape WITHOUT parsed contours can stroke its geometry
    /// faithfully: a plain rect/ellipse preset strokes as itself, but a custGeom or a
    /// preset outside <see cref="PresetShapeGeometry"/>'s coverage (triangle etc.) would
    /// stroke its bounding box — a frame Word doesn't draw.
    /// </summary>
    public static bool HasStrokeablePresetOutline(OpenXmlElement shapeProps)
    {
        if (shapeProps.GetFirstChild<A.CustomGeometry>() != null)
        {
            return false;
        }

        var prst = shapeProps.GetFirstChild<A.PresetGeometry>()?.Preset?.Value;
        return prst == null || prst == A.ShapeTypeValues.Rectangle || prst == A.ShapeTypeValues.Ellipse;
    }

    /// <summary>
    /// Extracts the closed contours of an <c>a:custGeom</c> as a list of sub-paths — each a
    /// polyline of points normalized into the unit square (0..1) of its path's declared
    /// <c>w</c>/<c>h</c>. Every <c>a:moveTo</c> begins a new contour, so disjoint pieces and
    /// holes stay separate rather than being joined by connector lines. Cubic and quadratic
    /// beziers are flattened into <see cref="bezierFlattenSegments"/> line segments. Returns
    /// null when the geometry is missing, uses ArcTo (whose parameters the de Casteljau
    /// flattening can't consume), or yields no contour with at least
    /// <paramref name="minContourPoints"/> points — those cases fall back to the bounding rect.
    /// <paramref name="minContourPoints"/> defaults to 3 (a fillable polygon); pass 2 to keep an
    /// open two-point line segment (<c>moveTo</c> + <c>lnTo</c>) that will be stroked rather than
    /// filled — e.g. the thin accent rules in the Agenda template.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>>? ExtractSubpaths(OpenXmlElement shapeProps, int minContourPoints = 3)
    {
        var custGeom = shapeProps.GetFirstChild<A.CustomGeometry>();
        var pathList = custGeom?.GetFirstChild<A.PathList>();
        if (pathList == null)
        {
            return null;
        }

        if (pathList.Descendants<A.ArcTo>().Any())
        {
            return null;
        }

        var subpaths = new List<IReadOnlyList<(double X, double Y)>>();
        foreach (var path in pathList.Elements<A.Path>())
        {
            AppendPathContours(path, subpaths, minContourPoints);
        }

        return subpaths.Count > 0 ? subpaths : null;
    }

    /// <summary>
    /// Walks a single <c>a:path</c>, banking each <c>moveTo</c>…<c>close</c> run whose point
    /// count reaches <paramref name="minContourPoints"/> as its own contour. Points are
    /// normalized into the unit square of the path's <c>w</c>/<c>h</c>.
    /// </summary>
    static void AppendPathContours(A.Path path, List<IReadOnlyList<(double X, double Y)>> subpaths, int minContourPoints)
    {
        var pathW = path.Width?.Value ?? 0;
        var pathH = path.Height?.Value ?? 0;
        if (pathW <= 0 || pathH <= 0)
        {
            return;
        }

        var currentX = 0d;
        var currentY = 0d;
        List<(double X, double Y)>? contour = null;

        bool TryReadPoint(A.Point? point, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (point?.X is null || point.Y is null)
            {
                return false;
            }

            if (!long.TryParse(point.X.Value, out var px) ||
                !long.TryParse(point.Y.Value, out var py))
            {
                return false;
            }

            x = px / (double) pathW;
            y = py / (double) pathH;
            return true;
        }

        // Bank the in-progress contour (if it has enough points) and reset.
        void Flush()
        {
            if (contour != null &&
                contour.Count >= minContourPoints)
            {
                subpaths.Add(contour);
            }
            contour = null;
        }

        foreach (var child in path.ChildElements)
        {
            switch (child)
            {
                case A.MoveTo move:
                    Flush();
                    if (TryReadPoint(move.Point, out var mx, out var my))
                    {
                        contour = [(mx, my)];
                        currentX = mx;
                        currentY = my;
                    }
                    break;
                case A.LineTo line:
                    if (contour != null &&
                        TryReadPoint(line.Point, out var lx, out var ly))
                    {
                        contour.Add((lx, ly));
                        currentX = lx;
                        currentY = ly;
                    }
                    break;
                case A.CubicBezierCurveTo cubic:
                    {
                        var points = cubic.Elements<A.Point>().ToList();
                        if (contour != null &&
                            points.Count == 3 &&
                            TryReadPoint(points[0], out var c1x, out var c1y) &&
                            TryReadPoint(points[1], out var c2x, out var c2y) &&
                            TryReadPoint(points[2], out var ex, out var ey))
                        {
                            for (var i = 1; i <= bezierFlattenSegments; i++)
                            {
                                var t = i / (double) bezierFlattenSegments;
                                var omt = 1 - t;
                                var x = omt * omt * omt * currentX +
                                        3 * omt * omt * t * c1x +
                                        3 * omt * t * t * c2x +
                                        t * t * t * ex;
                                var y = omt * omt * omt * currentY +
                                        3 * omt * omt * t * c1y +
                                        3 * omt * t * t * c2y +
                                        t * t * t * ey;
                                contour.Add((x, y));
                            }
                            currentX = ex;
                            currentY = ey;
                        }
                        break;
                    }
                case A.QuadraticBezierCurveTo quad:
                    {
                        var points = quad.Elements<A.Point>().ToList();
                        if (contour != null &&
                            points.Count == 2 &&
                            TryReadPoint(points[0], out var c1x, out var c1y) &&
                            TryReadPoint(points[1], out var ex, out var ey))
                        {
                            for (var i = 1; i <= bezierFlattenSegments; i++)
                            {
                                var t = i / (double) bezierFlattenSegments;
                                var omt = 1 - t;
                                var x = omt * omt * currentX +
                                        2 * omt * t * c1x +
                                        t * t * ex;
                                var y = omt * omt * currentY +
                                        2 * omt * t * c1y +
                                        t * t * ey;
                                contour.Add((x, y));
                            }
                            currentX = ex;
                            currentY = ey;
                        }
                        break;
                    }
                case A.CloseShapePath:
                    Flush();
                    break;
            }
        }

        Flush();
    }

    const int bezierFlattenSegments = 12;

    /// <summary>
    /// Reads rotation (degrees clockwise) and flip flags from an <c>a:xfrm</c>. Rotation is
    /// stored in 60,000ths of a degree on <c>@rot</c>; <c>@flipH</c>/<c>@flipV</c> are booleans.
    /// </summary>
    public static (double RotationDegrees, bool FlipHorizontal, bool FlipVertical) ExtractTransform(A.Transform2D? xfrm)
    {
        if (xfrm == null)
        {
            return (0, false, false);
        }

        var rotation = xfrm.Rotation?.Value is { } rot ? rot / 60000.0 : 0;
        return (rotation, xfrm.HorizontalFlip?.Value == true, xfrm.VerticalFlip?.Value == true);
    }

    /// <summary>
    /// Parses a shape within a group, applying group transforms to get individual shape dimensions.
    /// Filters out decorative shapes (those with complex bezier paths).
    /// </summary>
    static FloatingShapeElement? ParseGroupedShape(
        WPS.WordprocessingShape wsp,
        ThemeColors? themeColors,
        AnchorPositioning positioning,
        long chOffX, long chOffY,
        double scaleX, double scaleY,
        MainDocumentPart? mainPart,
        Func<OpenXmlPart, byte[]>? partBytes,
        (double Left, double Top, double Width, double Height)? groupFrame = null)
    {
        var shapeProps = wsp.GetFirstChild<WPS.ShapeProperties>();
        if (shapeProps == null)
        {
            return null;
        }

        // Filter out decorative shapes (complex paths with curves)
        if (IsDecorativeShape(shapeProps))
        {
            return null;
        }

        // Get shape transform first (needed for both fill types)
        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();
        if (xfrm == null)
        {
            return null;
        }

        var off = xfrm.Offset;
        var ext = xfrm.Extents;
        if (off == null || ext == null)
        {
            return null;
        }

        // Shape position in child coordinates (relative to group)
        long shapeX = off.X ?? 0;
        long shapeY = off.Y ?? 0;
        long shapeCx = ext.Cx ?? 0;
        long shapeCy = ext.Cy ?? 0;

        if (shapeCx == 0 || shapeCy == 0)
        {
            return null;
        }

        // Apply group transform: scale and translate
        // Position: (shapePos - childOffset) * scale, then convert to points
        var xPt = ((shapeX - chOffX) * scaleX).EmuToPoints();
        var yPt = ((shapeY - chOffY) * scaleY).EmuToPoints();
        var widthPt = (shapeCx * scaleX).EmuToPoints();
        var heightPt = (shapeCy * scaleY).EmuToPoints();

        var (lineColor, lineWidth, lineAlpha) = ExtractLineStyle(wsp, shapeProps, themeColors);
        var preset = ExtractPresetShape(shapeProps);
        var subpaths = ExtractSubpaths(shapeProps)
                       ?? PresetShapeGeometry.TryBuild(shapeProps.GetFirstChild<A.PresetGeometry>(), widthPt, heightPt);
        var (rotation, flipH, flipV) = ExtractTransform(xfrm);

        // Word cuts group children at the group's frame; both boxes share the anchor's
        // coordinate space, so the clip is plain geometry on the unit-square contours.
        // Rotated shapes' contours turn about their centre at render and are left unclipped.
        if (groupFrame is { } frame && rotation == 0)
        {
            var contours = subpaths ?? [[(0d, 0d), (1d, 0d), (1d, 1d), (0d, 1d)]];
            var clipped = GroupFrameClipper.Clip(
                contours,
                positioning.HorizontalPositionPoints + xPt,
                positioning.VerticalPositionPoints + yPt,
                widthPt,
                heightPt,
                frame.Left,
                frame.Top,
                frame.Width,
                frame.Height);
            if (clipped.Count == 0)
            {
                return null;
            }

            if (!ReferenceEquals(clipped, contours))
            {
                subpaths = clipped;
            }
        }

        // Try solid fill first
        var solidFill = shapeProps.GetFirstChild<A.SolidFill>();
        if (solidFill != null)
        {
            var fillColorHex = ExtractSolidFillColor(solidFill, themeColors);
            if (fillColorHex != null)
            {
                return new()
                {
                    WidthPoints = widthPt,
                    HeightPoints = heightPt,
                    RelativeHeight = positioning.RelativeHeight,
                    LayoutInCell = positioning.LayoutInCell,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints + xPt,
                    VerticalPositionPoints = positioning.VerticalPositionPoints + yPt,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    // Percent sizing intentionally not propagated to grouped sub-shapes:
                    // they're already sized by the group's EMU transform (scaleX/scaleY),
                    // so applying the anchor's pctWidth/pctHeight on top would double-scale.
                    FillColorHex = fillColorHex,
                    FillAlpha = ExtractSolidFillAlpha(solidFill),
                    LineColorHex = lineColor,
                    LineWidthPoints = lineWidth,
                LineAlpha = lineAlpha,
                    Preset = preset,
                    Subpaths = subpaths,
                    RotationDegrees = rotation,
                    FlipHorizontal = flipH,
                    FlipVertical = flipV
                };
            }
        }

        // Try blip fill (image fill)
        var blipFill = shapeProps.GetFirstChild<A.BlipFill>();
        if (blipFill != null &&
            mainPart != null)
        {
            var (imageData, contentType) = ExtractBlipFillImage(blipFill, mainPart, partBytes);
            if (imageData != null)
            {
                return new()
                {
                    WidthPoints = widthPt,
                    HeightPoints = heightPt,
                    RelativeHeight = positioning.RelativeHeight,
                    LayoutInCell = positioning.LayoutInCell,
                    HorizontalPositionPoints = positioning.HorizontalPositionPoints + xPt,
                    VerticalPositionPoints = positioning.VerticalPositionPoints + yPt,
                    HorizontalAnchor = positioning.HorizontalAnchor,
                    VerticalAnchor = positioning.VerticalAnchor,
                    BehindText = true,
                    // Percent sizing intentionally not propagated — see solid-fill branch.
                    ImageData = imageData,
                    ImageContentType = contentType,
                    LineColorHex = lineColor,
                    LineWidthPoints = lineWidth,
                LineAlpha = lineAlpha,
                    Preset = preset,
                    Subpaths = subpaths,
                    RotationDegrees = rotation,
                    FlipHorizontal = flipH,
                    FlipVertical = flipV
                };
            }
        }

        return TryBuildOutlineOnlyShape(wsp, shapeProps, positioning, widthPt, heightPt,
            xPt, yPt, lineColor, lineWidth, lineAlpha, preset, subpaths, rotation, flipH, flipV);
    }

    /// <summary>
    /// Determines if a shape is too complex / degenerate to render as a fillable polygon.
    /// Bezier curves are now flattened into contour points by <see cref="ExtractSubpaths"/>,
    /// so the only remaining filter is for ArcTo paths (unsupported flattening) and degenerate
    /// thin-line aspect ratios that would render as a line rather than a shape.
    /// </summary>
    static bool IsDecorativeShape(WPS.ShapeProperties shapeProps)
    {
        // Custom geometries with ArcTo segments aren't supported by the polygon flattener;
        // its parameter set differs from the de Casteljau bezier walk we use everywhere else.
        var custGeom = shapeProps.GetFirstChild<A.CustomGeometry>();
        if (custGeom?.Descendants<A.ArcTo>().Any() == true)
        {
            return true;
        }

        // Check aspect ratio as a backup heuristic
        var xfrm = shapeProps.GetFirstChild<A.Transform2D>();
        if (xfrm?.Extents != null)
        {
            long cx = xfrm.Extents.Cx ?? 0;
            long cy = xfrm.Extents.Cy ?? 0;

            if (cx > 0 &&
                cy > 0)
            {
                var aspectRatio = (double)cx / cy;
                // Very thin lines (width > 50x height) are likely decorative
                if (aspectRatio > 50)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the alpha (opacity) value from a solid fill element, as a 0.0–1.0 ratio.
    /// Reads `a:alpha` under either the srgbClr or schemeClr child, or the solidFill itself.
    /// Returns 1.0 (fully opaque) if no alpha is set.
    /// </summary>
    public static double ExtractSolidFillAlpha(A.SolidFill solidFill)
    {
        var alphaElement = solidFill.Descendants<A.Alpha>().FirstOrDefault();
        if (alphaElement?.Val?.HasValue == true)
        {
            return alphaElement.Val.Value / 100000.0;
        }
        return 1.0;
    }

    /// <summary>
    /// Extracts the color from a solid fill element.
    /// </summary>
    public static string? ExtractSolidFillColor(A.SolidFill solidFill, ThemeColors? themeColors)
    {
        // Try RGB color first
        var rgbColor = solidFill.GetFirstChild<A.RgbColorModelHex>();
        if (rgbColor?.Val?.HasValue == true)
        {
            // Check for color transforms on RGB color too
            var transforms = ExtractColorTransforms(rgbColor);
            if (transforms.HasTransforms)
            {
                return ApplyTransformsToRgb(rgbColor.Val.Value!, transforms);
            }
            return rgbColor.Val.Value;
        }

        // Try scheme color (theme-based)
        var schemeClr = solidFill.GetFirstChild<A.SchemeColor>();
        if (schemeClr?.Val?.HasValue == true &&
            themeColors != null)
        {
            // Get the actual XML value (e.g., "tx2" not "Text2")
            var schemeValue = ((IEnumValue)schemeClr.Val.Value).Value;

            // Check for alpha - if nearly invisible, skip this shape
            // Only skip shapes that are less than 5% opaque (nearly invisible)
            var alphaEl = schemeClr.GetFirstChild<A.Alpha>();
            if (alphaEl?.Val is { HasValue: true, Value: < 5000 })
            {
                // Skip nearly invisible shapes
                return null;
            }

            // Extract all color transforms
            var transforms = ExtractColorTransforms(schemeClr);

            return themeColors.ResolveColor(schemeValue, transforms);
        }

        return null;
    }

    /// <summary>
    /// Extracts a 2-stop linear gradient from a:gradFill. Multi-stop gradients are flattened to
    /// the lowest- and highest-position stops; radial / path gradients fall through to null.
    /// </summary>
    public static GradientFill? ExtractGradientFill(A.GradientFill gradFill, ThemeColors? themeColors)
    {
        var stopList = gradFill.GetFirstChild<A.GradientStopList>();
        if (stopList == null)
        {
            return null;
        }

        var stops = stopList.Elements<A.GradientStop>()
            .Where(_ => _.Position?.HasValue == true)
            .OrderBy(_ => _.Position!.Value)
            .ToList();

        if (stops.Count < 2)
        {
            return null;
        }

        var first = ExtractGradientStopColor(stops[0], themeColors);
        var last = ExtractGradientStopColor(stops[^1], themeColors);
        if (first == null || last == null)
        {
            return null;
        }

        // a:lin/@ang is in 60000ths of a degree, measured clockwise from horizontal-X-axis.
        // Linear gradient is the only type we model; path/radial fall through to null.
        var lin = gradFill.GetFirstChild<A.LinearGradientFill>();
        var angle = lin?.Angle?.Value is { } ang ? ang / 60000.0 : 0.0;

        return new()
        {
            StartColorHex = first,
            EndColorHex = last,
            DirectionDegrees = angle
        };
    }

    static string? ExtractGradientStopColor(A.GradientStop stop, ThemeColors? themeColors)
    {
        var rgb = stop.GetFirstChild<A.RgbColorModelHex>();
        if (rgb?.Val?.HasValue == true)
        {
            return rgb.Val.Value;
        }

        var scheme = stop.GetFirstChild<A.SchemeColor>();
        if (scheme?.Val?.HasValue == true && themeColors != null)
        {
            var schemeValue = ((IEnumValue) scheme.Val.Value).Value;
            var transforms = ExtractColorTransforms(scheme);
            return themeColors.ResolveColor(schemeValue, transforms);
        }

        return null;
    }

    /// <summary>
    /// Extracts image data from a blip fill element. <paramref name="partBytes"/> is the
    /// caller's per-parse part-buffer cache, so repeated references share one array.
    /// </summary>
    public static (byte[]? ImageData, string? ContentType) ExtractBlipFillImage(A.BlipFill blipFill, MainDocumentPart mainPart, Func<OpenXmlPart, byte[]>? partBytes)
    {
        var blip = blipFill.GetFirstChild<A.Blip>();
        if (blip == null)
        {
            return (null, null);
        }

        var embedAttribute = blip.Embed?.Value;
        if (string.IsNullOrEmpty(embedAttribute))
        {
            return (null, null);
        }

        // Try to get the image part
        if (mainPart.GetPartById(embedAttribute) is not ImagePart imagePart)
        {
            return (null, null);
        }

        byte[] imageData;
        if (partBytes != null)
        {
            imageData = partBytes(imagePart);
        }
        else
        {
            using var stream = imagePart.GetStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            imageData = ms.ToArray();
        }

        if (imageData.Length == 0)
        {
            return (null, null);
        }

        return (imageData, imagePart.ContentType);
    }

    /// <summary>
    /// Applies any <c>lumMod</c>/<c>lumOff</c>/<c>satMod</c>/<c>satOff</c>/<c>tint</c>/<c>shade</c>
    /// child of a literal colour element (<c>a:srgbClr</c>, <c>a:sysClr</c>) to its RGB value. A
    /// theme colour resolves its transforms through <see cref="ThemeColors"/> instead.
    /// </summary>
    public static string ApplyLiteralColorTransforms(string hexColor, OpenXmlElement colorElement)
    {
        var transforms = ExtractColorTransforms(colorElement);
        return transforms.HasTransforms ? ApplyTransformsToRgb(hexColor, transforms) : hexColor;
    }

    /// <summary>
    /// Extracts color transform parameters from a color element.
    /// </summary>
    public static ColorTransforms ExtractColorTransforms(OpenXmlElement colorElement)
    {
        byte? shade = null;
        byte? tint = null;
        double? lumMod = null;
        double? lumOff = null;
        double? satMod = null;
        double? satOff = null;

        // Shade (0-100000 -> 0-255)
        var shadeEl = colorElement.GetFirstChild<A.Shade>();
        if (shadeEl?.Val?.HasValue == true)
        {
            shade = (byte)Math.Clamp((int)(shadeEl.Val.Value / 100000.0 * 255), 0, 255);
        }

        // Tint (0-100000 -> 0-255)
        var tintEl = colorElement.GetFirstChild<A.Tint>();
        if (tintEl?.Val?.HasValue == true)
        {
            tint = (byte)Math.Clamp((int)(tintEl.Val.Value / 100000.0 * 255), 0, 255);
        }

        // Luminance modulation (0-100000+ -> percentage, e.g., 75000 -> 75%)
        var lumModEl = colorElement.GetFirstChild<A.LuminanceModulation>();
        if (lumModEl?.Val?.HasValue == true)
        {
            lumMod = lumModEl.Val.Value / 1000.0;
        }

        // Luminance offset (0-100000 -> percentage points)
        var lumOffEl = colorElement.GetFirstChild<A.LuminanceOffset>();
        if (lumOffEl?.Val?.HasValue == true)
        {
            lumOff = lumOffEl.Val.Value / 1000.0;
        }

        // Saturation modulation (0-100000+ -> percentage)
        var satModEl = colorElement.GetFirstChild<A.SaturationModulation>();
        if (satModEl?.Val?.HasValue == true)
        {
            satMod = satModEl.Val.Value / 1000.0;
        }

        // Saturation offset (0-100000 -> percentage points)
        var satOffEl = colorElement.GetFirstChild<A.SaturationOffset>();
        if (satOffEl?.Val?.HasValue == true)
        {
            satOff = satOffEl.Val.Value / 1000.0;
        }

        return new()
        {
            Shade = shade,
            Tint = tint,
            LumMod = lumMod,
            LumOff = lumOff,
            SatMod = satMod,
            SatOff = satOff
        };
    }

    /// <summary>
    /// Applies color transforms directly to an RGB hex color.
    /// </summary>
    static string ApplyTransformsToRgb(string hexColor, ColorTransforms transforms)
    {
        // For direct RGB colors with transforms, we need to apply the transforms ourselves
        // This is a simplified version - for full support, use ThemeColors
        if (!TryParseHexColor(hexColor, out var r, out var g, out var b))
        {
            return hexColor;
        }

        // Apply HSL transforms if present
        if (transforms.LumMod.HasValue || transforms.SatMod.HasValue ||
            transforms.LumOff.HasValue || transforms.SatOff.HasValue)
        {
            RgbToHsl(r, g, b, out var h, out var s, out var l);

            if (transforms.SatMod.HasValue)
            {
                s *= transforms.SatMod.Value / 100.0;
            }

            if (transforms.SatOff.HasValue)
            {
                s += transforms.SatOff.Value / 100.0;
            }

            if (transforms.LumMod.HasValue)
            {
                l *= transforms.LumMod.Value / 100.0;
            }

            if (transforms.LumOff.HasValue)
            {
                l += transforms.LumOff.Value / 100.0;
            }

            s = Math.Clamp(s, 0.0, 1.0);
            l = Math.Clamp(l, 0.0, 1.0);

            HslToRgb(h, s, l, out r, out g, out b);
        }

        // Apply shade/tint transforms
        // Per ECMA-376: shade darkens the color, tint lightens it
        // Values are in 0-255 scale
        if (transforms.Shade is > 0)
        {
            var shade = transforms.Shade.Value;
            r = (byte)(r * shade / 255);
            g = (byte)(g * shade / 255);
            b = (byte)(b * shade / 255);
        }

        if (transforms.Tint is > 0)
        {
            var tint = transforms.Tint.Value;
            r = (byte)(r + (255 - r) * tint / 255);
            g = (byte)(g + (255 - g) * tint / 255);
            b = (byte)(b + (255 - b) * tint / 255);
        }

        return $"{r:X2}{g:X2}{b:X2}";
    }

    static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (hex.Length != 6)
        {
            return false;
        }

        return byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out r) &&
               byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out g) &&
               byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out b);
    }

    static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;

        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;

        l = (max + min) / 2.0;

        if (delta == 0)
        {
            h = 0;
            s = 0;
        }
        else
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == rd)
            {
                h = ((gd - bd) / delta + (gd < bd ? 6 : 0)) / 6.0;
            }
            else if (max == gd)
            {
                h = ((bd - rd) / delta + 2) / 6.0;
            }
            else
            {
                h = ((rd - gd) / delta + 4) / 6.0;
            }
        }
    }

    static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double rd, gd, bd;

        if (s == 0)
        {
            rd = gd = bd = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;

            rd = HueToRgb(p, q, h + 1.0 / 3.0);
            gd = HueToRgb(p, q, h);
            bd = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        r = (byte)Math.Round(rd * 255);
        g = (byte)Math.Round(gd * 255);
        b = (byte)Math.Round(bd * 255);
    }

    static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6.0)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 1.0 / 2.0)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + (q - p) * (2.0 / 3.0 - t) * 6;
        }

        return p;
    }
}
