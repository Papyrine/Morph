/// <summary>
/// Represents a point in an ink stroke.
/// </summary>
sealed record InkPoint
{
    /// <summary>X coordinate in points.</summary>
    public required double X { get; init; }

    /// <summary>Y coordinate in points.</summary>
    public required double Y { get; init; }

    /// <summary>Optional pressure value (0.0 to 1.0).</summary>
    public double? Pressure { get; init; }
}