/// <summary>
/// Linear gradient fill for a shape (a:gradFill with a:lin direction).
/// We capture only the start/end stop colours and the angle — multi-stop gradients are
/// flattened to a 2-stop linear, and radial/path gradients fall back to the start colour.
/// </summary>
sealed record GradientFill
{
    /// <summary>First stop colour as 6-digit hex (e.g. "FF0000").</summary>
    public required string StartColorHex { get; init; }

    /// <summary>Last stop colour as 6-digit hex.</summary>
    public required string EndColorHex { get; init; }

    /// <summary>Direction in degrees. 0° = top-to-bottom (matches OOXML `a:lin/@ang` divided by 60000).</summary>
    public required double DirectionDegrees { get; init; }

    /// <summary>
    /// Opacity of the first and last stops, 0..1 (<c>a:alpha</c> on the stop colour, 1 when absent).
    /// A soft template accent — labels/04's pale hexagons — declares its gradient stops at 20-40%
    /// alpha over the page, and drawing them opaque saturated every one.
    /// </summary>
    public double StartAlpha { get; init; } = 1.0;

    public double EndAlpha { get; init; } = 1.0;
}
