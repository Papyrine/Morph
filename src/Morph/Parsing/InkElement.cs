/// <summary>
/// Represents an ink drawing (pen/handwriting annotation).
/// </summary>
sealed class InkElement : DocumentElement
{
    /// <summary>Width of the ink drawing in points.</summary>
    public required double WidthPoints { get; init; }

    /// <summary>Height of the ink drawing in points.</summary>
    public required double HeightPoints { get; init; }

    /// <summary>Collection of ink strokes/traces.</summary>
    public required IReadOnlyList<InkStroke> Strokes { get; init; }
}