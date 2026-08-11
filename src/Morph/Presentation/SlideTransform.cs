using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// The accumulated group transform mapping a shape's declared EMU coordinates into slide space.
///
/// A <c>p:grpSp</c> declares two rectangles: <c>a:off</c>/<c>a:ext</c> is where the group sits in its
/// PARENT's space, and <c>a:chOff</c>/<c>a:chExt</c> is the coordinate space its CHILDREN are authored
/// in. A child at <c>cx</c> therefore lands at <c>off.x + (cx − chOff.x) × ext.cx / chExt.cx</c>.
/// Groups nest, so the transforms compose.
/// </summary>
readonly record struct SlideTransform(double OffsetX, double OffsetY, double ScaleX, double ScaleY)
{
    public static SlideTransform Identity => new(0, 0, 1, 1);

    /// <summary>Maps a child rectangle (EMU, child space) to slide space (EMU).</summary>
    public (double X, double Y, double Width, double Height) Apply(double x, double y, double width, double height) =>
        (OffsetX + x * ScaleX, OffsetY + y * ScaleY, width * ScaleX, height * ScaleY);

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

        // The group's own origin maps through THIS transform first, then the child space rebases.
        var (groupX, groupY, _, _) = Apply(offsetX, offsetY, 0, 0);

        return new(
            groupX - childOffsetX * scaleX * ScaleX,
            groupY - childOffsetY * scaleY * ScaleY,
            ScaleX * scaleX,
            ScaleY * scaleY);
    }
}
