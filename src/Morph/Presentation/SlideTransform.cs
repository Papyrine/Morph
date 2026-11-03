using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// The accumulated group transform mapping a shape's declared EMU coordinates into slide space.
///
/// A <c>p:grpSp</c> declares two rectangles: <c>a:off</c>/<c>a:ext</c> is where the group sits in its
/// PARENT's space, and <c>a:chOff</c>/<c>a:chExt</c> is the coordinate space its CHILDREN are authored
/// in. A child at <c>cx</c> therefore lands at <c>off.x + (cx − chOff.x) × ext.cx / chExt.cx</c>.
/// Groups nest, so the transforms compose.
///
/// A group's <c>flipH</c>/<c>flipV</c> mirrors its children within the group's own box, which is a
/// NEGATIVE scale on that axis: <see cref="ScaleX"/> or <see cref="ScaleY"/> below zero means
/// everything under it is mirrored (<see cref="MirrorsX"/>, <see cref="MirrorsY"/>). Basic.pptx's
/// agenda layout draws its upper pair of triangles as the lower pair's group with both flips set —
/// read without them, the hourglass pointed both halves the same way.
/// </summary>
readonly record struct SlideTransform(double OffsetX, double OffsetY, double ScaleX, double ScaleY)
{
    public static SlideTransform Identity => new(0, 0, 1, 1);

    /// <summary>Whether a group flip above mirrors its children left to right.</summary>
    public bool MirrorsX => ScaleX < 0;

    /// <summary>Whether a group flip above mirrors its children top to bottom.</summary>
    public bool MirrorsY => ScaleY < 0;

    /// <summary>
    /// Maps a child rectangle (EMU, child space) to slide space (EMU). Under a mirror the child's far
    /// edge lands first, so the rectangle is normalised back to a left/top origin and a positive size.
    /// </summary>
    public (double X, double Y, double Width, double Height) Apply(double x, double y, double width, double height)
    {
        var (left, right) = Span(OffsetX + x * ScaleX, OffsetX + (x + width) * ScaleX);
        var (top, bottom) = Span(OffsetY + y * ScaleY, OffsetY + (y + height) * ScaleY);
        return (left, top, right - left, bottom - top);
    }

    static (double Low, double High) Span(double from, double to)
    {
        if (from <= to)
        {
            return (from, to);
        }

        return (to, from);
    }

    /// <summary>
    /// Composes this transform with a group's own, producing the transform its children use.
    /// A group with no child extent (or a zero one) contributes translation only — dividing by it
    /// would produce infinities, and PowerPoint treats the child space as 1:1 in that case.
    /// </summary>
    public SlideTransform Compose(A.TransformGroup? groupTransform)
    {
        if (groupTransform == null)
        {
            return this;
        }

        var offsetX = groupTransform.Offset?.X?.Value ?? 0;
        var offsetY = groupTransform.Offset?.Y?.Value ?? 0;
        var extentX = groupTransform.Extents?.Cx?.Value ?? 0;
        var extentY = groupTransform.Extents?.Cy?.Value ?? 0;
        var childOffsetX = groupTransform.ChildOffset?.X?.Value ?? 0;
        var childOffsetY = groupTransform.ChildOffset?.Y?.Value ?? 0;
        var childExtentX = groupTransform.ChildExtents?.Cx?.Value ?? 0;
        var childExtentY = groupTransform.ChildExtents?.Cy?.Value ?? 0;

        var scaleX = childExtentX > 0 ? (double) extentX / childExtentX : 1;
        var scaleY = childExtentY > 0 ? (double) extentY / childExtentY : 1;

        // In the group's PARENT space a child coordinate c lands at a + b·c: unmirrored that is
        // off + (c − chOff)·scale, mirrored it is reflected about the group box, off + ext − (c − chOff)·scale.
        var (originX, stepX) = Axis(offsetX, extentX, childOffsetX, scaleX, groupTransform.HorizontalFlip?.Value == true);
        var (originY, stepY) = Axis(offsetY, extentY, childOffsetY, scaleY, groupTransform.VerticalFlip?.Value == true);

        // Then THIS transform maps the parent space to the slide.
        return new(
            OffsetX + ScaleX * originX,
            OffsetY + ScaleY * originY,
            ScaleX * stepX,
            ScaleY * stepY);
    }

    static (double Origin, double Step) Axis(double offset, double extent, double childOffset, double scale, bool flip)
    {
        if (flip)
        {
            return (offset + extent + childOffset * scale, -scale);
        }

        return (offset - childOffset * scale, scale);
    }
}
