/// <summary>
/// An inline drawing that contains a group of primitive shapes and pictures instead of a
/// single picture. Word emits these for icon-style decorations — e.g. the down-arrow glyph on
/// heading rows, the colour-arrow accents on cover pages, or an icon graphic sitting on a
/// coloured circle — built up from connector lines, preset geometry (<c>wps:wsp</c>) and
/// pictures (<c>pic:pic</c>) inside a <c>wpg:wgp</c>.
/// </summary>
sealed class InlineShapeGroup
{
    /// <summary>Group's child coordinate-space width (matches <c>wpg:grpSpPr/a:xfrm/a:chExt/@cx</c>, EMU).</summary>
    public required double ChildExtentX { get; init; }

    /// <summary>Group's child coordinate-space height (EMU).</summary>
    public required double ChildExtentY { get; init; }

    /// <summary>Rotation applied to the whole group (degrees, clockwise).</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Component shapes in document order — the renderer paints them back to front.</summary>
    public required IReadOnlyList<GroupShape> Shapes { get; init; }
}

/// <summary>
/// A primitive shape or picture inside an <see cref="InlineShapeGroup"/>. Coordinates and
/// dimensions are in the group's child coordinate space (EMU); the renderer scales them into
/// the inline fragment's pixel rectangle.
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

    /// <summary>Stroke opacity, 0.0 (fully transparent) to 1.0 (fully opaque). Defaults to 1.0.</summary>
    public double LineAlpha { get; init; } = 1.0;

    /// <summary>True when the shape is flipped vertically (a:xfrm/@flipV="1").</summary>
    public bool FlipVertical { get; init; }

    /// <summary>True when the shape is flipped horizontally (a:xfrm/@flipH="1").</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Geometry preset — currently <c>line</c>, <c>rect</c> or <c>ellipse</c>.</summary>
    public GroupShapeGeometry Geometry { get; init; } = GroupShapeGeometry.Line;

    /// <summary>Solid fill colour (rectangles and ellipses only). Null = no fill / stroke-only line.</summary>
    public string? FillColorHex { get; init; }

    /// <summary>Fill opacity, 0.0 (fully transparent) to 1.0 (fully opaque). Defaults to 1.0.</summary>
    public double FillAlpha { get; init; } = 1.0;

    /// <summary>
    /// Image filling the shape, clipped to <see cref="Geometry"/> and taking precedence over
    /// <see cref="FillColorHex"/>. Set for the group's <c>pic:pic</c> members: an icon graphic on
    /// a <c>rect</c>, or a photo circle-cropped by an <c>ellipse</c>. Null for plain shapes.
    /// </summary>
    public byte[]? ImageData { get; init; }

    /// <summary>MIME type of <see cref="ImageData"/>, e.g. <c>image/svg+xml</c> or <c>image/png</c>.</summary>
    public string? ImageContentType { get; init; }

    /// <summary>Raster blob to draw when the backend cannot rasterize an SVG <see cref="ImageData"/>.</summary>
    public byte[]? ImageRasterFallbackData { get; init; }

    /// <summary>Source-rectangle crop (<c>a:srcRect</c>) applied to <see cref="ImageData"/>. Null when uncropped.</summary>
    public ImageCrop? ImageCrop { get; init; }

    /// <summary>Alt text for <see cref="ImageData"/> (the picture's <c>descr</c>/<c>title</c>). Used by the exporters.</summary>
    public string? ImageDescription { get; init; }

    /// <summary>Drop shadow cast behind the shape (<c>a:effectLst/a:outerShdw</c>). Null when there is none.</summary>
    public GroupShadow? Shadow { get; init; }
}

/// <summary>
/// An <c>a:outerShdw</c> drop shadow on a group member — the offset copy of its geometry that
/// Word paints behind it, e.g. under the circle-cropped photos on menu templates.
/// </summary>
sealed class GroupShadow
{
    /// <summary>Horizontal offset in the group's child coordinate space (EMU); positive is right.</summary>
    public required double OffsetX { get; init; }

    /// <summary>Vertical offset in the group's child coordinate space (EMU); positive is down.</summary>
    public required double OffsetY { get; init; }

    /// <summary>Shadow colour as 6-digit hex (e.g. "000000").</summary>
    public string ColorHex { get; init; } = "000000";

    /// <summary>Shadow opacity, 0.0 (invisible) to 1.0 (opaque).</summary>
    public double Alpha { get; init; } = 1.0;
}

enum GroupShapeGeometry
{
    Line,
    Rectangle,
    Ellipse
}
