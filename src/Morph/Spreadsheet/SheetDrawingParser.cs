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
sealed class SheetDrawingParser(ThemeColors? themeColors, DrawingTextParser textParser, Func<OpenXmlPart, byte[]> partBytes)
{
    const double emusPerPoint = 914400.0 / 72.0;

    /// <summary>
    /// The sheet's drawings as absolutely positioned floats, in document order (which is z-order).
    ///
    /// Positions are relative to the grid's top-left, and the caller emits these BEFORE the table so
    /// the flow cursor still sits at the content top — the same paragraph-anchor trick the slide
    /// parser relies on, which binds each float to the page being built instead of deferring it.
    /// </summary>
    public List<DocumentElement> Parse(WorksheetPart worksheetPart, SheetGeometry geometry, double scale, uint firstOrdinal)
    {
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

            Walk(anchor, box, SlideTransform.Identity, drawingsPart, elements, ref ordinal, anchorIsRoot: true);
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
        bool anchorIsRoot)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case XDR.Picture picture:
                    if (ParsePicture(picture, box, transform, drawingsPart, anchorIsRoot, ordinal) is { } image)
                    {
                        elements.Add(image);
                        ordinal++;
                    }

                    break;

                case XDR.Shape shape:
                    if (ParseShape(shape, box, transform, anchorIsRoot, ordinal) is { } drawn)
                    {
                        elements.Add(drawn);
                        ordinal++;
                    }

                    break;

                case XDR.GroupShape group:
                {
                    // A group's children are authored in its own coordinate space, so the anchor
                    // rectangle maps onto the group's chOff/chExt exactly as it does on a slide.
                    var groupTransform = GroupTransform(group, box);
                    Walk(group, box, groupTransform, drawingsPart, elements, ref ordinal, anchorIsRoot: false);
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
        uint ordinal)
    {
        var blipFill = picture.BlipFill;
        if (blipFill == null)
        {
            return null;
        }

        var (data, contentType) = ShapeParser.ExtractBlipFillImage(blipFill, drawingsPart, partBytes);
        if (data is not { Length: > 0 })
        {
            return null;
        }

        var placed = Placement(picture.ShapeProperties?.Transform2D, box, transform, anchorIsRoot);

        return new FloatingImageElement
        {
            ImageData = data,
            ContentType = contentType,
            WidthPoints = placed.Width,
            HeightPoints = placed.Height,
            HorizontalPositionPoints = placed.X,
            VerticalPositionPoints = placed.Y,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            WrapType = WrapType.None,
            Description = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value,
            RelativeHeight = ordinal
        };
    }

    DocumentElement? ParseShape(XDR.Shape shape, DrawingBox box, SlideTransform transform, bool anchorIsRoot, uint ordinal)
    {
        var properties = shape.ShapeProperties;
        var placed = Placement(properties?.Transform2D, box, transform, anchorIsRoot);

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
        if (shape.TextBody is { } body && body.Descendants<A.Text>().Any(_ => !string.IsNullOrWhiteSpace(_.Text)))
        {
            return new FloatingTextBoxElement
            {
                Content = textParser.Parse(body, new([])),
                WidthPoints = placed.Width,
                HeightPoints = placed.Height,
                HorizontalPositionPoints = placed.X,
                VerticalPositionPoints = placed.Y,
                HorizontalAnchor = HorizontalAnchor.Margin,
                VerticalAnchor = VerticalAnchor.Paragraph,
                BackgroundColorHex = fill,
                LineColorHex = lineColor,
                LineWidthPoints = outline?.Width?.Value is { } width ? width / emusPerPoint : 0,
                RelativeHeight = ordinal
            };
        }

        if (fill == null && gradient == null && lineColor == null)
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
            LineWidthPoints = outline?.Width?.Value is { } lineWidth ? lineWidth / emusPerPoint : null,
            Preset = properties?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value == A.ShapeTypeValues.Ellipse
                ? PresetShape.Ellipse
                : PresetShape.Rect,
            Subpaths = ShapeParser.ExtractSubpaths(properties!),
            RelativeHeight = ordinal
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
        return new(x / emusPerPoint, y / emusPerPoint, width / emusPerPoint, height / emusPerPoint);
    }

    /// <summary>Maps a group's child coordinate space onto the anchor rectangle it occupies.</summary>
    static SlideTransform GroupTransform(XDR.GroupShape group, DrawingBox box)
    {
        var xfrm = group.GroupShapeProperties?.TransformGroup;
        var childExtentX = xfrm?.ChildExtents?.Cx?.Value ?? 0;
        var childExtentY = xfrm?.ChildExtents?.Cy?.Value ?? 0;
        var childOffsetX = xfrm?.ChildOffset?.X?.Value ?? 0;
        var childOffsetY = xfrm?.ChildOffset?.Y?.Value ?? 0;

        var scaleX = childExtentX > 0 ? box.Width * emusPerPoint / childExtentX : 1;
        var scaleY = childExtentY > 0 ? box.Height * emusPerPoint / childExtentY : 1;

        return new(
            box.X * emusPerPoint - childOffsetX * scaleX,
            box.Y * emusPerPoint - childOffsetY * scaleY,
            scaleX,
            scaleY);
    }

    /// <summary>
    /// The anchor's rectangle in points, relative to the grid's top-left. A two-cell anchor spans
    /// from one cell offset to another; a one-cell anchor pins its top-left and carries its own
    /// extent; an absolute anchor is already in EMU from the sheet origin.
    /// </summary>
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
                (absolute.Position?.X?.Value ?? 0) / emusPerPoint * scale,
                (absolute.Position?.Y?.Value ?? 0) / emusPerPoint * scale,
                (absolute.Extent?.Cx?.Value ?? 0) / emusPerPoint * scale,
                (absolute.Extent?.Cy?.Value ?? 0) / emusPerPoint * scale);
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
            (extent?.Cx?.Value ?? 0) / emusPerPoint * scale,
            (extent?.Cy?.Value ?? 0) / emusPerPoint * scale);
    }

    /// <summary>
    /// A marker's position. Its column and row are ZERO-based while the range is one-based, and its
    /// offsets are EMU that scale with the sheet just as the cells do.
    /// </summary>
    static (double X, double Y) Marker(OpenXmlElement marker, SheetGeometry geometry, double scale)
    {
        var column = int.TryParse(marker.GetFirstChild<XDR.ColumnId>()?.Text, out var c) ? c : 0;
        var row = int.TryParse(marker.GetFirstChild<XDR.RowId>()?.Text, out var r) ? r : 0;
        var columnOffset = long.TryParse(marker.GetFirstChild<XDR.ColumnOffset>()?.Text, out var co) ? co : 0;
        var rowOffset = long.TryParse(marker.GetFirstChild<XDR.RowOffset>()?.Text, out var ro) ? ro : 0;

        return (
            geometry.ColumnLeft(column + 1) + columnOffset / emusPerPoint * scale,
            geometry.RowTop(row + 1) + rowOffset / emusPerPoint * scale);
    }
}

/// <summary>A drawing's rectangle in points, relative to the grid's top-left.</summary>
readonly record struct DrawingBox(double X, double Y, double Width, double Height);
