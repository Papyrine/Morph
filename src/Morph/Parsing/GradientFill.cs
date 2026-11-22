/// <summary>
/// Linear gradient fill for a shape (a:gradFill with a:lin direction): its stops and its angle.
/// Radial/path gradients fall back to the start colour.
/// </summary>
sealed record GradientFill
{
    /// <summary>
    /// Every stop, in order, where there are more than two or they do not sit at the ends; null for
    /// a plain two-stop gradient from <see cref="StartColorHex"/> to <see cref="EndColorHex"/>.
    /// gradient's frames run blue, pink, orange: from the first stop to the last alone they ran
    /// blue to orange through a muddy brown.
    /// </summary>
    public IReadOnlyList<GradientStop>? Stops { get; init; }

    /// <summary>The stops to draw, positions 0..1 along the gradient.</summary>
    public IReadOnlyList<GradientStop> AllStops =>
        Stops ??
        [
            new(0, StartColorHex, StartAlpha),
            new(1, EndColorHex, EndAlpha)
        ];

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

/// <summary>One stop of a <see cref="GradientFill"/>: where along it (0..1), its colour and opacity.</summary>
sealed record GradientStop(double Position, string ColorHex, double Alpha);
