/// <summary>
/// Shared anchor-resolution math for floating elements (images, text boxes, word art, shapes).
/// All coordinates are in points.
/// </summary>
static class FloatingPosition
{
    /// <summary>
    /// Resolves a floating image / text box / word art's anchor into absolute coordinates and
    /// converts size to pixels. Use <see cref="ResolveShapeBounds"/> for background shapes.
    /// </summary>
    public static FloatingBounds ResolveBounds(
        RenderContextBase context,
        HorizontalAnchor hAnchor,
        VerticalAnchor vAnchor,
        double horizontalPositionPoints,
        double verticalPositionPoints,
        double widthPoints,
        double heightPoints,
        double? horizontalPositionPercent = null,
        double? verticalPositionPercent = null)
    {
        var x = ResolveX(context, hAnchor, horizontalPositionPoints, horizontalPositionPercent);
        var y = ResolveY(context, vAnchor, verticalPositionPoints, verticalPositionPercent);
        return new(
            x,
            y,
            context.PointsToPixels(x),
            context.PointsToPixels(y),
            context.PointsToPixels((float) widthPoints),
            context.PointsToPixels((float) heightPoints));
    }

    /// <summary>
    /// Same as <see cref="ResolveBounds"/> but applies the shape-anchor rules (column/character
    /// fall back to the page margin since shapes are typically full-page backgrounds).
    /// </summary>
    public static FloatingBounds ResolveShapeBounds(
        RenderContextBase context,
        HorizontalAnchor hAnchor,
        VerticalAnchor vAnchor,
        double horizontalPositionPoints,
        double verticalPositionPoints,
        double widthPoints,
        double heightPoints,
        double? horizontalPositionPercent = null,
        double? verticalPositionPercent = null)
    {
        var x = ResolveShapeX(context, hAnchor, horizontalPositionPoints, horizontalPositionPercent);
        var y = ResolveShapeY(context, vAnchor, verticalPositionPoints, verticalPositionPercent);
        return new(
            x,
            y,
            context.PointsToPixels(x),
            context.PointsToPixels(y),
            context.PointsToPixels((float) widthPoints),
            context.PointsToPixels((float) heightPoints));
    }

    /// <summary>
    /// Resolves the absolute X coordinate of a floating image, text box, or word art
    /// from its anchor and offset. When <paramref name="positionPercent"/> is supplied
    /// (from <c>wp14:pctPosHOffset</c>), the offset is computed as that fraction of the
    /// anchor's reference dimension and replaces <paramref name="positionPoints"/>.
    /// </summary>
    public static float ResolveX(RenderContextBase context, HorizontalAnchor anchor, double positionPoints, double? positionPercent = null)
    {
        var baseX = anchor switch
        {
            HorizontalAnchor.Page => 0f,
            HorizontalAnchor.Margin => (float) context.PageSettings.MarginLeft,
            HorizontalAnchor.Column => context.ContentLeft,
            // Approximate
            HorizontalAnchor.Character => context.ContentLeft,
            _ => 0f
        };

        if (positionPercent.HasValue)
        {
            return baseX + (float) (positionPercent.Value * GetHorizontalReference(context, anchor));
        }

        return baseX + (float) positionPoints;
    }

    /// <summary>
    /// Resolves the absolute Y coordinate of a floating image, text box, or word art
    /// from its anchor and offset. When <paramref name="positionPercent"/> is supplied
    /// (from <c>wp14:pctPosVOffset</c>), the offset is computed as that fraction of the
    /// anchor's reference dimension and replaces <paramref name="positionPoints"/>.
    /// </summary>
    public static float ResolveY(RenderContextBase context, VerticalAnchor anchor, double positionPoints, double? positionPercent = null)
    {
        var baseY = anchor switch
        {
            VerticalAnchor.Page => 0f,
            VerticalAnchor.Margin => (float) context.PageSettings.MarginTop,
            // Approximate - relative to current paragraph
            VerticalAnchor.Paragraph => context.CurrentY,
            // Approximate
            VerticalAnchor.Line => context.CurrentY,
            _ => 0f
        };

        if (positionPercent.HasValue)
        {
            return baseY + (float) (positionPercent.Value * GetVerticalReference(context, anchor));
        }

        return baseY + (float) positionPoints;
    }

    /// <summary>
    /// Resolves the absolute X coordinate of a floating shape. Shapes are typically
    /// full-page backgrounds (BehindText), so column- and character-anchored shapes
    /// fall back to the page margin rather than the current column.
    /// </summary>
    public static float ResolveShapeX(RenderContextBase context, HorizontalAnchor anchor, double positionPoints, double? positionPercent = null)
    {
        var baseX = anchor switch
        {
            HorizontalAnchor.Page => 0f,
            _ => (float) context.PageSettings.MarginLeft
        };

        if (positionPercent.HasValue)
        {
            return baseX + (float) (positionPercent.Value * GetHorizontalReference(context, anchor));
        }

        return baseX + (float) positionPoints;
    }

    /// <summary>
    /// Resolves the absolute Y coordinate of a floating shape. Background shapes are
    /// rendered at page start before any content is placed, so paragraph- and
    /// line-anchored shapes use the top margin rather than <c>CurrentY</c>.
    /// </summary>
    public static float ResolveShapeY(RenderContextBase context, VerticalAnchor anchor, double positionPoints, double? positionPercent = null)
    {
        var baseY = anchor switch
        {
            VerticalAnchor.Page => 0f,
            _ => (float) context.PageSettings.MarginTop
        };

        if (positionPercent.HasValue)
        {
            return baseY + (float) (positionPercent.Value * GetVerticalReference(context, anchor));
        }

        return baseY + (float) positionPoints;
    }

    /// <summary>
    /// Reference width used to resolve a <c>wp14:pctPosHOffset</c>. Page-anchored elements
    /// resolve against the full page width; everything else uses the content area
    /// (matching how percentage *sizing* collapses leftMargin/rightMargin/insideMargin/
    /// outsideMargin into a single content-area reference).
    /// </summary>
    static double GetHorizontalReference(RenderContextBase context, HorizontalAnchor anchor) =>
        anchor == HorizontalAnchor.Page
            ? context.PageSettings.WidthPoints
            : context.ContentWidth;

    static double GetVerticalReference(RenderContextBase context, VerticalAnchor anchor) =>
        anchor == VerticalAnchor.Page
            ? context.PageSettings.HeightPoints
            : context.ContentHeight;

    /// <summary>
    /// Computes the effective width and height of a floating element, honouring
    /// <c>wp14:pctWidth</c> / <c>wp14:pctHeight</c> when present (overrides the
    /// literal <c>wp:extent</c>) and falling back to the explicit values otherwise.
    /// </summary>
    public static (double width, double height) ResolveEffectiveSize(
        RenderContextBase context,
        double widthPoints,
        double heightPoints,
        double? widthPercent,
        SizeRelativeFrom widthRelativeFrom,
        double? heightPercent,
        SizeRelativeFrom heightRelativeFrom)
    {
        var resolvedWidth = widthPercent.HasValue
            ? widthPercent.Value * GetReferenceWidth(context, widthRelativeFrom)
            : widthPoints;

        var resolvedHeight = heightPercent.HasValue
            ? heightPercent.Value * GetReferenceHeight(context, heightRelativeFrom)
            : heightPoints;

        return (resolvedWidth, resolvedHeight);
    }

    static double GetReferenceWidth(RenderContextBase context, SizeRelativeFrom relativeFrom) =>
        relativeFrom switch
        {
            SizeRelativeFrom.Page => context.PageSettings.WidthPoints,
            // Margin / leftMargin / rightMargin / inside / outside all collapse to the
            // content area between page margins.
            _ => context.ContentWidth
        };

    static double GetReferenceHeight(RenderContextBase context, SizeRelativeFrom relativeFrom) =>
        relativeFrom switch
        {
            SizeRelativeFrom.Page => context.PageSettings.HeightPoints,
            _ => context.ContentHeight
        };
}
