using A = DocumentFormat.OpenXml.Drawing;
using XDR = DocumentFormat.OpenXml.Drawing.Spreadsheet;

/// <summary>
/// Parses a worksheet's drawing part — the pictures and shapes floating over the grid rather than
/// living in it.
///
/// Anything a sheet draws instead of storing in cells arrives here, and for some sheets that is
/// everything a reader sees: <c>invoice-accessibility-guide</c>'s first sheet holds twelve cells of
/// narrow column-A text that Excel all but clips away, while its green banner, contents list and
/// thumbnail are entirely drawing.
///
/// A drawing anchors to CELLS, not to the page — <c>from</c> column 1 offset 6350 EMU — so the
/// rectangle is resolved through the same <see cref="SheetGeometry"/> the grid was built from. Every
/// anchor in the corpus is <c>twoCellAnchor</c> (95 of them), but one-cell and absolute anchors cost
/// little to support alongside it.
///
/// Both anchors are relative to the GRID rather than the page: horizontally to the margin, because
/// the grid starts at the content box's left edge, and vertically to the flow cursor, which still
/// sits at the content top when these are emitted. Anchoring X to the page instead drops the left
/// margin and shifts every drawing off by it.
/// </summary>
sealed class SheetDrawingParser(ThemeColors? themeColors, ThemeFonts? themeFonts, DrawingTextParser textParser, Func<OpenXmlPart, byte[]> partBytes, CultureInfo culture)
{
    readonly ChartReader chartReader = new(themeColors, themeFonts, culture);

    // The scale of the sheet being parsed, which its charts draw their text and rules at.
    double sheetScale = 1;

    /// <summary>
    /// The sheet's drawings as absolutely positioned floats, in document order (which is z-order).
    ///
    /// Positions are relative to the grid's top-left, and the caller emits these BEFORE the table so
    /// the flow cursor still sits at the content top — the same paragraph-anchor trick the slide
    /// parser relies on, which places each float where it is emitted instead of deferring it. The
    /// fragmenter then moves each to the page its rows land on (<c>PlaceSheetUnderFloats</c>).
    /// </summary>
    /// <remarks>
    /// With <paramref name="printed"/> the drawings Excel does not print are marked
    /// <see cref="DocumentElement.NotPrinted"/>: an anchor whose <c>xdr:clientData</c> says
    /// <c>fPrintsWithSheet="0"</c>, which is how a template marks its on-screen navigation buttons
    /// (invoice-accessibility-guide's "Let's go >" is on the sheet and on none of Excel's pages).
    /// The paged layout leaves those out and the text exports keep them. A sheet drawn whole rather
    /// than printed marks none.
    /// </remarks>
    public List<DocumentElement> Parse(WorksheetPart worksheetPart, SheetGeometry geometry, double scale, uint firstOrdinal, double gridLeftPoints = 0, bool printed = false)
    {
        sheetScale = scale;
        var elements = new List<DocumentElement>();
        var drawingsPart = worksheetPart.DrawingsPart;
        var root = drawingsPart?.WorksheetDrawing;
        if (drawingsPart == null || root == null)
        {
            return elements;
        }

        var ordinal = firstOrdinal;
        foreach (var anchor in root.ChildElements)
        {
            if (Rectangle(anchor, geometry, scale) is not { } box)
            {
                continue;
            }

            var notPrinted = printed &&
                             anchor.GetFirstChild<XDR.ClientData>()?.PrintWithSheet?.Value == false;

            // Anchors are measured from the GRID's left edge, but the elements come out anchored to
            // the page margin — so wherever the grid starts, the art has to start there too. That is
            // zero until printOptions/@horizontalCentered moves the grid off the margin, and every
            // sheet's logo detaches from its cells the moment the two disagree.
            Walk(
                anchor,
                box with
                {
                    X = box.X + gridLeftPoints
                },
                SlideTransform.Identity,
                drawingsPart,
                elements,
                ref ordinal,
                anchorIsRoot: true,
                notPrinted);
        }

        return elements;
    }

    void Walk(
        OpenXmlElement container,
        DrawingBox box,
        SlideTransform transform,
        DrawingsPart drawingsPart,
        List<DocumentElement> elements,
        ref uint ordinal,
        bool anchorIsRoot,
        bool notPrinted)
    {
        foreach (var child in container.ChildElements)
        {
            // A drawing hidden in the selection pane neither shows nor prints
            if (SlideShapeParser.Hidden(child))
            {
                continue;
            }

            switch (child)
            {
                case XDR.Picture picture:
                    if (ParsePicture(picture, box, transform, drawingsPart, anchorIsRoot, ordinal, notPrinted) is { } image)
                    {
                        // A blurred shadow stands under its picture: inventory-list-accessibility-guide's
                        // screenshots each cast one in Excel's print. A turned picture's is left out.
                        if (image is FloatingImageElement { RotationDegrees: 0 } upright)
                        {
                            elements.AddRange(SoftShadow.Layers(
                                picture.ShapeProperties?.GetFirstChild<A.EffectList>()?.GetFirstChild<A.OuterShadow>(),
                                themeColors,
                                (upright.HorizontalPositionPoints, upright.VerticalPositionPoints, upright.WidthPoints, upright.HeightPoints),
                                sheetScale,
                                upright.HorizontalAnchor,
                                upright.VerticalAnchor,
                                ordinal,
                                notPrinted));
                        }

                        elements.Add(image);
                        ordinal++;
                    }

                    break;

                case XDR.Shape shape:
                    if (ParseShape(shape, box, transform, anchorIsRoot, ordinal, notPrinted) is { } drawn)
                    {
                        elements.Add(drawn);
                        ordinal++;
                    }

                    break;

                case XDR.GraphicFrame frame:
                    if (ParseChart(frame, box, transform, drawingsPart, anchorIsRoot, ordinal, notPrinted) is { } chart)
                    {
                        elements.Add(chart);
                        ordinal++;
                    }

                    break;

                case XDR.GroupShape group:
                {
                    // A group's children are authored in its own coordinate space, so the anchor
                    // rectangle maps onto the group's chOff/chExt exactly as it does on a slide.
                    var groupTransform = anchorIsRoot ? GroupTransform(group, box) : transform.Compose(group.GroupShapeProperties?.TransformGroup);
                    Walk(group, box, groupTransform, drawingsPart, elements, ref ordinal, anchorIsRoot: false, notPrinted);
                    break;
                }
            }
        }
    }

    DocumentElement? ParsePicture(
        XDR.Picture picture,
        DrawingBox box,
        SlideTransform transform,
        DrawingsPart drawingsPart,
        bool anchorIsRoot,
        uint ordinal,
        bool notPrinted)
    {
        var blipFill = picture.BlipFill;
        if (blipFill == null)
        {
            return null;
        }

        var (data, contentType, rasterFallback, rasterFallbackContentType) = ShapeParser.ExtractPicture(blipFill, drawingsPart, partBytes);
        if (data is not { Length: > 0 })
        {
            return null;
        }

        var (placed, rotation, flipHorizontal, flipVertical) = Turned(picture.ShapeProperties?.Transform2D, Placement(picture.ShapeProperties?.Transform2D, box, transform, anchorIsRoot), transform, anchorIsRoot);

        return new FloatingImageElement
        {
            RotationDegrees = rotation,
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical,
            ImageData = data,
            ContentType = contentType,
            RasterFallbackData = rasterFallback,
            RasterFallbackContentType = rasterFallbackContentType,
            WidthPoints = placed.Width,
            HeightPoints = placed.Height,
            HorizontalPositionPoints = placed.X,
            VerticalPositionPoints = placed.Y,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            WrapType = WrapType.None,
            Description = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value,
            RelativeHeight = ordinal,
            NotPrinted = notPrinted
        };
    }

    // A graphic frame holding a chart (a:graphicData/c:chart): the chart part read into a model, drawn at
    // layout in the frame's box.
    DocumentElement? ParseChart(
        XDR.GraphicFrame frame,
        DrawingBox box,
        SlideTransform transform,
        DrawingsPart drawingsPart,
        bool anchorIsRoot,
        uint ordinal,
        bool notPrinted)
    {
        var reference = frame.Descendants().FirstOrDefault(_ => _.LocalName == "chart");
        var relationshipId = reference?.GetAttributes().FirstOrDefault(_ => _.LocalName == "id").Value;
        if (string.IsNullOrEmpty(relationshipId) ||
            !drawingsPart.TryGetPartById(relationshipId, out var part) ||
            chartReader.Read(part.RootElement) is not { } chart)
        {
            return null;
        }

        var transform2D = frame.Transform;
        var placed = anchorIsRoot || transform2D == null
            ? box
            : Placement(
                new(
                    new A.Offset
                    {
                        X = transform2D.Offset?.X,
                        Y = transform2D.Offset?.Y
                    },
                    new A.Extents
                    {
                        Cx = transform2D.Extents?.Cx,
                        Cy = transform2D.Extents?.Cy
                    }),
                box,
                transform,
                anchorIsRoot);

        return new FloatingChartElement
        {
            Chart = chart,
            WidthPoints = placed.Width,
            HeightPoints = placed.Height,
            HorizontalPositionPoints = placed.X,
            VerticalPositionPoints = placed.Y,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            RelativeHeight = ordinal,
            Scale = sheetScale,
            NotPrinted = notPrinted
        };
    }

    DocumentElement? ParseShape(XDR.Shape shape, DrawingBox box, SlideTransform transform, bool anchorIsRoot, uint ordinal, bool notPrinted)
    {
        var properties = shape.ShapeProperties;
        var (placed, rotation, flipHorizontal, flipVertical) = Turned(properties?.Transform2D, Placement(properties?.Transform2D, box, transform, anchorIsRoot), transform, anchorIsRoot);

        var solid = properties?.GetFirstChild<A.SolidFill>();
        var fill = solid != null ? ShapeParser.ExtractSolidFillColor(solid, themeColors) : null;
        var gradient = properties?.GetFirstChild<A.GradientFill>() is { } gradFill
            ? ShapeParser.ExtractGradientFill(gradFill, themeColors)
            : null;

        var outline = properties?.GetFirstChild<A.Outline>();
        var outlineFill = outline?.GetFirstChild<A.SolidFill>();
        var lineColor = outlineFill != null ? ShapeParser.ExtractSolidFillColor(outlineFill, themeColors) : null;

        // A shape carrying text becomes a text box so its label renders; one carrying only a fill or
        // outline becomes a plain shape.
        if (shape.TextBody is { } body &&
            body.Descendants<A.Text>().Any(_ => !string.IsNullOrWhiteSpace(_.Text)))
        {
            // A shape's text shrinks with the sheet as its box does, and stands in from the box by
            // DrawingML's insets (0.1in at the sides, 0.05in top and bottom unless bodyPr says): at 100%
            // a 16pt label filled inventory-list-accessibility-guide's 33pt buttons, printed at 46%, and
            // "Conditional formatting" wrapped where Excel fits it on one line
            var bodyProperties = body.BodyProperties;
            double Inset(int? declared, int fallback) => (declared ?? fallback) / OoxmlUnits.EmusPerPoint * sheetScale;
            return new FloatingTextBoxElement
            {
                // Text no run colours takes its style's fontRef colour: basic-business-invoice's
                // "LOGO GOES HERE" is lt1, white on its blue square, where it drew black
                Content = textParser.Parse(body, new([]), sheetScale, fontReferenceColor: ShapeParser.ExtractFontReferenceColor(shape.ShapeStyle?.FontReference, themeColors)),
                Insets = new(
                    Inset(bodyProperties?.LeftInset?.Value, 91440),
                    Inset(bodyProperties?.TopInset?.Value, 45720),
                    Inset(bodyProperties?.RightInset?.Value, 91440),
                    Inset(bodyProperties?.BottomInset?.Value, 45720)),
                WidthPoints = placed.Width,
                HeightPoints = placed.Height,
                HorizontalPositionPoints = placed.X,
                VerticalPositionPoints = placed.Y,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Paragraph,
                BackgroundColorHex = fill,
                LineColorHex = lineColor,
                LineWidthPoints = outline?.Width?.Value is { } width ? width / OoxmlUnits.EmusPerPoint : 0,
                RelativeHeight = ordinal,
                NotPrinted = notPrinted,
                Subpaths = ShapeParser.TextBoxOutline(properties, placed.Width, placed.Height),
                RotationDegrees = rotation,
                VerticalAlignment = ShapeParser.TextAnchor(body.BodyProperties?.Anchor?.Value)
            };
        }

        if (fill == null &&
            gradient == null &&
            lineColor == null)
        {
            return null;
        }

        return new FloatingShapeElement
        {
            WidthPoints = placed.Width,
            HeightPoints = placed.Height,
            HorizontalPositionPoints = placed.X,
            VerticalPositionPoints = placed.Y,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            FillColorHex = fill,
            FillAlpha = solid != null ? ShapeParser.ExtractSolidFillAlpha(solid) : 1,
            Gradient = gradient,
            LineColorHex = lineColor,
            LineWidthPoints = outline?.Width?.Value is { } lineWidth ? lineWidth / OoxmlUnits.EmusPerPoint : null,
            Preset = properties?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value == A.ShapeTypeValues.Ellipse
                ? PresetShape.Ellipse
                : PresetShape.Rect,
            Subpaths = ShapeParser.ExtractSubpaths(properties!) ??
                       PresetShapeGeometry.TryBuild(properties!.GetFirstChild<A.PresetGeometry>(), placed.Width, placed.Height),
            RotationDegrees = rotation,
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical,
            RelativeHeight = ordinal,
            NotPrinted = notPrinted
        };
    }

    /// <summary>
    /// Where a shape lands. Directly under an anchor the anchor's own rectangle wins — it is the
    /// authority, and the shape's <c>a:xfrm</c> merely restates it in sheet coordinates. Inside a
    /// group the transform decides, because only the group is anchored.
    /// </summary>
    static DrawingBox Placement(A.Transform2D? shapeTransform, DrawingBox box, SlideTransform transform, bool anchorIsRoot)
    {
        if (anchorIsRoot || shapeTransform == null)
        {
            return box;
        }

        var offsetX = shapeTransform.Offset?.X?.Value ?? 0;
        var offsetY = shapeTransform.Offset?.Y?.Value ?? 0;
        var extentX = shapeTransform.Extents?.Cx?.Value ?? 0;
        var extentY = shapeTransform.Extents?.Cy?.Value ?? 0;

        var (x, y, width, height) = transform.Apply(offsetX, offsetY, extentX, extentY);
        return new(x / OoxmlUnits.EmusPerPoint, y / OoxmlUnits.EmusPerPoint, width / OoxmlUnits.EmusPerPoint, height / OoxmlUnits.EmusPerPoint);
    }

    /// <summary>
    /// A drawing's turn and flips (<c>a:xfrm/@rot</c>, <c>@flipH</c>, <c>@flipV</c>), with the box it
    /// turns in. An anchor holds the drawing's frame as it stands on the sheet, which Excel swaps end
    /// for end when the turn lays the drawing on its side; the frame turned is the drawing's own
    /// extent, scaled as the anchor is, about the anchor's centre. home-contents-inventory-list's
    /// blender and lamp lean 30 and 20 degrees and stood upright.
    /// </summary>
    (DrawingBox Box, double Rotation, bool FlipHorizontal, bool FlipVertical) Turned(A.Transform2D? shapeTransform, DrawingBox placed, SlideTransform transform, bool anchorIsRoot)
    {
        // Inside a group the group's own turn and mirror carry down to its members, as on a slide
        if (!anchorIsRoot &&
            SlideShapeParser.ResolveGeometry(shapeTransform, transform) is { } member)
        {
            return (new(member.X, member.Y, member.Width, member.Height), member.RotationDegrees, member.FlipHorizontal, member.FlipVertical);
        }

        var rotation = (shapeTransform?.Rotation?.Value ?? 0) / 60000.0;
        var flipHorizontal = shapeTransform?.HorizontalFlip?.Value == true;
        var flipVertical = shapeTransform?.VerticalFlip?.Value == true;
        if (rotation == 0 ||
            !anchorIsRoot ||
            shapeTransform?.Extents is not { Cx.Value: > 0, Cy.Value: > 0 } extent)
        {
            return (placed, rotation, flipHorizontal, flipVertical);
        }

        var width = extent.Cx!.Value / OoxmlUnits.EmusPerPoint * sheetScale;
        var height = extent.Cy!.Value / OoxmlUnits.EmusPerPoint * sheetScale;
        var centerX = placed.X + placed.Width / 2;
        var centerY = placed.Y + placed.Height / 2;
        return (new(centerX - width / 2, centerY - height / 2, width, height), rotation, flipHorizontal, flipVertical);
    }

    /// <summary>
    /// Maps an anchored group's child coordinate space onto the sheet: the group's own frame
    /// (<c>a:off</c>/<c>a:ext</c>) onto the anchor rectangle — swapped end for end about its centre
    /// when the group's turn lays it on its side, as for a drawing (<see cref="Turned"/>) — and then
    /// the group's turn, mirror and child space as a slide composes them. Nested groups compose on
    /// from there. to-do-list's leaf is a group turned 344 degrees, and stood upright; a group
    /// inside a group was mapped onto the outer anchor as though it were the outer group.
    /// </summary>
    static SlideTransform GroupTransform(XDR.GroupShape group, DrawingBox box)
    {
        var xfrm = group.GroupShapeProperties?.TransformGroup;
        var extentX = (double) (xfrm?.Extents?.Cx?.Value ?? 0);
        var extentY = (double) (xfrm?.Extents?.Cy?.Value ?? 0);
        if (xfrm == null ||
            extentX <= 0 ||
            extentY <= 0)
        {
            var childExtentX = xfrm?.ChildExtents?.Cx?.Value ?? 0;
            var childExtentY = xfrm?.ChildExtents?.Cy?.Value ?? 0;
            var childOffsetX = xfrm?.ChildOffset?.X?.Value ?? 0;
            var childOffsetY = xfrm?.ChildOffset?.Y?.Value ?? 0;
            var scaleX = childExtentX > 0 ? box.Width * OoxmlUnits.EmusPerPoint / childExtentX : 1;
            var scaleY = childExtentY > 0 ? box.Height * OoxmlUnits.EmusPerPoint / childExtentY : 1;
            return new(scaleX, 0, 0, scaleY, box.X * OoxmlUnits.EmusPerPoint - childOffsetX * scaleX, box.Y * OoxmlUnits.EmusPerPoint - childOffsetY * scaleY);
        }

        var degrees = ((xfrm.Rotation?.Value ?? 0) / 60000.0 % 360 + 360) % 360;
        var onItsSide = degrees is >= 45 and < 135 or >= 225 and < 315;
        var frameWidth = onItsSide ? box.Height : box.Width;
        var frameHeight = onItsSide ? box.Width : box.Height;
        var frameX = box.X + box.Width / 2 - frameWidth / 2;
        var frameY = box.Y + box.Height / 2 - frameHeight / 2;
        var frameScaleX = frameWidth * OoxmlUnits.EmusPerPoint / extentX;
        var frameScaleY = frameHeight * OoxmlUnits.EmusPerPoint / extentY;
        var frame = new SlideTransform(
            frameScaleX,
            0,
            0,
            frameScaleY,
            frameX * OoxmlUnits.EmusPerPoint - (xfrm.Offset?.X?.Value ?? 0) * frameScaleX,
            frameY * OoxmlUnits.EmusPerPoint - (xfrm.Offset?.Y?.Value ?? 0) * frameScaleY);
        return frame.Compose(xfrm);
    }

    /// <summary>
    /// The anchor's rectangle in points, relative to the grid's top-left. A two-cell anchor spans
    /// from one cell offset to another; a one-cell anchor pins its top-left and carries its own
    /// extent; an absolute anchor is already in EMU from the sheet origin.
    /// </summary>
    /// <summary>
    /// Whether a printed drawing of the sheet starts between <paramref name="fromPoints"/> and
    /// <paramref name="toPoints"/> across the print area's grid — what keeps a page strip holding no
    /// value from being skipped. A drawing reaching into the strip from one before does not:
    /// to-do-list's chart runs on into column L, and Excel prints no page for L and M.
    /// </summary>
    public static bool AnyStartsIn(WorksheetPart worksheetPart, SheetGeometry geometry, double scale, double fromPoints, double toPoints)
    {
        if (worksheetPart.DrawingsPart?.WorksheetDrawing is not { } root)
        {
            return false;
        }

        foreach (var anchor in root.ChildElements)
        {
            if (anchor.GetFirstChild<XDR.ClientData>()?.PrintWithSheet?.Value != false &&
                Rectangle(anchor, geometry, scale) is { } box &&
                box.X >= fromPoints &&
                box.X < toPoints)
            {
                return true;
            }
        }

        return false;
    }

    static DrawingBox? Rectangle(OpenXmlElement anchor, SheetGeometry geometry, double scale)
    {
        var from = anchor.GetFirstChild<XDR.FromMarker>();
        if (from == null)
        {
            if (anchor is not XDR.AbsoluteAnchor absolute)
            {
                return null;
            }

            return new(
                (absolute.Position?.X?.Value ?? 0) / OoxmlUnits.EmusPerPoint * scale,
                (absolute.Position?.Y?.Value ?? 0) / OoxmlUnits.EmusPerPoint * scale,
                (absolute.Extent?.Cx?.Value ?? 0) / OoxmlUnits.EmusPerPoint * scale,
                (absolute.Extent?.Cy?.Value ?? 0) / OoxmlUnits.EmusPerPoint * scale);
        }

        var (x, y) = Marker(from, geometry, scale);

        if (anchor.GetFirstChild<XDR.ToMarker>() is { } to)
        {
            var (right, bottom) = Marker(to, geometry, scale);
            return new(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }

        var extent = anchor.GetFirstChild<XDR.Extent>();
        return new(
            x,
            y,
            (extent?.Cx?.Value ?? 0) / OoxmlUnits.EmusPerPoint * scale,
            (extent?.Cy?.Value ?? 0) / OoxmlUnits.EmusPerPoint * scale);
    }

    /// <summary>
    /// A marker's position. Its column and row are ZERO-based while the range is one-based, and its
    /// offsets are EMU that scale with the sheet just as the cells do.
    ///
    /// An offset reaches no further than its own cell: Excel reads one past the cell's far edge as
    /// that edge. Probed over COM (Excel 16, 2026-11-22), reading back each shape's Left and Width:
    /// back-to-school-planner's Budget breakdown chart ends 5048250 EMU (397.5pt) into column E,
    /// 363.6pt wide, and Excel ends it at 611.1pt against E's right edge at 611.4pt; 9000000 EMU
    /// into column D ended at D's edge, 9000000 into the FROM marker's column C started at C's
    /// edge, and 2000000 EMU into a 30pt row ended at that row's bottom. The chart, drawn its full
    /// offset, ran 34pt past the page's right edge.
    /// </summary>
    static (double X, double Y) Marker(OpenXmlElement marker, SheetGeometry geometry, double scale)
    {
        var column = int.TryParse(marker.GetFirstChild<XDR.ColumnId>()?.Text, out var c) ? c : 0;
        var row = int.TryParse(marker.GetFirstChild<XDR.RowId>()?.Text, out var r) ? r : 0;
        var columnOffset = long.TryParse(marker.GetFirstChild<XDR.ColumnOffset>()?.Text, out var co) ? co : 0;
        var rowOffset = long.TryParse(marker.GetFirstChild<XDR.RowOffset>()?.Text, out var ro) ? ro : 0;

        var left = geometry.ColumnLeft(column + 1);
        var top = geometry.RowTop(row + 1);
        return (
            left + Math.Min(columnOffset / OoxmlUnits.EmusPerPoint * scale, geometry.ColumnLeft(column + 2) - left),
            top + Math.Min(rowOffset / OoxmlUnits.EmusPerPoint * scale, geometry.RowTop(row + 2) - top));
    }
}

/// <summary>A drawing's rectangle in points, relative to the grid's top-left.</summary>
readonly record struct DrawingBox(double X, double Y, double Width, double Height);
