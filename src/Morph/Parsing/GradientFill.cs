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
}
