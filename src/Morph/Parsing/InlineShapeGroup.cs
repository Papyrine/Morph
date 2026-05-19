/// <summary>
/// An inline drawing that contains a group of primitive shapes (lines + rectangles) instead
/// of a single picture. Word emits these for icon-style decorations — e.g. the down-arrow
/// glyph on heading rows, or the colour-arrow accents on cover pages — built up from
/// connector lines (<c>wps:wsp</c> with <c>prstGeom prst="line"</c>) inside a <c>wpg:wgp</c>.
/// </summary>
sealed class InlineShapeGroup
{
    /// <summary>Group's child coordinate-space width (matches <c>wpg:grpSpPr/a:xfrm/a:chExt/@cx</c>, EMU).</summary>
    public required double ChildExtentX { get; init; }

    /// <summary>Group's child coordinate-space height (EMU).</summary>
    public required double ChildExtentY { get; init; }

    /// <summary>Rotation applied to the whole group (degrees, clockwise).</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Component shapes in document order.</summary>
    public required IReadOnlyList<GroupShape> Shapes { get; init; }
}

/// <summary>
/// A primitive shape inside an <see cref="InlineShapeGroup"/>. Coordinates and dimensions are
/// in the group's child coordinate space (EMU); the renderer scales them into the inline
/// fragment's pixel rectangle.
/// </summary>
sealed class GroupShape
{
    /// <summary>Top-left X in child coordinate space.</summary>
    public required double X { get; init; }

    /// <summary>Top-left Y in child coordinate space.</summary>
    public required double Y { get; init; }

    /// <summary>Width in child coordinate space (zero for vertical lines).</summary>
    public required double Width { get; init; }

    /// <summary>Height in child coordinate space (zero for horizontal lines).</summary>
    public required double Height { get; init; }

    /// <summary>Stroke colour as 6-digit hex (e.g. "4A7F74"). Defaults to black.</summary>
    public string ColorHex { get; init; } = "000000";

    /// <summary>Line/stroke width in EMU. Converted to points by the renderer.</summary>
    public double LineWidthEmu { get; init; }

    /// <summary>True when the shape is flipped vertically (a:xfrm/@flipV="1").</summary>
    public bool FlipVertical { get; init; }

    /// <summary>True when the shape is flipped horizontally (a:xfrm/@flipH="1").</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Geometry preset — currently <c>line</c> or <c>rect</c>.</summary>
    public GroupShapeGeometry Geometry { get; init; } = GroupShapeGeometry.Line;

    /// <summary>Solid fill colour (rectangles only). Null = no fill / stroke-only line.</summary>
    public string? FillColorHex { get; init; }
}

enum GroupShapeGeometry
{
    Line,
    Rectangle
}
