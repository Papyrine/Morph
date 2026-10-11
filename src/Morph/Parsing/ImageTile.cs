/// <summary>
/// A picture fill that repeats (<c>a:blipFill/a:tile</c>) instead of stretching over its shape: the
/// picture is drawn at <see cref="WidthPoints"/> by <see cref="HeightPoints"/> — its own size, the
/// pixels at the resolution the file declares, scaled by the tile's <c>sx</c>/<c>sy</c> — and laid
/// edge to edge in both directions from a corner or the centre of the shape's box.
/// </summary>
/// <param name="WidthPoints">Width of one tile.</param>
/// <param name="HeightPoints">Height of one tile.</param>
/// <param name="OffsetXPoints">The tile's <c>tx</c>: how far the pattern is shifted right of its aligned place.</param>
/// <param name="OffsetYPoints">The tile's <c>ty</c>: how far the pattern is shifted down from its aligned place.</param>
/// <param name="Alignment">The part of the box the first tile is aligned to (<c>algn</c>).</param>
sealed record ImageTile(
    double WidthPoints,
    double HeightPoints,
    double OffsetXPoints,
    double OffsetYPoints,
    ImageTileAlignment Alignment)
{
    /// <summary>
    /// Where the first tile's top-left corner stands, from the top-left of a box of the given size.
    /// The pattern repeats from there in every direction, so a tile aligned to the box's right edge
    /// still fills the box leftward.
    /// </summary>
    public (double X, double Y) Origin(double boxWidth, double boxHeight)
    {
        var x = Alignment switch
        {
            ImageTileAlignment.Top or ImageTileAlignment.Center or ImageTileAlignment.Bottom => (boxWidth - WidthPoints) / 2,
            ImageTileAlignment.TopRight or ImageTileAlignment.Right or ImageTileAlignment.BottomRight => boxWidth - WidthPoints,
            _ => 0
        };
        var y = Alignment switch
        {
            ImageTileAlignment.Left or ImageTileAlignment.Center or ImageTileAlignment.Right => (boxHeight - HeightPoints) / 2,
            ImageTileAlignment.BottomLeft or ImageTileAlignment.Bottom or ImageTileAlignment.BottomRight => boxHeight - HeightPoints,
            _ => 0
        };
        return (x + OffsetXPoints, y + OffsetYPoints);
    }

    /// <summary>
    /// The top-left corner of every tile that reaches into a box of the given size, row by row, from
    /// the box's top-left. Empty for a tile with no extent.
    /// </summary>
    public IEnumerable<(double X, double Y)> Corners(double boxWidth, double boxHeight)
    {
        if (WidthPoints <= 0 || HeightPoints <= 0)
        {
            yield break;
        }

        var (originX, originY) = Origin(boxWidth, boxHeight);
        // Step back from the origin to the first tile that still reaches into the box.
        var startX = originX - Math.Ceiling(originX / WidthPoints) * WidthPoints;
        var startY = originY - Math.Ceiling(originY / HeightPoints) * HeightPoints;
        for (var y = startY; y < boxHeight; y += HeightPoints)
        {
            for (var x = startX; x < boxWidth; x += WidthPoints)
            {
                yield return (x, y);
            }
        }
    }
}

/// <summary>The part of a shape's box a tiled picture fill is aligned to (<c>a:tile/@algn</c>).</summary>
enum ImageTileAlignment
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}
