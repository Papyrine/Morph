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
        double heightPoints)
    {
        var x = ResolveX(context, hAnchor, horizontalPositionPoints);
        var y = ResolveY(context, vAnchor, verticalPositionPoints);
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
        double heightPoints)
    {
        var x = ResolveShapeX(context, hAnchor, horizontalPositionPoints);
        var y = ResolveShapeY(context, vAnchor, verticalPositionPoints);
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
    /// from its anchor and offset.
    /// </summary>
    public static float ResolveX(RenderContextBase context, HorizontalAnchor anchor, double positionPoints)
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

        return baseX + (float) positionPoints;
    }

    /// <summary>
    /// Resolves the absolute Y coordinate of a floating image, text box, or word art
    /// from its anchor and offset.
    /// </summary>
    public static float ResolveY(RenderContextBase context, VerticalAnchor anchor, double positionPoints)
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

        return baseY + (float) positionPoints;
    }

    /// <summary>
    /// Resolves the absolute X coordinate of a floating shape. Shapes are typically
    /// full-page backgrounds (BehindText), so column- and character-anchored shapes
    /// fall back to the page margin rather than the current column.
    /// </summary>
    public static float ResolveShapeX(RenderContextBase context, HorizontalAnchor anchor, double positionPoints)
    {
        var baseX = anchor switch
        {
            HorizontalAnchor.Page => 0f,
            _ => (float) context.PageSettings.MarginLeft
        };

        return baseX + (float) positionPoints;
    }

    /// <summary>
    /// Resolves the absolute Y coordinate of a floating shape. Background shapes are
    /// rendered at page start before any content is placed, so paragraph- and
    /// line-anchored shapes use the top margin rather than <c>CurrentY</c>.
    /// </summary>
    public static float ResolveShapeY(RenderContextBase context, VerticalAnchor anchor, double positionPoints)
    {
        var baseY = anchor switch
        {
            VerticalAnchor.Page => 0f,
            _ => (float) context.PageSettings.MarginTop
        };

        return baseY + (float) positionPoints;
    }
}
