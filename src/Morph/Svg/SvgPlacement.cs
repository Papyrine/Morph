/// <summary>
/// Where an <see cref="SvgDrawing"/> lands on a page: a point (x, y) of the drawing goes to
/// (x · <see cref="ScaleX"/> + <see cref="OffsetX"/>, y · <see cref="ScaleY"/> +
/// <see cref="OffsetY"/>), and nothing is drawn outside the rectangle from (<see cref="Left"/>,
/// <see cref="Top"/>) to (<see cref="Right"/>, <see cref="Bottom"/>) — the part of the picture's box
/// the drawing's own frame covers. Made by <see cref="SvgDrawing.Place"/>.
/// </summary>
readonly record struct SvgPlacement(double ScaleX, double ScaleY, double OffsetX, double OffsetY, double Left, double Top, double Right, double Bottom)
{
    /// <summary>
    /// The one factor a stroke width is scaled by. A picture is stretched over its box, so the two
    /// scales can differ; the width takes their geometric mean, which is exact whenever the box
    /// keeps the drawing's proportions.
    /// </summary>
    public double StrokeScale => Math.Sqrt(Math.Abs(ScaleX * ScaleY));

    /// <summary>False when the box and the drawing's frame do not meet, and there is nothing to draw.</summary>
    public bool IsVisible => Right > Left && Bottom > Top;
}
