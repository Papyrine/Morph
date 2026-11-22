using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// The accumulated group transform mapping a shape's declared EMU coordinates into slide space: an
/// affine map, slide = M · child + offset.
///
/// A <c>p:grpSp</c> declares two rectangles: <c>a:off</c>/<c>a:ext</c> is where the group sits in its
/// PARENT's space, and <c>a:chOff</c>/<c>a:chExt</c> is the coordinate space its CHILDREN are authored
/// in. A child at <c>cx</c> therefore lands at <c>off.x + (cx − chOff.x) × ext.cx / chExt.cx</c>.
/// Groups nest, so the transforms compose.
///
/// A group's <c>flipH</c>/<c>flipV</c> mirrors its children within the group's own box, which is a
/// NEGATIVE scale on that axis: <see cref="MirrorsX"/>, <see cref="MirrorsY"/>. Basic.pptx's agenda
/// layout draws its upper pair of triangles as the lower pair's group with both flips set — read
/// without them, the hourglass pointed both halves the same way.
///
/// A group's <c>rot</c> turns its box about the box's own centre, after the flips, and everything in
/// it turns with it. leaf's title layout holds a leaf drawn upright in a group turned 45° (with
/// <c>flipH</c>); read without the rotation it stood upright on the slide.
/// </summary>
readonly record struct SlideTransform(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static SlideTransform Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>Whether no group above is rotated, so a child's box stays axis-aligned.</summary>
    public bool IsAxisAligned => M12 == 0 && M21 == 0;

    /// <summary>Whether a group flip above mirrors its children left to right (axis-aligned only).</summary>
    public bool MirrorsX => M11 < 0;

    /// <summary>Whether a group flip above mirrors its children top to bottom (axis-aligned only).</summary>
    public bool MirrorsY => M22 < 0;

    /// <summary>Whether the map, rotations aside, mirrors: an odd number of flips above.</summary>
    public bool Mirrors => M11 * M22 - M12 * M21 < 0;

    /// <summary>
    /// The angle, clockwise in degrees, a rotated group above turns its children by. A mirrored map
    /// is read as this rotation after a top-to-bottom flip: any mirrored similarity is
    /// R(θ)·diag(s, −s), with θ read off the first column.
    /// </summary>
    public double RotationDegrees => Math.Atan2(M21, M11) * (180 / Math.PI);

    /// <summary>
    /// Maps a child rectangle (EMU, child space) to slide space (EMU). Under a mirror the child's far
    /// edge lands first, so the rectangle is normalised back to a left/top origin and a positive size.
    /// Under a rotation the box is the child's, centred where its centre lands and scaled by how
    /// much each child axis is stretched; turning it is the caller's (<see cref="RotationDegrees"/>).
    /// An uneven scale outside a rotation would shear the child, which is dropped.
    /// </summary>
    public (double X, double Y, double Width, double Height) Apply(double x, double y, double width, double height)
    {
        if (IsAxisAligned)
        {
            var (left, right) = Span(OffsetX + x * M11, OffsetX + (x + width) * M11);
            var (top, bottom) = Span(OffsetY + y * M22, OffsetY + (y + height) * M22);
            return (left, top, right - left, bottom - top);
        }

        var centerX = x + width / 2;
        var centerY = y + height / 2;
        var mappedCenterX = OffsetX + M11 * centerX + M12 * centerY;
        var mappedCenterY = OffsetY + M21 * centerX + M22 * centerY;
        var mappedWidth = width * Math.Sqrt(M11 * M11 + M21 * M21);
        var mappedHeight = height * Math.Sqrt(M12 * M12 + M22 * M22);
        return (mappedCenterX - mappedWidth / 2, mappedCenterY - mappedHeight / 2, mappedWidth, mappedHeight);
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
        var mirrorX = groupTransform.HorizontalFlip?.Value == true ? -1d : 1d;
        var mirrorY = groupTransform.VerticalFlip?.Value == true ? -1d : 1d;

        // rot is clockwise 60000ths of a degree; in Y-down slide coordinates the clockwise rotation
        // matrix is [[cos, −sin], [sin, cos]].
        var radians = (groupTransform.Rotation?.Value ?? 0) / 60000.0 * Math.PI / 180;
        var cos = radians == 0 ? 1 : Math.Cos(radians);
        var sin = radians == 0 ? 0 : Math.Sin(radians);

        // In the group's PARENT space a child point p lands at c + R·F·(off + (p − chOff)·scale − c),
        // c the centre of the group's box: scaled into the box, mirrored and turned about its centre.
        var turn11 = cos * mirrorX;
        var turn12 = -sin * mirrorY;
        var turn21 = sin * mirrorX;
        var turn22 = cos * mirrorY;
        var centerX = offsetX + extentX / 2.0;
        var centerY = offsetY + extentY / 2.0;
        var baseX = offsetX - childOffsetX * scaleX - centerX;
        var baseY = offsetY - childOffsetY * scaleY - centerY;
        var local11 = turn11 * scaleX;
        var local12 = turn12 * scaleY;
        var local21 = turn21 * scaleX;
        var local22 = turn22 * scaleY;
        var localOffsetX = radians == 0
            ? mirrorX < 0 ? offsetX + extentX + childOffsetX * scaleX : offsetX - childOffsetX * scaleX
            : centerX + turn11 * baseX + turn12 * baseY;
        var localOffsetY = radians == 0
            ? mirrorY < 0 ? offsetY + extentY + childOffsetY * scaleY : offsetY - childOffsetY * scaleY
            : centerY + turn21 * baseX + turn22 * baseY;

        // Then THIS transform maps the parent space to the slide.
        return new(
            M11 * local11 + M12 * local21,
            M11 * local12 + M12 * local22,
            M21 * local11 + M22 * local21,
            M21 * local12 + M22 * local22,
            M11 * localOffsetX + M12 * localOffsetY + OffsetX,
            M21 * localOffsetX + M22 * localOffsetY + OffsetY);
    }
}
